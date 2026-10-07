using CrateDigger.Core.Models;

namespace CrateDigger.Core;

/// <summary>
/// Parses Emby's playlist m3u format into seed track references.
///
/// Emby writes m3u entries like:
///   #EXTALB:Album
///   #EXTART:Depeche Mode
///   #EXTINF:293,Policy Of Truth
///   ..\relative\path\to\file.mp3
///
/// Resolution deliberately IGNORES the (relative, format-dependent) path lines —
/// seed identity comes from the ART/TITLE metadata and is fuzzy-matched against
/// the library catalog by the caller, so path quirks can never break the flow.
/// </summary>
public static class M3uSeedParser
{
    /// <summary>Extract seed tracks from m3u lines; falls back to filename parsing.</summary>
    public static IReadOnlyList<TrackRef> ParseSeeds(IEnumerable<string> lines)
    {
        var seeds = new List<TrackRef>();

        string? artist = null;
        string? title = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("#EXTART:", StringComparison.OrdinalIgnoreCase))
            {
                artist = line[8..].Trim();
                continue;
            }

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                // "#EXTINF:293,Title Here" — title follows the first comma.
                var comma = line.IndexOf(',');
                title = comma >= 0 && comma < line.Length - 1
                    ? line[(comma + 1)..].Trim()
                    : null;
                continue;
            }

            if (line.StartsWith('#'))
                continue; // #EXTM3U / #PLAYLIST / #EXTALB / comments

            // Path line completes one entry.
            var entryTitle = title;
            var entryArtist = artist;
            if (string.IsNullOrWhiteSpace(entryTitle))
            {
                var fromFile = TrackRefFromFileName(line);
                if (fromFile == null)
                {
                    artist = null;
                    title = null;
                    continue; // unparseable — skip entry
                }
                entryArtist = fromFile.Value.Artist;
                entryTitle = fromFile.Value.Title;
            }

            // v0.3.0 (video m3u reality: NO #EXTART, titles often "Artist - Title ..."):
            //  - no artist recorded -> split the title at the FIRST " - "
            //  - artist recorded but title repeats it -> strip the "Artist - " prefix
            //    (prevents "38 Special - 38 Special - Hold On Loosely" in prompts)
            //  - no artist AND title begins with an artist prefix repeated in the
            //    filename path -> strip the leading "Artist - " from the title
            entryTitle = entryTitle ?? string.Empty;
            entryArtist = entryArtist ?? string.Empty;
            if (entryArtist.Length == 0)
            {
                var split = entryTitle.IndexOf(" - ", StringComparison.Ordinal);
                if (split > 0 && split < entryTitle.Length - 3)
                {
                    entryArtist = entryTitle[..split].Trim();
                    entryTitle = entryTitle[(split + 3)..].Trim();
                }
            }
            else if (entryTitle.StartsWith(entryArtist + " - ", StringComparison.OrdinalIgnoreCase))
            {
                entryTitle = entryTitle[(entryArtist.Length + 3)..].Trim();
            }

            // Strip a leading "Artist - Artist - Title" double prefix:
            // "Foreigner - Foreigner - I Want To Know..." -> "Foreigner - I Want To Know..."
            var secondSplit = entryTitle.IndexOf(" - ", StringComparison.Ordinal);
            if (entryArtist.Length > 0 &&
                secondSplit > 0 &&
                entryTitle[..secondSplit].Equals(entryArtist, StringComparison.OrdinalIgnoreCase))
            {
                entryTitle = entryTitle[(secondSplit + 3)..].Trim();
            }

            seeds.Add(new TrackRef(entryArtist, entryTitle));
            artist = null;
            title = null;
        }

        return seeds;
    }

    /// <summary>"Depeche Mode - Policy Of Truth.mp3" → (artist, title); null when no separator.
    /// Splits on both separators manually — Path.GetFileName is NOT portable (on Linux
    /// '\' is not a separator, which broke this test in CI while passing on Windows).</summary>
    private static (string Artist, string Title)? TrackRefFromFileName(string pathLine)
    {
        var fileName = pathLine.Trim();
        var lastSep = Math.Max(fileName.LastIndexOf('\\'), fileName.LastIndexOf('/'));
        if (lastSep >= 0)
            fileName = fileName[(lastSep + 1)..];

        var dot = fileName.LastIndexOf('.');
        if (dot > 0)
            fileName = fileName[..dot];
        if (fileName.Length == 0)
            return null;

        var sep = fileName.IndexOf(" - ", StringComparison.Ordinal);
        if (sep <= 0 || sep >= fileName.Length - 3)
            return null;

        return (fileName[..sep].Trim(), fileName[(sep + 3)..].Trim());
    }
}