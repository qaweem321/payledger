using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayLedger.Application.Common;

/// <summary>The one JSON shape used for API responses, stored idempotent responses and webhook payloads.</summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
