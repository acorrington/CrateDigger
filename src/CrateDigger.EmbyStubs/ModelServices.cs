// Verified against Emby 4.10.1.0: services live in MediaBrowser.Model.Services.
// (There is NO IRestfulService — that was an early guess, corrected by probe.)

namespace MediaBrowser.Model.Services
{
    /// <summary>Marker implemented by plugin REST services; auto-discovered by the host.</summary>
    public interface IService
    {
    }

    /// <summary>Implemented by services that need the current HTTP request injected.</summary>
    public interface IRequiresRequest
    {
        IRequest Request { get; set; }
    }

    /// <summary>Minimal view of the injected request (real interface is much larger).</summary>
    public interface IRequest
    {
        /// <summary>Raw Authorization header value.</summary>
        string? Authorization { get; }

        string? PathInfo { get; }

        string? HttpMethod { get; }
    }

    /// <summary>
    /// Declares an HTTP route — on the REQUEST DTO class (not the service method).
    /// Usage: [Route("/CrateDigger/Create", "POST")] class CreateRequest : IReturn<CreateResponse>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class RouteAttribute : Attribute
    {
        public RouteAttribute(string path) => Path = path;

        public RouteAttribute(string path, string verbs)
        {
            Path = path;
            Verbs = verbs;
        }

        public string Path { get; }

        /// <summary>e.g. "GET", "POST", "GET,POST". Null = inferred from the service method name.</summary>
        public string? Verbs { get; set; }

        public string? Summary { get; set; }

        public string? Description { get; set; }
    }

    /// <summary>Documents the response type of a request DTO (optional but conventional).</summary>
    public interface IReturn
    {
    }

    public interface IReturn<T> : IReturn
    {
    }
}