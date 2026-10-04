namespace MediaBrowser.Controller.Plugins
{
    /// <summary>
    /// Verified against 4.10.1.0: void Run() + IDisposable
    /// (NOT RunAsync — that was an early guess, corrected by probe).
    /// </summary>
    public interface IServerEntryPoint : IDisposable
    {
        void Run();
    }
}