using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RepoDoctor.Core.Json;

public static class StrictJson
{
    public const int MaxBytes = 256 * 1024;
    public const int MaxDepth = 16;
    public const int MaxRuleEntries = 2000;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = MaxDepth,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(namingPolicy: JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static T Deserialize<T>(string json, string sourceLabel)
    {
        ArgumentNullException.ThrowIfNull(json);

        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxBytes)
        {
            throw new RepoDoctorValidationException(
                $"{sourceLabel}: file is {bytes.Length} bytes, over the {MaxBytes} byte limit.");
        }

        EnsureNoDuplicatePropertyNames(bytes, sourceLabel);

        try
        {
            return JsonSerializer.Deserialize<T>(bytes, Options)
                ?? throw new RepoDoctorValidationException($"{sourceLabel}: document is JSON null.");
        }
        catch (JsonException ex)
        {
            throw new RepoDoctorValidationException($"{sourceLabel}: {ex.Message}", ex);
        }
    }

    private static void EnsureNoDuplicatePropertyNames(ReadOnlySpan<byte> utf8Json, string sourceLabel)
    {
        var reader = new Utf8JsonReader(
            utf8Json,
            new JsonReaderOptions
            {
                MaxDepth = MaxDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });

        var scopes = new Stack<HashSet<string>>();

        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;

                    case JsonTokenType.EndObject:
                        scopes.Pop();
                        break;

                    case JsonTokenType.PropertyName:
                        var name = reader.GetString()!;
                        if (!scopes.Peek().Add(name))
                        {
                            throw new RepoDoctorValidationException(
                                $"{sourceLabel}: duplicate JSON property '{name}'. A repeated key cannot silently choose a winner.");
                        }

                        break;
                }
            }
        }
        catch (JsonException ex)
        {
            throw new RepoDoctorValidationException($"{sourceLabel}: {ex.Message}", ex);
        }
    }
}
