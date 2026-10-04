namespace CrateDigger.Core.Models;

/// <summary>A suggestion from the LLM that resolved to a real library track.</summary>
public sealed record ResolvedTrack(TrackRef Suggestion, LibraryTrack Match, double Score);

/// <summary>A suggestion from the LLM that could not be confidently matched.</summary>
public sealed record UnresolvedTrack(TrackRef Suggestion, double BestScore);

/// <summary>Outcome of fuzzy-matching an LLM tracklist against the library.</summary>
public sealed record ResolveReport(
    IReadOnlyList<ResolvedTrack> Matched,
    IReadOnlyList<UnresolvedTrack> Unmatched)
{
    public static ResolveReport Empty { get; } =
        new(Array.Empty<ResolvedTrack>(), Array.Empty<UnresolvedTrack>());

    public int MatchedCount => Matched.Count;
    public int UnmatchedCount => Unmatched.Count;
    public int RequestedCount => Matched.Count + Unmatched.Count;

    public double MatchRate
    {
        get
        {
            var total = Matched.Count + Unmatched.Count;
            return total == 0 ? 0 : (double)Matched.Count / total;
        }
    }
}