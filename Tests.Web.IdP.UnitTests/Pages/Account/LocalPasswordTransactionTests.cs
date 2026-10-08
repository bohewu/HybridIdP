using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Interfaces;
using Core.Application.Options;
using Core.Domain;
using Core.Domain.Entities;
using HybridIdP.Infrastructure.Identity;
using Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP;
using Web.IdP.Controllers.Api;
using Web.IdP.Pages.Account;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public sealed class LocalPasswordTransactionTests
{
    [Fact]
    public async Task ChangePassword_ShouldPersistBoundedHistoryAndActualDate_AndRejectRecentReuse()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var oldHash = user.PasswordHash!;
        var olderHash = fixture.Users.PasswordHasher.HashPassword(user, "OlderPassword3!");
        var oldestHash = fixture.Users.PasswordHasher.HashPassword(user, "OldestPassword4!");
        user.PasswordHistory = JsonSerializer.Serialize(new[] { olderHash, oldestHash });
        Assert.True((await fixture.Users.UpdateAsync(user)).Succeeded);
        var before = DateTime.UtcNow;

        Assert.IsType<OkObjectResult>(await fixture.ChangeAsync(user));

        user = await fixture.ReloadAsync(user);
        Assert.InRange(user.LastPasswordChangeDate!.Value, before, DateTime.UtcNow);
        Assert.Equal(new[] { oldHash, olderHash }, JsonSerializer.Deserialize<string[]>(user.PasswordHistory));
        Assert.True(await fixture.Users.CheckPasswordAsync(user, Fixture.NewPassword));
        var validator = new DynamicPasswordValidator(fixture.PolicyService.Object,
            Mock.Of<ILogger<DynamicPasswordValidator>>());
        var reuse = await validator.ValidateAsync(fixture.Users, user, Fixture.OldPassword);
        Assert.Contains(reuse.Errors, error => error.Code == "PasswordReuse");
        // Count two compares current plus one prior, not two prior plus current.
        Assert.True((await validator.ValidateAsync(fixture.Users, user, "OlderPassword3!")).Succeeded);
    }

    [Theory]
    [InlineData(0, "malformed")]
    [InlineData(2, "malformed")]
    public async Task ChangePassword_ShouldPreserveDisabledAndMalformedHistoryBehavior(int count, string history)
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var oldHash = user.PasswordHash!;
        user.PasswordHistory = history;
        Assert.True((await fixture.Users.UpdateAsync(user)).Succeeded);
        fixture.Policy.PasswordHistoryCount = count;

        Assert.IsType<OkObjectResult>(await fixture.ChangeAsync(user));

        user = await fixture.ReloadAsync(user);
        if (count == 0) Assert.Equal(history, user.PasswordHistory);
        else Assert.Equal(new[] { oldHash }, JsonSerializer.Deserialize<string[]>(user.PasswordHistory));
        Assert.NotNull(user.LastPasswordChangeDate);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("audit")]
    public async Task ChangePassword_ShouldRollbackHashStampHistoryAndDate_WhenLaterWriteFails(string failure)
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var oldHash = user.PasswordHash;
        var oldStamp = user.SecurityStamp;
        var oldDate = user.LastPasswordChangeDate;
        var oldHistory = user.PasswordHistory;
        fixture.Rejection.RejectRecentDate = failure == "metadata";
        if (failure == "audit")
        {
            fixture.Audit.Setup(audit => audit.LogEventAsync(It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("audit failure"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ChangeAsync(user));
        }
        else Assert.IsType<BadRequestObjectResult>(await fixture.ChangeAsync(user));

        user = await fixture.ReloadAsync(user);
        Assert.Equal(oldHash, user.PasswordHash);
        Assert.Equal(oldStamp, user.SecurityStamp);
        Assert.Equal(oldDate, user.LastPasswordChangeDate);
        Assert.Equal(oldHistory, user.PasswordHistory);
        Assert.True(await fixture.Users.CheckPasswordAsync(user, Fixture.OldPassword));
        Assert.False(await fixture.Users.CheckPasswordAsync(user, Fixture.NewPassword));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task ChangePassword_ShouldPreserveMinimumAgeExceptions(bool forced, bool expired, bool allowed)
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        user.LastPasswordChangeDate = DateTime.UtcNow.AddDays(expired ? -2 : 0);
        user.RequiresPasswordChange = forced;
        Assert.True((await fixture.Users.UpdateAsync(user)).Succeeded);
        var oldHash = user.PasswordHash;
        fixture.Policy.MinPasswordAgeDays = 30;
        fixture.Policy.PasswordExpirationDays = expired ? 1 : 180;

        var result = await fixture.ChangeAsync(user);

        if (allowed) Assert.IsType<OkObjectResult>(result);
        else
        {
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(oldHash, (await fixture.ReloadAsync(user)).PasswordHash);
        }
    }

    [Fact]
    public async Task ChangePassword_ShouldPersistWrongPasswordLockout_OutsideRolledBackTransaction()
    {
        await using var fixture = await Fixture.CreateAsync();
        var user = await fixture.CreateUserAsync();
        var oldHash = user.PasswordHash;
        var oldDate = user.LastPasswordChangeDate;
        fixture.Policy.MaxFailedAccessAttempts = 1;

        Assert.IsType<BadRequestObjectResult>(await fixture.ChangeAsync(user, "WrongPassword9!"));

        user = await fixture.ReloadAsync(user);
        Assert.True(await fixture.Users.IsLockedOutAsync(user));
        Assert.Equal(oldHash, user.PasswordHash);
        Assert.Equal(oldDate, user.LastPasswordChangeDate);
        var blocked = Assert.IsType<ObjectResult>(await fixture.ChangeAsync(user));
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task Register_ShouldCommitInitialDateAndPersonBeforeSignIn_WithPositiveMinimumAge()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Policy.MinPasswordAgeDays = 30;
        var model = fixture.Register();
        var before = DateTime.UtcNow;
        fixture.SignIn.Setup(manager => manager.SignInWithClaimsAsync(It.IsAny<ApplicationUser>(), false,
            It.IsAny<IEnumerable<Claim>>())).Callback<ApplicationUser, bool, IEnumerable<Claim>>((user, _, _) =>
            {
                Assert.Null(fixture.Db.Database.CurrentTransaction);
                Assert.NotNull(fixture.Db.Users.AsNoTracking().Single().LastPasswordChangeDate);
            }).Returns(Task.CompletedTask);

        Assert.IsType<RedirectResult>(await model.OnPostAsync("/continue"));

        fixture.Db.ChangeTracker.Clear();
        var created = await fixture.Db.Users.SingleAsync();
        Assert.InRange(created.LastPasswordChangeDate!.Value, before, DateTime.UtcNow);
        Assert.Equal((await fixture.Db.Persons.SingleAsync()).Id, created.PersonId);
        Assert.True(await fixture.Users.CheckPasswordAsync(created, Fixture.NewPassword));
        Assert.True(await fixture.Users.IsInRoleAsync(created, "User"));
        fixture.SignIn.Verify(manager => manager.SignInWithClaimsAsync(It.IsAny<ApplicationUser>(), false,
            It.IsAny<IEnumerable<Claim>>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Register_ShouldRollbackUserAndPersonAndIssueNoCookie_WhenValidationOrDateUpdateFails(bool dateFailure)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Policy.MinPasswordAgeDays = 30;
        fixture.Rejection.RejectRecentDate = dateFailure;
        var model = fixture.Register();
        if (!dateFailure) model.Input.Password = model.Input.ConfirmPassword = "weakpassword";

        Assert.IsType<PageResult>(await model.OnPostAsync("/continue"));

        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.Users.ToListAsync());
        Assert.Empty(await fixture.Db.Persons.ToListAsync());
        Assert.False(model.ModelState.IsValid);
        fixture.SignIn.Verify(manager => manager.SignInWithClaimsAsync(It.IsAny<ApplicationUser>(), false,
            It.IsAny<IEnumerable<Claim>>()), Times.Never);
        Assert.False(model.Response.Headers.ContainsKey("Set-Cookie"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string OldPassword = "OldPassword1!";
        public const string NewPassword = "NewPassword2!";
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        public readonly SecurityPolicy Policy = new() { PasswordHistoryCount = 2, MinPasswordAgeDays = 0 };
        public readonly Mock<ISecurityPolicyService> PolicyService = new();
        public readonly Mock<IAuditService> Audit = new();
        public readonly RejectDateValidator Rejection = new();
        public ApplicationDbContext Db { get; }
        public UserManager<ApplicationUser> Users { get; }
        public Mock<SignInManager<ApplicationUser>> SignIn { get; }

        private Fixture(SqliteConnection connection)
        {
            _connection = connection;
            PolicyService.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(Policy);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(PolicyService.Object);
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection).UseOpenIddict<Guid>());
            services.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<ApplicationDbContext>();
            services.AddScoped<IPasswordValidator<ApplicationUser>, DynamicPasswordValidator>();
            services.AddSingleton<IUserValidator<ApplicationUser>>(Rejection);
            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateScope();
            Db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Users = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            SignIn = new Mock<SignInManager<ApplicationUser>>(Users, Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(), null, null, null, null);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);
            await fixture.Db.Database.EnsureCreatedAsync();
            var roles = fixture._scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            Assert.True((await roles.CreateAsync(new ApplicationRole { Name = "User" })).Succeeded);
            return fixture;
        }

        public async Task<ApplicationUser> CreateUserAsync()
        {
            var user = new ApplicationUser { UserName = "local@example.test", Email = "local@example.test" };
            Assert.True((await Users.CreateAsync(user, OldPassword)).Succeeded);
            user.LastPasswordChangeDate = DateTime.UtcNow.AddDays(-40);
            Assert.True((await Users.UpdateAsync(user)).Succeeded);
            return user;
        }

        public async Task<ApplicationUser> ReloadAsync(ApplicationUser user)
        {
            Db.ChangeTracker.Clear();
            return await Db.Users.SingleAsync(candidate => candidate.Id == user.Id);
        }

        public Task<IActionResult> ChangeAsync(ApplicationUser user, string password = OldPassword)
        {
            var controller = new ProfileManagementController(Mock.Of<ICurrentUserLifecycleEligibility>(), Users,
                SignIn.Object, Db, PolicyService.Object, Mock.Of<IPasskeyService>(), Audit.Object,
                Mock.Of<ILogger<ProfileManagementController>>(), Microsoft.Extensions.Options.Options.Create(new ExternalLoginOptions()));
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"));
            return controller.ChangePassword(new ChangePasswordRequest
            { CurrentPassword = password, NewPassword = NewPassword, ConfirmPassword = NewPassword });
        }

        public RegisterModel Register()
        {
            var lifecycle = new Mock<ICurrentUserLifecycleEligibility>();
            lifecycle.Setup(service => service.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var localizer = new Mock<IStringLocalizer<SharedResource>>();
            localizer.Setup(value => value[It.IsAny<string>(), It.IsAny<object[]>()])
                .Returns((string key, object[] _) => new LocalizedString(key, key));
            var model = new RegisterModel(lifecycle.Object, Users, SignIn.Object, Mock.Of<ITurnstileService>(),
                Microsoft.Extensions.Options.Options.Create(new TurnstileOptions()), Mock.Of<ILogger<RegisterModel>>(), Db, Audit.Object,
                Mock.Of<ISettingsService>(), Mock.Of<ITurnstileStateService>(), PolicyService.Object, localizer.Object)
            { Input = new RegisterModel.InputModel { Email = "registered@example.test", Password = NewPassword, ConfirmPassword = NewPassword } };
            var context = new DefaultHttpContext();
            context.Features.Set<ISessionFeature>(new SessionFeature { Session = new MemorySession() });
            model.PageContext = new PageContext(new ActionContext(context, new RouteData(), new ActionDescriptor()));
            return model;
        }

        public async ValueTask DisposeAsync()
        {
            _scope.Dispose();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RejectDateValidator : IUserValidator<ApplicationUser>
    {
        public bool RejectRecentDate { get; set; }
        public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
            Task.FromResult(RejectRecentDate && user.LastPasswordChangeDate > DateTime.UtcNow.AddMinutes(-1)
                ? IdentityResult.Failed(new IdentityError { Code = "DateUpdateRejected", Description = "Injected metadata persistence rejection" })
                : IdentityResult.Success);
    }

    private sealed class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = default!;
    }
}
