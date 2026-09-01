using Unity.Netcode;
using UnityEngine;

namespace Domino.Menu
{
    // Scene-placed NetworkObject in MainMenu.unity (same pattern as GameFlowManager in the
    // Gameplay Scene, Step 6) - spawns automatically when the Host starts, since MainMenu is
    // the active Scene at that point. Synchronizes just enough Online Session state (Step 9,
    // section 52-54) for every connected Client's local Menu UI to react correctly: which
    // Phase the Room is in, and (from Step 9C) which Stage the Host has selected. Host/Server
    // Authority only - Clients never write these NetworkVariables directly.
    public class NetworkSessionState : NetworkBehaviour
    {
        public static NetworkSessionState Instance { get; private set; }

        private readonly NetworkVariable<SessionPhase> _phase = new NetworkVariable<SessionPhase>(
            SessionPhase.Room, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _selectedStageId = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public SessionPhase Phase => _phase.Value;
        public int SelectedStageId => _selectedStageId.Value;

        public event System.Action<SessionPhase> PhaseChanged;
        public event System.Action<int> SelectedStageChanged;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            _phase.OnValueChanged += HandlePhaseChanged;
            _selectedStageId.OnValueChanged += HandleSelectedStageChanged;
        }

        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= HandlePhaseChanged;
            _selectedStageId.OnValueChanged -= HandleSelectedStageChanged;
        }

        private void HandlePhaseChanged(SessionPhase previous, SessionPhase current)
        {
            PhaseChanged?.Invoke(current);
        }

        private void HandleSelectedStageChanged(int previous, int current)
        {
            SelectedStageChanged?.Invoke(current);
        }

        // Host-only (Step 9C, section 34-35): highlighting a Stage in the Grid is separate
        // from starting the game - Online Stage Select is a two-step Select-then-Start flow,
        // unlike Single Player's one-click "select and go" (StageSelectController already
        // branches on GameSessionContext.IsOnline for this).
        public void HostSelectStage(int stageId)
        {
            if (!IsServer) return;
            if (_phase.Value != SessionPhase.StageSelect) return;
            _selectedStageId.Value = stageId;
        }

        // Host-only. Validates independently of whatever the UI already gated (section 37: "a
        // Client sending an illegitimate Start Request must not be able to start the game") -
        // connectedPlayerCount and catalog validity are both re-checked here, not just trusted
        // from the caller.
        public bool HostTryStartGame(int connectedPlayerCount)
        {
            if (!IsServer) return false;
            if (_phase.Value != SessionPhase.StageSelect) return false;
            if (connectedPlayerCount < 2 || connectedPlayerCount > 4) return false;
            if (_selectedStageId.Value < 0) return false;

            _phase.Value = SessionPhase.StartingGame;
            return true;
        }

        // Host-only (Step 9, section 53: advancing to Stage Select Phase is Host/Server
        // Authority). Caller (OnlineSessionController) is responsible for the >= 2 Player
        // check (section 28) before calling this - kept here too as a defensive re-check,
        // the same "never trust a single gate" pattern GameFlowManager already uses for READY.
        public void HostAdvanceToStageSelect(int connectedPlayerCount)
        {
            if (!IsServer) return;
            if (_phase.Value != SessionPhase.Room) return;
            if (connectedPlayerCount < 2) return;

            _phase.Value = SessionPhase.StageSelect;
        }

        // Host-only. Used when Player Count drops back below 2 while still in the Room -
        // returns to Room Phase (Step 9, section 38 keeps this simple by never allowing a
        // Stage Select/Start to proceed with < 2, rather than adding a separate "paused" state).
        public void HostReturnToRoom()
        {
            if (!IsServer) return;
            _phase.Value = SessionPhase.Room;
        }

        public void ResetForNewSession()
        {
            if (!IsServer) return;
            _phase.Value = SessionPhase.Room;
            _selectedStageId.Value = -1;
        }
    }
}
