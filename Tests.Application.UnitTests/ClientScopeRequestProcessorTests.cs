using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;
using Infrastructure.Services;
using Xunit;

namespace Tests.Application.UnitTests
{
    public class ClientScopeRequestProcessorTests
    {
        private class FakeAllowedScopesService : IClientAllowedScopesService
        {
            private readonly List<string> _allowed;
            public FakeAllowedScopesService(IEnumerable<string> allowed) => _allowed = allowed.ToList();
            public Task<IReadOnlyList<string>> GetAllowedScopesAsync(Guid clientId) => Task.FromResult((IReadOnlyList<string>)_allowed.AsReadOnly());
            public Task SetAllowedScopesAsync(Guid clientId, IEnumerable<string> scopes) => Task.CompletedTask;
            public Task<bool> IsScopeAllowedAsync(Guid clientId, string scope) => Task.FromResult(_allowed.Contains(scope));
            public Task<IReadOnlyList<string>> ValidateRequestedScopesAsync(Guid clientId, IEnumerable<string> requestedScopes) => Task.FromResult((IReadOnlyList<string>)requestedScopes.Where(_allowed.Contains).ToList().AsReadOnly());
        }

        private class CapturingAuditService : IAuditService
        {
            public List<(string eventType,string? userId,string? details)> Events = new();
            public Task LogEventAsync(string eventType, string? userId, string? details, string? ipAddress, string? userAgent)
            {
                Events.Add((eventType,userId,details));
                return Task.CompletedTask;
            }
            public Task<(IEnumerable<AuditEventDto> items, int totalCount)> GetEventsAsync(AuditEventFilterDto filter) => throw new NotImplementedException();
            public Task<AuditEventExportDto?> ExportEventAsync(int eventId) => throw new NotImplementedException();
        }

        [Fact]
        public async Task EnforceAsync_LogsAuditAndFiltersDisallowedScopes()
        {
            var allowedService = new FakeAllowedScopesService(new [] {"openid","profile"});
            var audit = new CapturingAuditService();
            var processor = new ClientScopeRequestProcessor(allowedService, audit);

            var clientId = Guid.NewGuid();
            var requested = new [] {"openid","email","profile","address"};

            var result = await processor.EnforceAsync(clientId, requested, logAuditIfRestricted: true);

            Assert.Contains("openid", result.AllowedScopes);
            Assert.Contains("profile", result.AllowedScopes);
            Assert.DoesNotContain("email", result.AllowedScopes);
            Assert.DoesNotContain("address", result.AllowedScopes);
            Assert.Contains("email", result.DisallowedScopes);
            Assert.Contains("address", result.DisallowedScopes);
            Assert.Single(audit.Events);
            var evt = audit.Events[0];
            Assert.Equal("AuthorizationClientScopeRestricted", evt.eventType);
            Assert.NotNull(evt.details);
            using var doc = JsonDocument.Parse(evt.details!);
            var disallowed = doc.RootElement.GetProperty("disallowed").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains("email", disallowed);
            Assert.Contains("address", disallowed);
        }

        [Fact]
        public async Task EnforceAsync_NoDisallowed_NoAuditLogged()
        {
            var allowedService = new FakeAllowedScopesService(new [] {"openid","profile"});
            var audit = new CapturingAuditService();
            var processor = new ClientScopeRequestProcessor(allowedService, audit);
            var clientId = Guid.NewGuid();
            var requested = new [] {"profile","openid"};

            var result = await processor.EnforceAsync(clientId, requested, logAuditIfRestricted: true);

            Assert.Equal(2, result.AllowedScopes.Count);
            Assert.Empty(result.DisallowedScopes);
            Assert.Empty(audit.Events);
        }
    }
}
