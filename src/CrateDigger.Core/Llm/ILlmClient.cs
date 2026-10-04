namespace CrateDigger.Core.Llm;

/// <summary>
/// Abstraction over an OpenAI-compatible chat-completions endpoint.
/// Returns the raw assistant message content; parsing lives in <see cref="ResponseParser"/>.
/// </summary>
public interface ILlmClient
{
    Task<string> CompleteAsync(LlmRequest request, LlmOptions options, CancellationToken cancellationToken = default);
}