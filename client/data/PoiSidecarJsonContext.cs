using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wayfinders.Client.Data;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(PoiSidecarDto))]
internal partial class PoiSidecarJsonContext : JsonSerializerContext { }
