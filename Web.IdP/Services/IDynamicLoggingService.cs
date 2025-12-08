using System.Threading.Tasks;

namespace Web.IdP.Services;

public interface IDynamicLoggingService
{
    Task SetGlobalLogLevelAsync(string level);
    Task<string> GetGlobalLogLevelAsync();
}
