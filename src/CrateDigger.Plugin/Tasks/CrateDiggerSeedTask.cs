using CrateDigger.Plugin.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace CrateDigger.Plugin.Tasks;

/// <summary>
/// Interval backstop for the seed flow (v0.2.0): runs every 3 minutes, processes
/// whatever is in the seed playlist at tick time. The fast path since v0.2.1 is the
/// PlaylistItemsAdded debounce on the plugin entry point — both share SeedPipeline
/// (single gate prevents overlap; whichever arrives first wins, the other no-ops).
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
            var cfg = CrateDiggerPlugin.Instance?.Configuration;
            var audioName = string.IsNullOrWhiteSpace(cfg?.SeedPlaylistName) ? "(audio off)" : cfg!.SeedPlaylistName;
            var videoName = string.IsNullOrWhiteSpace(cfg?.SeedPlaylistNameVideo) ? "(video off)" : cfg!.SeedPlaylistNameVideo;
            return $"Backstop for both seed queues — audio: '{audioName}', video: '{videoName}'. " +
                   "Scans every few minutes if one is pending (the event debounce normally gets there first).";
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
        var config = CrateDiggerPlugin.Instance?.Configuration;
        if (config == null || !config.SeedTriggerEnabled)
        {
            progress.Report(100);
            return;
        }

        var pipeline = new SeedPipeline(_libraryManager, _playlistManager, _userManager, _logger);
        try
        {
            // v0.3.0: both queues per tick — each mode no-ops fast when its
            // playlist is absent or empty (gate protects cross-mode overlap).
            foreach (var mode in SeedMode.GetModes(config))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await pipeline.RunAsync(mode, config, progress, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            progress.Report(100);
        }
    }
}