using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>
/// The JSON settings of the operations contract (ADR-0110): camel-case names, enums as camel-case strings, and
/// times in UTC ISO-8601. The endpoints always write with these settings, whatever the host configured.
/// </summary>
public static class OperationsJson
{
    /// <summary>The contract's schema version, carried by every response.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Gets the read-only serializer options of the contract.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        options.MakeReadOnly();
        return options;
    }
}
