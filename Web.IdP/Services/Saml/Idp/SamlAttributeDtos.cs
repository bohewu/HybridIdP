namespace Web.IdP.Services.Saml.Idp;

public sealed record SamlAttributeSourceDto(string Source, string DefaultName);
public sealed record SamlAttributeMappingDto(string Source, string Name);
public sealed record SamlAttributePolicyDto(string EntityId, SamlAttributeMappingDto[] Mappings);
public sealed record SamlAttributePreviewRequest(string EntityId, Guid UserId);
public sealed record SamlAttributeValueDto(string Name, string Value);
public sealed record SamlAttributeOmissionDto(string Source, string Name, string Reason);
public sealed record SamlAttributePreviewResponse(string EntityId, Guid UserId, string NameId,
    string NameIdFormat, string NameQualifier, string SpNameQualifier,
    IReadOnlyList<SamlAttributeValueDto> Attributes, IReadOnlyList<SamlAttributeOmissionDto> Omitted);
public sealed record SamlAttributePartnerDto(string EntityId, string AssertionConsumerServiceUrl);
public sealed record SamlAttributeAccountDto(Guid UserId, string DisplayName);

public sealed class SamlAttributeException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
