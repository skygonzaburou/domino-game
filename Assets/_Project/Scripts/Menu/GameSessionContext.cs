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
    }
}
