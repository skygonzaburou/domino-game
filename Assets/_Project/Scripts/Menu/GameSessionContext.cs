namespace Domino.Menu
{
    // Plain static data, not a MonoBehaviour - survives Scene loads for free (statics are not
    // Scene-bound), unlike GameObjects which would need explicit DontDestroyOnLoad. Holds just
    // enough Session Context (Step 9, section 44) for the Gameplay Scene to know which Stage
    // was selected and whether this is a Single Player or Online session. Not yet displayed
    // anywhere in Gameplay - reserved for future Stage Name HUD / Result / Next Course use.
    public static class GameSessionContext
    {
        public static int SelectedStageId { get; set; } = -1;
        public static bool IsOnline { get; set; }

        // Step 12.1: the local Client's own current Character choice. Unlike SelectedStageId,
        // this always has a valid value (Section 9: Character 1 = Classic Capsule is the Default
        // Selection) - there is no "nothing selected yet" state to represent, so no -1 sentinel
        // is needed. Read directly by CharacterSelectController to restore the highlight (Single
        // Player return via Pause - Section 30) and by GameplayPlayerSpawner for the Single
        // Player spawn path (Online instead resolves per-Client via NetworkSessionState, since
        // multiple Clients cannot share one static field).
        public static int SelectedCharacterId { get; set; } = 1;

        // Step 12.1.1 (Section 15/26): local-Client-only, optimistic mirror of whether THIS
        // Client has ever actually clicked a Character Card during the current Online Session
        // (Server-authoritative HasSelectedCharacter lives in PlayerCharacterSelectionStore and
        // drives the real Stage Select Gate - this flag exists only so CharacterSelectController
        // can show "Choose your character" vs "Selected: X, waiting..." for the local Player
        // without a network round-trip). Reset only on a fresh Create/Join (see
        // OnlineSessionController.OnSessionJoinedOrCreated) - NOT on every Character Select entry,
        // so it correctly stays true across a Pause -> Character Select return (Section 31, where
        // the prior Gameplay Selection is still valid).
        public static bool HasSelectedCharacterThisSession { get; set; }

        // Step 10B (Section 29): consumed once by MainMenuController.Start() to skip Title and
        // land directly on the correct Panel when returning from Gameplay (Pause Menu / SUCCESS
        // Result "Stage Select"). Left null for an ordinary Title return - MainMenuController
        // already defaults to Title when this is unset.
        public static MenuState? ReturnDestination { get; set; }

        // Step 10B (Section 33/34/42): set by Gameplay's "Return to Title" before Shutdown() so
        // the freshly-loaded MainMenu's OnlineSessionController knows to actually leave the
        // Unity Gaming Services Session (LeaveAsync) using its rehydrated static Session
        // reference, instead of treating this as a brand new visit to the Menu.
        public static bool PendingLeaveOnlineSession { get; set; }

        // Step 10B (Section 26-27): set by Gameplay's "Stage Select" before the Network Scene
        // Load. NetworkSessionState is a normal Scene-placed NetworkObject bound to MainMenu.unity
        // (unlike NetworkManager, it is NOT DontDestroyOnLoad) - it does not exist at all while
        // in Gameplay, so this Host-only intent cannot be applied directly from there. The fresh
        // NetworkSessionState that spawns once MainMenu reloads reads and consumes this itself
        // (OnNetworkSpawn) to move straight to StageSelect Phase instead of its usual Room
        // default.
        public static bool PendingHostReturnToStageSelect { get; set; }

        // Step 12.1 (Section 31/34): identical purpose to PendingHostReturnToStageSelect above,
        // for the new Pause -> Character Select Host action - consumed once by the freshly
        // spawned NetworkSessionState (OnNetworkSpawn) to move straight to CharacterSelect Phase.
        public static bool PendingHostReturnToCharacterSelect { get; set; }
    }
}
