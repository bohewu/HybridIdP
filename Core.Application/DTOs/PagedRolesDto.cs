namespace Core.Application.DTOs;

/// <summary>
/// Paginated list of roles with total count
/// </summary>
public class PagedRolesDto
{
    public List<RoleSummaryDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}
