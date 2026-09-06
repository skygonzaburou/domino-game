using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using Domino.DominoSystem;
using Domino.Networking;
using Domino.Player;

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

        [Header("Ready Polling (Step 7.8.1)")]
        [Tooltip("BUILDING/READY中、OnTriggerEnter/Exitイベントに頼らず定期的にStart Area在室状況を再検証する間隔(秒)。Client所有Playerの場合、Kinematic Rigidbody+NetworkTransform同期の組み合わせでServer側のOnTriggerEnterが発火しないケースがあるための保険")]
        [SerializeField] private float readyPollInterval = 0.5f;

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

        private float _settledSince = -1f;
        private float _timeSinceLastFailureCheck;
        private float _timeSinceLastReadyPoll;
        private bool _failureSequenceRunning;

        // Step 14.3: same re-entrancy guard shape as _failureSequenceRunning above, for the new
        // RUNNING -> BUILDING Cancel path.
        private bool _cancelSequenceRunning;

        // Step 10A: renamed from _isRetrying - now guards both the SUCCESS -> Retry path and
        // the new Pause -> Restart path, since both run the same shared reset coroutine.
        private bool _isResettingStage;

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

        // Distinct from IsChallengeActive (Step 8.5): the Start Area movement Constraint should
        // release on SUCCESS so Players can walk the Stage they built, even though the Overview
        // Camera (which keys off IsChallengeActive) correctly stays put. FAILED keeps the same
        // confinement it always had - only SUCCESS is excluded here.
        public bool IsMovementConfined =>
            _phase.Value == GamePhase.RUNNING || _phase.Value == GamePhase.FAILED;

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

            // Periodic Ready poll (Step 7.8.1): the OnTriggerEnter/Exit "fast path" is not
            // fully reliable for a Client-owned Player's Collider on the Server (Kinematic
            // Rigidbody driven by NetworkTransform sync does not always raise a fresh trigger
            // event the Server observes). Without this, a missed Enter event leaves
            // _playersInStartArea stale indefinitely, since ReconcilePlayersInStartArea()
            // otherwise only runs from other discrete events (Enter/Exit/Disconnect). This
            // periodic call is the same self-healing reconciliation, just also time-driven.
            if (_phase.Value == GamePhase.BUILDING || _phase.Value == GamePhase.READY)
            {
                _timeSinceLastReadyPoll += Time.deltaTime;
                if (_timeSinceLastReadyPoll >= readyPollInterval)
                {
                    _timeSinceLastReadyPoll = 0f;
                    ReevaluateReady();
                }
            }

            if (_phase.Value != GamePhase.RUNNING) return;
            if (!_startTriggered.Value) return;

            _timeSinceLastFailureCheck += Time.deltaTime;
            if (_timeSinceLastFailureCheck < failureCheckInterval) return;
            _timeSinceLastFailureCheck = 0f;

            bool anyActive = DominoCleanupManager.Instance != null &&
                DominoCleanupManager.Instance.HasMeaningfulActivity(linearVelocityThreshold, angularVelocityThreshold);

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

        // READY depends only on the current state (Step 8.5) - no memory of past Failures,
        // exits, or re-arm history. Re-evaluated from scratch every time this is called.
        private void ReevaluateReady()
        {
            if (!IsServer) return;
            if (_phase.Value == GamePhase.RUNNING || _phase.Value == GamePhase.SUCCESS || _phase.Value == GamePhase.FAILED) return;

            ReconcilePlayersInStartArea();

            bool allReady = AllConnectedPlayersInStartArea() && startArea.CountStandingInside() > 0;

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
            // Step 10A defense-in-depth: Client-side input is already gated on Pause
            // (ChallengeInput), but an RPC send is not blocked by Time.timeScale, so the Server
            // must not trust that gate alone.
            if (GameplayPauseManager.Instance != null && GameplayPauseManager.Instance.IsPaused) return;
            if (_phase.Value != GamePhase.READY) return;
            if (!AllConnectedPlayersInStartArea()) return;

            // Defensive re-check (Step 8.5): READY should already guarantee this, but never
            // trust cached/UI-only state for something that would otherwise let RUNNING begin
            // with no Domino to ever trigger _startTriggered - which would leave RUNNING
            // unable to ever end (see GameFlow.md "DominoなしRUNNINGを防止する").
            if (startArea.CountStandingInside() <= 0) return;

            BeginRun();
        }

        private void BeginRun()
        {
            ResetRunState();
            SetPhase(GamePhase.RUNNING);
        }

        // Step 14.3: same F key as RequestStartChallengeRpc above, same Authority policy
        // (SendTo.Server, any connected Player may ask - not Host-only), just for the opposite
        // direction (RUNNING -> BUILDING instead of READY -> RUNNING). Deliberately a separate
        // RPC rather than folding into RequestStartChallengeRpc - the two only ever apply in
        // mutually exclusive Phases, and keeping them separate avoids one method silently doing
        // two different things depending on hidden state.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCancelChallengeRpc(RpcParams rpcParams = default)
        {
            if (GameplayPauseManager.Instance != null && GameplayPauseManager.Instance.IsPaused) return;
            if (_phase.Value != GamePhase.RUNNING) return;
            if (_cancelSequenceRunning) return;

            _cancelSequenceRunning = true;
            StartCoroutine(CancelRunSequence());
        }

        // Section 4: this is not a full Retry/Restart - Standing Dominoes are left exactly
        // where they are, only ones that already fell during the cancelled Run are cleaned up
        // (mirrors FailureResetSequence's own Fallen-only cleanup below, minus its Display
        // Duration wait - a deliberate Player Cancel has nothing to show/wait for). Route
        // Progress resets via the same ResetRunState() a fresh Run itself uses. Build Time is
        // not touched here at all - ChallengeTimer already resumes it for free the instant
        // CurrentPhase leaves RUNNING for BUILDING (its own Update() polls IsActivePhase()).
        private IEnumerator CancelRunSequence()
        {
            bool despawnComplete = false;
            if (DominoCleanupManager.Instance != null)
            {
                DominoCleanupManager.Instance.DespawnFallenRegistered(() => despawnComplete = true);
            }
            else
            {
                despawnComplete = true;
            }
            yield return new WaitUntil(() => despawnComplete);

            ResetRunState();
            _cancelSequenceRunning = false;
            SetPhase(GamePhase.BUILDING);

            // Same reasoning as FailureResetSequence/FullStageResetSequence: check immediately
            // rather than waiting for the next Enter/Exit event or poll tick.
            ReevaluateReady();
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
        // RUNNING. Whether this counts as progress depends on comparing this Zone's fixed
        // order position against the one currently expected next (Step 10.3.2):
        // - firedIndex < expected: an Already Completed Zone firing again (e.g. a second
        //   Domino inside the same already-passed Zone toppling moments after the first).
        //   DominoFallenZone.IsSatisfied already latches per-Zone and normally prevents
        //   this call from happening a second time at all, but this comparison is a
        //   general, Zone-agnostic invariant this method enforces on its own terms too -
        //   ignored as a harmless no-op rather than relying solely on that latch.
        // - firedIndex > expected: skipped ahead of the Zone actually expected next - a
        //   genuine Wrong Route (or a Goal reached too early) - fails the run.
        // - firedIndex == expected: normal in-order Progress.
        public void OnZoneFallen(DominoFallenZone zone)
        {
            if (!IsServer) return;
            if (_phase.Value != GamePhase.RUNNING) return;

            int firedIndex = _orderedZones.IndexOf(zone);

            // Already Completed Zone: deliberately touches nothing - no TriggerFailure, no
            // Progress/Index/StartTriggered change, no settle timer reset. Letting a
            // harmless repeat event keep extending the settle-timer grace period would
            // mask a genuinely stalled Chain elsewhere.
            if (firedIndex < _expectedZoneIndex) return;

            if (firedIndex > _expectedZoneIndex)
            {
                TriggerFailure();
                return;
            }

            // Step 8.6: a legitimate (in-order) Fresh Fallen Event is itself Meaningful Chain
            // Activity, independent of what DominoCleanupManager.HasMeaningfulActivity() sees
            // on its own polling cadence. Without this, a settle-timer that happened to already
            // be counting down from an earlier, unrelated lull could expire and fire a False
            // FAILED within moments of genuine Progress being made.
            _settledSince = -1f;

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
                Debug.Log($"[GameFlowManager] Checkpoint progress -> {_checkpointProgress.Value}/{_orderedCheckpoints.Count}");
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

            // Step 8.6: FAILED despawns only Fallen Dominoes, leaving Standing ones on the
            // Stage so the Player can re-place just the failed section instead of rebuilding
            // from scratch. Retry (RetrySequence below) intentionally still uses
            // DespawnAllRegistered - the two are different operations with different intent.
            bool despawnComplete = false;
            if (DominoCleanupManager.Instance != null)
            {
                DominoCleanupManager.Instance.DespawnFallenRegistered(() => despawnComplete = true);
            }
            else
            {
                despawnComplete = true;
            }
            yield return new WaitUntil(() => despawnComplete);

            ResetRunState();
            _failureSequenceRunning = false;
            SetPhase(GamePhase.BUILDING);

            // Immediately check whether the new (Step 8.5) READY condition already holds -
            // e.g. Players never left the Start Area and a Standing Domino already exists -
            // rather than waiting for the next Enter/Exit event or poll tick.
            ReevaluateReady();
        }

        // Step 8.5: Prototype allows either Host or Client to request a Retry (see Step 9
        // Preparation notes - may become Host-only later). Server independently validates
        // CurrentPhase == SUCCESS regardless of who asked, so a stray/late request from a
        // Client is never trusted blindly.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RetryRequestRpc(RpcParams rpcParams = default)
        {
            if (_phase.Value != GamePhase.SUCCESS) return;
            if (_isResettingStage) return;

            _isResettingStage = true;
            StartCoroutine(FullStageResetSequence());
        }

        // Step 10A: Pause Menu's Restart. GameFlowManager (not GameplayPauseManager) decides
        // whether this specific request is allowed right now - the caller's own gating (Pause
        // Menu only reachable while Paused) is never trusted blindly, same philosophy as
        // RetryRequestRpc above. Shares FullStageResetSequence with Retry (Section 23 of the
        // Step 10 spec) rather than duplicating the reset logic.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRestartRpc(RpcParams rpcParams = default)
        {
            if (GameplayPauseManager.Instance == null || !GameplayPauseManager.Instance.IsPaused) return;
            if (_isResettingStage) return;

            _isResettingStage = true;
            StartCoroutine(FullStageResetSequence());
        }

        // Shared by SUCCESS -> Retry and Pause -> Restart (Step 10A, Section 23): full Domino
        // Despawn, Progress/BUILD TIME/Phase reset, and (new in Step 10A) repositioning every
        // connected Player back to a Spawn Point - Retry never did this before Step 10A, but the
        // Step 10 spec calls for both entry points to share one reset, so this is a deliberate,
        // additive extension of Retry's existing behaviour, not a change to it.
        private IEnumerator FullStageResetSequence()
        {
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
            if (ChallengeTimer.Instance != null) ChallengeTimer.Instance.ResetForRetry();
            RepositionAllPlayersToSpawnPoints();
            _isResettingStage = false;
            SetPhase(GamePhase.BUILDING);

            // Never leave a freshly-reset stage frozen (Restart always begins from Paused; a
            // no-op for the Retry path, which can never happen while Paused - see Section 3).
            if (GameplayPauseManager.Instance != null) GameplayPauseManager.Instance.ServerForceUnpause();

            // Same reasoning as FailureResetSequence: check immediately rather than waiting
            // for the next event/poll tick (though after a Reset, Standing Domino count is
            // always 0 until Players rebuild, so this will not reach READY yet).
            ReevaluateReady();
        }

        // Step 10A (Section 21): deterministically re-assigns each connected Client to a Spawn
        // Point, reusing the exact same index-based assignment GameplayPlayerSpawner uses on
        // initial Spawn (Networking.md "Player Spawn Lifecycle") - just applied to an
        // already-Spawned Player instead of a freshly-Spawned one. Owner Authority is preserved:
        // the Server never writes a Client-owned Player's Transform/Rigidbody directly - it only
        // decides WHERE and asks that Player's own Owner to apply it locally
        // (PlayerMotor.ServerTeleportRpc, mirrors the existing ApplyPushRpc pattern).
        private void RepositionAllPlayersToSpawnPoints()
        {
            if (GameplayPlayerSpawner.Instance == null) return;

            Transform[] spawnPoints = GameplayPlayerSpawner.Instance.ResolveSpawnPoints();
            if (spawnPoints == null || spawnPoints.Length == 0) return;

            int i = 0;
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                Transform point = spawnPoints[i % spawnPoints.Length];
                i++;

                if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null) continue;

                PlayerMotor motor = client.PlayerObject.GetComponent<PlayerMotor>();
                if (motor != null) motor.ServerTeleportRpc(point.position, point.rotation);
            }
        }

        private void SetPhase(GamePhase newPhase)
        {
            if (!IsServer) return;
            _phase.Value = newPhase;
            Debug.Log($"[GameFlowManager] Phase -> {newPhase} (readyPlayers={_playersInStartArea.Count}/{NetworkManager.ConnectedClientsIds.Count}, checkpointProgress={_checkpointProgress.Value}/{_orderedCheckpoints.Count})");
        }
    }
}
