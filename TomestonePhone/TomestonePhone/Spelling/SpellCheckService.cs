using System.Text.RegularExpressions;

namespace TomestonePhone.Spelling;

public sealed class SpellCheckService
{
    private static readonly Regex WordPattern = new(@"\b[\p{L}][\p{L}'’\-]*\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Lazy<EngineState> engineState;

    public SpellCheckService()
    {
        this.engineState = new Lazy<EngineState>(LoadEngine, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => this.engineState.Value.Engine is not null;

    public string? AvailabilityMessage => this.engineState.Value.Message;

    public SpellCheckAnalysis Analyze(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return SpellCheckAnalysis.Empty;
        }

        var state = this.engineState.Value;
        if (state.Engine is null)
        {
            return SpellCheckAnalysis.Empty;
        }

        var issues = new List<SpellCheckIssue>();
        foreach (Match match in WordPattern.Matches(text))
        {
            if (!match.Success)
            {
                continue;
            }

            var token = match.Value;
            if (!ShouldCheckToken(text, match.Index, token))
            {
                continue;
            }

            var normalized = token.ToLowerInvariant();
            var suggestions = state.Engine.Lookup(normalized, SymSpell.Verbosity.Closest, GetLookupDistance(normalized));
            if (suggestions.Count == 0 || suggestions[0].distance <= 0)
            {
                continue;
            }

            if (suggestions[0].distance > 1 && !char.Equals(char.ToLowerInvariant(suggestions[0].term[0]), char.ToLowerInvariant(normalized[0])))
            {
                continue;
            }

            var mappedSuggestions = suggestions
                .Where(static suggestion => suggestion.distance > 0)
                .Select(suggestion => new SpellCheckSuggestion(ApplyWordCasing(token, suggestion.term), suggestion.distance, suggestion.count))
                .DistinctBy(static suggestion => suggestion.Text)
                .Take(5)
                .ToArray();

            if (mappedSuggestions.Length == 0)
            {
                continue;
            }

            issues.Add(new SpellCheckIssue(match.Index, match.Length, token, mappedSuggestions));
        }

        return issues.Count == 0 ? SpellCheckAnalysis.Empty : new SpellCheckAnalysis(issues);
    }

    private static EngineState LoadEngine()
    {
        var dictionaryPath = ResolveDictionaryPath();
        if (string.IsNullOrWhiteSpace(dictionaryPath))
        {
            return new EngineState(null, "English dictionary asset is missing.");
        }

        try
        {
            var engine = new SymSpell(82_765, 2);
            if (!engine.LoadDictionary(dictionaryPath, 0, 1))
            {
                return new EngineState(null, "English dictionary failed to load.");
            }

            return new EngineState(engine, null);
        }
        catch (Exception ex)
        {
            return new EngineState(null, ex.Message);
        }
    }

    private static string? ResolveDictionaryPath()
    {
        const string dictionaryFileName = "frequency_dictionary_en_82_765.txt";
        var candidateRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidateRoot(candidateRoots, AppContext.BaseDirectory);
        AddCandidateRoot(candidateRoots, Path.GetDirectoryName(typeof(SpellCheckService).Assembly.Location));
        AddCandidateRoot(candidateRoots, Directory.GetCurrentDirectory());
        AddCandidateRoot(candidateRoots, Path.GetDirectoryName(Environment.ProcessPath));

        foreach (var root in candidateRoots)
        {
            var current = root;
            for (var depth = 0; depth < 4 && !string.IsNullOrWhiteSpace(current); depth++)
            {
                var candidate = Path.Combine(current, "assets", "spelling", dictionaryFileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = Path.GetDirectoryName(current);
            }
        }

        return null;
    }

    private static void AddCandidateRoot(HashSet<string> roots, string? root)
    {
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            roots.Add(root);
        }
    }

    private static bool ShouldCheckToken(string fullText, int tokenIndex, string token)
    {
        if (token.Length < 3 || token.Any(char.IsDigit))
        {
            return false;
        }

        if (HasUppercaseAfterFirstLetter(token) || token.All(char.IsUpper))
        {
            return false;
        }

        if (char.IsUpper(token[0]) && !IsSentenceStart(fullText, tokenIndex))
        {
            return false;
        }

        var segmentStart = tokenIndex;
        while (segmentStart > 0 && !char.IsWhiteSpace(fullText[segmentStart - 1]))
        {
            segmentStart--;
        }

        var segmentEnd = tokenIndex + token.Length;
        while (segmentEnd < fullText.Length && !char.IsWhiteSpace(fullText[segmentEnd]))
        {
            segmentEnd++;
        }

        var segment = fullText[segmentStart..segmentEnd];
        if (segment.Contains("://", StringComparison.Ordinal) ||
            segment.Contains('@') ||
            segment.Contains('#') ||
            segment.Contains('/') ||
            segment.Contains('\\') ||
            segment.Contains('_') ||
            segment.Contains('.'))
        {
            return false;
        }

        return true;
    }

    private static bool HasUppercaseAfterFirstLetter(string token)
    {
        for (var index = 1; index < token.Length; index++)
        {
            if (char.IsUpper(token[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSentenceStart(string fullText, int tokenIndex)
    {
        for (var index = tokenIndex - 1; index >= 0; index--)
        {
            var character = fullText[index];
            if (char.IsWhiteSpace(character) || character is '"' or '\'' or '(' or ')' or '[' or ']')
            {
                continue;
            }

            return character is '.' or '!' or '?' or '\n' or '\r';
        }

        return true;
    }

    private static int GetLookupDistance(string token)
    {
        return token.Length >= 6 ? 2 : 1;
    }

    private static string ApplyWordCasing(string source, string suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return suggestion;
        }

        if (source.All(char.IsUpper))
        {
            return suggestion.ToUpperInvariant();
        }

        if (!char.IsUpper(source[0]))
        {
            return suggestion;
        }

        return char.ToUpperInvariant(suggestion[0]) + suggestion[1..];
    }

    private sealed record EngineState(SymSpell? Engine, string? Message);
}
