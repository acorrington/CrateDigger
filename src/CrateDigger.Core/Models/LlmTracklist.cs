namespace CrateDigger.Core.Models;

/// <summary>Structured result parsed from the LLM's JSON response.</summary>
public sealed record LlmTracklist(
    string? PlaylistName,
    IReadOnlyList<TrackRef> Tracks,
    string? Notes = null)
{
    public static LlmTracklist Empty { get; } = new(null, Array.Empty<TrackRef>());
}