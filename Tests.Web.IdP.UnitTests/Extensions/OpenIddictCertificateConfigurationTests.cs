using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Server;
using Web.IdP.Extensions;

namespace Tests.Web.IdP.UnitTests.Extensions;

public class OpenIddictCertificateConfigurationTests
{
    [Theory]
    [InlineData("absent")]
    [InlineData("signing-missing")]
    [InlineData("encryption-missing")]
    [InlineData("malformed")]
    [InlineData("wrong-password")]
    public void Production_ShouldRejectMissingOrUnreadableCredentialsAtOptionsEvaluation(string condition)
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var path = CreateCertificate(password);
        try
        {
            var values = Credentials(path, password);
            if (condition == "absent") values.Clear();
            if (condition == "signing-missing") values["Certificates:SigningCertificatePath"] = path + ".missing";
            if (condition == "encryption-missing") values["Certificates:EncryptionCertificatePath"] = path + ".missing";
            if (condition == "malformed") File.WriteAllText(path, "not a certificate");
            if (condition == "wrong-password") values["Certificates:EncryptionCertificatePassword"] = Guid.NewGuid().ToString();
            Assert.ThrowsAny<Exception>(() =>
            {
                using var services = CreateServices(Environments.Production, values);
                _ = services.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value;
            });
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Production_ShouldAcceptExplicitSelfSignedPfx()
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var path = CreateCertificate(password);
        try
        {
            using var services = CreateServices(Environments.Production, Credentials(path, password));
            var options = services.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value;
            Assert.NotEmpty(options.SigningCredentials);
            Assert.NotEmpty(options.EncryptionCredentials);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void DevelopmentAndTest_ShouldRetainEphemeralTestingMode(string environment)
    {
        using var services = CreateServices(environment, new() { ["OpenIddict:UseEphemeralKeysForTesting"] = "true" });
        Assert.NotEmpty(services.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value.SigningCredentials);
    }

    private static ServiceProvider CreateServices(string name, Dictionary<string, string?> values)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(name);
        environment.SetupGet(value => value.ApplicationName).Returns("Web.IdP");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCustomIdentityAndAccess(new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            environment.Object, "SqlServer", "Server=(localdb)\\mssqllocaldb;Database=CertificateOptions;Trusted_Connection=True");
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> Credentials(string path, string password) => new()
    {
        ["Certificates:SigningCertificatePath"] = path, ["Certificates:SigningCertificatePassword"] = password,
        ["Certificates:EncryptionCertificatePath"] = path, ["Certificates:EncryptionCertificatePassword"] = password
    };

    private static string CreateCertificate(string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=HybridIdP certificate unit test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var path = Path.Combine(Path.GetTempPath(), "hybrididp-cert-" + Guid.NewGuid().ToString("N") + ".pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return path;
    }
}
