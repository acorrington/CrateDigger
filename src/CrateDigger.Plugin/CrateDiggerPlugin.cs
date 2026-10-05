using CrateDigger.Core.Llm;
using CrateDigger.Plugin.Configuration;
using CrateDigger.Plugin.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System.Reflection;

namespace CrateDigger.Plugin;

/// <summary>
/// CrateDigger — turn a plain-English vibe into a native Emby playlist.
///
/// Combines the three auto-discovered roles Emby supports (see dev.emby.media docs):
///   * BasePlugin&lt;T&gt;     — identity + persisted configuration
///   * IHasWebPages    — the Dashboard config page (HTML + paired AMD JS module)
///   * IServerEntryPoint — Run() at server startup
/// REST endpoints live in <see cref="Api.CrateDiggerService"/> (IService, also auto-discovered).
/// </summary>
public class CrateDiggerPlugin : BasePlugin<PluginConfiguration>, IHasWebPages, IServerEntryPoint, IHasThumbImage
{
    /// <summary>Embedded artwork served by GetThumbImage (see tools/make-thumb.ps1).</summary>
    private const string ThumbResourceName = "CrateDigger.Plugin.thumb.png";

    /// <summary>Fixed identity shared with Resources/configPage.js (PluginUniqueId).</summary>
    public static readonly Guid PluginGuid = new("9b2f7a1c-5d4e-4a68-b3f1-8c0e2d7f6a54");

    /// <summary>Latest instance; the service reaches it statically (one instance per server).</summary>
    public static CrateDiggerPlugin? Instance { get; private set; }

    private readonly ILogger _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly IUserManager _userManager;

    // Debounce state (v0.2.1): each add resets this timer; fire after quiet window.
    private readonly object _debounceLock = new();
    private CancellationTokenSource? _debounceCts;

    public CrateDiggerPlugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogManager logManager,
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        IUserManager userManager)
        : base(applicationPaths, xmlSerializer)
    {
        _logger = logManager.GetLogger("CrateDigger");
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _userManager = userManager;
        Instance = this;
    }

    public override Guid Id => PluginGuid;

    public override string Name => "CrateDigger";

    public override string Description =>
        "AI playlist generator: describe a vibe in plain English and CrateDigger digs the matching " +
        "tracks out of your local music library and builds a native Emby playlist.";

    /// <summary>
    /// Page entries follow the first-party convention (see MBBackup): an HTML page plus its
    /// AMD controller module, referenced from the markup via data-controller="__plugin/...".
    ///
    /// NOTE: the resource URL is web/ConfigurationPage?name=...<emby-app-version> — the
    /// cache-busting query only reflects the SERVER version, so plugin updates keep the same
    /// URL and stale browser copies can linger. Bump these names (and the matching
    /// data-controller in configPage.html) on releases when cache issues appear.
    /// Current generation: v7 (seed debounce: event-driven runs with reset-on-add quiet window).
    /// </summary>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "cratedigger7",
            DisplayName = "CrateDigger",
            EnableInMainMenu = true,
            MenuSection = "settings",
            MenuIcon = "playlist",
            EmbeddedResourcePath = GetType().Namespace + ".Resources.configPage.html",
        };

        yield return new PluginPageInfo
        {
            Name = "cratediggerjs7",
            EmbeddedResourcePath = GetType().Namespace + ".Resources.configPage.js",
        };
    }

    /// <summary>
    /// Plugins-page thumbnail (GET /Plugins/{Id}/Thumb). The handler resolves the
    /// plugin and reads IHasThumbImage without a null-guard — a plugin WITHOUT this
    /// interface makes the endpoint 500 with an NRE (verified live on 4.10.1.0).
    /// </summary>
    public ImageFormat ThumbImageFormat => ImageFormat.Png;

    public Stream GetThumbImage() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(ThumbResourceName)
        ?? throw new InvalidOperationException($"Embedded thumb resource '{ThumbResourceName}' not found.");

    /// <summary>Called by the server after plugins load.</summary>
    public void Run()
    {
        _logger.Info("CrateDigger v{0} loaded (endpoint: {1}, model: {2})",
            GetType().Assembly.GetName().Version,
            Configuration.BaseUrl,
            Configuration.Model);

        // v0.2.1: event-driven seed runs — generation starts after a quiet window
        // that RESETS on every add (classic debounce); the interval task is backstop.
        _playlistManager.PlaylistItemsAdded += OnPlaylistItemsAdded;
    }

    /// <summary>
    /// Fires whenever ANY user adds items to any playlist. We filter to the seed
    /// playlist, then (re)arm the debounce timer.
    /// </summary>
    private void OnPlaylistItemsAdded(object? sender, PlaylistItemsAddedEventArgs e)
    {
        try
        {
            var config = Configuration;
            if (!config.SeedTriggerEnabled || !config.SeedDebounceEnabled)
                return;
            if (e?.Playlist == null ||
                !string.Equals(e.Playlist.Name, config.SeedPlaylistName, StringComparison.Ordinal))
                return;

            var seconds = Math.Clamp(config.SeedDebounceSeconds, 5, 3600);
            CancellationTokenSource cts;
            lock (_debounceLock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = cts = new CancellationTokenSource();
            }

            _logger.Info("Seed debounce: item(s) added to '{0}' — starting run in {1}s (timer resets on each add)",
                e.Playlist.Name, seconds);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return; // a newer add reset the quiet window — that timer owns the run
                }

                _logger.Info("Seed debounce: quiet window elapsed — starting run.");
                var pipeline = new SeedPipeline(_libraryManager, _playlistManager, _userManager, _logger);
                try
                {
                    await pipeline.RunAsync(Configuration, null, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Seed debounce: run failed.", ex);
                }
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.ErrorException("Seed debounce: event handler failed.", ex);
        }
    }

    public void Dispose()
    {
        try
        {
            _playlistManager.PlaylistItemsAdded -= OnPlaylistItemsAdded;
        }
        catch
        {
            // server may be tearing down
        }

        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }

        Instance = null;
    }

    /// <summary>Builds LLM connection options from the current configuration.</summary>
    public LlmOptions ToLlmOptions() => new()
    {
        ApiKey = Configuration.ApiKey?.Trim() ?? string.Empty,
        BaseUrl = Configuration.BaseUrl?.Trim() ?? string.Empty,
        Model = string.IsNullOrWhiteSpace(Configuration.Model) ? "gpt-4o-mini" : Configuration.Model.Trim(),
        TimeoutSeconds = Math.Clamp(Configuration.LlmTimeoutSeconds, 10, 3600),
        MaxRetries = 1,
        MaxTokens = Math.Clamp(Configuration.LlmMaxTokens, 0, 1_000_000),
        ExtraJson = Configuration.LlmExtraJson?.Trim() ?? string.Empty,
    };
}