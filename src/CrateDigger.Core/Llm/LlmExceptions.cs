namespace CrateDigger.Core.Llm;

/// <summary>Transport-level failure talking to the LLM endpoint (HTTP error, timeout, empty body).</summary>
public sealed class LlmException : Exception
{
    public int? StatusCode { get; }

    public LlmException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

/// <summary>The LLM responded, but its payload could not be turned into a tracklist.</summary>
public sealed class LlmParseException : Exception
{
    public string? RawPayload { get; }

    public LlmParseException(string message, string? rawPayload = null, Exception? inner = null)
        : base(message, inner)
    {
        RawPayload = rawPayload;
    }
}