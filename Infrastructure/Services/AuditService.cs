using Core.Application;
using Core.Application.DTOs;
using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly IApplicationDbContext _db;

    public AuditService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task LogEventAsync(string eventType, string? userId, string? details, string? ipAddress, string? userAgent)
    {
        var auditEvent = new AuditEvent
        {
            EventType = eventType,
            UserId = userId,
            Details = details,
            IPAddress = ipAddress,
            UserAgent = userAgent,
            Timestamp = DateTime.UtcNow
        };

        _db.AuditEvents.Add(auditEvent);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task<(IEnumerable<AuditEventDto> items, int totalCount)> GetEventsAsync(AuditEventFilterDto filter)
    {
        var query = _db.AuditEvents.AsQueryable();

        if (!string.IsNullOrEmpty(filter.EventType))
        {
            query = query.Where(e => e.EventType == filter.EventType);
        }

        if (!string.IsNullOrEmpty(filter.UserId))
        {
            query = query.Where(e => e.UserId == filter.UserId);
        }

        if (filter.StartDate.HasValue)
        {
            query = query.Where(e => e.Timestamp >= filter.StartDate.Value);
        }

        if (filter.EndDate.HasValue)
        {
            query = query.Where(e => e.Timestamp <= filter.EndDate.Value);
        }

        if (!string.IsNullOrEmpty(filter.IPAddress))
        {
            query = query.Where(e => e.IPAddress == filter.IPAddress);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(e => new AuditEventDto
            {
                Id = e.Id,
                EventType = e.EventType,
                UserId = e.UserId,
                Timestamp = e.Timestamp,
                Details = e.Details,
                IPAddress = e.IPAddress,
                UserAgent = e.UserAgent
            })
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<AuditEventExportDto?> ExportEventAsync(int eventId)
    {
        var auditEvent = await _db.AuditEvents
            .Where(e => e.Id == eventId)
            .Select(e => new AuditEventExportDto
            {
                Id = e.Id,
                EventType = e.EventType,
                UserId = e.UserId,
                Timestamp = e.Timestamp,
                Details = e.Details,
                IPAddress = e.IPAddress,
                UserAgent = e.UserAgent,
                Username = null // TODO: Join with Users table if needed
            })
            .FirstOrDefaultAsync();

        return auditEvent;
    }
}