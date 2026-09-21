namespace Infrastructure.Services;

public interface IOpenIddictSubjectTokenRevoker
{
    Task<int> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken);
}
