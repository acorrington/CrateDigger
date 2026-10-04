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
            "3. Every track must exist in the catalog verbatim (artist and title spelling as given).";

        return new LlmRequest(
            new LlmMessage("system", SystemRules),
            new LlmMessage("user", user));
    }

    private static string BuildFullCatalog(IReadOnlyList<LibraryTrack> catalog)
    {
        var lines = new List<string>(catalog.Count);
        for (var i = 0; i < catalog.Count; i++)
            lines.Add($"{i + 1}. {catalog[i].Track.Display}");
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
}