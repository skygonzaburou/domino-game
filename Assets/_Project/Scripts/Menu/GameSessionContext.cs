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
    }
}
