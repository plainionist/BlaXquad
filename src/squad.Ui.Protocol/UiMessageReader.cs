using System.Text.Json;

namespace squad.Ui.Protocol;

/// <summary>Parses a UI envelope while preserving version and shape errors as protocol data for the caller to publish.</summary>
internal static class UiMessageReader
{
    internal static UiMessage Read(
        string serializedMessage,
        int protocolVersion)
    {
        using var document = JsonDocument.Parse(serializedMessage);
        var envelope = document.RootElement;
        if (!envelope.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || version.GetInt32() != protocolVersion)
        {
            return new(
                null,
                null,
                null,
                default,
                "The UI protocol version is not supported.");
        }
        if (!envelope.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String)
        {
            return new(
                null,
                null,
                null,
                default,
                "The UI message is missing a type.");
        }

        return new(
            type.GetString()!,
            GetString(envelope, "requestId"),
            GetString(envelope, "role"),
            envelope.TryGetProperty("payload", out var payload)
                ? payload.Clone()
                : default,
            null);
    }

    private static string? GetString(
        JsonElement envelope,
        string property) =>
        envelope.TryGetProperty(property, out var element)
        && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}



