using Core.Domain;

namespace Core.Application.Security;

/// <summary>
/// Defines the profile properties that may be used as token claim sources.
/// </summary>
public static class ClaimSourcePropertyPolicy
{
    private static readonly Dictionary<string, Func<ApplicationUser, object?>> Accessors =
        new(StringComparer.Ordinal)
        {
            [nameof(ApplicationUser.Id)] = user => user.Id,
            [nameof(ApplicationUser.UserName)] = user => user.UserName,
            [nameof(ApplicationUser.Email)] = user => user.Email,
            [nameof(ApplicationUser.EmailConfirmed)] = user => user.EmailConfirmed,
            [nameof(ApplicationUser.PhoneNumber)] = user => user.PhoneNumber,
            [nameof(ApplicationUser.PhoneNumberConfirmed)] = user => user.PhoneNumberConfirmed,
            [nameof(ApplicationUser.FirstName)] = user => user.FirstName,
            [nameof(ApplicationUser.MiddleName)] = user => user.MiddleName,
            [nameof(ApplicationUser.LastName)] = user => user.LastName,
            [nameof(ApplicationUser.Nickname)] = user => user.Nickname,
            [nameof(ApplicationUser.Department)] = user => user.Department,
            [nameof(ApplicationUser.JobTitle)] = user => user.JobTitle,
            [nameof(ApplicationUser.ProfileUrl)] = user => user.ProfileUrl,
            [nameof(ApplicationUser.PictureUrl)] = user => user.PictureUrl,
            [nameof(ApplicationUser.Website)] = user => user.Website,
            [nameof(ApplicationUser.Address)] = user => user.Address,
            [nameof(ApplicationUser.Birthdate)] = user => user.Birthdate,
            [nameof(ApplicationUser.Gender)] = user => user.Gender,
            [nameof(ApplicationUser.TimeZone)] = user => user.TimeZone,
            [nameof(ApplicationUser.Locale)] = user => user.Locale,
            [nameof(ApplicationUser.EmployeeId)] = user => user.EmployeeId,
            [nameof(ApplicationUser.PersonId)] = user => user.PersonId,
            ["Person.Email"] = user => user.Person?.Email,
            ["Person.PhoneNumber"] = user => user.Person?.PhoneNumber,
            ["Person.FirstName"] = user => user.Person?.FirstName,
            ["Person.MiddleName"] = user => user.Person?.MiddleName,
            ["Person.LastName"] = user => user.Person?.LastName,
            ["Person.Nickname"] = user => user.Person?.Nickname,
            ["Person.EmployeeId"] = user => user.Person?.EmployeeId,
            ["Person.Department"] = user => user.Person?.Department,
            ["Person.JobTitle"] = user => user.Person?.JobTitle,
            ["Person.ProfileUrl"] = user => user.Person?.ProfileUrl,
            ["Person.PictureUrl"] = user => user.Person?.PictureUrl,
            ["Person.Website"] = user => user.Person?.Website,
            ["Person.Address"] = user => user.Person?.Address,
            ["Person.Birthdate"] = user => user.Person?.Birthdate,
            ["Person.Gender"] = user => user.Person?.Gender,
            ["Person.TimeZone"] = user => user.Person?.TimeZone,
            ["Person.Locale"] = user => user.Person?.Locale,
            ["Person.NationalId"] = user => user.Person?.NationalId
        };

    public static IEnumerable<string> AllowedPaths => Accessors.Keys;

    public static bool TryNormalize(string? path, out string normalizedPath)
    {
        normalizedPath = Normalize(path);
        return Accessors.ContainsKey(normalizedPath);
    }

    public static bool TryResolve(
        ApplicationUser user,
        string? path,
        out string? value)
    {
        ArgumentNullException.ThrowIfNull(user);

        var normalizedPath = Normalize(path);
        if (!Accessors.TryGetValue(normalizedPath, out var accessor))
        {
            value = null;
            return false;
        }

        value = accessor(user)?.ToString();
        return true;
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return string.Join(
            '.',
            path.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
    }
}
