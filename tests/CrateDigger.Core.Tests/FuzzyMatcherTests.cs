using CrateDigger.Core.Matching;
using CrateDigger.Core.Models;

namespace CrateDigger.Core.Tests;

/// <summary>Spec §4.1 UT-001: string matcher resolves typos against the library.</summary>
public class FuzzyMatcherTests
{
    private static readonly FuzzyMatcher Matcher = new();

    private static IReadOnlyList<LibraryTrack> BeatlesLibrary() =>
    [
        new("1", new TrackRef("The Beatles", "Norwegian Wood")),
        new("2", new TrackRef("The Beatles", "Come Together")),
        new("3", new TrackRef("The Beatles", "Let It Be")),
    ];

    [Fact]
    public void Ut001_TypoInTitle_StillMatches()
    {
        // LLM returns "Norweigan Wood" (misspelled); library has "Norwegian Wood".
        var suggestion = new TrackRef("The Beatles", "Norweigan Wood");

        var report = Matcher.ResolveAll([suggestion], BeatlesLibrary());

        var match = Assert.Single(report.Matched);
        Assert.Equal("1", match.Match.Id);
        Assert.True(match.Score >= FuzzyMatcher.DefaultAcceptanceThreshold,
            $"Expected score >= {FuzzyMatcher.DefaultAcceptanceThreshold} but was {match.Score}");
        Assert.Empty(report.Unmatched);
    }

    [Fact]
    public void ExactMatch_ScoresPerfect()
    {
        var score = Matcher.Score(
            new TrackRef("The Beatles", "Let It Be"),
            new TrackRef("The Beatles", "Let It Be"));

        Assert.Equal(1.0, score, 3);
    }

    [Fact]
    public void Diacritics_AreIgnored()
    {
        var report = Matcher.ResolveAll(
            [new TrackRef("Beyonce", "Crazy In Love")],
            [new("b1", new TrackRef("Beyoncé", "Crazy in Love"))]);

        var match = Assert.Single(report.Matched);
        Assert.Equal("b1", match.Match.Id);
    }

    [Fact]
    public void FeatSuffix_IsStrippedFromComparison()
    {
        // Library has the long form; LLM returned the short form.
        var report = Matcher.ResolveAll(
            [new TrackRef("Drake", "One Dance")],
            [new("d1", new TrackRef("Drake", "One Dance (feat. Wizkid & Kyla)"))]);

        Assert.Single(report.Matched);
    }

    [Fact]
    public void UnrelatedTracks_AreRejected()
    {
        var report = Matcher.ResolveAll(
            [new TrackRef("Metallica", "Enter Sandman")],
            BeatlesLibrary());

        Assert.Empty(report.Matched);
        var unresolved = Assert.Single(report.Unmatched);
        Assert.True(unresolved.BestScore < FuzzyMatcher.DefaultAcceptanceThreshold);
    }

    [Fact]
    public void SameLibraryTrack_SuggestedTwice_KeepsOnlyOne()
    {
        var report = Matcher.ResolveAll(
            [
                new TrackRef("The Beatles", "Norwegian Wood"),
                new TrackRef("The Beatles", "Norwegian Wood (Live)"),
            ],
            BeatlesLibrary());

        Assert.Single(report.Matched);
    }

    [Fact]
    public void EmptyLibrary_MatchesNothing()
    {
        var report = Matcher.ResolveAll([new TrackRef("Anyone", "Anything")], []);

        Assert.Empty(report.Matched);
        Assert.Single(report.Unmatched);
    }

    [Theory]
    [InlineData("The Beatles", "the beatles")]
    [InlineData("AC/DC", "ac dc")]
    [InlineData("Simon & Garfunkel", "simon and garfunkel")]
    [InlineData("Céline Dion", "celine dion")]
    public void Normalize_EquivalentForms_CollapseIdentically(string a, string b)
    {
        Assert.Equal(FuzzyMatcher.Normalize(a), FuzzyMatcher.Normalize(b));
    }

    [Fact]
    public void Levenshtein_KnownValues()
    {
        Assert.Equal(0, FuzzyMatcher.LevenshteinDistance("kitten", "kitten"));
        Assert.Equal(3, FuzzyMatcher.LevenshteinDistance("kitten", "sitting"));
        // "norwegian" vs "norweigan": the g/i swap costs 2 plain-Levenshtein edits.
        Assert.Equal(2, FuzzyMatcher.LevenshteinDistance("norwegian", "norweigan"));
    }

    [Fact]
    public void ParenthesizedTitle_MatchesUnparenthesizedSuggestion()
    {
        // Library: "(Don't Fear) The Reaper" — paren-stripping alone would leave "The Reaper".
        // Variant comparison must still accept "Don't Fear The Reaper" (live ST-002 case).
        var report = Matcher.ResolveAll(
            [new TrackRef("Blue Oyster Cult", "Don't Fear The Reaper")],
            [new("boc", new TrackRef("Blue Öyster Cult", "(Don't Fear) The Reaper"))]);

        var match = Assert.Single(report.Matched);
        Assert.Equal("boc", match.Match.Id);
    }

    [Fact]
    public void RemasterSuffix_StillMatchesBareTitle()
    {
        var score = Matcher.Score(
            new TrackRef("Queen", "Bohemian Rhapsody"),
            new TrackRef("Queen", "Bohemian Rhapsody (Remastered 2011)"));

        Assert.True(score >= FuzzyMatcher.DefaultAcceptanceThreshold, $"score was {score}");
    }
}