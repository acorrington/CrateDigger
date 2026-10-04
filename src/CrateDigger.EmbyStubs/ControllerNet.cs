namespace MediaBrowser.Controller.Net
{
    using MediaBrowser.Model.Services;

    /// <summary>Resolves the authenticated session behind an IRequest.</summary>
    public interface IAuthorizationContext
    {
        AuthorizationInfo GetAuthorizationInfo(IRequest requestContext);
    }

    /// <summary>Verified members used by CrateDigger.</summary>
    public class AuthorizationInfo
    {
        /// <summary>Internal (Int64) user id — NOT the public Guid.</summary>
        public long UserId { get; set; }

        /// <summary>Resolved user entity (may be null when unauthenticated).</summary>
        public MediaBrowser.Controller.Entities.User? User { get; set; }

        public string? Token { get; set; }

        public string? Client { get; set; }

        public string? Device { get; set; }
    }

    /// <summary>
    /// Marks a request DTO as requiring an authenticated session;
    /// unauthenticated calls receive HTTP 401 from the host's AuthService.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class AuthenticatedAttribute : Attribute
    {
        public string? Roles { get; set; }

        public bool AllowBeforeStartupWizard { get; set; }

        public bool AllowLocal { get; set; }
    }

    /// <summary>Opposite of <see cref="AuthenticatedAttribute"/> (present on login DTOs).</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class UnauthenticatedAttribute : Attribute
    {
    }
}