using CrateDigger.Core;
using CrateDigger.Core.Llm;
using CrateDigger.Core.Models;

namespace CrateDigger.Core.Tests;

/// <summary>End-to-end pipeline tests with a scripted (fake) LLM.</summary>
public class PlaylistGeneratorTests
{
    private sealed class FakeLlm : ILlmClient
    {
        private readonly Queue<string> _responses;
        public List<LlmRequest> Requests { get; } = [];

        public FakeLlm(params string[] responses) => _responses = new Queue<string>(responses);

        public Task<string> CompleteAsync(LlmRequest request, LlmOptions options, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : throw new InvalidOperationException("No scripted LLM response left."));
        }
    }

    private static IReadOnlyList<LibraryTrack> Library() =>
    [
        new("1", new TrackRef("Kraftwerk", "The Robots")),
        new("2", new TrackRef("Depeche Mode", "Enjoy the Silence")),
        new("3", new TrackRef("New Order", "Blue Monday")),
    ];

    private static readonly LlmOptions Options = new() { ApiKey = "test", Model = "test-model" };

    [Fact]
    public async Task HappyPath_ReturnsMatchedTracks()
    {
        var llm = new FakeLlm("""
            {"name": "Synth Night", "tracks": [
              {"artist": "Kraftwerk", "title": "The Robots"},
              {"artist": "Depeche Mode", "title": "Enjoy the Silence"}
            ]}
            """);
        var generator = new PlaylistGenerator(llm);

        var result = await generator.GenerateAsync("synth pop", Library(), Options, new GenerationOptions());

        Assert.Equal("Synth Night", result.PlaylistName);
        Assert.Equal(2, result.PlaylistTracks.Count);
        Assert.Equal(2, result.Report.MatchedCount);
        Assert.Equal(0, result.Report.UnmatchedCount);
    }

    [Fact]
    public async Task MisspelledSuggestion_StillResolves()
    {
        var llm = new FakeLlm("""{"tracks": [{"artist": "Depeche Mode", "title": "Enjoy the Silense"}]}""");
        var generator = new PlaylistGenerator(llm);

        var result = await generator.GenerateAsync("moody", Library(), Options, new GenerationOptions());

        Assert.Single(result.PlaylistTracks);
        Assert.Equal("2", result.PlaylistTracks[0].Id);
    }

    [Fact]
    public async Task NoMatches_ThrowsWithHelpfulMessage()
    {
        var llm = new FakeLlm("""{"tracks": [{"artist": "Someband", "title": "Totally Different Song"}]}""");
        var generator = new PlaylistGenerator(llm);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateAsync("vibes", Library(), Options, new GenerationOptions()));

        Assert.Contains("matched", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProgressStages_AreReportedInOrder()
    {
        var llm = new FakeLlm("""{"tracks": [{"artist": "Kraftwerk", "title": "The Robots"}]}""");
        var generator = new PlaylistGenerator(llm);
        var stages = new List<GenerationStage>();
        var syncStages = new SynchronousProgress(stages);

        await generator.GenerateAsync("robot funk", Library(), Options, new GenerationOptions(), syncStages);

        Assert.Equal(
            new[]
            {
                GenerationStage.AnalyzingLibrary,
                GenerationStage.Thinking,
                GenerationStage.MatchingTracks,
            },
            stages);
    }

    [Fact]
    public async Task EmptyPrompt_Throws()
    {
        var generator = new PlaylistGenerator(new FakeLlm());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            generator.GenerateAsync("  ", Library(), Options, new GenerationOptions()));
    }

    [Fact]
    public async Task EmptyLibrary_Throws()
    {
        var generator = new PlaylistGenerator(new FakeLlm());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateAsync("anything", [], Options, new GenerationOptions()));
    }

    [Fact]
    public async Task HugeLibrary_TriggersArtistShortlistStep()
    {
        var many = Enumerable.Range(1, 400)
            .Select(i => new LibraryTrack(i.ToString(), new TrackRef($"Artist {i % 60}", $"Song {i}")))
            .ToList();

        var llm = new FakeLlm(
            """{"artists": ["Artist 5", "Artist 9"]}""",
            """{"tracks": [{"artist": "Artist 5", "title": "Song 5"}]}""");
        var generator = new PlaylistGenerator(llm, new PromptBuilder { FullListLimit = 100 });

        var result = await generator.GenerateAsync("focus", many, Options, new GenerationOptions());

        Assert.Equal(2, llm.Requests.Count); // shortlist + tracks
        Assert.Single(result.PlaylistTracks);
    }

    [Fact]
    public async Task UnparseableFirstReply_RetriesOnceWithReinforcedPrompt()
    {
        var llm = new FakeLlm(
            "I am afraid I cannot produce a playlist right now.",        // prose-only failure
            """{"tracks": [{"artist": "Kraftwerk", "title": "The Robots"}]}""");
        var generator = new PlaylistGenerator(llm);

        var result = await generator.GenerateAsync("robot funk", Library(), Options, new GenerationOptions());

        Assert.Equal(2, llm.Requests.Count); // first attempt + reinforced retry
        Assert.Single(result.PlaylistTracks);
        var retrySystem = llm.Requests[1].Messages.Last();
        Assert.Equal("system", retrySystem.Role);
        Assert.Contains("ONLY the raw JSON", retrySystem.Content);
    }

    [Fact]
    public async Task BothRepliesUnparseable_SurfacesBothErrors()
    {
        var llm = new FakeLlm("no json", "still no json");
        var generator = new PlaylistGenerator(llm);

        var ex = await Assert.ThrowsAsync<LlmParseException>(() =>
            generator.GenerateAsync("x", Library(), Options, new GenerationOptions()));

        Assert.Contains("both attempts", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, llm.Requests.Count);
    }

    private sealed class SynchronousProgress(List<GenerationStage> sink) : IProgress<GenerationStage>
    {
        public void Report(GenerationStage value) => sink.Add(value);
    }
}