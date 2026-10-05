// Verified against Emby 4.10.1.0 (probe round 15/16/17): scheduled tasks.

namespace MediaBrowser.Model.Tasks
{
    /// <summary>
    /// Auto-discovered background task (Automatic Type Discovery — same mechanism as
    /// IServerEntryPoint). Verified members: 4 name/description props, Execute,
    /// GetDefaultTriggers. (First-party samples add IsHidden/IsEnabled/IsLogged —
    /// those come from a separate optional interface, not required here.)
    /// </summary>
    public interface IScheduledTask
    {
        string Name { get; }

        string Key { get; }

        string Description { get; }

        string Category { get; }

        Task Execute(CancellationToken cancellationToken, IProgress<double> progress);

        IEnumerable<TaskTriggerInfo> GetDefaultTriggers();
    }

    /// <summary>Verified members; interval scheduling uses Type + IntervalTicks (TimeSpan ticks).</summary>
    public class TaskTriggerInfo
    {
        /// <summary>Verified literals: "IntervalTrigger", "StartupTrigger".</summary>
        public string Type { get; set; } = string.Empty;

        public long? IntervalTicks { get; set; }

        public long? TimeOfDayTicks { get; set; }

        public string? SystemEvent { get; set; }

        public int? DayOfWeek { get; set; }

        public long? MaxRuntimeTicks { get; set; }
    }
}