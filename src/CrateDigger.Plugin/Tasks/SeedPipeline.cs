using CrateDigger.Core;
using CrateDigger.Core.Llm;
using CrateDigger.Core.Matching;
using CrateDigger.Core.Models;
using CrateDigger.Plugin.Configuration;
using CrateDigger.Plugin.Library;
using CrateDigger.Plugin.Playlists;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;

namespace CrateDigger.Plugin.Tasks;

/// <summary>
/// The seed→playlist run itself, shared by BOTH triggers:
///   * CrateDiggerSeedTask     (3-minute interval backstop)
///   * CrateDiggerPlugin       (PlaylistItemsAdded debounce, v0.2.1)
///
/// Design notes (all live-verified on 4.10.1.0):
///  - Children come from the playlist's m3u metadata — m3u-backed playlists are
///    NOT tree-linked, so every InternalItemsQuery parent scoping returns 0.
///  - Seeds are fuzzy-resolved against the audio catalog; the catalog passed to
///    generation EXCLUDES them, so the model cannot suggest them back.
///  - Clearing = delete + recreate (RemoveFromPlaylist needs REST-only entry ids),
///    guarded by a mid-run re-read: if NEW seeds arrived during generation they are
///    kept for the next batch instead of being wiped.
///  - A single non-blocking gate prevents concurrent runs from the two triggers.
/// </summary>
public sealed class SeedPipeline
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly IUserManager _userManager;
    private readonly ILogger _logger;

    public SeedPipeline(ILibraryManager libraryManager, IPlaylistManager playlistManager, IUserManager userManager, ILogger logger)
    {
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>Returns false when there was nothing to do (or another run holds the gate).</summary>
    public async Task<bool> RunAsync(PluginConfiguration config, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!Gate.Wait(0, cancellationToken))
        {
            _logger.Info("Seed run: already in progress — skipping this trigger.");
            return false;
        }

        try
        {
            return await RunCoreAsync(config, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<bool> RunCoreAsync(PluginConfiguration config, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(2);

        // 1. Locate the seed playlist by name (Name+type query — verified working).
        var seedPlaylist = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { "Playlist" },
            Name = config.SeedPlaylistName,
            Recursive = true,
        }).FirstOrDefault();

        if (seedPlaylist == null)
        {
            _logger.Debug("Seed run: playlist '{0}' not present — nothing to do.", config.SeedPlaylistName);
            return false;
        }

        // 2. Read seed metadata from the playlist's m3u (no tree queries).
        var seedRefs = ReadSeedMetadata(config.SeedPlaylistName);
        if (seedRefs.Count == 0)
        {
            _logger.Debug("Seed run: no parseable seeds in '{0}' m3u — nothing to do.", config.SeedPlaylistName);
            return false;
        }

        _logger.Info("Seed run: {0} seed(s) read from m3u for '{1}'", seedRefs.Count, config.SeedPlaylistName);
        progress?.Report(8);
        var processedKeys = new HashSet<string>(seedRefs.Select(Key), StringComparer.OrdinalIgnoreCase);

        // 3. One catalog pass: ALL audio items (exclusion + matching universe).
        var allItems = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { nameof(Audio) },
            Recursive = true,
        });
        var catalog = allItems
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(LibraryExtractor.MapItem)
            .ToList();

        // 4. Resolve seeds → library items (fuzzy — m3u metadata vs tags may differ).
        var matcher = new FuzzyMatcher();
        var resolvedSeeds = matcher.ResolveAll(seedRefs, catalog, 0.75);
        var seedIdSet = new HashSet<string>(
            resolvedSeeds.Matched.Select(m => m.Match.Id), StringComparer.Ordinal);
        if (seedIdSet.Count > 0 && seedIdSet.Count < seedRefs.Count)
            _logger.Warn("Seed run: {0} of {1} seeds could not be resolved to library items.",
                seedRefs.Count - seedIdSet.Count, seedRefs.Count);

        var generationCatalog = seedIdSet.Count > 0
            ? catalog.Where(t => !seedIdSet.Contains(t.Id)).ToList()
            : catalog;
        if (generationCatalog.Count == 0)
        {
            _logger.Warn("Seed run: catalog empty after removing seeds — aborting.");
            return false;
        }

        // 5. Seed-derived brief → shared pipeline (seeds excluded from catalog).
        var promptBuilder = new PromptBuilder();
        var prompt = promptBuilder.BuildSeedPrompt(seedRefs);

        var generator = new PlaylistGenerator(new LlmClient(), promptBuilder);
        GenerationResult result;
        try
        {
            result = await generator.GenerateAsync(
                prompt,
                generationCatalog,
                BuildLlmOptions(config),
                new GenerationOptions
                {
                    MaxTracks = Math.Clamp(config.MaxPlaylistTracks, 1, 500),
                    MatchThreshold = Math.Clamp(config.MatchThreshold, 0.1, 1.0),
                },
                new StageToPercent(progress),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.ErrorException("Seed run: generation failed — seeds left in place for retry.", ex);
            return false; // keep seeds: never lose the user's picks on a failed run
        }

        progress?.Report(88);

        // 6. Result playlist (timestamped so batches never collide on names).
        var owner = ResolveOwner(config);
        var name = $"{config.SeedResultName} {DateTime.Now:MM-dd HH:mm}";
        var creator = new PlaylistCreator(_playlistManager, _logger);
        var playlistId = await creator.CreateAsync(name, result.PlaylistTracks, owner).ConfigureAwait(false);

        progress?.Report(96);

        // 7. Clear the seeds — but ONLY what we processed: re-read the m3u and keep
        //    anything that arrived mid-run (never wipe a user's fresh addition).
        var cleared = 0;
        if (config.SeedClearAfterRun)
        {
            var currentKeys = new HashSet<string>(
                ReadSeedMetadata(config.SeedPlaylistName).Select(Key), StringComparer.OrdinalIgnoreCase);
            var newSeeds = currentKeys.Count(k => !processedKeys.Contains(k));
            if (newSeeds > 0)
            {
                _logger.Info("Seed run: {0} new seed(s) arrived during the run — keeping ALL seeds for the next batch.", newSeeds);
            }
            else
            {
                try
                {
                    _libraryManager.DeleteItem(seedPlaylist, new DeleteOptions { DeleteFileLocation = true });
                    await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
                    {
                        Name = config.SeedPlaylistName,
                        ItemIdList = Array.Empty<long>(),
                        MediaType = "Audio",
                        User = owner,
                    }).ConfigureAwait(false);
                    cleared = seedRefs.Count;
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Seed run: could not clear seeds (playlist will re-trigger).", ex);
                }
            }
        }

        _logger.Info(
            "Seed run: created '{0}' ({1}) with {2} tracks from {3} seed(s); {4} cleared; {5} unmatched",
            name, playlistId, result.Report.MatchedCount, seedRefs.Count, cleared, result.Report.UnmatchedCount);

        progress?.Report(100);
        return true;
    }

    /// <summary>Seed identity for m3u diffing: "artist - title".</summary>
    private static string Key(TrackRef t) => $"{t.Artist} - {t.Title}";

    /// <summary>LLM options — from the live plugin configuration (falling back to defaults).</summary>
    private static LlmOptions BuildLlmOptions(PluginConfiguration config) => new()
    {
        ApiKey = config.ApiKey?.Trim() ?? string.Empty,
        BaseUrl = config.BaseUrl?.Trim() ?? string.Empty,
        Model = string.IsNullOrWhiteSpace(config.Model) ? "gpt-4o-mini" : config.Model.Trim(),
        TimeoutSeconds = Math.Clamp(config.LlmTimeoutSeconds, 10, 3600),
        MaxRetries = 1,
        MaxTokens = Math.Clamp(config.LlmMaxTokens, 0, 1_000_000),
        ExtraJson = config.LlmExtraJson?.Trim() ?? string.Empty,
    };

    /// <summary>
    /// Locate and parse the playlist's m3u. Path: {programdata}\data\userplaylists\
    /// {Name} [playlist]\{Name}.m3u — programdata derived from the plugin's own
    /// configuration file path (...\plugins\configurations\X.xml → up two).
    /// </summary>
    private IReadOnlyList<TrackRef> ReadSeedMetadata(string playlistName)
    {
        try
        {
            var plugin = CrateDiggerPlugin.Instance;
            var configFile = plugin?.ConfigurationFilePath;
            if (string.IsNullOrWhiteSpace(configFile))
            {
                _logger.Warn("Seed run: ConfigurationFilePath unavailable — cannot locate m3u.");
                return Array.Empty<TrackRef>();
            }

            var configurationsDir = Path.GetDirectoryName(configFile)!;                  // ...\plugins\configurations
            var pluginsDir = Directory.GetParent(configurationsDir)!.FullName;           // ...\plugins
            var programData = Directory.GetParent(pluginsDir)!.FullName;                 // ...\programdata

            var folder = Path.Combine(programData, "data", "userplaylists", $"{playlistName} [playlist]");
            var m3uPath = Path.Combine(folder, $"{playlistName}.m3u");
            if (!File.Exists(m3uPath))
            {
                _logger.Warn("Seed run: m3u not found at '{0}'.", m3uPath);
                return Array.Empty<TrackRef>();
            }

            return M3uSeedParser.ParseSeeds(File.ReadAllLines(m3uPath));
        }
        catch (Exception ex)
        {
            _logger.ErrorException("Seed run: failed reading seed m3u.", ex);
            return Array.Empty<TrackRef>();
        }
    }

    /// <summary>
    /// Seed playlists have no session — owner comes from config, else the first user.
    /// </summary>
    private User ResolveOwner(PluginConfiguration config)
    {
        if (Guid.TryParse(config.OwnerUserId, out var configured) && configured != Guid.Empty)
        {
            var byConfig = _userManager.GetUserById(configured);
            if (byConfig != null)
                return byConfig;
        }

        var users = _userManager.GetUserList(new UserQuery());
        if (users is { Length: > 0 })
            return users[0];

        throw new InvalidOperationException("No user available to own the generated playlist.");
    }

    /// <summary>Maps the pipeline's staged progress onto the task's 0-100 scale.</summary>
    private sealed class StageToPercent(IProgress<double>? sink) : IProgress<GenerationStage>
    {
        public void Report(GenerationStage value)
        {
            var pct = value switch
            {
                GenerationStage.AnalyzingLibrary => 15d,
                GenerationStage.Thinking => 40d,
                GenerationStage.MatchingTracks => 75d,
                GenerationStage.CreatingPlaylist => 90d,
                _ => 0d,
            };
            if (pct > 0)
                sink?.Report(pct);
        }
    }
}