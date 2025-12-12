using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public class LocalizationManagementService : ILocalizationManagementService
{
    private readonly IApplicationDbContext _db;

    public LocalizationManagementService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<(IEnumerable<ResourceDto> Items, int TotalCount)> GetResourcesAsync(int skip, int take, string? search, string? sort)
    {
        var query = _db.Resources.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim().ToLower();
            query = query.Where(r => 
                r.Key.ToLower().Contains(search) || 
                r.Value.ToLower().Contains(search) ||
                (r.Category != null && r.Category.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync();

        // Sorting
        query = sort switch
        {
            "key:desc" => query.OrderByDescending(r => r.Key),
            "key:asc" => query.OrderBy(r => r.Key),
            "culture:desc" => query.OrderByDescending(r => r.Culture),
            "culture:asc" => query.OrderBy(r => r.Culture),
            "category:desc" => query.OrderByDescending(r => r.Category),
            "category:asc" => query.OrderBy(r => r.Category),
            _ => query.OrderBy(r => r.Key).ThenBy(r => r.Culture) // Default sort
        };

        var items = await query
            .Skip(skip)
            .Take(take)
            .Select(r => new ResourceDto
            {
                Id = r.Id,
                Key = r.Key,
                Culture = r.Culture,
                Value = r.Value,
                Category = r.Category,
                UpdatedUtc = r.UpdatedUtc
            })
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<ResourceDto?> GetResourceByIdAsync(int id)
    {
        var r = await _db.Resources.FindAsync(id);
        if (r == null) return null;

        return new ResourceDto
        {
            Id = r.Id,
            Key = r.Key,
            Culture = r.Culture,
            Value = r.Value,
            Category = r.Category,
            UpdatedUtc = r.UpdatedUtc
        };
    }

    public async Task<ResourceDto> CreateResourceAsync(CreateResourceRequest request)
    {
        // Check if exists
        var exists = await _db.Resources.AnyAsync(r => r.Key == request.Key && r.Culture == request.Culture);
        if (exists)
        {
            throw new InvalidOperationException($"Resource with key '{request.Key}' and culture '{request.Culture}' already exists.");
        }

        var resource = new Resource
        {
            Key = request.Key,
            Culture = request.Culture,
            Value = request.Value,
            Category = request.Category,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        _db.Resources.Add(resource);
        await _db.SaveChangesAsync(default);

        return new ResourceDto
        {
            Id = resource.Id,
            Key = resource.Key,
            Culture = resource.Culture,
            Value = resource.Value,
            Category = resource.Category,
            UpdatedUtc = resource.UpdatedUtc
        };
    }

    public async Task<bool> UpdateResourceAsync(int id, UpdateResourceRequest request)
    {
        var resource = await _db.Resources.FindAsync(id);
        if (resource == null) return false;

        resource.Value = request.Value;
        resource.Category = request.Category;
        resource.UpdatedUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(default);
        return true;
    }

    public async Task<bool> DeleteResourceAsync(int id)
    {
        var resource = await _db.Resources.FindAsync(id);
        if (resource == null) return false;

        _db.Resources.Remove(resource);
        await _db.SaveChangesAsync(default);
        return true;
    }
}
