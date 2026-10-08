using System.Formats.Cbor;
using System.Text;
using System.Text.Json;
using Core.Application;
using Core.Domain;
using Core.Domain.Entities;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Infrastructure;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Tests.Infrastructure.IntegrationTests;

[Collection(OperationalAdminBootstrapProviderCollection.CollectionName)]
public sealed class PasskeyEnrollmentProviderTests(OperationalAdminBootstrapProviderFixture fixture)
{
    [Theory]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, "none")]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, "none")]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, "reset")]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, "reset")]
    [InlineData(OperationalAdminBootstrapProviderFixture.SqlServer, "removal")]
    [InlineData(OperationalAdminBootstrapProviderFixture.PostgreSql, "removal")]
    public async Task RegisterCredentialsAsync_ShouldCommitOnlyWhileAuthorizingUserVersionIsCurrent(string providerName, string intervention)
    {
        var database = fixture.GetDatabase(providerName);
        await database.ResetAsync();
        await using var services = database.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "enrollment-user" };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        var originalStamp = user.SecurityStamp;
        var existing = new UserCredential { UserId = user.Id, CredentialId = [9], PublicKey = [8] };
        if (intervention == "removal")
        {
            db.UserCredentials.Add(existing);
            await db.SaveChangesAsync();
        }
        var fido = new Mock<IFido2>();
        var policy = new Mock<ISecurityPolicyService>();
        policy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());
        fido.Setup(value => value.MakeNewCredentialAsync(It.IsAny<MakeNewCredentialParams>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // This is the existing FIDO verification seam; persistence uses real Identity and EF stores.
                if (intervention != "none")
                {
                    await using var competingScope = services.CreateAsyncScope();
                    var competingUsers = competingScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                    var current = (await competingUsers.FindByIdAsync(user.Id.ToString()))!;
                    if (intervention == "reset") Assert.True((await competingUsers.UpdateSecurityStampAsync(current)).Succeeded);
                    else
                    {
                        var competingDb = competingScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var removal = new PasskeyService(fido.Object, competingUsers, competingDb,
                            NullLogger<PasskeyService>.Instance, policy.Object);
                        Assert.True(await removal.DeletePasskeyAsync(user.Id, existing.Id));
                        Assert.NotEqual(originalStamp, current.SecurityStamp);
                    }
                }
                return new RegisteredPublicKeyCredential
                {
                    Type = PublicKeyCredentialType.PublicKey, Id = [1], PublicKey = [2], SignCount = 0,
                    AaGuid = Guid.Empty, User = new Fido2User { Id = Encoding.UTF8.GetBytes(user.Id.ToString()),
                        Name = user.UserName!, DisplayName = user.UserName! },
                    AttestationFormat = "none", AttestationObject = [], AttestationClientDataJson = [], Transports = []
                };
            });
        var sut = new PasskeyService(fido.Object, users, db, NullLogger<PasskeyService>.Instance, policy.Object);
        var options = JsonSerializer.Serialize(new
        {
            rp = new { id = "localhost", name = "Tests" },
            user = new { id = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(user.Id.ToString())), name = user.UserName, displayName = user.UserName },
            challenge = "AQ", pubKeyCredParams = new[] { new { type = "public-key", alg = -7 } }
        });
        var result = await sut.RegisterCredentialsAsync(user, CreateAttestationResponse(), options);
        fido.Verify(value => value.MakeNewCredentialAsync(It.IsAny<MakeNewCredentialParams>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(intervention == "none", result.Success);
        // A later legitimate save must not resurrect a rejected enrollment.
        user.PhoneNumber = "later-save";
        Assert.True((await users.UpdateAsync(user)).Succeeded);
        var keys = await db.UserCredentials.AsNoTracking().ToListAsync();
        if (intervention == "none")
        {
            Assert.True(result.UserVerified);
            Assert.Single(keys);
            Assert.Null(keys[0].DisabledAtUtc);
            Assert.Equal(originalStamp, user.SecurityStamp);
        }
        else
        {
            Assert.DoesNotContain(keys, key => key.DisabledAtUtc == null);
            Assert.Equal(intervention == "removal" ? 1 : 0, keys.Count);
            Assert.NotEqual(originalStamp, user.SecurityStamp);
        }
    }

    private static string CreateAttestationResponse()
    {
        // Parseable UV/UP flags; cryptographic verification is explicitly mocked above.
        var authData = new byte[37];
        authData[32] = 5;
        var writer = new CborWriter();
        writer.WriteStartMap(3);
        writer.WriteTextString("fmt"); writer.WriteTextString("none");
        writer.WriteTextString("attStmt"); writer.WriteStartMap(0); writer.WriteEndMap();
        writer.WriteTextString("authData"); writer.WriteByteString(authData);
        writer.WriteEndMap();
        return JsonSerializer.Serialize(new
        {
            id = "AQ", rawId = "AQ", type = "public-key",
            response = new { attestationObject = WebEncoders.Base64UrlEncode(writer.Encode()),
                clientDataJSON = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"type\":\"webauthn.create\",\"challenge\":\"AQ\",\"origin\":\"https://localhost\"}")) }
        });
    }
}
