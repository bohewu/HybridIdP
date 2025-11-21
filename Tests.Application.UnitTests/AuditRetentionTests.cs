using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Application;
using Core.Domain.Entities;
using Core.Domain.Events;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Application.UnitTests
{
    public class AuditRetentionTests
    {
        private class FakeDomainEventPublisher : IDomainEventPublisher
        {
            public Task PublishAsync<TDomainEvent>(TDomainEvent domainEvent) where TDomainEvent : IDomainEvent => Task.CompletedTask;
        }

        private class FakeSettingsService : ISettingsService
        {
            private readonly Dictionary<string,string> _values = new();
            public void Set(string key, object value) => _values[key] = value is string s ? s : value.ToString()!;
            public Task<string?> GetValueAsync(string key, CancellationToken ct = default) => Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);
            public async Task<T?> GetValueAsync<T>(string key, CancellationToken ct = default)
            {
                var raw = await GetValueAsync(key, ct);
                if (raw == null) return default;
                try
                {
                    object? parsed = typeof(T) switch
                    {
                        var t when t == typeof(string) => raw,
                        var t when t == typeof(int) && int.TryParse(raw, out var i) => i,
                        var t when t == typeof(bool) && bool.TryParse(raw, out var b) => b,
                        _ => System.Text.Json.JsonSerializer.Deserialize<T>(raw)
                    };
                    return (T?)parsed;
                }
                catch { return default; }
            }
            public Task SetValueAsync(string key, object value, string? updatedBy = null, CancellationToken ct = default)
            { Set(key,value); return Task.CompletedTask; }
            public Task<IDictionary<string, string>> GetByPrefixAsync(string prefix, CancellationToken ct = default) => Task.FromResult((IDictionary<string,string>)_values.Where(k=>k.Key.StartsWith(prefix)).ToDictionary(k=>k.Key,v=>v.Value));
            public Task InvalidateAsync(string? keyOrPrefix = null) => Task.CompletedTask;
        }

        private AuditService CreateService(FakeSettingsService settings, ApplicationDbContext db)
        {
            var publisher = new FakeDomainEventPublisher();
            return new AuditService(db, publisher, settings);
        }

        [Fact]
        public async Task LogEventAsync_PurgesOldEvents_WhenRetentionConfigured()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ApplicationDbContext(options);
            var settings = new FakeSettingsService();
            settings.Set("Audit.RetentionDays", 1); // keep only last 1 day
            var service = CreateService(settings, db);

            // Seed old events (>1 day)
            db.AuditEvents.Add(new AuditEvent { EventType = "OldEvent", Timestamp = DateTime.UtcNow.AddDays(-2) });
            db.AuditEvents.Add(new AuditEvent { EventType = "OldEvent", Timestamp = DateTime.UtcNow.AddDays(-10) });
            db.AuditEvents.Add(new AuditEvent { EventType = "RecentEvent", Timestamp = DateTime.UtcNow.AddHours(-12) });
            await db.SaveChangesAsync(CancellationToken.None);

            // Log new event triggers purge
            await service.LogEventAsync("NewEvent", null, null, null, null);

            Assert.Equal(2, db.AuditEvents.Count());
            Assert.DoesNotContain(db.AuditEvents, e => e.EventType == "OldEvent");
            Assert.Contains(db.AuditEvents, e => e.EventType == "RecentEvent");
            Assert.Contains(db.AuditEvents, e => e.EventType == "NewEvent");
        }

        [Fact]
        public async Task LogEventAsync_NoPurge_WhenRetentionZero()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new ApplicationDbContext(options);
            var settings = new FakeSettingsService();
            settings.Set("Audit.RetentionDays", 0);
            var service = CreateService(settings, db);

            db.AuditEvents.Add(new AuditEvent { EventType = "VeryOld", Timestamp = DateTime.UtcNow.AddDays(-100) });
            await db.SaveChangesAsync(CancellationToken.None);

            await service.LogEventAsync("New", null, null, null, null);

            Assert.Equal(2, db.AuditEvents.Count());
        }
    }
}
