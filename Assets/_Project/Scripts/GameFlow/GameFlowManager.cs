using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Domino.GameFlow
{
    public class GameFlowManager : NetworkBehaviour
    {
        public static GameFlowManager Instance { get; private set; }

        [Header("Zones")]
        [SerializeField] private StartAreaZone startArea;
        [SerializeField] private CheckpointZone[] checkpoints;
        [SerializeField] private GoalAreaZone goalArea;

        private readonly NetworkVariable<GamePhase> _phase = new NetworkVariable<GamePhase>(
            GamePhase.BUILDING, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _checkpointProgress = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _startTriggered = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly HashSet<ulong> _playersInStartArea = new HashSet<ulong>();
        private List<CheckpointZone> _orderedCheckpoints;

        public GamePhase CurrentPhase => _phase.Value;
        public int CheckpointProgress => _checkpointProgress.Value;
        public bool StartTriggered => _startTriggered.Value;

        private void Awake()
        {
            Instance = this;
            _orderedCheckpoints = new List<CheckpointZone>(checkpoints);
            _orderedCheckpoints.Sort((a, b) => a.CheckpointIndex.CompareTo(b.CheckpointIndex));
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
            if (_phase.Value == GamePhase.RUNNING || _phase.Value == GamePhase.SUCCESS) return;

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

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStartChallengeRpc(RpcParams rpcParams = default)
        {
            if (_phase.Value != GamePhase.READY) return;
            if (!AllConnectedPlayersInStartArea()) return;

            BeginRun();
        }

        private void BeginRun()
        {
            _checkpointProgress.Value = 0;
            _startTriggered.Value = false;

            startArea.OnRunStart();
            foreach (var cp in _orderedCheckpoints) cp.OnRunStart();
            goalArea.OnRunStart();

            SetPhase(GamePhase.RUNNING);
        }

        // Called by zones (Host-only, since only the server ever sets IsSatisfied=true)
        // whenever their Fallen condition newly becomes true.
        public void OnZoneStateChanged()
        {
            if (!IsServer) return;
            if (_phase.Value != GamePhase.RUNNING) return;

            if (!startArea.IsSatisfied) return;
            _startTriggered.Value = true;

            int progress = 0;
            foreach (var cp in _orderedCheckpoints)
            {
                if (!cp.IsSatisfied) break;
                progress++;
            }
            _checkpointProgress.Value = progress;

            if (progress >= _orderedCheckpoints.Count && goalArea.IsSatisfied)
            {
                SetPhase(GamePhase.SUCCESS);
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
