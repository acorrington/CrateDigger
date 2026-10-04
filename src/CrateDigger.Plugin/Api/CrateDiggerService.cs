using System.Diagnostics;
using CrateDigger.Core;
using CrateDigger.Core.Llm;
using CrateDigger.Plugin.Configuration;
using CrateDigger.Plugin.Library;
using CrateDigger.Plugin.Playlists;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Services;

namespace CrateDigger.Plugin.Api;

/// <summary>
/// CrateDigger's REST surface (auto-discovered IService).
///
///   POST /CrateDigger/Create   { "prompt": "..." }   → { "jobId": "..." }
///   GET  /CrateDigger/Status?jobId=...               → staged progress / final result
///
/// Both DTOs carry [Authenticated]: the host's AuthService rejects calls without a
/// valid session token with HTTP 401 (spec ST-001) before reaching this code.
/// </summary>
public class CrateDiggerService : IService, IRequiresRequest
{
    /// <summary>Injected per-request by the host; used to resolve the calling user.</summary>
    public IRequest Request { get; set; } = null!;

    private readonly ILogger _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly IUserManager _userManager;
    private readonly IAuthorizationContext _authorizationContext;

    public CrateDiggerService(
        ILogManager logManager,
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        IUserManager userManager,
        IAuthorizationContext authorizationContext)
    {
        _logger = logManager.GetLogger("CrateDigger");
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _userManager = userManager;
        _authorizationContext = authorizationContext;
    }

    /// <summary>Kick off a generation run; returns immediately with a job id (spec §3.1).</summary>
    public object Post(CreateRequest request)
    {
        var plugin = CrateDiggerPlugin.Instance
            ?? throw new InvalidOperationException("CrateDigger plugin instance is not loaded.");

        if (string.IsNullOrWhiteSpace(request?.Prompt))
            throw new ArgumentException("A playlist prompt is required.");

        var config = plugin.Configuration;
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            throw new InvalidOperationException("Configure the LLM endpoint in the CrateDigger settings first.");

        // OpenAI requires a key; local endpoints (Ollama) do not — only hard-fail for OpenAI.
        if (config.BaseUrl.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new InvalidOperationException("An OpenAI API key is required — set it in the CrateDigger settings.");
        }

        // Capture per-request state NOW: Request/authorization are only valid during this call,
        // while the generation runs on a background task.
        var ownerUserId = ResolveOwnerUserId(config);
        var llmOptions = plugin.ToLlmOptions();
        var maxTracks = Math.Clamp(config.MaxPlaylistTracks, 1, 500);
        var threshold = Math.Clamp(config.MatchThreshold, 0.1, 1.0);

        var jobId = GenerationJobStore.Create();
        GenerationJobStore.Update(jobId, GenerationStage.Queued, "Queued…");

        _ = Task.Run(() => RunGenerationAsync(jobId, request.Prompt.Trim(), ownerUserId, llmOptions, maxTracks, threshold));

        return new CreateResponse { JobId = jobId };
    }

    public object Get(StatusRequest request)
    {
        GenerationJobStore.TryGet(request?.JobId ?? string.Empty, out var state);
        return state;
    }

    private async Task RunGenerationAsync(
        string jobId,
        string prompt,
        Guid ownerUserId,
        LlmOptions llmOptions,
        int maxTracks,
        double threshold)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. Read the local music library.
            GenerationJobStore.Update(jobId, GenerationStage.AnalyzingLibrary, "Analyzing library…");
            var extractor = new LibraryExtractor(_libraryManager);
            var library = extractor.GetTracks();
            _logger.Info("Job {0}: library scan found {1} tracks", jobId, library.Count);

            if (library.Count == 0)
                throw new InvalidOperationException("No music tracks found in the Emby library.");

            // 2-3. Ask the model, parse, fuzzy-match (progress reported by the pipeline).
            var progress = new StageReporter(stage => GenerationJobStore.Update(jobId, stage, MessageFor(stage)));
            var generator = new PlaylistGenerator(new LlmClient());

            var result = await generator.GenerateAsync(
                prompt,
                library,
                llmOptions,
                new GenerationOptions { MaxTracks = maxTracks, MatchThreshold = threshold },
                progress).ConfigureAwait(false);

