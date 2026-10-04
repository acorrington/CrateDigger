using CrateDigger.Core.Llm;
using CrateDigger.Core.Matching;
using CrateDigger.Core.Models;

namespace CrateDigger.Core;

/// <summary>
/// End-to-end pipeline (library → LLM → parse → fuzzy-match), server-agnostic:
///
///   AnalyzingLibrary → [optional artist shortlist when library is huge] →
///   Thinking → MatchingTracks → Completed | Failed
/// </summary>
public sealed class PlaylistGenerator
{
    private readonly ILlmClient _llm;
    private readonly PromptBuilder _promptBuilder;
    private readonly FuzzyMatcher _matcher;

    public PlaylistGenerator(ILlmClient llm, PromptBuilder? promptBuilder = null, FuzzyMatcher? matcher = null)
    {
        _llm = llm;
        _promptBuilder = promptBuilder ?? new PromptBuilder();
        _matcher = matcher ?? new FuzzyMatcher();
    }

    public async Task<GenerationResult> GenerateAsync(
        string prompt,
        IReadOnlyList<LibraryTrack> library,
        LlmOptions llmOptions,
        GenerationOptions options,
        IProgress<GenerationStage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Playlist prompt must not be empty.", nameof(prompt));
        if (library.Count == 0)
            throw new InvalidOperationException("The music library contains no tracks.");

        progress?.Report(GenerationStage.AnalyzingLibrary);

        // Large library: shortlist relevant artists first, then work with their tracks.
        var workingSet = library;
        if (library.Count > _promptBuilder.FullListLimit)
        {
            try
            {
                var selectionRequest = _promptBuilder.BuildArtistSelectionRequest(prompt, library);
                var selectionRaw = await _llm.CompleteAsync(selectionRequest, llmOptions, cancellationToken)
                    .ConfigureAwait(false);
                var artists = ResponseParser.ParseArtists(selectionRaw);

                var artistSet = new HashSet<string>(artists, StringComparer.OrdinalIgnoreCase);
                var subset = library.Where(t => artistSet.Contains(t.Track.Artist)).ToList();

                // Fall back to the full library if the model's shortlist matched nothing.
                if (subset.Count > 0)
                    workingSet = subset;
            }
            catch (LlmParseException)
            {
                // Shortlist is an optimization — degrade to the summary path instead of failing.
            }
        }

        progress?.Report(GenerationStage.Thinking);

        var request = _promptBuilder.BuildTrackRequest(prompt, workingSet, options.MaxTracks);
        var raw = await _llm.CompleteAsync(request, llmOptions, cancellationToken).ConfigureAwait(false);
        var tracklist = ResponseParser.ParseTracklist(raw);

        progress?.Report(GenerationStage.MatchingTracks);

        // Match against the *full* library, not just the working set — shortlist misses should
        // still resolve if the track exists anywhere.
        var report = _matcher.ResolveAll(tracklist.Tracks, library, options.MatchThreshold);

        var playlistTracks = report.Matched
            .OrderByDescending(m => m.Score)
            .Select(m => m.Match)
            .ToList();

        if (playlistTracks.Count == 0)
            throw new InvalidOperationException(
                $"None of the {report.RequestedCount} suggested tracks could be matched to the library. " +
                "Try a different prompt, or lower the match threshold.");

        return new GenerationResult(tracklist.PlaylistName, report, playlistTracks);
    }
}