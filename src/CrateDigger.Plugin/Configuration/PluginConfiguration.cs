using CrateDigger.Core.Models;
using MediaBrowser.Model.Plugins;

namespace CrateDigger.Plugin.Configuration;

/// <summary>
/// Persisted plugin settings (BasePlugin writes these to
/// programdata\plugins\configurations\ as XML; served by GET/POST /Plugins/{Id}/Configuration).
///
/// SECURITY: persisted as plain XML on disk — the API key is unencrypted unless
/// DPAPI hardening is added (tracked in README).
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>OpenAI API key (or compatible endpoint key). Empty for key-less local servers.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>OpenAI-compatible base URL. Default: https://api.openai.com/v1</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Model id, e.g. gpt-4o-mini, llama3.1 (Ollama).</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Fuzzy-match acceptance threshold in [0,1]; lower = looser matching.</summary>
    public double MatchThreshold { get; set; } = 0.75;

    /// <summary>Target playlist length requested from the model.</summary>
    public int MaxPlaylistTracks { get; set; } = 30;

    /// <summary>
    /// Per-request LLM timeout in seconds. Local models (Unsloth/llama.cpp/vLLM) serving
    /// large prompts can easily exceed the old 60s default — 300s suits a 27B quantized model.
    /// </summary>
    public int LlmTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Cap on generated tokens. Local servers often default to unlimited output —
    /// a runaway generation would hang until the timeout. 0 = no cap.
    /// 16384 since v0.1.5: 8192 proved too small when qwen reasoned its way through
    /// a long tracklist — truncation mid-JSON (observed live: cut at `"artist]`).
    /// </summary>
    public int LlmMaxTokens { get; set; } = 16384;

    /// <summary>
    /// Raw JSON merged into the LLM request body for server-specific options, e.g.
    /// {"chat_template_kwargs":{"enable_thinking":false}} (llama.cpp / Unsloth).
    /// </summary>
    public string LlmExtraJson { get; set; } = string.Empty;

    /// <summary>
    /// Optional fallback playlist owner (user Guid). Normally the requesting session's
    /// user is resolved automatically via IAuthorizationContext; this is only used
    /// when session resolution fails (and by the seed task, which has no session).
    /// </summary>
    public string OwnerUserId { get; set; } = string.Empty;

    // ------------------------------------------------------------------
    // Seed-playlist trigger (v0.2.0): the user-facing flow — add songs to
    // the seed playlist from ANY Emby app's "Add to playlist" menu; the
    // CrateDiggerSeedTask scheduled task turns them into a fresh playlist.
    // ------------------------------------------------------------------

    /// <summary>Master switch for the scheduled seed task.</summary>
    public bool SeedTriggerEnabled { get; set; } = true;

    /// <summary>
    /// Playlist the user adds seed songs to. Self-documenting default (v0.3.1) — the
    /// name appears verbatim in every client's "Add to playlist" picker, so it is the
    /// instruction. Empty = audio queue disabled.
    /// </summary>
    public string SeedPlaylistName { get; set; } = PlaylistNames.SeedAudio;

    /// <summary>Fallback base name for generated results when AI naming is off/absent.</summary>
    public string SeedResultName { get; set; } = PlaylistNames.ResultAudio;

    /// <summary>Remove seed entries after a successful run (one batch = one playlist).</summary>
    public bool SeedClearAfterRun { get; set; } = true;

    /// <summary>
    /// v0.2.1: react to PlaylistItemsAdded instead of waiting for the interval tick.
    /// Generation starts after <see cref="SeedDebounceSeconds"/> of QUIET — each new
    /// add resets the window (classic debounce), so a session of adding = one batch.
    /// The 3-minute interval task remains as the safety-net backstop.
    /// </summary>
    public bool SeedDebounceEnabled { get; set; } = true;

    /// <summary>Quiet window in seconds before a debounced run starts (5–3600).</summary>
    public int SeedDebounceSeconds { get; set; } = 60;

    // ------------------------------------------------------------------
    // Video seed queue (v0.3.0): Emby playlists are single-media-type, so
    // music-video seeds live in their own playlist with their own result
    // name. Both queues are watched by the same triggers. Empty name = off.
    // ------------------------------------------------------------------

    /// <summary>Seed playlist for music videos (created on demand in any client).</summary>
    public string SeedPlaylistNameVideo { get; set; } = PlaylistNames.SeedVideo;

    /// <summary>Fallback base name for generated video results when AI naming is off/absent.</summary>
    public string SeedResultNameVideo { get; set; } = PlaylistNames.ResultVideo;

    // ------------------------------------------------------------------
    // AI-generated result names (v0.3.1)
    // ------------------------------------------------------------------

    /// <summary>
    /// Use the model's evocative playlist name (from the same completion we already
    /// parse) instead of the timestamped fallback. Falls back automatically when the
    /// name is missing/unusable, and de-dupes against existing playlists.
    /// </summary>
    public bool UseAiNames { get; set; } = true;
}