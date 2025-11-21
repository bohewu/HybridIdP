using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Application;
using Core.Application.DTOs;

namespace Infrastructure.Services
{
    public class ClientScopeRequestProcessor
    {
        private readonly IClientAllowedScopesService _allowedScopesService;
        private readonly IAuditService _auditService;

        public ClientScopeRequestProcessor(IClientAllowedScopesService allowedScopesService, IAuditService auditService)
        {
            _allowedScopesService = allowedScopesService;
            _auditService = auditService;
        }

        public async Task<ClientScopeEvaluationResult> EnforceAsync(Guid clientId, IEnumerable<string> requestedScopes, bool logAuditIfRestricted = true)
        {
            var requestedList = requestedScopes?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
            var valid = await _allowedScopesService.ValidateRequestedScopesAsync(clientId, requestedList);
            var allowedSet = valid.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var disallowed = requestedList.Where(s => !allowedSet.Contains(s)).ToList();

            if (disallowed.Any() && logAuditIfRestricted)
            {
                var details = JsonSerializer.Serialize(new
                {
                    clientId,
                    requested = requestedList,
                    allowed = allowedSet.ToList(),
                    disallowed
                });
                await _auditService.LogEventAsync("AuthorizationClientScopeRestricted", null, details, null, null);
            }

            return new ClientScopeEvaluationResult
            {
                AllowedScopes = allowedSet.ToList(),
                DisallowedScopes = disallowed
            };
        }
    }
}
