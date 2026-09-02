using System.Collections.Generic;
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

        [Header("Character Selection (Step 12.1)")]
        [Tooltip("Server側でCharacter IDを検証するためのCatalog参照。任意のPrefab/ObjectはClientから送らず、IDのみ受け取りここで存在確認する")]
        [SerializeField] private CharacterCatalog characterCatalog;

        private const int DefaultCharacterId = 1;

        private readonly NetworkVariable<SessionPhase> _phase = new NetworkVariable<SessionPhase>(
            SessionPhase.Room, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _selectedStageId = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Step 12.1 (Section 15/16/18): Server-authoritative ClientId -> CharacterId, kept as a
        // plain Server-only Dictionary rather than a synced NetworkVariable/NetworkList - each
        // Client only ever needs to know/display its OWN pick locally (GameSessionContext,
        // optimistic - set immediately on click), never anyone else's, while the Server needs the
        // full map only once, at Gameplay Player Spawn time. A Client absent from this Dictionary
        // (never touched a Card) resolves to DefaultCharacterId via GetSelectedCharacterId below -
        // this is what Section 21 means by "no Ready System needed, a valid selection always
        // exists".
        private readonly Dictionary<ulong, int> _selectedCharacterByClient = new Dictionary<ulong, int>();

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

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
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

            // Step 12.1 (Section 31/34): same pattern, for the new Pause -> Character Select
            // Host action.
            if (IsServer && GameSessionContext.PendingHostReturnToCharacterSelect)
            {
                GameSessionContext.PendingHostReturnToCharacterSelect = false;
                _phase.Value = SessionPhase.CharacterSelect;
            }
        }

        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= HandlePhaseChanged;
            _selectedStageId.OnValueChanged -= HandleSelectedStageChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            _selectedCharacterByClient.Remove(clientId);
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
        public void HostAdvanceToStageSelect(int connectedPlayerCount)
        {
            if (!IsServer) return;
            if (_phase.Value != SessionPhase.CharacterSelect) return;
            if (connectedPlayerCount < 2) return;

            _phase.Value = SessionPhase.StageSelect;
        }

        // Step 12.1 (Section 21): the Server-side source of truth for "what Character does this
        // Client use" - defaults to Classic Capsule when the Client never touched a Card, so a
        // valid Selection always exists with no separate Ready System. Used by
        // GameplayPlayerSpawner at spawn time.
        public int GetSelectedCharacterId(ulong clientId)
        {
            return _selectedCharacterByClient.TryGetValue(clientId, out int characterId) ? characterId : DefaultCharacterId;
        }

        // Step 12.1 (Section 16/17): Client-to-Server, callable by any connected Client (not
        // Host-only - unlike Stage Select, each Player picks their own Character). Validates the
        // Id against the Catalog server-side (Section 17: only ever trust an Id, never a Prefab/
        // Object reference sent from a Client) and is a no-op outside Character Select Phase.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSelectCharacterRpc(int characterId, RpcParams rpcParams = default)
        {
            if (_phase.Value != SessionPhase.CharacterSelect) return;
            if (characterCatalog == null || characterCatalog.FindById(characterId) == null) return;

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            _selectedCharacterByClient[senderClientId] = characterId;
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
            _selectedCharacterByClient.Clear();
        }
    }
}
