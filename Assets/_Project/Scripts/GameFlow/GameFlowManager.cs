using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using Domino.DominoSystem;

namespace Domino.GameFlow
{
    public class GameFlowManager : NetworkBehaviour
    {
        public static GameFlowManager Instance { get; private set; }

        [Header("Zones")]
        [SerializeField] private StartAreaZone startArea;
        [SerializeField] private CheckpointZone[] checkpoints;
        [SerializeField] private GoalAreaZone goalArea;

        [Header("Failure Detection")]
        [Tooltip("この速度(m/秒)未満なら「静止している」とみなす")]
        [SerializeField] private float linearVelocityThreshold = 0.02f;
        [Tooltip("この角速度(ラジアン/秒)未満なら「静止している」とみなす")]
        [SerializeField] private float angularVelocityThreshold = 0.05f;
        [Tooltip("StartTriggered後、登録済み全Dominoがこの秒数連続で静止し続けたらFAILEDとする")]
        [SerializeField] private float failureSettleDuration = 1.75f;
        [Tooltip("何秒おきにDominoの静止状態を確認するか")]
        [SerializeField] private float failureCheckInterval = 0.25f;

        [Header("Failure Reset")]
        [Tooltip("FAILED状態を表示し続ける秒数(この間はDominoを削除しない)")]
        [SerializeField] private float failureDisplayDuration = 1.75f;

