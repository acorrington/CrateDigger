namespace CrateDigger.Core.Llm;

/// <summary>One message in a chat-completions conversation.</summary>
public sealed record LlmMessage(string Role, string Content);

/// <summary>A fully-built chat request ready for an OpenAI-compatible endpoint.</summary>
public sealed record LlmRequest(IReadOnlyList<LlmMessage> Messages)
{
    public LlmRequest(params LlmMessage[] messages) : this((IReadOnlyList<LlmMessage>)messages) { }
}

/// <summary>Connection + model settings for the LLM endpoint.</summary>
public sealed record LlmOptions
{
    /// <summary>API key. May be empty for local endpoints (Ollama, LM Studio).</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// OpenAI-compatible base URL, e.g. https://api.openai.com/v1,
    /// http://localhost:11434/v1 (Ollama), http://localhost:1234/v1 (LM Studio).
    /// </summary>
    public string BaseUrl { get; init; } = "https://api.openai.com/v1";

    public string Model { get; init; } = "gpt-4o-mini";

    public double Temperature { get; init; } = 0.8;

    public int TimeoutSeconds { get; init; } = 60;

    /// <summary>Additional attempts on 429/5xx responses (total attempts = 1 + MaxRetries).</summary>
    public int MaxRetries { get; init; } = 1;

    /// <summary>
    /// Cap on generated tokens (OpenAI `max_tokens`). Critical for local servers whose
    /// default is unlimited — a runaway generation would otherwise hang until the timeout.
    /// 0 disables the cap.
    /// </summary>
    public int MaxTokens { get; init; } = 8192;

    /// <summary>
    /// Raw JSON object merged into the request body for server-specific options,
    /// e.g. {"chat_template_kwargs":{"enable_thinking":false}} for llama.cpp/Unsloth.
    /// Empty = nothing extra.
    /// </summary>
    public string ExtraJson { get; init; } = string.Empty;
}