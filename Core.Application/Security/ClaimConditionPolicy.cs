using System.Text.Json;
using Core.Application.DTOs;

namespace Core.Application.Security;

public static class ClaimConditionPolicy
{
    public static bool IsValid(ClaimCondition condition, IReadOnlyDictionary<string, string> schema)
    {
        var nodes = 0;
        return Validate(condition, 1);

        bool Validate(ClaimCondition? node, int depth)
        {
            if (node is null || depth > 4 || ++nodes > 32) return false;
            if (node.Operator is "All" or "Any")
                return node.Property is null && node.Value is null && node.Children is { Count: > 0 and <= 8 } &&
                    node.Children.All(child => Validate(child, depth + 1));

            if (node.Children is not null || node.Property is null || node.Value is not { } value ||
                !schema.TryGetValue(node.Property, out var type)) return false;
            return node.Operator switch
            {
                "Equals" or "NotEquals" => type == "Boolean" ? value.ValueKind is JsonValueKind.True or JsonValueKind.False :
                    type == "String" && IsString(value),
                "StartsWith" => type == "String" && IsString(value) && value.GetString()!.Length > 0,
                "Contains" => IsString(value) && (type == "StringArray" ||
                    type == "String" && value.GetString()!.Length > 0),
                _ => false
            };
        }
    }

    public static bool? Evaluate(ClaimCondition condition, IReadOnlyDictionary<string, string> schema,
        IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!IsValid(condition, schema) || !HasAllInputs(condition)) return null;
        return EvaluateKnown(condition);

        bool HasAllInputs(ClaimCondition node) => node.Children is not null
            ? node.Children.All(HasAllInputs)
            : properties.TryGetValue(node.Property!, out var value) &&
              ProviderProfileContract.IsValueOfType(value, schema[node.Property!]);

        bool EvaluateKnown(ClaimCondition node) => node.Operator switch
        {
            "All" => node.Children!.All(EvaluateKnown),
            "Any" => node.Children!.Any(EvaluateKnown),
            "StartsWith" => properties[node.Property!].GetString()!.StartsWith(node.Value!.Value.GetString()!, StringComparison.Ordinal),
            "Contains" => schema[node.Property!] == "StringArray"
                ? properties[node.Property!].EnumerateArray().Any(item => string.Equals(
                    item.GetString(), node.Value!.Value.GetString(), StringComparison.Ordinal))
                : properties[node.Property!].GetString()!.Contains(node.Value!.Value.GetString()!, StringComparison.Ordinal),
            "NotEquals" => !Equal(node),
            _ => Equal(node)
        };

        bool Equal(ClaimCondition node) => schema[node.Property!] == "Boolean"
                ? properties[node.Property!].GetBoolean() == node.Value!.Value.GetBoolean()
                : string.Equals(properties[node.Property!].GetString(), node.Value!.Value.GetString(), StringComparison.Ordinal);
    }

    private static bool IsString(JsonElement value) => value.ValueKind == JsonValueKind.String &&
        value.GetString()!.Length <= ProviderProfileContract.MaximumStringLength;
}
