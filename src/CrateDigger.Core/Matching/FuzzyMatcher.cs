using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CrateDigger.Core.Models;

namespace CrateDigger.Core.Matching;

/// <summary>
/// Resolves free-text track suggestions to real library entries (spec UT-001).
///
/// Pipeline: normalize (case/diacritics/punctuation/decorations) →
/// score = 0.7 × title similarity + 0.3 × artist similarity (artist neutral if missing) →
/// accept best candidate at/above the threshold.
/// </summary>
public sealed class FuzzyMatcher
{
    /// <summary>Suggestions scoring below this are reported unmatched.</summary>
    public const double DefaultAcceptanceThreshold = 0.75;

    private const double TitleWeight = 0.7;
    private const double ArtistWeight = 0.3;

    public ResolveReport ResolveAll(
        IEnumerable<TrackRef> suggestions,
        IReadOnlyList<LibraryTrack> library,
        double acceptanceThreshold = DefaultAcceptanceThreshold)
    {
        var candidates = new List<(TrackRef Suggestion, LibraryTrack Match, double Score)>();

        foreach (var suggestion in suggestions)
        {
            LibraryTrack? best = null;
            var bestScore = 0.0;

            foreach (var candidate in library)
            {
                var score = Score(suggestion, candidate.Track);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best is not null && bestScore >= acceptanceThreshold)
                candidates.Add((suggestion, best, bestScore));
            else
                candidates.Add((suggestion, best!, bestScore));
        }

        return SelectMatches(candidates, acceptanceThreshold);
    }

    /// <summary>Combined similarity in [0,1] between a suggestion and a library track.</summary>
    public double Score(TrackRef suggestion, TrackRef libraryTrack)
    {
        var titleScore = Similarity(suggestion.Title, libraryTrack.Title);

        var artistScore =
            string.IsNullOrWhiteSpace(suggestion.Artist) || string.IsNullOrWhiteSpace(libraryTrack.Artist)
                ? titleScore // artist unknown on one side → neutral, don't penalize
                : Similarity(suggestion.Artist, libraryTrack.Artist);

        return Math.Clamp(TitleWeight * titleScore + ArtistWeight * artistScore, 0, 1);
    }

    /// <summary>
    /// Max similarity over normalization variants of both strings.
    /// Variant 1 keeps parenthetical/feat text ("(Don't Fear) The Reaper" → "don t fear the reaper");
    /// variant 2 strips it ("the reaper"). Taking the max makes decorations symmetric —
    /// a suggestion that drops or adds parens/feats still matches.
    /// </summary>
    public static double Similarity(string a, string b)
    {
        var best = 0.0;
        foreach (var va in Variants(a))
        {
            foreach (var vb in Variants(b))
            {
                if (va.Length == 0 && vb.Length == 0) { best = 1; continue; }
                if (va.Length == 0 || vb.Length == 0) continue;
                if (va == vb) return 1;
                var score = Math.Max(LevenshteinRatio(va, vb), TokenSetSimilarity(va, vb));
                if (score > best) best = score;
            }
        }
        return best;
    }

    /// <summary>The two comparable forms of a string: full text and decoration-stripped text.</summary>
    internal static IReadOnlyList<string> Variants(string input)
    {
        var full = BasicNormalize(input);
        if (string.IsNullOrEmpty(input)) return new[] { full };

        // Strip decorations from the case/diacritic-normalized text (brackets must still exist
        // for the paren regex to work), then run the remaining normalization.
        var lowered = CaseAndDiacriticsNormalize(input);
        var stripped = Regex.Replace(lowered, @"\s+(feat|ft|featuring|with)\.?\s+.*$", string.Empty);
        stripped = Regex.Replace(stripped, @"\s*[\(\[][^\)\]]*[\)\]]", " ");
        stripped = FinishNormalize(stripped);

        return stripped == full ? new[] { full } : new[] { full, stripped };
    }

    /// <summary>
    /// Lowercase → decompose/remove diacritics → ampersand fix → punctuation to spaces →
    /// collapse whitespace. Parenthetical text is KEPT (see <see cref="Variants"/>).
    /// </summary>
    public static string Normalize(string input) => BasicNormalize(input);

    private static string BasicNormalize(string input)
        => FinishNormalize(CaseAndDiacriticsNormalize(input));

    private static string CaseAndDiacriticsNormalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var s = input.Trim().ToLowerInvariant();

        // Decompose diacritics (é → e + ́) and drop the combining marks.
        s = s.Normalize(NormalizationForm.FormD);
        s = new string(s.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return s.Normalize(NormalizationForm.FormC);
    }

    private static string FinishNormalize(string s)
    {
        // Ampersand as word.
        s = s.Replace("&", " and ");

        // Strip remaining punctuation to spaces.
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');

        // Collapse whitespace.
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>Classic Levenshtein edit distance.</summary>
    public static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    /// <summary>1 − distance / maxLen, clamped to [0,1]. Identical strings → 1.</summary>
    public static double LevenshteinRatio(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1;
        return Math.Clamp(1.0 - (double)LevenshteinDistance(a, b) / maxLen, 0, 1);
    }

    /// <summary>Jaccard similarity over whitespace-separated tokens.</summary>
    public static double TokenSetSimilarity(string a, string b)
    {
        var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ta.Length == 0 && tb.Length == 0) return 1;
        if (ta.Length == 0 || tb.Length == 0) return 0;

        var setA = new HashSet<string>(ta, StringComparer.Ordinal);
        var setB = new HashSet<string>(tb, StringComparer.Ordinal);
        var intersection = setA.Count(setB.Contains);
        var union = setA.Count + setB.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    /// <summary>
    /// Accept the highest-scoring suggestion per library track: when several suggestions
    /// resolve to the same library item, the strongest wins and the losers are reported
    /// unmatched (so a playlist never contains the same track twice).
    /// </summary>
    private static ResolveReport SelectMatches(
        List<(TrackRef Suggestion, LibraryTrack Match, double Score)> candidates,
        double threshold)
    {
        var accepted = new List<ResolvedTrack>();
        var rejected = new List<UnresolvedTrack>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        // Strongest scores first so duplicate targets go to the best suggestion.
        foreach (var group in candidates
                     .Where(c => c.Match is not null && c.Score >= threshold)
                     .OrderByDescending(c => c.Score))
        {
            if (taken.Add(group.Match.Id))
                accepted.Add(new ResolvedTrack(group.Suggestion, group.Match, group.Score));
            else
                rejected.Add(new UnresolvedTrack(group.Suggestion, group.Score));
        }

        // Below-threshold suggestions, in original order.
        foreach (var c in candidates)
        {
            if (c.Score < threshold)
                rejected.Add(new UnresolvedTrack(c.Suggestion, c.Score));
        }

        return new ResolveReport(accepted, rejected);
    }
}