using System.Threading;
using System.Threading.Tasks;

namespace Core.Application;

public interface IBrandingService
{
    Task<string> GetAppNameAsync(CancellationToken ct = default);
    Task<string> GetProductNameAsync(CancellationToken ct = default);
}