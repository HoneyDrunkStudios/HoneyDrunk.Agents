using HoneyDrunk.Capabilities.Abstractions;
using HoneyDrunk.Kernel.Abstractions.Identity;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HoneyDrunk.Agents;

/// <summary>
/// A stable key for one tool call: <c>tool-</c> plus the first 32 hex characters of HMAC-SHA-256, under a per-host
/// secret, over the tenant, the agent id, the tool key and the canonical arguments (object keys sorted ordinally at every depth,
/// no whitespace). The secret keeps the key from being used to guess low-entropy arguments.
/// </summary>
internal static class ToolRequestKey
{
    private const char Separator = '\u001f';

    /// <summary>Computes the key.</summary>
    /// <param name="secret">The per-host HMAC key.</param>
    /// <param name="tenant">The grid tenant; an approval never crosses tenants.</param>
    /// <param name="agentId">The agent id.</param>
    /// <param name="key">The tool key.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The request key.</returns>
    public static string For(byte[] secret, TenantId tenant, string agentId, CapabilityKey key, JsonElement arguments)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var buffer = new ArrayBufferWriter<byte>();
        buffer.Write(Encoding.UTF8.GetBytes(tenant.ToString() + Separator + agentId + Separator + key.Name + Separator + key.Version + Separator));
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, arguments);
        }

        var hash = HMACSHA256.HashData(secret, buffer.WrittenSpan);
        return "tool-" + Convert.ToHexStringLower(hash)[..32];
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
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
