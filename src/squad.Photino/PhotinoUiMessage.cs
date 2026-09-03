using System.Text.Json;

namespace squad.Photino;

internal readonly record struct PhotinoUiMessage(
    string? Type,
    string? RequestId,
    string? Role,
    JsonElement Payload,
    string? EnvelopeError);




