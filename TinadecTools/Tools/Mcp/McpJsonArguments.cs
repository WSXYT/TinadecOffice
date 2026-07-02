using System.Text.Json;

namespace TinadecTools.Tools.Mcp;

internal static class McpJsonArguments
{
    public static IReadOnlyDictionary<string, object?>? ToDictionary(JsonElement? arguments)
    {
        if (arguments is null || arguments.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (arguments.Value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("arguments must be a JSON object.");

        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in arguments.Value.EnumerateObject())
            dictionary[property.Name] = ToObject(property.Value);

        return dictionary;
    }

    private static object? ToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => ToObject(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ToObject).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null
        };
    }
}
