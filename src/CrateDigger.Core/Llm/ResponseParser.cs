using System.Text.Json;
using System.Text.RegularExpressions;
using CrateDigger.Core.Models;

namespace CrateDigger.Core.Llm;

/// <summary>
/// Extracts structured data from raw LLM text, tolerating the usual mess:
/// markdown fences, leading/trailing prose, wrong casing, string-form tracks, root arrays.
/// Failures throw <see cref="LlmParseException"/> (spec UT-002).
/// </summary>
public static class ResponseParser
{
    /// <summary>Parse the tracklist step: {"name": "...", "tracks": [{"artist","title"}]}.</summary>
    public static LlmTracklist ParseTracklist(string raw)
    {
        var json = ExtractJson(raw, out var trailingNotes);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? playlistName = null;
            IReadOnlyList<TrackRef> tracks;

            if (root.ValueKind == JsonValueKind.Array)
            {
                tracks = ParseTrackArray(root);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                playlistName = GetFirstString(root, "name", "playlist", "playlist_name", "playlistname", "title");
                tracks = root.TryGetPropertyInsensitive("tracks", out var tracksEl) && tracksEl.ValueKind == JsonValueKind.Array
                    ? ParseTrackArray(tracksEl)
                    : Array.Empty<TrackRef>();
            }
            else
            {
                throw new LlmParseException($"Unexpected JSON root kind '{root.ValueKind}'.", raw);
            }

            tracks = DeDupe(tracks);
            if (tracks.Count == 0)
                throw new LlmParseException("LLM JSON contained no usable tracks.", raw);

            return new LlmTracklist(playlistName, tracks, trailingNotes);
        }
        catch (JsonException ex)
        {
            // Truncated response (hit max_tokens mid-JSON) — salvage complete track
            // objects instead of failing the whole generation.
            var salvaged = TrySalvageTracklist(raw);
            if (salvaged != null)
                return salvaged;
            throw new LlmParseException($"Could not parse LLM JSON: {ex.Message}", raw, ex);
        }
    }

    /// <summary>Parse the artist-selection step: {"artists": ["...", ...]}.</summary>
    public static IReadOnlyList<string> ParseArtists(string raw)
    {
        var json = ExtractJson(raw, out _);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array)
                array = root;
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetPropertyInsensitive("artists", out array) &&
                     array.ValueKind == JsonValueKind.Array)
            { /* found */ }
            else
                throw new LlmParseException("JSON did not contain an 'artists' array.", raw);

            var result = array.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? string.Empty)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (result.Count == 0)
                throw new LlmParseException("LLM returned an empty artist selection.", raw);
            return result;
        }
        catch (JsonException ex)
        {
            var salvaged = TrySalvageArtists(raw);
            if (salvaged != null)
                return salvaged;
            throw new LlmParseException($"Could not parse LLM artist JSON: {ex.Message}", raw, ex);
        }
    }

    /// <summary>
    /// Pull the first JSON object/array out of arbitrary text:
    /// handles ```json fences, leading chatter, and trailing commentary (UT-002).
    /// </summary>
    internal static string ExtractJson(string raw, out string? trailingNotes)
    {
        trailingNotes = null;
        if (string.IsNullOrWhiteSpace(raw))
            throw new LlmParseException("LLM returned an empty response.", raw);

        var text = raw.Trim();

        // Strip a fenced code block if present: ```json ... ``` (language tag optional).
        var fenceMatch = System.Text.RegularExpressions.Regex.Match(
            text, @"```(?:json|JSON)?\s*(.*?)\s*```",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (fenceMatch.Success)
        {
            var fenced = fenceMatch.Groups[1].Value.Trim();
            var before = text[..fenceMatch.Index].Trim();
            var after = text[(fenceMatch.Index + fenceMatch.Length)..].Trim();
            if (before.Length > 0 && LooksLikeJsonStart(before))
                text = before; // unusual: JSON started before the fence
            else
                text = fenced;
            trailingNotes = string.Join("\n", new[] { before, after }.Where(s => s.Length > 0)) is { Length: > 0 } joined
                ? joined : null;
            if (text.Length == 0)
                throw new LlmParseException("Fenced block was empty.", raw);
            return text;
        }

        // No fence: slice from the first '{' or '[' to its matching close.
        var start = IndexOfJsonStart(text);
        if (start < 0)
            throw new LlmParseException("No JSON object or array found in LLM response.", raw);

        var end = IndexOfJsonEnd(text, start);
        var json = text[start..(end < 0 ? text.Length : end + 1)];

        var beforeJson = text[..start].Trim();
        var afterJson = end < 0 ? string.Empty : text[(end + 1)..].Trim();
        var notes = string.Join("\n", new[] { beforeJson, afterJson }.Where(s => s.Length > 0));
        trailingNotes = notes.Length > 0 ? notes : null;

        return json;
    }

    private static bool LooksLikeJsonStart(string s)
        => s.Length > 0 && (s[0] == '{' || s[0] == '[');

    private static int IndexOfJsonStart(string text)
    {
        var obj = text.IndexOf('{');
        var arr = text.IndexOf('[');
        return (obj, arr) switch
        {
            (-1, -1) => -1,
            (-1, _) => arr,
            (_, -1) => obj,
            _ => Math.Min(obj, arr),
        };
    }

    /// <summary>Scan forward respecting strings/escapes to find the close matching the opener at <paramref name="start"/>.</summary>
    private static int IndexOfJsonEnd(string text, int start)
    {
        var open = text[start];
        var close = open == '{' ? '}' : ']';
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') { inString = true; continue; }
            if (c == open || (open == '{' && c == '{') || (open == '[' && c == '[')) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return i;
            }
            else if ((open == '{' && c == '[') || (open == '[' && c == '{'))
            {
                // Nested mixed container: find its end and skip over it.
                var nestedEnd = IndexOfJsonEnd(text, i);
                if (nestedEnd < 0) return -1;
                i = nestedEnd;
            }
        }
        return -1;
    }

    private static IReadOnlyList<TrackRef> ParseTrackArray(JsonElement array)
    {
        var tracks = new List<TrackRef>();
        foreach (var item in array.EnumerateArray())
        {
            var track = ParseTrack(item);
            if (track is not null)
                tracks.Add(track);
        }
        return tracks;
    }

    private static TrackRef? ParseTrack(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            // Bare "Artist - Title" form.
            var s = item.GetString();
            return ParseDisplayString(s);
        }

        if (item.ValueKind != JsonValueKind.Object)
            return null;

        var title = GetFirstString(item, "title", "name", "song", "track");
        var artist = GetFirstString(item, "artist", "artists", "artist_name") ?? string.Empty;
        var album = GetFirstString(item, "album");
        var genre = GetFirstString(item, "genre");

        if (string.IsNullOrWhiteSpace(title))
            return null;

        // "artists" sometimes arrives as an array — flatten to a readable string.
        if (item.TryGetPropertyInsensitive("artists", out var artistsEl) && artistsEl.ValueKind == JsonValueKind.Array)
            artist = string.Join(", ", artistsEl.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.String)
                .Select(a => a.GetString()));

        if (string.IsNullOrWhiteSpace(artist) && title.Contains(" - "))
            return ParseDisplayString(title);

        return new TrackRef(artist.Trim(), title.Trim(), album?.Trim(), genre?.Trim());
    }

    private static TrackRef? ParseDisplayString(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var idx = s.IndexOf(" - ", StringComparison.Ordinal);
        if (idx <= 0) return new TrackRef(string.Empty, s.Trim());
        return new TrackRef(s[..idx].Trim(), s[(idx + 3)..].Trim());
    }

    private static string? GetFirstString(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetPropertyInsensitive(name, out var el)) continue;
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                    break;
                case JsonValueKind.Array:
                    var first = el.EnumerateArray().FirstOrDefault(e => e.ValueKind == JsonValueKind.String);
                    if (first.ValueKind == JsonValueKind.String)
                    {
                        var v = first.GetString();
                        if (!string.IsNullOrWhiteSpace(v)) return v;
                    }
                    break;
            }
        }
        return null;
    }

    private static bool TryGetPropertyInsensitive(this JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    // ------------------------------------------------------------------
    // Truncation salvage: when the model hits the output token cap mid-JSON,
    // the response dies with a JsonException but earlier entries are complete
    // and usable. Real case (v0.1.5): payload ended at `"artist]` mid-key —
    // salvaging kept the 3 finished tracks instead of failing the job.
    // ------------------------------------------------------------------

    /// <summary>Best-effort tracklist recovery from a truncated/malformed payload; null when nothing usable.</summary>
    internal static LlmTracklist? TrySalvageTracklist(string raw)
    {
        try
        {
            var json = ExtractJson(raw, out var notes);

            var nameMatch = Regex.Match(json, "\"name\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            var name = nameMatch.Success ? UnescapeJsonString(nameMatch.Groups[1].Value) : null;

            var tracks = new List<TrackRef>();
            foreach (var objText in CompleteObjects(json))
            {
                try
                {
                    using var doc = JsonDocument.Parse(objText);
                    var el = doc.RootElement;
                    if (el.ValueKind != JsonValueKind.Object)
                        continue;
                    // Skip container objects (the truncated root / wrapper arrays).
                    if (el.TryGetPropertyInsensitive("tracks", out _) ||
                        el.TryGetPropertyInsensitive("artists", out _))
                        continue;
                    var track = ParseTrack(el);
                    if (track != null)
                        tracks.Add(track);
                }
                catch (JsonException)
                {
                    // Partial nested object — skip, keep scanning.
                }
            }

            var deduped = DeDupe(tracks);
            return deduped.Count == 0 ? null : new LlmTracklist(name, deduped, notes);
        }
        catch (LlmParseException)
        {
            return null;
        }
    }

    /// <summary>Best-effort artist-list recovery from a truncated payload.</summary>
    internal static IReadOnlyList<string>? TrySalvageArtists(string raw)
    {
        try
        {
            var json = ExtractJson(raw, out _);
            var strings = Regex.Matches(json, "\"((?:[^\"\\\\]|\\\\.)*)\"")
                .Select(m => UnescapeJsonString(m.Groups[1].Value))
                .Where(s => !string.IsNullOrWhiteSpace(s) &&
                            !string.Equals(s, "artists", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return strings.Count == 0 ? null : strings;
        }
        catch (LlmParseException)
        {
            return null;
        }
    }

    /// <summary>
    /// Enumerate complete balanced {...} substrings. When a container is unterminated
    /// (truncation), advance one char instead of stopping so completed NESTED objects
    /// inside it are still found.
    /// </summary>
    private static IEnumerable<string> CompleteObjects(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '{')
            {
                i++;
                continue;
            }

            var end = IndexOfJsonEnd(text, i);
            if (end < 0)
            {
                i++; // truncated at/past here — look deeper for completed nested objects
                continue;
            }

            yield return text.Substring(i, end - i + 1);
            i = end + 1;
        }
    }

    private static string UnescapeJsonString(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>("\"" + value.Replace("\"", "\\\"") + "\"") ?? value;
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static IReadOnlyList<TrackRef> DeDupe(IReadOnlyList<TrackRef> tracks)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TrackRef>(tracks.Count);
        foreach (var t in tracks)
        {
            var key = $"{t.Artist}|{t.Title}";
            if (seen.Add(key))
                result.Add(t);
        }
        return result;
    }
}