            // 4. Create the native Emby playlist.
            GenerationJobStore.Update(jobId, GenerationStage.CreatingPlaylist, "Creating playlist…");

            var playlistName = string.IsNullOrWhiteSpace(result.PlaylistName)
                ? FallbackPlaylistName(prompt)
                : result.PlaylistName.Trim();

            var owner = ResolveOwnerUser(ownerUserId);
            var creator = new PlaylistCreator(_playlistManager, _logger);
            var playlistId = await creator.CreateAsync(playlistName, result.PlaylistTracks, owner).ConfigureAwait(false);

            var unmatchedLines = result.Report.Unmatched
                .Select(u => $"{u.Suggestion.Display} (best score {u.BestScore:F2})")
                .ToList();

            GenerationJobStore.Complete(jobId, playlistId, playlistName, result.Report.MatchedCount, unmatchedLines);

            _logger.Info(
                "Job {0}: created playlist '{1}' ({2}) with {3} tracks, {4} unmatched, elapsed {5:F1}s",
                jobId, playlistName, playlistId, result.Report.MatchedCount,
                result.Report.UnmatchedCount, stopwatch.Elapsed.TotalSeconds);
        }
        catch (LlmParseException ex)
        {
            var payload = ex.RawPayload ?? string.Empty;
            if (payload.Length > 2000) payload = payload[..2000] + "…(truncated)";
            _logger.ErrorException(
                $"Job {jobId}: LLM returned unparseable output ({ex.Message}). Raw payload: [{payload}]",
                ex);
            GenerationJobStore.Fail(jobId,
                "The model returned an unexpected response format. Try again, or switch models in settings.");
        }
        catch (LlmException ex)
        {
            _logger.ErrorException($"Job {jobId}: LLM call failed", ex);
            GenerationJobStore.Fail(jobId, $"AI request failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.ErrorException($"Job {jobId}: generation failed", ex);
            GenerationJobStore.Fail(jobId, ex.Message);
        }
    }

    /// <summary>
    /// Playlist owner: the authenticated session's user first (via IAuthorizationContext),
    /// then config fallback, then the server's first user as a last resort.
    /// </summary>
    private Guid ResolveOwnerUserId(PluginConfiguration config)
    {
        try
        {
            var auth = _authorizationContext.GetAuthorizationInfo(Request);
            if (auth != null)
            {
                if (auth.User != null)
                    return auth.User.Id;
                if (auth.UserId != 0)
                {
                    var byInternalId = _userManager.GetUserById(auth.UserId);
                    if (byInternalId != null)
                        return byInternalId.Id;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("Session user resolution failed, falling back to config: {0}", ex.Message);
        }

        if (Guid.TryParse(config.OwnerUserId, out var configured) && configured != Guid.Empty)
            return configured;

        var first = _userManager.GetUserList(new UserQuery());
        if (first != null && first.Length > 0)
            return first[0].Id;

        return Guid.Empty;
    }

    private MediaBrowser.Controller.Entities.User ResolveOwnerUser(Guid userId)
    {
        if (userId != Guid.Empty)
        {
            var user = _userManager.GetUserById(userId);
            if (user != null)
                return user;
        }

        var users = _userManager.GetUserList(new UserQuery());
        if (users != null && users.Length > 0)
            return users[0];

        throw new InvalidOperationException("Could not resolve a user to own the playlist.");
    }

    private static string MessageFor(GenerationStage stage) => stage switch
    {
        GenerationStage.AnalyzingLibrary => "Analyzing library…",
        GenerationStage.Thinking => "Thinking…",
        GenerationStage.MatchingTracks => "Matching tracks…",
        GenerationStage.CreatingPlaylist => "Creating playlist…",
        _ => "Working…",
    };

    private static string FallbackPlaylistName(string prompt)
    {
        var cleaned = new string(prompt.Trim()
            .Where(c => !char.IsControl(c))
            .Take(50)
            .ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned)
            ? $"CrateDigger {DateTime.Now:yyyy-MM-dd HH:mm}"
            : $"CrateDigger - {cleaned}";
    }

    /// <summary>Inline IProgress bridge — no SynchronizationContext indirection.</summary>
    private sealed class StageReporter(Action<GenerationStage> sink) : IProgress<GenerationStage>
    {
        public void Report(GenerationStage value) => sink(value);
    }
}