using System.Collections.Generic;

namespace Core.Application.DTOs
{
    public class ClientScopeEvaluationResult
    {
        public List<string> AllowedScopes { get; set; } = new();
        public List<string> DisallowedScopes { get; set; } = new();
    }
}
