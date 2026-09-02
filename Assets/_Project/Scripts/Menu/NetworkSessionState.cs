using Unity.Netcode;
using UnityEngine;
using Domino.Networking;

namespace Domino.Menu
{
    // Scene-placed NetworkObject in MainMenu.unity (same pattern as GameFlowManager in the
    // Gameplay Scene, Step 6) - spawns automatically when the Host starts, since MainMenu is
    // the active Scene at that point. Synchronizes just enough Online Session state (Step 9,
    // section 52-54) for every connected Client's local Menu UI to react correctly: which
    // Phase the Room is in, and (from Step 9C) which Stage the Host has selected. Host/Server
    // Authority only - Clients never write these NetworkVariables directly.
    //
    // Step 12.1.1: this is the UI/Network RPC entry point for Character Selection (Section 10 of
    // the fix request), but the actual ClientId -> CharacterId data now lives in
    // PlayerCharacterSelectionStore (a Persistent Networking-side component, see that file for
    // why - it is what actually survives the Menu -> Gameplay Scene transition, unlike this
    // Scene-placed NetworkObject).
    public class NetworkSessionState : NetworkBehaviour
    {
        public static NetworkSessionState Instance { get; private set; }

        [Header("Character Selection (Step 12.1)")]
        [Tooltip("Server側でCharacter IDを検証するためのCatalog参照。任意のPrefab/ObjectはClientから送らず、IDのみ受け取りここで存在確認する")]
        [SerializeField] private CharacterCatalog characterCatalog;

