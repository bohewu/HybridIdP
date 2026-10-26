using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Web.IdP.Services.Saml.Sp;

public sealed class SamlSpAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, SamlSpService service)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        try
        {
            var destination = await service.ChallengeAsync(Context, properties, Context.RequestAborted);
            Response.Headers.CacheControl = "no-store";
            Response.Redirect(destination);
        }
        catch (OperationCanceledException) when (Context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Never log the SAML XML, claims, certificate paths, browser nonce or provider exception.
            Logger.LogWarning("SAML SP challenge was rejected.");
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }
}
