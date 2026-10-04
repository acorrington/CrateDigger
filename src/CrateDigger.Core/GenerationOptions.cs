using CrateDigger.Core.Models;

namespace CrateDigger.Core;

/// <summary>User-facing progress states surfaced to the config page (spec §3.1).</summary>
public enum GenerationStage
{
    Queued,
    AnalyzingLibrary,
    Thinking,
    MatchingTracks,
    CreatingPlaylist,
    Completed,
    Failed,
}

/// <summary>Options controlling one generation run.</summary>
public sealed record GenerationOptions
{
    public int MaxTracks { get; init; } = 30;
    public double MatchThreshold { get; init; } = FuzzyMatchingThreshold;

    // Alias kept out of the public docs; single source of truth is the matcher default.
    private const double FuzzyMatchingThreshold = Matching.FuzzyMatcher.DefaultAcceptanceThreshold;
}

/// <summary>Final output of a generation run, ready for playlist creation.</summary>
public sealed record GenerationResult(
    string? PlaylistName,
    ResolveReport Report,
    IReadOnlyList<LibraryTrack> PlaylistTracks)
{
    public int RequestedCount => Report.MatchedCount + Report.UnmatchedCount;
}