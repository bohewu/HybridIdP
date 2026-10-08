using System.Text.Json;

namespace Core.Application.DTOs;

// A bounded expression over approved properties from one explicitly selected source.
public sealed record ClaimCondition
{
    public string Operator { get; init; } = string.Empty;
    public string? Property { get; init; }
    public JsonElement? Value { get; init; }
    public IReadOnlyList<ClaimCondition>? Children { get; init; }
}
