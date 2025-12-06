using System.Collections.Generic;

namespace Web.IdP.Services
{
    public class ScopeInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? ConsentDisplayName { get; set; }
        public string? ConsentDescription { get; set; }
        public string? IconUrl { get; set; }
        public bool IsRequired { get; set; }
        public int DisplayOrder { get; set; }
        public string? Category { get; set; }
    }
}
