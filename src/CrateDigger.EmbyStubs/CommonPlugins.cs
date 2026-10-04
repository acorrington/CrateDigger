using MediaBrowser.Common;
using MediaBrowser.Model.Plugins;

namespace MediaBrowser.Common.Plugins
{
    /// <summary>Base contract every Emby plugin implements.</summary>
    public interface IPlugin
    {
        Guid Id { get; }
        string Name { get; }
        string Description { get; }
    }

    /// <summary>
    /// Convenience base class handling configuration loading/saving.
    /// The real implementation persists <see cref="Configuration"/> to
    /// programdata\plugins\configurations\{Name}.xml.
    /// </summary>
    public abstract class BasePlugin<TConfiguration> : IPlugin
        where TConfiguration : class, IPluginConfiguration, new()
    {
        protected BasePlugin(IApplicationHost applicationHost, ILogger logger)
        {
            ApplicationHost = applicationHost;
            Logger = logger;
            Configuration = new TConfiguration();
        }

        public IApplicationHost ApplicationHost { get; }

        public ILogger Logger { get; }

        /// <summary>Live configuration; mutations are saved via the Dashboard.</summary>
        public TConfiguration Configuration { get; set; }

        /// <summary>Stable plugin identity. Override for a fixed GUID (used by config-page JS).</summary>
        public virtual Guid Id { get; } = Guid.NewGuid();

        public abstract string Name { get; }

        public virtual string Description => Name;
    }
}