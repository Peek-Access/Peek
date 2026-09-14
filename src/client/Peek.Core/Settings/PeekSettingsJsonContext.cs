using System.Text.Json.Serialization;

namespace Peek.Core.Settings;

[JsonSerializable(typeof(PeekSettings))]
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
internal sealed partial class PeekSettingsJsonContext : JsonSerializerContext;
