using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Core.Application.Security;
using Core.Domain;
using Core.Domain.Constants;

namespace Web.IdP.Services.Saml.Idp;

/// <summary>A deliberately narrower local-source boundary than OIDC claim mappings.</summary>
public static class SamlAttributePolicy
{
    public const string SettingPrefix = SettingKeys.SamlAttributePolicyPrefix;
    public const string SubjectName = "urn:hybrididp:claims:subject";
    private static readonly string[] Sources = [
        "FirstName", "MiddleName", "LastName", "Nickname", "Email", "EmployeeId", "Department", "JobTitle",
        "Person.FirstName", "Person.MiddleName", "Person.LastName", "Person.Nickname", "Person.Email",
        "Person.EmployeeId", "Person.Department", "Person.JobTitle"];

    public static IReadOnlyList<SamlAttributeSourceDto> Catalog { get; } = Array.AsReadOnly(
        Sources.Select(source => new SamlAttributeSourceDto(source, source)).ToArray());

    public static string SettingKey(string entityId) => SettingPrefix +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entityId)));

    public static void Validate(SamlAttributePolicyDto policy)
    {
        if (policy.Mappings is null || policy.Mappings.Length > 16)
            throw new SamlAttributeException("invalid_policy");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in policy.Mappings)
        {
            if (mapping is null || !Sources.Contains(mapping.Source, StringComparer.Ordinal))
                throw new SamlAttributeException("invalid_source");
            ValidateName(mapping.Name);
            if (!names.Add(mapping.Name)) throw new SamlAttributeException("duplicate_name");
            if (!sources.Add(mapping.Source)) throw new SamlAttributeException("duplicate_source");
        }
    }

    public static void ValidateName(string? name)
    {
        if (name is not { Length: > 0 and <= 128 } || !char.IsAsciiLetter(name[0]) ||
            name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '.' and not ':' and not '-') ||
            name.Equals(SubjectName, StringComparison.OrdinalIgnoreCase) ||
            name.ToLowerInvariant() is "nameid" or "subject" or "id" or "personid" ||
            ClaimSourcePropertyPolicy.IsProtectedProviderClaimType(name.ToLowerInvariant()))
            throw new SamlAttributeException("invalid_name");
    }

    public static void ValidateValue(string value)
    {
        if (value.Length > 2048) throw new SamlAttributeException("value_invalid");
        try { XmlConvert.VerifyXmlChars(value); }
        catch (XmlException) { throw new SamlAttributeException("value_invalid"); }
    }

    public static (IReadOnlyList<SamlAttributeValueDto> Attributes, IReadOnlyList<SamlAttributeOmissionDto> Omitted)
        Resolve(SamlAttributePolicyDto policy, ApplicationUser user)
    {
        Validate(policy);
        var attributes = new List<SamlAttributeValueDto> { new(SubjectName, user.Id.ToString("N")) };
        var omitted = new List<SamlAttributeOmissionDto>();
        foreach (var mapping in policy.Mappings)
        {
            if (!ClaimSourcePropertyPolicy.TryResolve(user, mapping.Source, out var value))
                throw new SamlAttributeException("invalid_source");
            if (string.IsNullOrWhiteSpace(value)) omitted.Add(new(mapping.Source, mapping.Name, "missing_value"));
            else
            {
                ValidateValue(value);
                attributes.Add(new(mapping.Name, value));
            }
        }
        return (attributes, omitted);
    }
}
