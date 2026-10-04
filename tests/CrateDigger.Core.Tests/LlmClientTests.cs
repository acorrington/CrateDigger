using CrateDigger.Core.Llm;

namespace CrateDigger.Core.Tests;

public class LlmClientTests
{
    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1/", "https://api.openai.com/v1/chat/completions")]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1/chat/completions")]
    [InlineData("https://x.ai/v1/chat/completions", "https://x.ai/v1/chat/completions")]
    [InlineData("", "https://api.openai.com/v1/chat/completions")]
    public void BuildChatCompletionsUrl_NormalizesInput(string input, string expected)
    {
        Assert.Equal(expected, LlmClient.BuildChatCompletionsUrl(input));
    }

    [Fact]
    public void ExtractContent_ReadsOpenAIShape()
    {
        const string body = """{"choices":[{"message":{"content":"hello"}}]}""";

        Assert.Equal("hello", LlmClient.ExtractContent(body));
    }

    [Fact]
    public void ExtractContent_MissingChoices_ThrowsParse()
    {
        Assert.Throws<LlmParseException>(() => LlmClient.ExtractContent("""{"result":"weird"}"""));
    }
}