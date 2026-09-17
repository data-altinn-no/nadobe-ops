using System.Text.Json.Serialization;

namespace Nadobe.Ops.Functions.Slack;

/// <param name="Text">Fallback text used for notifications and clients that cannot render blocks.</param>
public sealed record SlackMessage(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("blocks")] IReadOnlyList<SlackBlock> Blocks);

public sealed record SlackBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] SlackText Text)
{
    public static SlackBlock Header(string text) => new("header", new SlackText("plain_text", text));

    public static SlackBlock Section(string markdown) => new("section", new SlackText("mrkdwn", markdown));
}

public sealed record SlackText(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);
