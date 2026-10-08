using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Events;
using Infrastructure;
using Infrastructure.Services;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Moq;
using OpenIddict.Abstractions;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class UserManagementTransactionTests
{
    [Theory]
    [InlineData("generic", "b@example.test")]
    [InlineData("without-roles", "b@example.test")]
    [InlineData("jit", "b@example.test")]
    [InlineData("stage1", "b@example.test")]
    [InlineData("generic", "A@example.test")]
    [InlineData("without-roles", "A@example.test")]
    [InlineData("jit", "A@example.test")]
    [InlineData("stage1", "A@example.test")]
    [InlineData("generic", "a@example.test")]
    [InlineData("without-roles", "a@example.test")]
    [InlineData("jit", "a@example.test")]
    [InlineData("stage1", "a@example.test")]
    public async Task EmailProfileWriter_ShouldKeepPendingMfaProofBoundToItsOriginalDestination(
        string writer, string destination)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        using var manager = CreateManager(context);
        using var roleManager = CreateRoleManager(context);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Original", Email = "a@example.test" };
        var user = new ApplicationUser
        {
            UserName = "pending-factor", Email = "a@example.test", EmailConfirmed = true,
            Person = person, PersonId = person.Id
        };
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        Assert.True((await manager.AddLoginAsync(user, new UserLoginInfo("Legacy", "subject", "Legacy"))).Succeeded);
        var deliveries = new List<(string Destination, string Code)>();
        var email = new Mock<IEmailService>();
        email.Setup(service => service.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, bool, CancellationToken>((address, _, code, _, _) => deliveries.Add((address, code)))
            .Returns(Task.CompletedTask);
        var templates = new Mock<IEmailTemplateService>();
        templates.Setup(service => service.RenderMfaCodeEmailAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((string code, int _, string? _) => ("MFA", code));
        // Delivery/rendering and cooldown are existing mock seams; hashing, Identity,
        // attempt reservation, email writers and SQLite persistence are real.
        var mfa = new MfaService(manager, Mock.Of<IBrandingService>(), email.Object, templates.Object,
            new PasswordHasher<ApplicationUser>(), Mock.Of<IDistributedCache>(), Mock.Of<ISecurityPolicyService>(),
            new EmailMfaAttemptStore(context), context, Mock.Of<ILogger<MfaService>>());
        Assert.True((await mfa.SendEmailMfaCodeAsync(user)).Success);
        var originalDelivery = Assert.Single(deliveries);
        Assert.Equal("a@example.test", originalDelivery.Destination);
        var originalHash = user.EmailMfaCode;
        var originalExpiry = user.EmailMfaCodeExpiry;
        user.EmailMfaVerificationAttempts = 2;
        Assert.True((await manager.UpdateAsync(user)).Succeeded);
        var originalStamp = user.SecurityStamp;

        if (writer is "generic" or "without-roles")
        {
            var service = new UserManagementService(manager, roleManager, Mock.Of<IDomainEventPublisher>(),
                context, Mock.Of<IOpenIddictApplicationManager>());
            var request = new UpdateUserDto
            {
                Email = destination, EmailConfirmed = true, IsActive = true, FirstName = "Updated", Roles = []
            };
            var result = writer == "generic"
                ? await service.UpdateUserAsync(user.Id, request)
                : await service.UpdateUserWithoutRolesAsync(user.Id, request);
            Assert.True(result.Success);
        }
        else if (writer == "jit")
        {
            var service = new JitProvisioningService(manager, context, Options.Create(new ExternalLoginOptions()));
            await service.ProvisionExternalUserAsync(new ExternalAuthResult
            {
                Provider = "Legacy", ProviderKey = "subject", Email = destination,
                EmailVerified = true, FirstName = "Updated"
            });
        }
        else
        {
            var outcome = await new Stage1BindingRefreshService(context).BindAndRefreshAsync(new Stage1BindingRefreshRequest(
                user.Id, "provider", "subject", new ManagedDirectoryIdentity(Guid.NewGuid(), "account", true, true, false,
                    new AssuredProfile
                    {
                        Email = destination, GivenName = "Updated",
                        AssuredFields = [AssuredProfileField.Email, AssuredProfileField.GivenName]
                    })));
            Assert.Equal(Stage1BindingRefreshOutcome.BoundAndRefreshed, outcome);
        }

        context.ChangeTracker.Clear();
        user = await context.Users.Include(candidate => candidate.Person).SingleAsync();
        Assert.Equal(destination, user.Email);
        Assert.Equal(originalStamp, user.SecurityStamp);
        Assert.True(user.EmailConfirmed);
        Assert.False(user.EmailMfaEnabled);
        Assert.Equal("Updated", user.FirstName);
        Assert.Equal("Updated", user.Person!.FirstName);
        if (destination != originalDelivery.Destination)
        {
            Assert.False(await mfa.VerifyAndEnableEmailMfaAsync(user, originalDelivery.Code));
            Assert.Null(user.EmailMfaCode);
            Assert.Null(user.EmailMfaCodeExpiry);
            Assert.Equal(0, user.EmailMfaVerificationAttempts);
            context.ChangeTracker.Clear();
            user = await context.Users.SingleAsync();
            Assert.False(user.EmailMfaEnabled);
            Assert.True((await mfa.SendEmailMfaCodeAsync(user)).Success);
            var freshDelivery = deliveries[1];
            Assert.Equal(destination, freshDelivery.Destination);
            context.ChangeTracker.Clear();
            user = await context.Users.SingleAsync();
            Assert.True(await mfa.VerifyAndEnableEmailMfaAsync(user, freshDelivery.Code));
        }
        else
        {
            Assert.Equal(originalHash, user.EmailMfaCode);
            Assert.Equal(originalExpiry, user.EmailMfaCodeExpiry);
            Assert.Equal(2, user.EmailMfaVerificationAttempts);
            Assert.True(await mfa.VerifyAndEnableEmailMfaAsync(user, originalDelivery.Code));
            Assert.Single(deliveries);
        }
        context.ChangeTracker.Clear();
        var enrolled = await context.Users.SingleAsync();
        Assert.True(enrolled.EmailMfaEnabled);
        Assert.Equal(destination, enrolled.Email);
        Assert.Null(enrolled.EmailMfaCode);
        Assert.Null(enrolled.EmailMfaCodeExpiry);
        Assert.Equal(0, enrolled.EmailMfaVerificationAttempts);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task UpdateUser_ShouldPersistNeitherProfile_WhenIdentityValidationOrEnrollmentConcurrencyFails(
        bool updateRoles, bool concurrentEnrollment)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        using var manager = CreateManager(context);
        using var roleManager = CreateRoleManager(context);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Original", Email = "contact@example.test" };
        var user = new ApplicationUser
        {
            UserName = "original", Email = "factor@example.test", EmailConfirmed = true,
            EmailMfaCode = "original-pending-hash", EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5),
            EmailMfaVerificationAttempts = 2,
            FirstName = "Original", Person = person, PersonId = person.Id
        };
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        if (concurrentEnrollment)
        {
            // The editor keeps its unenrolled tracked snapshot while another request enrolls MFA.
            await using var enrollmentContext = new ApplicationDbContext(options);
            using var enrollmentManager = CreateManager(enrollmentContext);
            var enrolled = await enrollmentContext.Users.SingleAsync();
            enrolled.EmailMfaEnabled = true;
            Assert.True((await enrollmentManager.UpdateAsync(enrolled)).Succeeded);
        }
        else
        {
            Assert.True((await manager.CreateAsync(new ApplicationUser { UserName = "taken" })).Succeeded);
        }
        var events = new Mock<IDomainEventPublisher>();
        var service = new UserManagementService(manager, roleManager, events.Object,
            context, Mock.Of<IOpenIddictApplicationManager>());
        var request = new UpdateUserDto
        {
            UserName = concurrentEnrollment ? "original" : "taken", Email = "replacement@example.test",
            EmailConfirmed = true, FirstName = "Changed", IsActive = true, Roles = []
        };

        var result = updateRoles
            ? await service.UpdateUserAsync(user.Id, request)
            : await service.UpdateUserWithoutRolesAsync(user.Id, request);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Null(context.Database.CurrentTransaction);
        await context.SaveChangesAsync(); // Rejected tracked values cannot leak into a later save.
        context.ChangeTracker.Clear();
        var persisted = await context.Users.Include(candidate => candidate.Person).SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal("original", persisted.UserName);
        Assert.Equal("factor@example.test", persisted.Email);
        Assert.Equal("original-pending-hash", persisted.EmailMfaCode);
        Assert.NotNull(persisted.EmailMfaCodeExpiry);
        Assert.Equal(2, persisted.EmailMfaVerificationAttempts);
        Assert.Equal(concurrentEnrollment, persisted.EmailMfaEnabled);
        Assert.True(persisted.EmailConfirmed);
        Assert.Equal("Original", persisted.FirstName);
        Assert.Equal("Original", persisted.Person!.FirstName);
        Assert.Equal("contact@example.test", persisted.Person.Email);
        events.Verify(value => value.PublishAsync(It.IsAny<UserUpdatedEvent>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateUser_ShouldCommitProfilesOnlyWhenSubsequentIdentityRoleWriteSucceeds(bool roleFailure)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        using var manager = CreateManager(context);
        using var roleManager = CreateRoleManager(context);
        var person = new Person { Id = Guid.NewGuid(), FirstName = "Original" };
        var user = new ApplicationUser
        {
            UserName = "original", Email = "original@example.test", FirstName = "Original",
            EmailMfaCode = "original-pending-hash", EmailMfaCodeExpiry = DateTime.UtcNow.AddMinutes(5),
            EmailMfaVerificationAttempts = 2,
            Person = person, PersonId = person.Id
        };
        Assert.True((await manager.CreateAsync(user)).Succeeded);
        var managerMock = new Mock<UserManager<ApplicationUser>>(
            new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(context),
            Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!,
            Mock.Of<ILogger<UserManager<ApplicationUser>>>()) { CallBase = true };
        managerMock.Setup(value => value.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(roleFailure
                ? IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure", Description = "Role write conflict" })
                : IdentityResult.Success);
        var events = new Mock<IDomainEventPublisher>();
        var service = new UserManagementService(managerMock.Object, roleManager, events.Object,
            context, Mock.Of<IOpenIddictApplicationManager>());

        var result = await service.UpdateUserAsync(user.Id, new UpdateUserDto
        {
            Email = "changed@example.test", FirstName = "Changed", IsActive = true, Roles = ["User"]
        });

        Assert.Equal(!roleFailure, result.Success);
        Assert.Null(context.Database.CurrentTransaction);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var persisted = await context.Users.Include(candidate => candidate.Person).SingleAsync();
        Assert.Equal(roleFailure ? "original@example.test" : "changed@example.test", persisted.Email);
        Assert.Equal(roleFailure ? "original-pending-hash" : null, persisted.EmailMfaCode);
        Assert.Equal(roleFailure, persisted.EmailMfaCodeExpiry.HasValue);
        Assert.Equal(roleFailure ? 2 : 0, persisted.EmailMfaVerificationAttempts);
        Assert.Equal(roleFailure ? "Original" : "Changed", persisted.FirstName);
        Assert.Equal(roleFailure ? "Original" : "Changed", persisted.Person!.FirstName);
        events.Verify(value => value.PublishAsync(It.IsAny<UserUpdatedEvent>()), roleFailure ? Times.Never() : Times.Once());
        managerMock.Object.Dispose();
    }

    private static UserManager<ApplicationUser> CreateManager(ApplicationDbContext context) => new(
        new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(context),
        Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
        [new UserValidator<ApplicationUser>()], [], new UpperInvariantLookupNormalizer(),
        new IdentityErrorDescriber(), null!, Mock.Of<ILogger<UserManager<ApplicationUser>>>());

    private static RoleManager<ApplicationRole> CreateRoleManager(ApplicationDbContext context) => new(
        new RoleStore<ApplicationRole, ApplicationDbContext, Guid>(context), [],
        new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
        Mock.Of<ILogger<RoleManager<ApplicationRole>>>());
}
