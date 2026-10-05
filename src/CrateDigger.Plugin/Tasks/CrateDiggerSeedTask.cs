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
            var seedName = CrateDiggerPlugin.Instance?.Configuration?.SeedPlaylistName ?? "CrateDigger Seeds";
            var resultName = CrateDiggerPlugin.Instance?.Configuration?.SeedResultName ?? "CrateDigger Radio";
            return $"Backstop for the seed flow: scans '{seedName}' every few minutes and generates " +
                   $"a '{resultName}' playlist if one is pending (the event debounce normally gets there first).";
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
            await pipeline.RunAsync(config, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            progress.Report(100);
        }
    }
}