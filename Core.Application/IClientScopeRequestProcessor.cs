using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Application.DTOs;

namespace Core.Application
{
    public interface IClientScopeRequestProcessor
    {
        Task<ClientScopeEvaluationResult> EnforceAsync(Guid clientId, IEnumerable<string> requestedScopes, bool logAuditIfRestricted = true);
    }
}
