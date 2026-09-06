using System.Text.Json;

namespace squad.Ui.Protocol;

/// <summary>Represents a parsed UI envelope, carrying validation failure as data for protocol-error publication.</summary>
internal readonly record struct UiMessage(
    string? Type,
    string? RequestId,
    string? Role,
    JsonElement Payload,
    string? EnvelopeError);



