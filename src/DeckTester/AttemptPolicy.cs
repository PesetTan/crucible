namespace DeckTester;

public static class AttemptPolicy
{
    public const int MaximumFailures=4;
    public static bool CanResume(GameSlot slot)=>slot.Attempts.Count(a=>a.Status=="failed")<MaximumFailures;
}
