using CrateDigger.Core;
using CrateDigger.Core.Llm;
using CrateDigger.Core.Matching;
using CrateDigger.Core.Models;
using CrateDigger.Plugin.Configuration;
using CrateDigger.Plugin.Library;
using CrateDigger.Plugin.Playlists;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;

namespace CrateDigger.Plugin.Tasks;

/// <summary>
/// The seed→playlist run itself, shared by BOTH triggers and BOTH media modes:
///   * CrateDiggerSeedTask     (3-minute interval backstop, iterates all modes)
///   * CrateDiggerPlugin       (PlaylistItemsAdded debounce, mode picked by playlist name)
///
/// Design notes (all live-verified on 4.10.1.0):
///  - Children come from the playlist's m3u metadata — m3u-backed playlists are
///    NOT tree-linked, so every InternalItemsQuery parent scoping returns 0 for them.
///  - Video playlists write m3u WITHOUT #EXTART (title-only) — M3uSeedParser splits
///    "Artist - Title" from the EXTINF title and strips a repeated artist prefix.
///  - Seeds are fuzzy-resolved against the catalog (matching the mode's item types);
///    the catalog passed to generation EXCLUDES them, so the model cannot suggest
///    them back.
///  - Clearing = delete + recreate with the mode's MediaType (RemoveFromPlaylist
///    needs REST-only entry ids), guarded by a mid-run re-read: if NEW seeds arrived
///    during generation they are kept for the next batch instead of being wiped.
///  - A single non-blocking gate prevents concurrent runs across triggers/modes.
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
    public async Task<bool> RunAsync(
        SeedMode mode,
        PluginConfiguration config,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!Gate.Wait(0, cancellationToken))
        {
            _logger.Info("Seed run ({0}): already in progress — skipping this trigger.", mode.MediaType);
            return false;
        }

        try
        {
            return await RunCoreAsync(mode, config, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<bool> RunCoreAsync(
        SeedMode mode,
        PluginConfiguration config,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(2);

        // 1. Locate this mode's seed playlist — tolerant match: trimmed, case-insensitive.
        var seedPlaylist = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { "Playlist" },
            Recursive = true,
        }).FirstOrDefault(p => string.Equals(p.Name?.Trim(), mode.PlaylistName, StringComparison.OrdinalIgnoreCase));

        if (seedPlaylist == null)
        {
            _logger.Debug("Seed run ({0}): playlist '{1}' not present — nothing to do.", mode.MediaType, mode.PlaylistName);
            return false;
        }

        // 2. Read seed metadata from the playlist's m3u (no tree queries).
        var seedRefs = ReadSeedMetadata(mode.PlaylistName);
        if (seedRefs.Count == 0)
        {
            _logger.Debug("Seed run ({0}): no parseable seeds in '{1}' m3u — nothing to do.", mode.MediaType, mode.PlaylistName);
            return false;
        }

        _logger.Info("Seed run ({0}): {1} seed(s) read from m3u for '{2}'", mode.MediaType, seedRefs.Count, mode.PlaylistName);
        progress?.Report(8);
        var processedKeys = new HashSet<string>(seedRefs.Select(Key), StringComparer.OrdinalIgnoreCase);

        // 3. One catalog pass over the mode's item types (exclusion + matching universe).
        var allItems = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = mode.IncludeItemTypes,
            Recursive = true,
        });
        var catalog = allItems
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(LibraryExtractor.MapItem)
            .ToList();
        if (catalog.Count == 0)
        {
            _logger.Warn("Seed run ({0}): no {1} items in the library — aborting.", mode.MediaType, string.Join("/", mode.IncludeItemTypes));
            return false;
        }

        // 4. Resolve seeds → library items (fuzzy — m3u metadata vs tags may differ).
        var matcher = new FuzzyMatcher();
        var resolvedSeeds = matcher.ResolveAll(seedRefs, catalog, 0.75);
        var seedIdSet = new HashSet<string>(
            resolvedSeeds.Matched.Select(m => m.Match.Id), StringComparer.Ordinal);
        if (seedIdSet.Count > 0 && seedIdSet.Count < seedRefs.Count)
            _logger.Warn("Seed run ({0}): {1} of {2} seeds could not be resolved to library items.",
                mode.MediaType, seedRefs.Count - seedIdSet.Count, seedRefs.Count);

        var generationCatalog = seedIdSet.Count > 0
            ? catalog.Where(t => !seedIdSet.Contains(t.Id)).ToList()
            : catalog;
        if (generationCatalog.Count == 0)
        {
            _logger.Warn("Seed run ({0}): catalog empty after removing seeds — aborting.", mode.MediaType);
            return false;
        }

        // 5. Seed-derived brief → shared pipeline (seeds excluded from catalog).
        var promptBuilder = new PromptBuilder { IncludeCatalogDetails = mode.IncludeCatalogDetails };
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
            _logger.ErrorException($"Seed run ({mode.MediaType}): generation failed — seeds left in place for retry.", ex);
            return false; // keep seeds: never lose the user's picks on a failed run
        }

        progress?.Report(88);

        // 6. Result playlist name: AI-generated when enabled (v0.3.1), else timestamped.
        //    Fallback keeps a batch unique; collision guard appends the date.
        var owner = ResolveOwner(config);
        var name = ResolveResultName(config, mode, result);
        var creator = new PlaylistCreator(_playlistManager, _logger);
        var playlistId = await creator.CreateAsync(name, result.PlaylistTracks, owner, mode.MediaType).ConfigureAwait(false);

        progress?.Report(96);

        // 7. Clear the seeds — but ONLY what we processed: re-read the m3u and keep
        //    anything that arrived mid-run (never wipe a user's fresh addition).
        var cleared = 0;
        if (config.SeedClearAfterRun)
        {
            var currentKeys = new HashSet<string>(
                ReadSeedMetadata(mode.PlaylistName).Select(Key), StringComparer.OrdinalIgnoreCase);
            var newSeeds = currentKeys.Count(k => !processedKeys.Contains(k));
            if (newSeeds > 0)
            {
                _logger.Info("Seed run ({0}): {1} new seed(s) arrived during the run — keeping ALL seeds for the next batch.",
                    mode.MediaType, newSeeds);
            }
            else
            {
                try
                {
                    _libraryManager.DeleteItem(seedPlaylist, new DeleteOptions { DeleteFileLocation = true });
                    await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
                    {
                        Name = mode.PlaylistName,
                        ItemIdList = Array.Empty<long>(),
                        MediaType = mode.MediaType,
                        User = owner,
                    }).ConfigureAwait(false);
                    cleared = seedRefs.Count;
                }
                catch (Exception ex)
                {
                    _logger.ErrorException($"Seed run ({mode.MediaType}): could not clear seeds (playlist will re-trigger).", ex);
                }
            }
        }

        _logger.Info(
            "Seed run ({0}): created '{1}' ({2}) with {3} items from {4} seed(s); {5} cleared; {6} unmatched",
            mode.MediaType, name, playlistId, result.Report.MatchedCount, seedRefs.Count, cleared, result.Report.UnmatchedCount);

        progress?.Report(100);
        return true;
    }

    /// <summary>Seed identity for m3u diffing: "artist - title".</summary>
    private static string Key(TrackRef t) => $"{t.Artist} - {t.Title}";

    /// <summary>
    /// Result playlist name (v0.3.1): prefer the model's evocative name when AI naming
    /// is on and it sanitizes to something usable; otherwise the timestamped base name.
    /// Then de-dupe against existing playlists so a run never silently overwrites/merges.
    /// </summary>
    private string ResolveResultName(PluginConfiguration config, SeedMode mode, GenerationResult result)
    {
        var candidate = config.UseAiNames ? PlaylistNameSanitizer.Sanitize(result.PlaylistName) : null;
        var name = candidate ?? $"{mode.ResultName} {DateTime.Now:MM-dd HH:mm}";

        // Collision guard: if a playlist with this name exists, append the date.
        if (PlaylistNameExists(name))
        {
            var dated = $"{name} ({DateTime.Now:MM-dd})";
            if (PlaylistNameExists(dated))
                dated = $"{name} ({DateTime.Now:MM-dd HH:mm})";
            name = dated;
        }

        if (candidate != null)
            _logger.Info("Seed run ({0}): AI-named playlist '{1}'", mode.MediaType, name);
        return name;
    }

    private bool PlaylistNameExists(string name)
        => _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { "Playlist" },
            Name = name,
            Recursive = true,
        }).Length > 0;

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