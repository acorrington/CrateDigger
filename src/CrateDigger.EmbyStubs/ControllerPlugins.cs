namespace MediaBrowser.Controller.Plugins
{
    /// <summary>
    /// Long-lived object started by the server after all plugins load.
    /// </summary>
    public interface IServerEntryPoint
    {
        Task RunAsync();

        void Dispose();
    }
}