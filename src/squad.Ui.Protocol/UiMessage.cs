using System.Text.Json;

namespace squad.Ui.Protocol;

internal readonly record struct UiMessage(
    string? Type,
    string? RequestId,
    string? Role,
    JsonElement Payload,
    string? EnvelopeError);




