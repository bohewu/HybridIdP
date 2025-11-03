namespace Core.Application.DTOs;

/// <summary>
/// Paginated list of users with total count
/// </summary>
public class PagedUsersDto
{
    public List<UserSummaryDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}
