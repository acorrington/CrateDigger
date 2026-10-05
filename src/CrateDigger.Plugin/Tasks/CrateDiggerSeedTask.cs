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
using MediaBrowser.Model.Tasks;

namespace CrateDigger.Plugin.Tasks;

/// <summary>
/// The user-facing trigger (v0.2.0): songs added to the "seed" playlist from ANY Emby
/// client's "Add to playlist" menu are turned into a fresh playlist, then the seeds
/// are cleared — the playlist doubles as a "send next" queue.
///
/// Children come from the playlist's m3u file (metadata only — Emby's m3u-backed
/// playlists are NOT tree-linked, so InternalItemsQuery parent scoping returns 0 for
/// them: verified with a 10-shape diagnostic matrix). Seeds are resolved to library
/// items with the fuzzy matcher; clearing = delete + recreate (both live-verified
/// primitives). Auto-discovered via Automatic Type Discovery; default 3-minute
/// interval trigger (user-editable in Dashboard → Scheduled Tasks).
/// </summary>
public class CrateDiggerSeedTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly IUserManager _userManager;
    private readonly ILogger _logger;

    public CrateDiggerSeedTask(
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        IUserManager userManager,
        ILogManager logManager)
    {
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _userManager = userManager;
        _logger = logManager.GetLogger("CrateDigger");
    }

    public string Name => "CrateDigger: Generate from seed playlist";

    public string Key => "CrateDiggerSeedTask";

    public string Description
    {
        get
        {
            var seedName = CrateDiggerPlugin.Instance?.Configuration?.SeedPlaylistName ?? "CrateDigger Seeds";
            var resultName = CrateDiggerPlugin.Instance?.Configuration?.SeedResultName ?? "CrateDigger Radio";
            return $"Scans the '{seedName}' playlist; when it has songs, generates a '{resultName}' " +
                   "playlist with the configured LLM and clears the seeds (see plugin settings).";
        }
    }

    public string Category => "CrateDigger";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo
        {
            Type = "IntervalTrigger",
            IntervalTicks = TimeSpan.FromMinutes(3).Ticks,
        },
    };

    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        var plugin = CrateDiggerPlugin.Instance;
        var config = plugin?.Configuration;
        if (plugin == null || config == null)
        {
            progress.Report(100);
            return;
        }
        if (!config.SeedTriggerEnabled)
        {
            progress.Report(100);
            return;
        }

        progress.Report(2);

        // 1. Locate the seed playlist by name (Name+type query — verified working).
        var seedPlaylist = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { "Playlist" },
            Name = config.SeedPlaylistName,
            Recursive = true,
        }).FirstOrDefault();

        if (seedPlaylist == null)
        {
            _logger.Debug("Seed task: playlist '{0}' not present — nothing to do.", config.SeedPlaylistName);
            progress.Report(100);
            return;
        }

        // 2. Read seed metadata from the playlist's m3u (no tree queries).
        var seedRefs = ReadSeedMetadata(config.SeedPlaylistName);
        if (seedRefs.Count == 0)
        {
            _logger.Debug("Seed task: no parseable seeds in '{0}' m3u — nothing to do.", config.SeedPlaylistName);
            progress.Report(100);
            return;
        }

        _logger.Info("Seed task: {0} seed(s) read from m3u for '{1}'", seedRefs.Count, config.SeedPlaylistName);
        progress.Report(8);

        // 3. One catalog pass: ALL audio items (also the exclusion + matching universe).
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
            _logger.Warn("Seed task: {0} of {1} seeds could not be resolved to library items.",
                seedRefs.Count - seedIdSet.Count, seedRefs.Count);

        var generationCatalog = seedIdSet.Count > 0
            ? catalog.Where(t => !seedIdSet.Contains(t.Id)).ToList()
            : catalog;
        if (generationCatalog.Count == 0)
        {
            _logger.Warn("Seed task: catalog empty after removing seeds — aborting.");
            progress.Report(100);
            return;
        }

        // 5. Seed-derived brief → shared pipeline (seeds excluded from catalog,
        //    so the model cannot suggest them back).
        var promptBuilder = new PromptBuilder();
        var prompt = promptBuilder.BuildSeedPrompt(seedRefs);

        var generator = new PlaylistGenerator(new LlmClient(), promptBuilder);
        GenerationResult result;
        try
        {
            result = await generator.GenerateAsync(
                prompt,
                generationCatalog,
                plugin.ToLlmOptions(),
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
            _logger.ErrorException("Seed task: generation failed — seeds left in place for retry.", ex);
            progress.Report(100);
            return; // keep seeds: never lose the user's picks on a failed run
        }

        progress.Report(88);

        // 6. Result playlist (timestamped so batches never collide on names).
        var owner = ResolveOwner(config);
        var name = $"{config.SeedResultName} {DateTime.Now:MM-dd HH:mm}";
        var creator = new PlaylistCreator(_playlistManager, _logger);
        var playlistId = await creator.CreateAsync(name, result.PlaylistTracks, owner).ConfigureAwait(false);

        progress.Report(96);

        // 7. Clear the seeds: delete + recreate empty (verified primitives; entry-id
        //    removal is impossible without tree-linked children).
        var cleared = 0;
        if (config.SeedClearAfterRun)
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
                _logger.ErrorException("Seed task: could not clear seeds (playlist will re-trigger).", ex);
            }
        }

        _logger.Info(
            "Seed task: created '{0}' ({1}) with {2} tracks from {3} seed(s); {4} cleared; {5} unmatched",
            name, playlistId, result.Report.MatchedCount, seedRefs.Count, cleared, result.Report.UnmatchedCount);

        progress.Report(100);
    }

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
                _logger.Warn("Seed task: ConfigurationFilePath unavailable — cannot locate m3u.");
                return Array.Empty<TrackRef>();
            }

            var configurationsDir = Path.GetDirectoryName(configFile)!;   // ...\plugins\configurations
            var pluginsDir = Directory.GetParent(configurationsDir)!.FullName;      // ...\plugins
            var programData = Directory.GetParent(pluginsDir)!.FullName;            // ...\programdata

            var folder = Path.Combine(programData, "data", "userplaylists", $"{playlistName} [playlist]");
            var m3uPath = Path.Combine(folder, $"{playlistName}.m3u");
            if (!File.Exists(m3uPath))
            {
                _logger.Warn("Seed task: m3u not found at '{0}'.", m3uPath);
                return Array.Empty<TrackRef>();
            }

            var lines = File.ReadAllLines(m3uPath);
            var seeds = M3uSeedParser.ParseSeeds(lines);
            _logger.Debug("Seed task: parsed {0} seed(s) from {1}", seeds.Count, m3uPath);
            return seeds;
        }
        catch (Exception ex)
        {
            _logger.ErrorException("Seed task: failed reading seed m3u.", ex);
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
    private sealed class StageToPercent(IProgress<double> sink) : IProgress<GenerationStage>
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
                sink.Report(pct);
        }
    }
}