        private readonly NetworkVariable<GamePhase> _phase = new NetworkVariable<GamePhase>(
            GamePhase.BUILDING, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _checkpointProgress = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _startTriggered = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly HashSet<ulong> _playersInStartArea = new HashSet<ulong>();
        private List<CheckpointZone> _orderedCheckpoints;

        // Ordered [Start, CP0, CP1, ..., Goal]. Progress is driven purely by which of these
        // fires next during RUNNING - no rescanning of other zones' state (see OnZoneFallen).
        private List<DominoFallenZone> _orderedZones;
        private int _expectedZoneIndex;

        // Blocks BUILDING->READY after a Failure reset until every currently-connected
        // Player has left the Start Area at least once (see ReevaluateReady).
        private bool _readyRearmPending;

        private float _settledSince = -1f;
        private float _timeSinceLastFailureCheck;
        private bool _failureSequenceRunning;

        public GamePhase CurrentPhase => _phase.Value;
        public int CheckpointProgress => _checkpointProgress.Value;
        public bool StartTriggered => _startTriggered.Value;
        public StartAreaZone StartArea => startArea;

        // Single source of truth for "the Challenge is in progress or just concluded" - the
        // Player-movement Start Area confinement (PlayerMotor), the RUNNING-basis movement
        // controls, and the Overview Camera (ThirdPersonCamera) all key off this same set of
        // Phases (Step 7.6) instead of each re-listing RUNNING/SUCCESS/FAILED separately.
        public bool IsChallengeActive =>
            _phase.Value == GamePhase.RUNNING || _phase.Value == GamePhase.SUCCESS || _phase.Value == GamePhase.FAILED;

        private void Awake()
        {
            Instance = this;
            _orderedCheckpoints = new List<CheckpointZone>(checkpoints);
            _orderedCheckpoints.Sort((a, b) => a.CheckpointIndex.CompareTo(b.CheckpointIndex));

            _orderedZones = new List<DominoFallenZone> { startArea };
            _orderedZones.AddRange(_orderedCheckpoints);
            _orderedZones.Add(goalArea);
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        // Chain Stop Failure monitor. Only runs RUNNING, and only once the Start Domino has
        // actually fallen - entering RUNNING with nobody having touched anything yet must
        // never read as "everything is settled, therefore Failed".
        private void Update()
        {
            if (!IsServer) return;
            if (_phase.Value != GamePhase.RUNNING) return;
            if (!_startTriggered.Value) return;

            _timeSinceLastFailureCheck += Time.deltaTime;
            if (_timeSinceLastFailureCheck < failureCheckInterval) return;
            _timeSinceLastFailureCheck = 0f;

            bool anyActive = DominoCleanupManager.Instance != null &&
                DominoCleanupManager.Instance.AnyTrackedDominoActive(linearVelocityThreshold, angularVelocityThreshold);

            if (anyActive)
            {
                _settledSince = -1f;
                return;
            }

            if (_settledSince < 0f)
            {
                _settledSince = Time.time;
                return;
            }

            if (Time.time - _settledSince >= failureSettleDuration)
            {
                TriggerFailure();
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            _playersInStartArea.Remove(clientId);
            ReevaluateReady();
        }

        public void NotifyPlayerEnteredStart(ulong clientId)
        {
            if (!IsServer) return;
            _playersInStartArea.Add(clientId);
            ReevaluateReady();
        }

        public void NotifyPlayerExitedStart(ulong clientId)
        {
            if (!IsServer) return;
            _playersInStartArea.Remove(clientId);
            ReevaluateReady();
        }

        private void ReevaluateReady()
        {
            if (!IsServer) return;
            if (_phase.Value == GamePhase.RUNNING || _phase.Value == GamePhase.SUCCESS || _phase.Value == GamePhase.FAILED) return;

            ReconcilePlayersInStartArea();

            if (_readyRearmPending)
            {
                if (_playersInStartArea.Count > 0) return;
                _readyRearmPending = false;
            }

            bool allReady = AllConnectedPlayersInStartArea();

            if (allReady && _phase.Value == GamePhase.BUILDING)
            {
                SetPhase(GamePhase.READY);
            }
            else if (!allReady && _phase.Value == GamePhase.READY)
            {
                SetPhase(GamePhase.BUILDING);
            }
        }

        private bool AllConnectedPlayersInStartArea()
        {
            if (NetworkManager.ConnectedClientsIds.Count == 0) return false;
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                if (!_playersInStartArea.Contains(clientId)) return false;
            }
            return true;
        }

        // Self-heals _playersInStartArea against each connected Player's actual current
        // position before every Ready decision, instead of trusting OnTriggerEnter/Exit's
        // cumulative Add/Remove history as the sole source of truth indefinitely. Bounded by
        // the (small) connected-player count - not a scene-wide search - and only runs at
        // discrete decision points (ReevaluateReady), not every frame.
        private void ReconcilePlayersInStartArea()
        {
            if (startArea == null) return;

            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                bool trackedInside = _playersInStartArea.Contains(clientId);
                bool actuallyInside = NetworkManager.ConnectedClients.TryGetValue(clientId, out var client)
                    && client.PlayerObject != null
                    && startArea.ContainsWorldPoint(client.PlayerObject.transform.position);

                if (actuallyInside && !trackedInside) _playersInStartArea.Add(clientId);
                else if (!actuallyInside && trackedInside) _playersInStartArea.Remove(clientId);
            }

            // Drop any tracked id for a Client that has since disconnected (belt-and-suspenders
            // alongside HandleClientDisconnected, which already removes it on the same event).
            _playersInStartArea.RemoveWhere(id => !NetworkManager.ConnectedClientsIds.Contains(id));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStartChallengeRpc(RpcParams rpcParams = default)
        {
            if (_phase.Value != GamePhase.READY) return;
            if (!AllConnectedPlayersInStartArea()) return;

            BeginRun();
        }

        private void BeginRun()
        {
            ResetRunState();
            SetPhase(GamePhase.RUNNING);
        }

        private void ResetRunState()
        {
            _checkpointProgress.Value = 0;
            _startTriggered.Value = false;
            _expectedZoneIndex = 0;
            _settledSince = -1f;
            _timeSinceLastFailureCheck = 0f;

            startArea.OnRunStart();
            foreach (var cp in _orderedCheckpoints) cp.OnRunStart();
            goalArea.OnRunStart();
        }

        // Called by a Zone (Host-only) whenever a Domino freshly falls inside it during
        // RUNNING. Whether this counts as progress depends solely on whether this Zone is
        // the one currently expected next - a Zone that already fired earlier this run
        // can never fire again (DominoFallenZone.IsSatisfied latches), and a Zone firing
        // out of order fails the run instead of being silently ignored or catching up
        // other Zones' state.
        public void OnZoneFallen(DominoFallenZone zone)
        {
            if (!IsServer) return;
            if (_phase.Value != GamePhase.RUNNING) return;

            int firedIndex = _orderedZones.IndexOf(zone);
            if (firedIndex != _expectedZoneIndex)
            {
                TriggerFailure();
                return;
            }

            if (zone == goalArea)
            {
                SetPhase(GamePhase.SUCCESS);
                return;
            }

            if (zone == startArea)
            {
                _startTriggered.Value = true;
            }
            else
            {
                _checkpointProgress.Value++;
            }

            _expectedZoneIndex++;
        }

        private void TriggerFailure()
        {
            if (_failureSequenceRunning) return;
            _failureSequenceRunning = true;
            SetPhase(GamePhase.FAILED);
            StartCoroutine(FailureResetSequence());
        }

        private IEnumerator FailureResetSequence()
        {
            yield return new WaitForSeconds(failureDisplayDuration);

            bool despawnComplete = false;
            if (DominoCleanupManager.Instance != null)
            {
                DominoCleanupManager.Instance.DespawnAllRegistered(() => despawnComplete = true);
            }
            else
            {
                despawnComplete = true;
            }
            yield return new WaitUntil(() => despawnComplete);

            ResetRunState();
            _readyRearmPending = true;
            _failureSequenceRunning = false;
            SetPhase(GamePhase.BUILDING);

            // In case every Player already left the Start Area while RUNNING/FAILED, clear
            // the re-arm block immediately instead of waiting for the next Enter/Exit event.
            ReevaluateReady();
        }

        private void SetPhase(GamePhase newPhase)
        {
            if (!IsServer) return;
            _phase.Value = newPhase;
            Debug.Log($"[GameFlowManager] Phase -> {newPhase} (readyPlayers={_playersInStartArea.Count}/{NetworkManager.ConnectedClientsIds.Count}, checkpointProgress={_checkpointProgress.Value}/{_orderedCheckpoints.Count})");
        }
    }
}
