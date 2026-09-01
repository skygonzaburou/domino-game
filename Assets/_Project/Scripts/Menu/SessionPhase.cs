namespace Domino.Menu
{
    // Online Session lifecycle (Step 9, section 52). Enum, not string comparison, so UI/Host
    // authority/Late Join checks are all driven by a single well-defined state machine.
    public enum SessionPhase
    {
        Room,
        StageSelect,
        StartingGame,
        InGame
    }
}
