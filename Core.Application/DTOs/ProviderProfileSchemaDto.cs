namespace Core.Application.DTOs;

// Administrative mapping choices only; never expose source connection or profile data.
public sealed record ProviderProfileSchemaDto(bool Enabled, IReadOnlyList<ProviderProfileSourceSchemaDto> Sources);
public sealed record ProviderProfileSourceSchemaDto(string Name, IReadOnlyDictionary<string, string> Properties);
