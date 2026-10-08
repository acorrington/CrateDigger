using CrateDigger.Core.Models;

namespace CrateDigger.Core.Llm;

/// <summary>
/// Builds chat requests that constrain the LLM to the user's actual library.
///
/// Two strategies:
///  * Small libraries (≤ <see cref="FullListLimit"/> tracks): embed the complete track list.
///  * Large libraries: first ask the model to shortlist relevant artists, then embed
///    only those artists' tracks (see <see cref="PlaylistGenerator"/> for the two-step flow).
/// </summary>
public sealed class PromptBuilder
{
    /// <summary>Maximum tracks embedded verbatim in a single request.</summary>
    public int FullListLimit { get; init; } = 300;

    private const string SystemRules =
        "You are CrateDigger, a meticulous DJ and music curator. " +
        "You build playlists strictly from tracks that appear in the user's own music library — you never invent tracks. " +
        "You answer with JSON only: no markdown, no commentary, no explanations before or after the JSON.";

    /// <summary>Step A for large libraries: ask the model which artists fit the vibe.</summary>
    public LlmRequest BuildArtistSelectionRequest(string userPrompt, IReadOnlyList<LibraryTrack> library)
    {
        var artists = library
            .Select(t => t.Track.Artist)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var user =
            $"Playlist request: {userPrompt}\n\n" +
            $"Here are the artists available in my library ({artists.Count}):\n" +
            BuildNumberedList(artists) + "\n\n" +
            "Reply with JSON only in exactly this shape:\n" +
            "{\"artists\": [\"Artist name\", \"...\"]}\n" +
            "Pick the artists most relevant to the request (typically 5-25 artists).";

        return new LlmRequest(
            new LlmMessage("system", SystemRules + " You only select artist names that appear in the provided list."),
            new LlmMessage("user", user));
    }

    /// <summary>Step B (or the only step for small libraries): ask for an actual tracklist.</summary>
    public LlmRequest BuildTrackRequest(string userPrompt, IReadOnlyList<LibraryTrack> catalog, int maxTracks)
    {
        var catalogSection = catalog.Count <= FullListLimit
            ? BuildFullCatalog(catalog)
            : BuildSummaryCatalog(catalog);

        var user =
            $"Playlist request: {userPrompt}\n\n" +
            $"Target length: about {maxTracks} tracks.\n\n" +
            catalogSection + "\n\n" +
            "Reply with JSON only in exactly this shape:\n" +
            "{\"name\": \"Short playlist name\", \"tracks\": [{\"artist\": \"...\", \"title\": \"...\"}]}\n" +
            "Rules:\n" +
            "1. Only use tracks that appear in the catalog above — never invent or approximate titles.\n" +
            $"2. Return up to {maxTracks} tracks ordered as the best openers first.\n" +
            "3. Every track must exist in the catalog verbatim (artist and title spelling as given).\n" +
            "4. \"name\" is a short evocative playlist title (2-5 words, no quotes) that captures " +
            "the mood/era of these picks — e.g. \"Midnight Synth Drive\", \"Backyard BBQ Burners\". " +
            "Never a date, filename, or the words 'Untitled'.";

        return new LlmRequest(
            new LlmMessage("system", SystemRules),
            new LlmMessage("user", user));
    }

    private string BuildFullCatalog(IReadOnlyList<LibraryTrack> catalog)
    {
        var lines = new List<string>(catalog.Count);
        for (var i = 0; i < catalog.Count; i++)
            lines.Add($"{i + 1}. {CatalogLine(catalog[i].Track)}");
        return $"My library tracks ({catalog.Count}):\n" + string.Join('\n', lines);
    }

    /// <summary>Genre + artist overview used when the full list would blow the context window.</summary>
    internal static string BuildSummaryCatalog(IReadOnlyList<LibraryTrack> catalog)
    {
        var genres = catalog
            .SelectMany(t => string.IsNullOrWhiteSpace(t.Track.Genre)
                ? Array.Empty<string>()
                : t.Track.Genre.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(40)
            .Select(g => $"{g.Key} ({g.Count()})");

        var artists = catalog
            .GroupBy(t => t.Track.Artist, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(300)
            .Select(g => g.Key);

        return string.Join('\n', new[]
        {
            $"My library summary ({catalog.Count} tracks total — too large to list in full):",
            "",
            "Genres: " + string.Join(", ", genres),
            "",
            "Artists: " + BuildNumberedList(artists.ToList()),
        });
    }

    private static string BuildNumberedList(IReadOnlyList<string> items)
        => string.Join('\n', items.Select((item, i) => $"{i + 1}. {item}"));

    /// <summary>Catalog line rendering: plain display, or enriched when IncludeCatalogDetails.</summary>
    internal string CatalogLine(TrackRef track)
        => IncludeCatalogDetails ? $"{track.Display}{track.DetailSuffix}" : track.Display;

    // ------------------------------------------------------------------
    // Seed-playlist trigger (v0.2.0): no free-text prompt — the seed songs
    // ARE the brief. The caller passes a library with the seeds EXCLUDED, so
    // the model physically cannot suggest them back.
    // ------------------------------------------------------------------

    /// <summary>Maximum tracks embedded in the prompt (long lists add noise, not signal).</summary>
    public int MaxSeedTracks { get; init; } = 10;

    /// <summary>
    /// v0.3.0: append (year) and [genre] detail to CATALOG lines when the fields exist.
    /// Off for the proven audio path; on for video mode, where year/genre is often the
    /// only disambiguation beyond the title (music videos lack album metadata).
    /// </summary>
    public bool IncludeCatalogDetails { get; init; }

    /// <summary>Build the "belongs alongside these" prompt from user-chosen seed songs.</summary>
    public string BuildSeedPrompt(IReadOnlyList<TrackRef> seeds)
    {
        var shown = seeds.Take(MaxSeedTracks).ToList();
        var seedLines = string.Join("; ", shown.Select(s => s.Display));
        var listed = string.Join('\n', shown.Select(s => $"- {s.Display}{s.DetailSuffix}"));
        var more = seeds.Count > shown.Count
            ? $"\n(and {seeds.Count - shown.Count} more seeds not listed)"
            : string.Empty;

        return
            $"These tracks from my library are my starting point: {seedLines}.\n\n" +
            $"Seed tracks in detail:\n{listed}{more}\n\n" +
            "Pick OTHER tracks from the catalog above that belong alongside them — " +
            "shared vibe, era, energy, genre; a natural continuation of these seeds, " +
            "not the same songs again. The seed tracks are intentionally absent from " +
            "the catalog; never try to include them.";
    }
}