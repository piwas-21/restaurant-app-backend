using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Common.Conventers;

/// <summary>
/// Custom JSON converter for nullable enums that serializes as strings
/// and accepts both strings and integers for deserialization.
/// Supports EnumMember attribute for custom serialization values.
/// </summary>
public class NullableStringEnumConverter<T> : JsonConverter<T?> where T : struct, Enum
{
    private readonly Dictionary<string, T> _stringToEnum;
    private readonly Dictionary<T, string> _enumToString;

    public NullableStringEnumConverter()
    {
        var type = typeof(T);
        _stringToEnum = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        _enumToString = new Dictionary<T, string>();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var enumValue = (T)field.GetValue(null)!;

            // Check for EnumMember attribute
            var enumMemberAttr = field.GetCustomAttribute<EnumMemberAttribute>();
            var stringValue = enumMemberAttr?.Value ?? field.Name;

            _enumToString[enumValue] = stringValue;
            _stringToEnum[stringValue] = enumValue;

            // Also add the original field name for backwards compatibility
            if (enumMemberAttr != null && !_stringToEnum.ContainsKey(field.Name))
            {
                _stringToEnum[field.Name] = enumValue;
            }

            // Also add the numeric value as a string
            var numericValue = Convert.ToInt32(enumValue).ToString();
            if (!_stringToEnum.ContainsKey(numericValue))
            {
                _stringToEnum[numericValue] = enumValue;
            }
        }
    }

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Handle null values
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                {
                    var stringValue = reader.GetString();

                    // Handle empty strings as null
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        return null;
                    }

                    if (_stringToEnum.TryGetValue(stringValue, out var enumValue))
                    {
                        return enumValue;
                    }

                    // Provide detailed error with available values
                    var availableValues = string.Join(", ", _stringToEnum.Keys.OrderBy(k => k));
                    throw new JsonException(
                        $"Unable to convert \"{stringValue}\" to enum {typeof(T).Name}. " +
                        $"Available values: {availableValues}");
                }
            case JsonTokenType.Number:
                {
                    var intValue = reader.GetInt32();

                    if (Enum.IsDefined(typeof(T), intValue))
                    {
                        return (T)Enum.ToObject(typeof(T), intValue);
                    }

                    var validNumbers = string.Join(", ", Enum.GetValues<T>().Select(e => Convert.ToInt32(e)));
                    throw new JsonException(
                        $"Unable to convert {intValue} to enum {typeof(T).Name}. " +
                        $"Valid numeric values: {validNumbers}");
                }
            default:
                throw new JsonException(
                    $"Unexpected token type {reader.TokenType} when reading nullable enum {typeof(T).Name}. " +
                    $"Expected String, Number, or Null token.");
        }
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            // Write using the EnumMember value if available
            if (_enumToString.TryGetValue(value.Value, out var stringValue))
            {
                writer.WriteStringValue(stringValue);
            }
            else
            {
                writer.WriteStringValue(value.Value.ToString());
            }
        }
        else
        {
            // Write null for nullable enums without value
            writer.WriteNullValue();
        }
    }
}
