namespace Core.Application.DTOs;

/// <summary>
/// Simple dashboard statistics DTO for admin overview.
/// </summary>
public sealed class DashboardStatsDto
{
    public int TotalClients { get; set; }
    public int TotalScopes { get; set; }
    public int TotalUsers { get; set; }
}
