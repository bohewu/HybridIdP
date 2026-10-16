namespace Core.Application.DTOs;

public sealed record DashboardHealthDto(
    string Status,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyDictionary<string, DashboardDependencyHealthDto> Entries);

public sealed record DashboardDependencyHealthDto(string Status);