        private readonly NetworkVariable<SessionPhase> _phase = new NetworkVariable<SessionPhase>(
            SessionPhase.Room, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _selectedStageId = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Step 12.1.1 (Section 15/18/27): how many currently-connected Clients have selected at
        // least once (HasSelectedCharacter == true), synced so every Client's local UI can show
        // "X / Y selected" and the Host's Stage Select gating (Section 19/20) without needing to
        // see who-picked-what. The actual per-Client HasSelected/CharacterId data stays
        // Server-only in PlayerCharacterSelectionStore; this is just the aggregate count.
        private readonly NetworkVariable<int> _selectedCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public SessionPhase Phase => _phase.Value;
        public int SelectedStageId => _selectedStageId.Value;
        public int SelectedCount => _selectedCount.Value;

        public event System.Action<SessionPhase> PhaseChanged;
        public event System.Action<int> SelectedStageChanged;
        public event System.Action<int> SelectedCountChanged;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            _phase.OnValueChanged += HandlePhaseChanged;
            _selectedStageId.OnValueChanged += HandleSelectedStageChanged;
            _selectedCount.OnValueChanged += HandleSelectedCountChanged;

            if (IsServer && PlayerCharacterSelectionStore.Instance != null)
            {
                PlayerCharacterSelectionStore.Instance.ClientSelectionRemoved += HandleClientSelectionRemoved;
            }

            // Step 10B (Section 26-27): this instance always spawns at its NetworkVariable
            // default (Room) - a Host returning from Gameplay to Stage Select cannot have set
            // Phase directly (the previous MainMenu's NetworkSessionState, and this one, are two
            // different instances - see GameSessionContext.PendingHostReturnToStageSelect). Apply
            // that intent here, once, as this fresh instance comes online.
            if (IsServer && GameSessionContext.PendingHostReturnToStageSelect)
            {
                GameSessionContext.PendingHostReturnToStageSelect = false;
                _phase.Value = SessionPhase.StageSelect;
            }

            // Step 12.1 (Section 31/34): same pattern, for the Pause -> Character Select Host
            // action. Step 12.1.1 (Section 31 of THIS fix): every currently-connected Player's
            // prior Gameplay Character Selection is still valid, so restore HasSelectedCharacter
            // for all of them (PlayerCharacterSelectionStore survives the Scene transition) and
            // seed SelectedCount to match, rather than reopening a fresh empty Character Select.
            if (IsServer && GameSessionContext.PendingHostReturnToCharacterSelect)
            {
                GameSessionContext.PendingHostReturnToCharacterSelect = false;
                _phase.Value = SessionPhase.CharacterSelect;

                if (PlayerCharacterSelectionStore.Instance != null)
                {
                    PlayerCharacterSelectionStore.Instance.MarkAllConnectedAsSelected(NetworkManager.ConnectedClientsIds);
                    _selectedCount.Value = PlayerCharacterSelectionStore.Instance.CountSelectedAmong(NetworkManager.ConnectedClientsIds);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= HandlePhaseChanged;
            _selectedStageId.OnValueChanged -= HandleSelectedStageChanged;
            _selectedCount.OnValueChanged -= HandleSelectedCountChanged;

            if (IsServer && PlayerCharacterSelectionStore.Instance != null)
            {
                PlayerCharacterSelectionStore.Instance.ClientSelectionRemoved -= HandleClientSelectionRemoved;
            }
        }

        // Section 28: a disconnecting Client that HAD selected must not leave SelectedCount
        // permanently overcounted (PlayerCharacterSelectionStore.ClientSelectionRemoved is the
        // single source of truth for "did this disconnect actually remove a real Selection" -
        // see that file for why this isn't handled via a second independent disconnect handler).
        private void HandleClientSelectionRemoved(ulong clientId)
        {
            if (!IsServer) return;
            _selectedCount.Value = Mathf.Max(0, _selectedCount.Value - 1);
        }

        private void HandlePhaseChanged(SessionPhase previous, SessionPhase current)
        {
            PhaseChanged?.Invoke(current);
        }

        private void HandleSelectedStageChanged(int previous, int current)
        {
            SelectedStageChanged?.Invoke(current);
        }

        private void HandleSelectedCountChanged(int previous, int current)
        {
            SelectedCountChanged?.Invoke(current);
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

            // Step 12.1.1 (Section 30): Start Game itself is the last line of defense - every
            // Connected Player must have a valid Character Selection by this point (the Stage
            // Select Gate below should already guarantee this, but Late Join during Stage Select
            // is possible under the current Architecture, so this is re-checked independently
            // here too rather than assumed).
            if (PlayerCharacterSelectionStore.Instance == null
                || !PlayerCharacterSelectionStore.Instance.AllSelected(NetworkManager.ConnectedClientsIds))
            {
                return false;
            }

            _phase.Value = SessionPhase.StartingGame;
            return true;
        }

        // Host-only (Step 9, section 53: advancing to Stage Select Phase is Host/Server
        // Authority). Caller (OnlineSessionController) is responsible for the >= 2 Player
        // check (section 28) before calling this - kept here too as a defensive re-check,
        // the same "never trust a single gate" pattern GameFlowManager already uses for READY.
        //
        // Step 12.1 (Section 19): Room's "next" action now leads to Character Select first, not
        // Stage Select directly - the target Phase changed, the Player Count Gate/semantics did
        // not.
        public void HostAdvanceToCharacterSelect(int connectedPlayerCount)
        {
            if (!IsServer) return;
            if (_phase.Value != SessionPhase.Room) return;
            if (connectedPlayerCount < 2) return;

            _phase.Value = SessionPhase.CharacterSelect;
        }

        // Step 12.1 (Section 22/36): Character Select's own "STAGE SELECT" button advances one
        // step further. Same Host-only/Player-Count-Gate shape as HostAdvanceToCharacterSelect
        // above, just re-anchored to start from CharacterSelect instead of Room now that Room no
        // longer leads directly to Stage Select.
        //
        // Step 12.1.1 (Section 18/19/24): also re-validates that every Connected Player has
        // selected a Character - this is the Server-side half of the All-player Gate, called both
        // by the (already Host-Confirmation-gated) UI action and defensively here regardless of
        // what the caller already checked.
        public void HostAdvanceToStageSelect(int connectedPlayerCount)
        {
            if (!IsServer) return;
            if (_phase.Value != SessionPhase.CharacterSelect) return;
            if (connectedPlayerCount < 2) return;
            if (PlayerCharacterSelectionStore.Instance == null
                || !PlayerCharacterSelectionStore.Instance.AllSelected(NetworkManager.ConnectedClientsIds))
            {
                return;
            }

            _phase.Value = SessionPhase.StageSelect;
        }

        // Step 12.1.1 (Section 16/17): Client-to-Server, callable by any connected Client (not
        // Host-only - unlike Stage Select, each Player picks their own Character). Validates the
        // Id against the Catalog server-side (Section 17: only ever trust an Id, never a Prefab/
        // Object reference sent from a Client) and is a no-op outside Character Select Phase.
        // Writes through to PlayerCharacterSelectionStore (Persistent, survives the Scene
        // transition into Gameplay) rather than a local Dictionary of this Scene-bound Object.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSelectCharacterRpc(int characterId, RpcParams rpcParams = default)
        {
            if (_phase.Value != SessionPhase.CharacterSelect) return;
            if (characterCatalog == null || characterCatalog.FindById(characterId) == null) return;
            if (PlayerCharacterSelectionStore.Instance == null) return;

            ulong senderClientId = rpcParams.Receive.SenderClientId;

            // Section 17/22: a first-time Selection increments the synced Count (Section 18's
            // All-player Gate); changing an already-made Selection updates only the stored
            // CharacterId - HasSelectedCharacter (and therefore the Count) does not change.
            bool wasNewSelection = !PlayerCharacterSelectionStore.Instance.HasSelected(senderClientId);
            PlayerCharacterSelectionStore.Instance.SetSelection(senderClientId, characterId);

            if (wasNewSelection)
            {
                _selectedCount.Value++;
            }
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
            _selectedCount.Value = 0;
            PlayerCharacterSelectionStore.Instance?.ResetForNewSession();
        }
    }
}
