// Verified: Emby's logger lives in MediaBrowser.Model.Logging and is obtained
// from ILogManager.GetLogger(name). Methods take (string, params object[]).

namespace MediaBrowser.Model.Logging
{
    public interface ILogger
    {
        void Debug(string message, params object?[] paramList);

        void Info(string message, params object?[] paramList);

        void Warn(string message, params object?[] paramList);

        void Error(string message, params object?[] paramList);

        void ErrorException(string message, Exception exception, params object?[] paramList);

        void Fatal(string message, params object?[] paramList);
    }

    public interface ILogManager
    {
        ILogger GetLogger(string name);
    }
}

namespace MediaBrowser.Model.Serialization
{
    /// <summary>XML serializer used by BasePlugin for configuration persistence.</summary>
    public interface IXmlSerializer
    {
    }
}

namespace MediaBrowser.Model.Querying
{
    /// <summary>User listing query (parameterless construction).</summary>
    public class UserQuery
    {
    }
}