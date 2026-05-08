namespace TomestonePhone.Spelling;

public sealed record SpellCheckSuggestion(string Text, int Distance, long Count);

public sealed record SpellCheckIssue(int StartIndex, int Length, string OriginalText, IReadOnlyList<SpellCheckSuggestion> Suggestions);

public sealed record SpellCheckAnalysis(IReadOnlyList<SpellCheckIssue> Issues)
{
    public static SpellCheckAnalysis Empty { get; } = new([]);
}
