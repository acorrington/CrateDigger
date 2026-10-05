using CrateDigger.Core.Llm;

namespace CrateDigger.Core.Tests;

/// <summary>Spec §4.1 UT-002: robust parsing of malformed / unexpected LLM responses.</summary>
public class ResponseParserTests
{
    [Fact]
    public void Ut002_JsonWrappedInFencesAndProse_Parses()
    {
        const string raw = """
            Sure! Here's your playlist:
            ```json
            {"name": "Rainy Night", "tracks": [
              {"artist": "The Beatles", "title": "Norwegian Wood"}
            ]}
            ```
            Enjoy!
            """;

        var result = ResponseParser.ParseTracklist(raw);

        Assert.Equal("Rainy Night", result.PlaylistName);
        var track = Assert.Single(result.Tracks);
        Assert.Equal("The Beatles", track.Artist);
        Assert.Equal("Norwegian Wood", track.Title);
    }

    [Fact]
    public void JsonEmbeddedInProseWithoutFences_Parses()
    {
        const string raw = """Here you go {"tracks": [{"artist": "Daft Punk", "title": "Around the World"}]} hope that helps!""";

        var result = ResponseParser.ParseTracklist(raw);

        var track = Assert.Single(result.Tracks);
        Assert.Equal("Daft Punk", track.Artist);
    }

    [Fact]
    public void RootLevelArray_Parses()
    {
        const string raw = """[{"artist": "Radiohead", "title": "Karma Police"}]""";

        var result = ResponseParser.ParseTracklist(raw);

        Assert.Equal("Radiohead", Assert.Single(result.Tracks).Artist);
    }

    [Fact]
    public void StringFormTracks_ParseIntoArtistTitle()
    {
        const string raw = """{"tracks": ["Portishead - Glory Box", "Massive Attack - Teardrop"]}""";

        var result = ResponseParser.ParseTracklist(raw);

        Assert.Equal(2, result.Tracks.Count);
        Assert.Equal("Portishead", result.Tracks[0].Artist);
        Assert.Equal("Glory Box", result.Tracks[0].Title);
    }

    [Fact]
    public void WrongCasingKeys_Parse()
    {
        const string raw = """{"PlaylistName": "X", "Tracks": [{"Artist": "A", "Title": "B"}]}""";

        var result = ResponseParser.ParseTracklist(raw);

        Assert.Equal("X", result.PlaylistName);
        Assert.Single(result.Tracks);
    }

    [Fact]
    public void ProseWrappedArrayWithTrailingComment_Parses()
    {
        const string raw = """
            [{"artist": "Nina Simone", "title": "Feeling Good"}]
            Let me know if you want more tracks!
            """;

        var result = ResponseParser.ParseTracklist(raw);

        Assert.Equal("Nina Simone", Assert.Single(result.Tracks).Artist);
        Assert.NotNull(result.Notes);
        Assert.Contains("more tracks", result.Notes);
    }

    [Fact]
    public void Garbage_ThrowsLlmParseException()
    {
        Assert.Throws<LlmParseException>(() => ResponseParser.ParseTracklist("I could not find any suitable tracks, sorry!"));
    }

    [Fact]
    public void EmptyResponse_ThrowsLlmParseException()
    {
        Assert.Throws<LlmParseException>(() => ResponseParser.ParseTracklist("   "));
    }

    [Fact]
    public void JsonWithoutTracks_ThrowsLlmParseException()
    {
        Assert.Throws<LlmParseException>(() => ResponseParser.ParseTracklist("""{"note": "no tracks here"}"""));
    }

    [Fact]
    public void DuplicateTracks_AreDeDuplicated()
    {
        const string raw = """{"tracks": [{"artist": "X", "title": "Y"}, {"artist": "x", "title": "y"}]}""";

        var result = ResponseParser.ParseTracklist(raw);

        Assert.Single(result.Tracks);
    }

    [Fact]
    public void MalformedJsonInsideFence_ThrowsLlmParseException()
    {
        const string raw = "```json\n{\"tracks\": [{\"artist\": \"A\", \"title\": \n```";

        Assert.Throws<LlmParseException>(() => ResponseParser.ParseTracklist(raw));
    }

    [Fact]
    public void ParseArtists_FencedObject_Parses()
    {
        const string raw = """
            ```json
            {"artists": ["Tycho", "Bonobo", "ODESZA"]}
            ```
            """;

        var artists = ResponseParser.ParseArtists(raw);

        Assert.Equal(3, artists.Count);
        Assert.Contains("Bonobo", artists);
    }

