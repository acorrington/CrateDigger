using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Common
{
    /// <summary>Root host providing services to plugins (minimal verified view).</summary>
    public interface IApplicationHost
    {
        T Resolve<T>() where T : class;
    }
}

namespace MediaBrowser.Common.Configuration
{
    /// <summary>Application path facts injected into plugin constructors.</summary>
    public interface IApplicationPaths
    {
    }
}

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
    /// Verified against 4.10.1.0:
    ///   protected BasePlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
    ///   Name = virtual abstract; Id/Description = virtual; Configuration = get/set.
    /// </summary>
    public abstract class BasePlugin<TConfiguration> : IPlugin
        where TConfiguration : class, IPluginConfiguration, new()
    {
        protected BasePlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        {
            ApplicationPaths = applicationPaths;
            XmlSerializer = xmlSerializer;
            Configuration = new TConfiguration();
        }

        public IApplicationPaths ApplicationPaths { get; }

        public IXmlSerializer XmlSerializer { get; }

        /// <summary>Live configuration; persisted by the server on update.</summary>
        public TConfiguration Configuration { get; set; }

        public virtual Guid Id { get; } = Guid.NewGuid();

        public abstract string Name { get; }

        public virtual string Description => Name;

        /// <summary>
        /// Verified on real BasePlugin&lt;T&gt;: full path of the persisted configuration
        /// file (...\plugins\configurations\X.xml) — the seed task derives programdata
        /// from it to locate playlist m3u files.
        /// </summary>
        public string ConfigurationFilePath { get; set; } = string.Empty;
    }
}