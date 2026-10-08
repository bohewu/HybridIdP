using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain;
using Infrastructure;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class ProofProvisioningTests
{
    [Theory]
    [InlineData("account/123")]
    [InlineData("maximum")]
    public async Task ProvisionAsync_ShouldRetainOpaqueTupleAndResolveRepeatLogin(string subject)
    {
        if (subject == "maximum") subject = new string('x', 256);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        using var users = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(db),
            Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()], [], new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, NullLogger<UserManager<ApplicationUser>>.Instance);
        var service = new JitProvisioningService(users, db,
            Options.Create(new ExternalLoginOptions { AutoProvisionDefaultRole = null }));
        var auth = new ExternalAuthResult { Provider = "example.provider", ProviderKey = subject };
        var first = await service.ProvisionExternalUserAsync(auth);
        Assert.True(first.UserName!.Length <= 256);
        Assert.DoesNotContain('/', first.UserName);
        var link = Assert.Single(await users.GetLoginsAsync(first));
        Assert.Equal(subject, link.ProviderKey);
        Assert.Equal("example.provider", link.LoginProvider);
        var repeated = await service.ProvisionExternalUserAsync(auth);
        Assert.Equal(first.Id, repeated.Id);
        Assert.Single(db.Users);
        Assert.Single(db.Persons);
    }
}
