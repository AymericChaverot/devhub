namespace DevHub.Core.Services;

/// <summary>
/// Ranks how well a typed query matches a piece of text, the way editor command palettes do:
/// "dvhb" finds "devhub", "ptc" finds "Project Template Catalog". Case-insensitive.
/// </summary>
public static class FuzzyMatcher
{
    private const int ExactScore = 1000;
    private const int PrefixScore = 900;
    private const int WordStartSubstringScore = 800;
    private const int SubstringScore = 700;
    private const int SubsequenceScore = 100;

    private const int WordStartBonus = 10;
    private const int ConsecutiveBonus = 5;

    /// <summary>
    /// Scores <paramref name="text"/> against <paramref name="query"/>; higher is better, null when
    /// it doesn't match. Space-separated words of the query must all match, in any order.
    /// </summary>
    public static int? Score(string query, string text)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return 0;
        }

        var total = 0;
        foreach (var word in words)
        {
            if (ScoreWord(word, text) is not { } score)
            {
                return null;
            }

            total += score;
        }

        return total / words.Length;
    }

    private static int? ScoreWord(string word, string text)
    {
        if (text.Equals(word, StringComparison.OrdinalIgnoreCase))
        {
            return ExactScore;
        }

        var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        if (index == 0)
        {
            return PrefixScore - (text.Length - word.Length);
        }

        if (index > 0)
        {
            // Prefer a later occurrence that starts a word ("term" in "Open in Terminal").
            var wordStart = FindAtWordStart(word, text, index);
            return wordStart >= 0
                ? WordStartSubstringScore - wordStart
                : SubstringScore - index;
        }

        return ScoreSubsequence(word, text);
    }

    private static int FindAtWordStart(string word, string text, int from)
    {
        for (var i = from; i >= 0; i = text.IndexOf(word, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (IsWordStart(text, i))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Every character of the word appears in order; word starts and runs score extra.</summary>
    private static int? ScoreSubsequence(string word, string text)
    {
        var score = SubsequenceScore;
        var previous = -1;
        foreach (var c in word)
        {
            var found = IndexOfIgnoreCase(text, c, previous + 1);
            if (found < 0)
            {
                return null;
            }

            if (IsWordStart(text, found))
            {
                score += WordStartBonus;
            }
            else if (found == previous + 1)
            {
                score += ConsecutiveBonus;
            }
            else
            {
                score -= found - previous - 1;
            }

            previous = found;
        }

        // Never let gaps push a real match to or below "no match" territory.
        return Math.Max(score, 1);
    }

    private static int IndexOfIgnoreCase(string text, char c, int start)
    {
        var lower = char.ToLowerInvariant(c);
        for (var i = start; i < text.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) == lower)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Start of the text, after a separator, or a camelCase hump ("Template" in "ProjectTemplate").</summary>
    private static bool IsWordStart(string text, int index) =>
        index == 0
        || !char.IsLetterOrDigit(text[index - 1])
        || (char.IsUpper(text[index]) && char.IsLower(text[index - 1]));
}
