using System.Text.Json;
using System.Text.Json.Serialization;

namespace squad.Handoffs;

/// <summary>
/// Centralizes strict <see cref="JsonSerializerOptions"/>, validated reading, and atomic writing for
/// <see cref="HandoffDocument"/> so no producer or consumer hand-rolls its own parser, property probing, or
/// serializer. Every read and write goes through <see cref="System.Text.Json"/> exclusively.
/// </summary>
public static class HandoffJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Deserializes and validates one handoff document, wrapping any malformed JSON, unsupported schema
    /// version, or invalid kind/variant pairing in a <see cref="HandoffFormatException"/> naming the source file.</summary>
    public static HandoffDocument Read(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            var document = JsonSerializer.Deserialize<HandoffDocument>(text, Options)
                ?? throw new HandoffFormatException("document is empty or null");
            document.Validate();
            return document;
        }
        catch (JsonException exception)
        {
            throw new HandoffFormatException($"malformed handoff JSON in {path}: {exception.Message}", exception);
        }
        catch (HandoffFormatException exception)
        {
            throw new HandoffFormatException($"invalid handoff document in {path}: {exception.Message}", exception);
        }
    }

    /// <summary>Validates, then serializes one handoff document by writing a temporary sibling file and atomically
    /// moving it into place, creating the target directory as needed.</summary>
    public static void Write(string path, HandoffDocument document)
    {
        document.Validate();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>Reads one handoff document, applies a lifecycle update, and atomically rewrites it in place.</summary>
    public static void Update(string path, Func<HandoffDocument, HandoffDocument> update) =>
        Write(path, update(Read(path)));
}
