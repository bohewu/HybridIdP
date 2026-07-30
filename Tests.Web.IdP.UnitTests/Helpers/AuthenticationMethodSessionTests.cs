using Core.Domain.Constants;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP.Helpers;

namespace Tests.Web.IdP.UnitTests.Helpers;

public class AuthenticationMethodSessionTests
{
    [Fact]
    public void CreateClaims_LocalPasswordAndOtp_PreservesTruthfulMethods()
    {
        var session = new MemorySession();

        AuthenticationMethodSession.Replace(session, AuthConstants.Amr.Password);
        AuthenticationMethodSession.Add(
            session,
            AuthConstants.Amr.Mfa,
            AuthConstants.Amr.Otp);

        var methods = AuthenticationMethodSession.CreateClaims(session)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Equal(
            [AuthConstants.Amr.Password, AuthConstants.Amr.Mfa, AuthConstants.Amr.Otp],
            methods);
    }

    [Fact]
    public void CreateClaims_ExternalLoginAndOtp_DoesNotInventPasswordMethod()
    {
        var session = new MemorySession();

        AuthenticationMethodSession.Replace(session, AuthConstants.Amr.External);
        AuthenticationMethodSession.Add(
            session,
            AuthConstants.Amr.Mfa,
            AuthConstants.Amr.Otp);

        var methods = AuthenticationMethodSession.CreateClaims(session)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Equal(
            [AuthConstants.Amr.External, AuthConstants.Amr.Mfa, AuthConstants.Amr.Otp],
            methods);
        Assert.DoesNotContain(AuthConstants.Amr.Password, methods);
    }

    [Fact]
    public void CreateClaims_ExternalLoginAndPasskeySetup_PreservesBothFactors()
    {
        var session = new MemorySession();

        AuthenticationMethodSession.Replace(session, AuthConstants.Amr.External);
        AuthenticationMethodSession.Add(
            session,
            AuthConstants.Amr.HardwareKey,
            AuthConstants.Amr.UserPresence,
            AuthConstants.Amr.Mfa);

        var methods = AuthenticationMethodSession.CreateClaims(session)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Equal(
            [
                AuthConstants.Amr.External,
                AuthConstants.Amr.HardwareKey,
                AuthConstants.Amr.UserPresence,
                AuthConstants.Amr.Mfa
            ],
            methods);
        Assert.DoesNotContain(AuthConstants.Amr.Password, methods);
    }

    [Fact]
    public void CreateClaims_MfaOnlySession_RestoresLocalPasswordFallback()
    {
        var session = new MemorySession();
        AuthenticationMethodSession.Replace(
            session,
            AuthConstants.Amr.Mfa,
            AuthConstants.Amr.Otp);

        var methods = AuthenticationMethodSession.CreateClaims(session)
            .Select(claim => claim.Value)
            .ToList();

        Assert.Equal(
            [AuthConstants.Amr.Password, AuthConstants.Amr.Mfa, AuthConstants.Amr.Otp],
            methods);
    }
}
