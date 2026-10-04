// ---------------------------------------------------------------------------
// COMPILE-TIME STAND-INS FOR THE EMBY SERVER PLUGIN API.
//
// These types mirror the shape of the real MediaBrowser.*.dll assemblies so the
// plugin can be developed on machines without an Emby installation. When the
// real DLLs are dropped into lib\, CrateDigger.Plugin references those instead
// and any signature drift is reported as a compile error (see docs/api-surface.md).
//
// Plausibility notes (verify against the real DLLs, ILSpy/dotPeek):
//   * IPlugin/BasePlugin      -> MediaBrowser.Common.Plugins
//   * IHasWebPages/PluginPage -> MediaBrowser.Model.Plugins
//   * IServerEntryPoint       -> MediaBrowser.Controller.Plugins
//   * IRestfulService + Route -> MediaBrowser.Common.Api (EXACT namespace/verb
//     mechanism is the biggest unknown — Emby is closed-source and version-specific)
// ---------------------------------------------------------------------------

namespace MediaBrowser.Model.Plugins
{
    /// <summary>Marker for plugin configuration objects persisted as XML by the server.</summary>
    public interface IPluginConfiguration
    {
    }

    /// <summary>Marks a plugin that contributes HTML pages to the Emby Dashboard.</summary>
    public interface IHasWebPages
    {
        IEnumerable<PluginPageInfo> GetPages();
    }

    /// <summary>Describes one embedded HTML page served by the plugin.</summary>
    public class PluginPageInfo
    {
        /// <summary>Internal page name (used in URLs/lookups).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Human-readable title.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Show the page in the main Dashboard menu.</summary>
        public bool EnableInMainMenu { get; set; }

        public string? MenuSection { get; set; }

        public string? MenuIcon { get; set; }

        /// <summary>
        /// Assembly-qualified name of the embedded HTML resource,
        /// e.g. "CrateDigger.Plugin.Resources.configPage.html".
        /// </summary>
        public string? EmbeddedResourceName { get; set; }

        /// <summary>Alternative: resource holding only the configuration fragment.</summary>
        public string? ConfigHtmlResourceName { get; set; }
    }
}