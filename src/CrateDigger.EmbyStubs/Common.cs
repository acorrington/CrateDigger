namespace MediaBrowser.Common
{
    /// <summary>
    /// Emby's logger abstraction.
    /// NOTE: exact namespace/method set varies by Emby version — verify against the real DLL.
    /// </summary>
    public interface ILogger
    {
        void Debug(string message, params object?[] args);
        void Info(string message, params object?[] args);
        void Warn(string message, params object?[] args);
        void Error(string message, params object?[] args);
        void Error(Exception ex, string message, params object?[] args);
    }

    /// <summary>Root host providing services to plugins (DI entry point).</summary>
    public interface IApplicationHost
    {
        /// <summary>Resolves a registered service by type (stub shape — verify).</summary>
        T GetService<T>() where T : class;
    }
}