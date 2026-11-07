using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Identity;
using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace HoneyDrunk.Agents;

internal static class ToolRequestKey
{
    internal static string Create(TenantId tenant, string agentId, CapabilityKey tool, JsonElement arguments)
    {
        var bytes = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { MaxDepth = 128 }))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(tenant.ToString());
            writer.WriteStringValue(agentId);
            writer.WriteStringValue(tool.Name);
            writer.WriteStringValue(tool.Version);
            WriteCanonical(writer, arguments);
            writer.WriteEndArray();
        }

        return "tool-" + Convert.ToHexStringLower(SHA256.HashData(bytes.WrittenSpan).AsSpan(0, 16));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var value in element.EnumerateArray())
                {
                    WriteCanonical(writer, value);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