    [Fact]
    public void ParseArtists_Garbage_Throws()
    {
        Assert.Throws<LlmParseException>(() => ResponseParser.ParseArtists("no artists today"));
    }

    // ------------------------------------------------------------------
    // Truncation salvage (v0.1.5 — real production payload that ended mid-key)
    // ------------------------------------------------------------------

    [Fact]
    public void TruncatedMidKey_SalvagesCompleteTracks()
    {
        // Verbatim shape from the amc-media failure: JSON sliced at `"artist]` by max_tokens.
        const string raw = """
            {"name": "Depeche Mode Radio", "tracks": [
            {"artist": "Depeche Mode", "title": "World in My Eyes"},
            {"artist": "Depeche Mode", "title": "It’s No Good"},
            {"artist": "Depeche Mode", "title": "Dangerous"},
            {"artist"
            """;

        var result = ResponseParser.ParseTracklist(raw);

        Assert.Equal("Depeche Mode Radio", result.PlaylistName);
        Assert.Equal(3, result.Tracks.Count);
        Assert.Equal("World in My Eyes", result.Tracks[0].Title);
    }

    [Fact]
    public void TruncatedSingleTrack_NothingSalvageable_Throws()
    {
        const string raw = """{"name": "X", "tracks": [{"art""";
        Assert.Throws<LlmParseException>(() => ResponseParser.ParseTracklist(raw));
    }

    [Fact]
    public void TruncatedArtistList_SalvagesCompleteNames()
    {
        const string raw = """{"artists": ["Depeche Mode", "Kraftwerk", "Nin""";

        var artists = ResponseParser.ParseArtists(raw);

        Assert.Equal(2, artists.Count);
        Assert.Contains("Depeche Mode", artists);
        Assert.Contains("Kraftwerk", artists);
    }

    // ------------------------------------------------------------------
    // m3u seed parsing (v0.2.0 — real Emby playlist file format)
    // ------------------------------------------------------------------

    [Fact]
    public void M3u_RealEmbyPlaylist_ParsesAllSeeds()
    {
        // Verbatim from Emby 4.10.1.0's generated playlist file.
        var lines = new[]
        {
            "#EXTM3U",
            "#PLAYLIST:CrateDigger Seeds",
            "#EXTALB:Depeche Mode - Singles Box 5 (US Release)",
            "#EXTART:Depeche Mode",
            "#EXTINF:293,Policy Of Truth",
            @"..\..\..\..\..\..\..\OneDrive\Music\Depeche Mode\Depeche Mode - Singles Box 5 (US Release)\Depeche Mode - Policy Of Truth.mp3",
            "#EXTALB:The Best of Depeche Mode, Volume 1",
            "#EXTART:Depeche Mode",
            "#EXTINF:253,Enjoy the Silence - 2006 Remaster",
            @"..\..\..\..\..\..\..\OneDrive\Music\Depeche Mode\The Best of Depeche Mode, Volume 1\Depeche Mode - Enjoy the Silence - 2006 Remaster.mp3",
        };

        var seeds = CrateDigger.Core.M3uSeedParser.ParseSeeds(lines);

        Assert.Equal(2, seeds.Count);
        Assert.Equal(("Depeche Mode", "Policy Of Truth"), (seeds[0].Artist, seeds[0].Title));
        Assert.Equal(("Depeche Mode", "Enjoy the Silence - 2006 Remaster"), (seeds[1].Artist, seeds[1].Title));
    }

    [Fact]
    public void M3u_PathOnlyEntry_FallsBackToFilename()
    {
        var lines = new[]
        {
            "#EXTM3U",
            @"D:\Music\Some Artist\Some Album\Some Artist - Great Song.mp3",
        };

        var seeds = CrateDigger.Core.M3uSeedParser.ParseSeeds(lines);

        var s = Assert.Single(seeds);
        Assert.Equal(("Some Artist", "Great Song"), (s.Artist, s.Title));
    }

    [Fact]
    public void M3u_EmptyPlaylist_ReturnsNothing()
    {
        Assert.Empty(CrateDigger.Core.M3uSeedParser.ParseSeeds(new[] { "#EXTM3U", "#PLAYLIST:Empty" }));
    }
}