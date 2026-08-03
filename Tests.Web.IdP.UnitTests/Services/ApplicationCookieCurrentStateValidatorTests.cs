using System.Security.Claims;
using Core.Application;
using Core.Domain;
using Core.Domain.Constants;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Services;

public class ApplicationCookieCurrentStateValidatorTests
{
    [Theory]
    [InlineData(UserFailure.Inactive)]
    [InlineData(UserFailure.SoftDeleted)]
    [InlineData(UserFailure.LockedOut)]
    public async Task ValidateAsync_ShouldRejectPrincipal_WhenUserIsIneligible(UserFailure failure)
    {
        await using var database = CreateDatabase();
        var user = CreateUser();

        switch (failure)
        {
            case UserFailure.Inactive:
                user.IsActive = false;
                break;
            case UserFailure.SoftDeleted:
                user.IsDeleted = true;
                break;
            case UserFailure.LockedOut:
                user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(1);
                break;
        }

        database.Users.Add(user);
        await database.SaveChangesAsync();
        var cookieContext = CreateCookieContext(CreatePrincipal(user.Id));

        await new ApplicationCookieCurrentStateValidator(database).ValidateAsync(cookieContext);

        Assert.Null(cookieContext.Principal);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectPrincipal_WhenLinkedPersonIsMissing()
    {
        await using var database = CreateDatabase();
        var user = CreateUser();
        user.PersonId = Guid.NewGuid();
        database.Users.Add(user);
        await database.SaveChangesAsync();
        var cookieContext = CreateCookieContext(CreatePrincipal(user.Id));

        await new ApplicationCookieCurrentStateValidator(database).ValidateAsync(cookieContext);

        Assert.Null(cookieContext.Principal);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectPrincipal_WhenLinkedPersonCannotAuthenticate()
    {
        await using var database = CreateDatabase();
        var person = new Person
        {
            Id = Guid.NewGuid(),
            Status = PersonStatus.Suspended
        };
        var user = CreateUser(person.Id);
        database.AddRange(person, user);
        await database.SaveChangesAsync();
        var cookieContext = CreateCookieContext(CreatePrincipal(user.Id));

        await new ApplicationCookieCurrentStateValidator(database).ValidateAsync(cookieContext);

        Assert.Null(cookieContext.Principal);
    }

    [Fact]
    public async Task ValidateAsync_ShouldKeepPrincipal_WhenUserAndPersonAreEligible()
    {
        await using var database = CreateDatabase();
        var person = new Person
        {
            Id = Guid.NewGuid(),
            Status = PersonStatus.Active
        };
        var user = CreateUser(person.Id);
        database.AddRange(person, user);
        await database.SaveChangesAsync();
        var principal = CreatePrincipal(user.Id);
        var cookieContext = CreateCookieContext(principal);

        await new ApplicationCookieCurrentStateValidator(database).ValidateAsync(cookieContext);

        Assert.Same(principal, cookieContext.Principal);
    }

    [Fact]
    public async Task Compose_ShouldKeepBaseValidatorRejection()
    {
        var baseValidatorCalled = false;
        var cookieContext = CreateCookieContext(CreatePrincipal(Guid.NewGuid()));
        var validation = ApplicationCookieCurrentStateValidator.Compose(context =>
        {
            baseValidatorCalled = true;
            context.RejectPrincipal();
            return Task.CompletedTask;
        });

        await validation(cookieContext);

        Assert.True(baseValidatorCalled);
        Assert.Null(cookieContext.Principal);
    }

    [Fact]
    public async Task Compose_ShouldRejectCurrentIneligibleUser_WhenBaseStampValidationDoesNothing()
    {
        await using var database = CreateDatabase();
        var user = CreateUser();
        user.IsActive = false;
        database.Users.Add(user);
        await database.SaveChangesAsync();

        using var services = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(database)
            .AddSingleton<ApplicationCookieCurrentStateValidator>()
            .BuildServiceProvider();
        var cookieContext = CreateCookieContext(CreatePrincipal(user.Id), services);
        var validation = ApplicationCookieCurrentStateValidator.Compose(_ => Task.CompletedTask);

        await validation(cookieContext);

        Assert.Null(cookieContext.Principal);
    }

    [Fact]
    public async Task Compose_ShouldKeepRefreshedImpersonationPrincipal_WhenCurrentStateIsEligible()
    {
        await using var database = CreateDatabase();
        var person = new Person
        {
            Id = Guid.NewGuid(),
            Status = PersonStatus.Active
        };
        var user = CreateUser(person.Id);
        database.AddRange(person, user);
        await database.SaveChangesAsync();

        using var services = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(database)
            .AddSingleton<ApplicationCookieCurrentStateValidator>()
            .BuildServiceProvider();
        var impersonatorId = Guid.NewGuid().ToString();
        var actor = new ClaimsIdentity("Impersonation");
        actor.AddClaim(new Claim(ClaimTypes.NameIdentifier, impersonatorId));
        var refreshedPrincipal = CreatePrincipal(user.Id);
        var refreshedIdentity = Assert.IsType<ClaimsIdentity>(refreshedPrincipal.Identity);
        refreshedIdentity.Actor = actor;
        refreshedIdentity.AddClaim(new Claim(AuthConstants.Claims.ImpersonatorId, impersonatorId));
        var cookieContext = CreateCookieContext(CreatePrincipal(user.Id), services);
        var validation = ApplicationCookieCurrentStateValidator.Compose(context =>
        {
            context.ReplacePrincipal(refreshedPrincipal);
            return Task.CompletedTask;
        });

        await validation(cookieContext);

        Assert.Same(refreshedPrincipal, cookieContext.Principal);
        var validatedIdentity = Assert.IsType<ClaimsIdentity>(cookieContext.Principal!.Identity);
        Assert.Same(actor, validatedIdentity.Actor);
        Assert.Equal(
            impersonatorId,
            cookieContext.Principal.FindFirst(AuthConstants.Claims.ImpersonatorId)?.Value);
    }

    private static ApplicationDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ApplicationUser CreateUser(Guid? personId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserName = "cookie-validator-user",
        NormalizedUserName = "COOKIE-VALIDATOR-USER",
        IsActive = true,
        PersonId = personId,
        SecurityStamp = Guid.NewGuid().ToString()
    };

    private static ClaimsPrincipal CreatePrincipal(Guid userId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            IdentityConstants.ApplicationScheme));

    private static CookieValidatePrincipalContext CreateCookieContext(
        ClaimsPrincipal principal,
        IServiceProvider? services = null)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services ?? new ServiceCollection().BuildServiceProvider()
        };
        var scheme = new AuthenticationScheme(
            IdentityConstants.ApplicationScheme,
            displayName: null,
            typeof(CookieAuthenticationHandler));
        var ticket = new AuthenticationTicket(
            principal,
            new AuthenticationProperties(),
            IdentityConstants.ApplicationScheme);

        return new CookieValidatePrincipalContext(
            httpContext,
            scheme,
            new CookieAuthenticationOptions(),
            ticket);
    }

    public enum UserFailure
    {
        Inactive,
        SoftDeleted,
        LockedOut
    }
}
