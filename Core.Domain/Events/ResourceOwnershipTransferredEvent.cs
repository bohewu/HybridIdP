using System;

namespace Core.Domain.Events;

public class ResourceOwnershipTransferredEvent
{
    public Guid FromPersonId { get; }
    public Guid ToPersonId { get; }
    public int ScopesTransferred { get; }
    public int ClientsTransferred { get; }
    public int ApiResourcesTransferred { get; }

    public ResourceOwnershipTransferredEvent(Guid fromPersonId, Guid toPersonId, int scopesTransferred, int clientsTransferred, int apiResourcesTransferred)
    {
        FromPersonId = fromPersonId;
        ToPersonId = toPersonId;
        ScopesTransferred = scopesTransferred;
        ClientsTransferred = clientsTransferred;
        ApiResourcesTransferred = apiResourcesTransferred;
    }
}
