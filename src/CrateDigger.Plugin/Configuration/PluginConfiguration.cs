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
    /// </summary>
    public int LlmMaxTokens { get; set; } = 8192;

    /// <summary>
    /// Raw JSON merged into the LLM request body for server-specific options, e.g.
    /// {"chat_template_kwargs":{"enable_thinking":false}} (llama.cpp / Unsloth).
    /// </summary>
    public string LlmExtraJson { get; set; } = string.Empty;

    /// <summary>
    /// Optional fallback playlist owner (user Guid). Normally the requesting session's
    /// user is resolved automatically via IAuthorizationContext; this is only used
    /// when session resolution fails.
    /// </summary>
    public string OwnerUserId { get; set; } = string.Empty;
}