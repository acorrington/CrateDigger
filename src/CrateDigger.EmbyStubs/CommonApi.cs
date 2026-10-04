namespace MediaBrowser.Common.Api
{
    /// <summary>
    /// Marker implemented by plugin REST services; the host registers their routes
    /// on the internal web gateway. VERIFY the real namespace + registration model.
    /// </summary>
    public interface IRestfulService
    {
    }

    /// <summary>Declares an HTTP route for a service method. VERIFY attribute name/verbs.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class RouteAttribute : Attribute
    {
        public RouteAttribute(string path, string verb = "GET")
        {
            Path = path;
            Verb = verb;
        }

        /// <summary>Route path relative to the server root, e.g. "/CrateDigger/Create".</summary>
        public string Path { get; }

        /// <summary>HTTP verb: GET, POST, DELETE, ...</summary>
        public string Verb { get; }
    }
}