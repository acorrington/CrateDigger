using System.Collections.Concurrent;
using CrateDigger.Core;
using MediaBrowser.Model.Services;

namespace CrateDigger.Plugin.Api;

// ---------------------------------------------------------------------------
// Request/response DTOs + in-memory job store for the async generation flow.
//
// Per the official docs (dev.emby.media -> Creating Api Endpoints), routes live
// on the request DTOs:
//     [Route("/path", "VERB")] [Authenticated] class X : IReturn<Y>
// and the service exposes verb-named methods (Get/Post) taking the DTO.
//
// The Dashboard POSTs /CrateDigger/Create, receives a job id, then polls
// /CrateDigger/Status to drive the staged status indicator (spec §3.1).
// ---------------------------------------------------------------------------

[Route("/CrateDigger/Create", "POST")]
[MediaBrowser.Controller.Net.Authenticated]
public class CreateRequest : IReturn<CreateResponse>
{
    /// <summary>Natural-language playlist theme, e.g. "upbeat 80s synth for working out".</summary>
    public string Prompt { get; set; } = string.Empty;
}

public class CreateResponse
{
    public string JobId { get; set; } = string.Empty;
}

[Route("/CrateDigger/Status", "GET")]
[MediaBrowser.Controller.Net.Authenticated]
public class StatusRequest : IReturn<StatusResponse>
{
    public string JobId { get; set; } = string.Empty;
}

public class StatusResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Stage { get; set; } = GenerationStage.Queued.ToString();
    public string Message { get; set; } = "Queued…";
    public bool Done { get; set; }
    public bool Success { get; set; }

    /// <summary>Set when generation failed; safe-to-display text.</summary>
    public string Error { get; set; } = string.Empty;

    public string PlaylistId { get; set; } = string.Empty;
    public string PlaylistName { get; set; } = string.Empty;
    public int Matched { get; set; }
    public int Unmatched { get; set; }

    /// <summary>Human-readable lines for suggestions that could not be matched.</summary>
    public List<string> UnmatchedTracks { get; set; } = new();
}

/// <summary>Process-local job tracking. Jobs live only for the duration of a generation run.</summary>
public static class GenerationJobStore
{
    private sealed class Job
    {
        public string Id { get; init; } = string.Empty;
        public StatusResponse State { get; } = new();
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    }

    private static readonly ConcurrentDictionary<string, Job> Jobs = new();

    public static string Create()
    {
        var job = new Job { Id = Guid.NewGuid().ToString("N") };
        Jobs[job.Id] = job;
        Prune();
        return job.Id;
    }

    public static bool TryGet(string jobId, out StatusResponse state)
    {
        if (!string.IsNullOrEmpty(jobId) && Jobs.TryGetValue(jobId, out var job))
        {
            state = job.State;
            return true;
        }
        // Unknown/expired job: report Done so the dashboard poller stops instead of
        // sitting on an eternal "Queued" (defense in depth for client-side bugs).
        state = new StatusResponse
        {
            JobId = jobId ?? string.Empty,
            Stage = GenerationStage.Failed.ToString(),
            Message = "Failed.",
            Error = "Unknown job id.",
            Done = true,
            Success = false,
        };
        return false;
    }

    public static void Update(string jobId, GenerationStage stage, string message)
    {
        if (!Jobs.TryGetValue(jobId, out var job)) return;
        lock (job.State)
        {
            job.State.Stage = stage.ToString();
            job.State.Message = message;
            if (stage is GenerationStage.Completed or GenerationStage.Failed)
            {
                job.State.Done = true;
                job.State.Success = stage == GenerationStage.Completed;
            }
        }
    }

    public static void Complete(
        string jobId,
        string playlistId,
        string playlistName,
        int matched,
        IEnumerable<string> unmatched)
    {
        if (!Jobs.TryGetValue(jobId, out var job)) return;
        lock (job.State)
        {
            job.State.PlaylistId = playlistId;
            job.State.PlaylistName = playlistName;
            job.State.Matched = matched;
            job.State.UnmatchedTracks = unmatched.ToList();
            job.State.Unmatched = job.State.UnmatchedTracks.Count;
            job.State.Stage = GenerationStage.Completed.ToString();
            job.State.Message = "Playlist created.";
            job.State.Done = true;
            job.State.Success = true;
        }
    }

    public static void Fail(string jobId, string error)
    {
        if (!Jobs.TryGetValue(jobId, out var job)) return;
        lock (job.State)
        {
            job.State.Stage = GenerationStage.Failed.ToString();
            job.State.Error = error;
            job.State.Message = "Failed.";
            job.State.Done = true;
            job.State.Success = false;
        }
    }

    /// <summary>Drop jobs older than an hour so the store cannot grow unbounded.</summary>
    private static void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        foreach (var pair in Jobs.Where(j => j.Value.CreatedAt < cutoff))
            Jobs.TryRemove(pair.Key, out _);
    }
}