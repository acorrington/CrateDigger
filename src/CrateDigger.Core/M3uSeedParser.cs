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

            seeds.Add(new TrackRef(entryArtist ?? string.Empty, entryTitle ?? string.Empty));
            artist = null;
            title = null;
        }

        return seeds;
    }

    /// <summary>"Depeche Mode - Policy Of Truth.mp3" → (artist, title); null when no separator.</summary>
    private static (string Artist, string Title)? TrackRefFromFileName(string pathLine)
    {
        var fileName = Path.GetFileName(pathLine.Trim());
        if (fileName.Length == 0)
            return null;
        fileName = Path.ChangeExtension(fileName, string.Empty).TrimEnd('.');

        var sep = fileName.IndexOf(" - ", StringComparison.Ordinal);
        if (sep <= 0 || sep >= fileName.Length - 3)
            return null;

        return (fileName[..sep].Trim(), fileName[(sep + 3)..].Trim());
    }
}