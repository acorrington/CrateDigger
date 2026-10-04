using CrateDigger.Core.Llm;
using CrateDigger.Core.Models;

namespace CrateDigger.Core.Tests;

public class PromptBuilderTests
{
    private static IReadOnlyList<LibraryTrack> MakeLibrary(int count)
        => Enumerable.Range(1, count)
            .Select(i => new LibraryTrack(i.ToString(), new TrackRef($"Artist {i % 50}", $"Song {i}")))
            .ToList();

    [Fact]
    public void SmallLibrary_EmbedsEveryTrack()
    {
        var builder = new PromptBuilder();
        var library = MakeLibrary(20);

        var request = builder.BuildTrackRequest("chill evening", library, maxTracks: 10);

        var user = request.Messages.Single(m => m.Role == "user").Content;
        Assert.Contains("1. Artist 1 - Song 1", user);
        Assert.Contains("20. Artist 20 - Song 20", user);
        Assert.Contains("about 10 tracks", user);
        Assert.Contains("chill evening", user);
    }

    [Fact]
    public void LargeLibrary_UsesSummaryNotFullList()
    {
        var builder = new PromptBuilder { FullListLimit = 50 };
        var library = MakeLibrary(500);

        var request = builder.BuildTrackRequest("gym mix", library, maxTracks: 30);

        var user = request.Messages.Single(m => m.Role == "user").Content;
        Assert.Contains("too large to list in full", user);
        Assert.Contains("Genres:", user);
        Assert.Contains("Artists:", user);
        Assert.DoesNotContain("500. Artist", user); // no full enumeration
    }

    [Fact]
    public void ArtistSelectionRequest_ListsDistinctArtistsOnly()
    {
        var builder = new PromptBuilder();
        var library = MakeLibrary(400); // artists repeat via i % 50

        var request = builder.BuildArtistSelectionRequest("disco night", library);

        var user = request.Messages.Single(m => m.Role == "user").Content;
        Assert.Contains("disco night", user);
        // 400 tracks / 50 distinct artists → the list must contain 50 numbered entries, not 400.
        var numberedLines = user.Split('\n').Count(l => l.StartsWith($"{1}. ") || System.Text.RegularExpressions.Regex.IsMatch(l.TrimStart(), @"^\d+\. Artist"));
        Assert.True(numberedLines <= 50, $"Expected <= 50 artist lines, found {numberedLines}");
    }

    [Fact]
    public void SystemMessage_OrdersJsonOnly()
    {
        var builder = new PromptBuilder();

        var request = builder.BuildTrackRequest("x", MakeLibrary(5), 10);

        var system = request.Messages.Single(m => m.Role == "system").Content;
        Assert.Contains("JSON only", system);
    }
}