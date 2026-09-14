using System.Text.Json;
using System.Text.Json.Serialization;

namespace Peek.Worker.Contracts.Rpc;

public sealed class IntPtrJsonConverter : JsonConverter<nint>
{
    public override nint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
            return new nint(number);

        throw new JsonException($"Cannot convert token type {reader.TokenType} to nint.");
    }

    public override void Write(Utf8JsonWriter writer, nint value, JsonSerializerOptions options) =>
        writer.WriteNumberValue((long)value);
}
