using Core.Domain;

namespace Core.Application.Ports;

/// <summary>Reads current upstream properties for one preview without saving or refreshing snapshots.</summary>
public interface IProviderProfilePreviewReader
{
    Task<ProviderProfileSnapshotResult> ReadForPreviewAsync(ApplicationUser user, string source,
        CancellationToken cancellationToken = default);
}
