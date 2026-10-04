using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrateDigger.Core.Llm;

/// <summary>
/// Minimal OpenAI-compatible chat-completions client.
/// Works against OpenAI, Ollama (/v1), LM Studio (/v1), OpenRouter, etc.
/// </summary>
public sealed class LlmClient : ILlmClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public LlmClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;

        // HttpClient.Timeout defaults to 100s and would fire BEFORE our configurable
        // per-request timeout (options.TimeoutSeconds) — disable it and rely solely
        // on the linked CancellationTokenSource below.
        try
        {
            _http.Timeout = Timeout.InfiniteTimeSpan;
        }
        catch (InvalidOperationException)
        {
            // Shared client already used by a request — its timeout must stay as-is.
        }
    }

    public async Task<string> CompleteAsync(
        LlmRequest request,
        LlmOptions options,
        CancellationToken cancellationToken = default)
    {
        var url = BuildChatCompletionsUrl(options.BaseUrl);
        var payload = BuildPayload(request, options);
        var body = JsonSerializer.Serialize(payload, JsonOptions);
        var maxAttempts = Math.Max(1, options.MaxRetries + 1);
        Exception? lastTransportError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                    message.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);

                using var response = await _http.SendAsync(message, timeoutCts.Token).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var content = ExtractContent(responseBody);
                    if (string.IsNullOrWhiteSpace(content))
                        throw new LlmException("LLM returned an empty completion.", (int)response.StatusCode);
                    return content;
                }

                var retriable = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
                if (!retriable || attempt == maxAttempts)
                    throw new LlmException(
                        $"LLM request failed with HTTP {(int)response.StatusCode}: {Truncate(responseBody, 500)}",
                        (int)response.StatusCode);

                lastTransportError = new LlmException(
                    $"LLM attempt {attempt} failed with HTTP {(int)response.StatusCode}.",
                    (int)response.StatusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // Timeouts are NOT retried: a local model that needs longer once will need
                // longer again — retrying only triples the time-to-failure. Fix the timeout
                // setting instead.
                throw new LlmException(
                    $"LLM request timed out after {options.TimeoutSeconds}s " +
                    "(raise 'LLM timeout' in the CrateDigger settings for slow/local models).",
                    inner: ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == maxAttempts)
                    throw new LlmException($"Could not reach LLM endpoint '{url}': {ex.Message}", inner: ex);
                lastTransportError = ex;
            }

            // Exponential-ish backoff: 1s, 2s, ...
            await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
        }

        throw new LlmException("LLM request failed after retries.", inner: lastTransportError);
    }

    /// <summary>
    /// Builds the chat-completions payload: model/temperature/messages plus optional
    /// max_tokens and user-supplied extra JSON (server-specific knobs).
    /// </summary>
    internal static object BuildPayload(LlmRequest request, LlmOptions options)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = options.Model,
            ["temperature"] = options.Temperature,
            ["messages"] = request.Messages.Select(m => new Dictionary<string, string>
            {
                ["role"] = m.Role,
                ["content"] = m.Content,
            }).ToArray(),
        };

        if (options.MaxTokens > 0)
            payload["max_tokens"] = options.MaxTokens;

        if (!string.IsNullOrWhiteSpace(options.ExtraJson))
        {
            using var doc = JsonDocument.Parse(options.ExtraJson); // validates early; throws LlmParseException-ish JsonException
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new LlmParseException("Extra JSON must be a JSON object.", options.ExtraJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
                payload[prop.Name] = prop.Value.Clone();
        }

        return payload;
    }

    /// <summary>Accepts a bare base URL or a full /chat/completions URL.</summary>
    public static string BuildChatCompletionsUrl(string baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
            trimmed = "https://api.openai.com/v1";
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        return trimmed + "/chat/completions";
    }

    /// <summary>choices[0].message.content, or a friendly exception if the shape is unexpected.</summary>
    internal static string ExtractContent(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new LlmParseException("LLM response did not contain a 'choices' array.", responseBody);
        }

        var first = choices[0];
        if (first.ValueKind != JsonValueKind.Object ||
            !first.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content))
        {
            throw new LlmParseException("LLM response choice was missing 'message.content'.", responseBody);
        }

        return content.ValueKind == JsonValueKind.String ? content.GetString() ?? string.Empty : content.ToString();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}