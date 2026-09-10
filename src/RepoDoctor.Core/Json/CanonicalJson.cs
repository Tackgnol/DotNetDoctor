using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RepoDoctor.Core.Json;

public static class CanonicalJson
{
    public const string AlgorithmVersion = "rd-canon-1";
    public const string HashAlgorithmVersion = AlgorithmVersion + "-sha256";

    private const int CanonicalMaxDepth = 64;

    public static string Canonicalize(string json)
    {
        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { MaxDepth = CanonicalMaxDepth, AllowTrailingCommas = false });

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            Write(document.RootElement, writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static string Sha256Hex(string json)
    {
        var canonical = Canonicalize(json);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(item, writer);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;

            default:
                throw new RepoDoctorValidationException($"Unsupported JSON token '{element.ValueKind}' during canonicalization.");
        }
    }
}
