using System.Text.Json;
using System.Text.Json.Serialization;

namespace squad.Handoffs;

/// <summary>Maps a typed value object to and from its existing scalar JSON representation, without reflection, so a
/// value-object property can serialize exactly as the primitive it replaces did. Reused across handoff value-object
/// slices by constructing one instance per <typeparamref name="TValue"/>/<typeparamref name="TScalar"/> pairing.</summary>
public sealed class ScalarJsonConverter<TValue, TScalar> : JsonConverter<TValue>
{
    private readonly Func<TValue, TScalar> myToScalar;
    private readonly Func<TScalar, TValue> myFromScalar;

    public ScalarJsonConverter(Func<TValue, TScalar> toScalar, Func<TScalar, TValue> fromScalar)
    {
        myToScalar = toScalar;
        myFromScalar = fromScalar;
    }

    public override TValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var scalar = JsonSerializer.Deserialize<TScalar>(ref reader, options)
            ?? throw new JsonException($"Expected a non-null {typeof(TScalar).Name} for {typeToConvert.Name}.");
        return myFromScalar(scalar);
    }

    public override void Write(Utf8JsonWriter writer, TValue value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, myToScalar(value), options);
}
