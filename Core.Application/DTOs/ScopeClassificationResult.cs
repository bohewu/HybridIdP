using System.Collections.Generic;

namespace Core.Application.DTOs
{
    /// <summary>
    /// Result of scope classification for consent / token issuance.
    /// </summary>
    public sealed class ScopeClassificationResult
    {
        public IReadOnlyList<string> Allowed { get; init; } = new List<string>();
        public IReadOnlyList<string> Required { get; init; } = new List<string>();
        public IReadOnlyList<string> Rejected { get; init; } = new List<string>();
        public bool IsPartialGrant { get; init; }
    }
}
