using System;
using System.ComponentModel.DataAnnotations;

namespace Core.Application.DTOs;

public class ResourceDto
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Culture { get; set; }
    public required string Value { get; set; }
    public string? Category { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public class CreateResourceRequest
{
    [Required]
    [MaxLength(200)]
    public required string Key { get; set; }

    [Required]
    [MaxLength(20)]
    public required string Culture { get; set; }

    [Required]
    public required string Value { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }
}

public class UpdateResourceRequest
{
    [Required]
    public required string Value { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }
}

public class PagedResourcesDto
{
    public IEnumerable<ResourceDto> Items { get; set; } = Enumerable.Empty<ResourceDto>();
    public int TotalCount { get; set; }
}
