// ---------------------------------------------------------------------------
// COMPILE-TIME STAND-INS FOR THE EMBY SERVER PLUGIN API (Emby 4.10.1.0).
//
// These types mirror the VERIFIED shapes documented in docs/api-surface.md
// (confirmed by compiling against the real MediaBrowser.*.dll, reflection
// probes in tools/EmbyApiProbe, first-party plugin decompiles, and the
// official docs at dev.emby.media). When the real DLLs are dropped into lib\,
// CrateDigger.Plugin references those instead and any drift is a compile error.
// ---------------------------------------------------------------------------

namespace MediaBrowser.Model.Plugins
{
    /// <summary>Marker for persisted plugin configuration (real BasePlugin<T> constrains on it).</summary>
    public interface IPluginConfiguration
    {
    }

    /// <summary>
    /// Base class for persisted plugin settings; the real server serializes
    /// derived types to programdata\configurations\{Name}.xml.
    /// Real linkage: BasePluginConfiguration implements IPluginConfiguration.
    /// </summary>
    public class BasePluginConfiguration : IPluginConfiguration
    {
    }

    /// <summary>Marks a plugin that contributes HTML pages to the Emby Dashboard.</summary>
    public interface IHasWebPages
    {
        IEnumerable<PluginPageInfo> GetPages();
    }

    /// <summary>Describes one embedded HTML/JS page served by the plugin.</summary>
    public class PluginPageInfo
    {
        /// <summary>Internal page name (web/ConfigurationPage?name=...).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Human-readable title.</summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// Full embedded-resource name, e.g. "CrateDigger.Plugin.Resources.configPage.html".
        /// Verified property name: EmbeddedResourcePath (NOT EmbeddedResourceName).
        /// </summary>
        public string? EmbeddedResourcePath { get; set; }

        public bool EnableInMainMenu { get; set; }

        public bool EnableInUserMenu { get; set; }

        public string? MenuSection { get; set; }

        public string? MenuIcon { get; set; }

        public string? FeatureId { get; set; }

        public bool IsMainConfigPage { get; set; }
    }
}