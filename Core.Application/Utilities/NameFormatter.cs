using System.Linq;

namespace Core.Application.Utilities;

public static class NameFormatter
{
    public static string? BuildDisplayName(string? firstName, string? middleName, string? lastName)
    {
        var parts = new[] { firstName, middleName, lastName }
            .Select(p => p?.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p));

        var displayName = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(displayName) ? null : displayName;
    }
}