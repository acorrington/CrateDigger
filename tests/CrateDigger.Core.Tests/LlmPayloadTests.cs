using System.Text.Json;
using CrateDigger.Core.Llm;

namespace CrateDigger.Core.Tests;

public class LlmPayloadTests
{
    private static readonly LlmRequest Request = new(
        new LlmMessage("system", "sys"),
        new LlmMessage("user", "hello"));

    [Fact]
    public void DefaultPayload_ContainsModelTemperatureMessagesAndMaxTokens()
    {
        var payload = LlmClient.BuildPayload(Request, new LlmOptions { Model = "m1" }) as Dictionary<string, object>;

        Assert.NotNull(payload);
        Assert.Equal("m1", payload!["model"]);
        Assert.Equal(8192, payload["max_tokens"]);
        Assert.Equal(0.8, (double)payload["temperature"], 3);
        var messages = (Dictionary<string, string>[])payload["messages"];
        Assert.Equal(2, messages.Length);
        Assert.Equal("user", messages[1]["role"]);
    }

    [Fact]
    public void MaxTokensZero_OmitsCap()
    {
        var payload = (Dictionary<string, object>)LlmClient.BuildPayload(
            Request, new LlmOptions { MaxTokens = 0 });

        Assert.False(payload.ContainsKey("max_tokens"));
    }

    [Fact]
    public void ExtraJson_MergesServerSpecificKnobs()
    {
        var payload = (Dictionary<string, object>)LlmClient.BuildPayload(
            Request,
            new LlmOptions { ExtraJson = """{"chat_template_kwargs":{"enable_thinking":false}}""" });

        Assert.True(payload.ContainsKey("chat_template_kwargs"));
        var kwargs = Assert.IsType<JsonElement>(payload["chat_template_kwargs"]);
        Assert.False(kwargs.GetProperty("enable_thinking").GetBoolean());
    }

    [Fact]
    public void ExtraJson_NotAnObject_Throws()
    {
        Assert.Throws<LlmParseException>(() => LlmClient.BuildPayload(
            Request, new LlmOptions { ExtraJson = "[1,2]" }));
    }

    [Fact]
    public void ExtraJson_Malformed_Throws()
    {
        Assert.ThrowsAny<Exception>(() => LlmClient.BuildPayload(
            Request, new LlmOptions { ExtraJson = "{oops" }));
    }
}