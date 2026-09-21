using System.Text.Json;
using Core.Application;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using HybridIdP.Infrastructure.Identity;
using Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using Tests.Web.IdP.UnitTests.TestSupport;
using Web.IdP;
using Web.IdP.Helpers;
using Web.IdP.Pages.Account;

namespace Tests.Web.IdP.UnitTests.Pages.Account;

public sealed class RequiredPasswordChangeModelTests
{
    [Fact]
    public async Task OnPostAsync_ValidForcedChange_UpdatesCredentialStateAndReturnsToLogin()
    {
        await using var fixture = await Fixture.CreateAsync();
        var oldHash = fixture.User.PasswordHash!;
        var model = fixture.CreateModel();
        model.Input = new RequiredPasswordChangeModel.InputModel
        {
            CurrentPassword = Fixture.OldPassword,
            NewPassword = Fixture.NewPassword,
            ConfirmPassword = Fixture.NewPassword
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Login", redirect.PageName);
        fixture.Db.ChangeTracker.Clear();
        var updated = (await fixture.UserManager.FindByIdAsync(fixture.User.Id.ToString()))!;
        Assert.False(updated.RequiresPasswordChange);
        Assert.NotNull(updated.LastPasswordChangeDate);
        Assert.Contains(oldHash, JsonSerializer.Deserialize<List<string>>(updated.PasswordHistory)!);
        Assert.True(await fixture.UserManager.CheckPasswordAsync(updated, Fixture.NewPassword));
        Assert.False(await fixture.UserManager.CheckPasswordAsync(updated, Fixture.OldPassword));
        Assert.False(model.HttpContext.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task OnPostAsync_WrongCurrentPassword_UsesConfiguredLockoutAndConsumesState()
    {
        await using var fixture = await Fixture.CreateAsync(maxFailedAttempts: 1);
        var model = fixture.CreateModel();
        model.Input = new RequiredPasswordChangeModel.InputModel
        {
            CurrentPassword = "WrongPassword1!",
            NewPassword = Fixture.NewPassword,
            ConfirmPassword = Fixture.NewPassword
        };

        Assert.IsType<PageResult>(await model.OnPostAsync(CancellationToken.None));

        Assert.True(await fixture.UserManager.IsLockedOutAsync(fixture.User));
        Assert.False(RequiredPasswordChangeSession.TryRead(
            model.HttpContext.Session,
            DateTimeOffset.UtcNow,
            out _));
    }

    [Fact]
    public async Task OnPostAsync_AuditFailure_RollsBackPasswordAndForcedState()
    {
        await using var fixture = await Fixture.CreateAsync(auditFailure: true);
        var model = fixture.CreateModel();
        model.Input = new RequiredPasswordChangeModel.InputModel
        {
            CurrentPassword = Fixture.OldPassword,
            NewPassword = Fixture.NewPassword,
            ConfirmPassword = Fixture.NewPassword
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => model.OnPostAsync(CancellationToken.None));

        fixture.Db.ChangeTracker.Clear();
        var unchanged = (await fixture.UserManager.FindByIdAsync(fixture.User.Id.ToString()))!;
        Assert.True(unchanged.RequiresPasswordChange);
        Assert.True(await fixture.UserManager.CheckPasswordAsync(unchanged, Fixture.OldPassword));
        Assert.False(await fixture.UserManager.CheckPasswordAsync(unchanged, Fixture.NewPassword));
    }

    [Fact]
    public async Task OnPostAsync_DirectoryState_UsesBoundChangeServiceWithoutLocalPasswordWrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalHash = fixture.User.PasswordHash;
        var directoryObjectId = Guid.NewGuid();
        var model = fixture.CreateDirectoryModel(directoryObjectId);
        model.Input = new RequiredPasswordChangeModel.InputModel
        {
            CurrentPassword = Fixture.OldPassword,
            NewPassword = Fixture.NewPassword,
            ConfirmPassword = Fixture.NewPassword
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.Equal("./Login", Assert.IsType<RedirectToPageResult>(result).PageName);
        fixture._directoryChange.Verify(service => service.ChangeAsync(
            It.Is<DirectoryRequiredCredentialChangeRequest>(request =>
                request.LocalAccountId == fixture.User.Id &&
                request.DirectoryObjectId == directoryObjectId),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(originalHash, fixture.User.PasswordHash);
        Assert.False(model.HttpContext.Response.Headers.ContainsKey("Set-Cookie"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string OldPassword = "OldPassword1!";
        public const string NewPassword = "NewPassword2!";

        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        private readonly Mock<ICredentialMigrationStateStore> _migration = new();
        private readonly Mock<IAuditService> _audit = new();
        private readonly Mock<IStringLocalizer<SharedResource>> _localizer = new();
        internal readonly Mock<IDirectoryRequiredCredentialChangeService> _directoryChange = new();
        private readonly ISecurityPolicyService _policy;

        private Fixture(
            SqliteConnection connection,
            ServiceProvider provider,
            IServiceScope scope,
            ISecurityPolicyService policy)
        {
            _connection = connection;
            _provider = provider;
            _scope = scope;
            _policy = policy;
            Db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            UserManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        }

        public ApplicationDbContext Db { get; }
        public UserManager<ApplicationUser> UserManager { get; }
        public ApplicationUser User { get; private set; } = default!;

        public static async Task<Fixture> CreateAsync(
            int maxFailedAttempts = 5,
            bool auditFailure = false)
        {
            var policy = new Mock<ISecurityPolicyService>();
            policy.Setup(service => service.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy
            {
                MinPasswordLength = 8,
                RequireUppercase = true,
                RequireLowercase = true,
                RequireDigit = true,
                RequireNonAlphanumeric = true,
                PasswordHistoryCount = 3,
                MinPasswordAgeDays = 30,
                MaxFailedAccessAttempts = maxFailedAttempts,
                LockoutDurationMinutes = 15
            });
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(policy.Object);
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(connection).UseOpenIddict<Guid>());
            services.AddIdentityCore<ApplicationUser>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            services.AddScoped<IPasswordValidator<ApplicationUser>, DynamicPasswordValidator>();
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateScope();
            var fixture = new Fixture(connection, provider, scope, policy.Object);
            await fixture.Db.Database.EnsureCreatedAsync();
            fixture.User = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "forced-user",
                Email = "forced@example.test",
                IsActive = true,
                RequiresPasswordChange = true,
                LastPasswordChangeDate = DateTime.UtcNow
            };
            Assert.True((await fixture.UserManager.CreateAsync(fixture.User, OldPassword)).Succeeded);
            fixture._migration.Setup(store => store.FindAsync(fixture.User.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((CredentialMigrationRecord?)null);
            fixture._localizer.Setup(localizer => localizer[It.IsAny<string>()])
                .Returns((string name) => new LocalizedString(name, name));
            if (auditFailure)
            {
                fixture._audit.Setup(audit => audit.LogEventAsync(
                        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                        It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("audit unavailable"));
            }
            else
            {
                fixture._audit.Setup(audit => audit.LogEventAsync(
                        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                        It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
            }

            return fixture;
        }

        public RequiredPasswordChangeModel CreateModel()
        {
            var model = new RequiredPasswordChangeModel(
                UserManager,
                _migration.Object,
                _audit.Object,
                _policy,
                Db,
                _localizer.Object);
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<ISessionFeature>(new SessionFeature { Session = new MemorySession() });
            model.PageContext = new PageContext(new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor(),
                new ModelStateDictionary()));
            RequiredPasswordChangeSession.Begin(
                httpContext.Session,
                User,
                "/connect/authorize",
                DateTimeOffset.UtcNow);
            return model;
        }

        public RequiredPasswordChangeModel CreateDirectoryModel(Guid directoryObjectId)
        {
            _migration.Setup(store => store.FindAsync(User.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CredentialMigrationRecord(
                    User.Id,
                    new DirectoryObjectBinding("provider", "subject", directoryObjectId),
                    CredentialMigrationState.LocalFinalized));
            _directoryChange.Setup(service => service.ChangeAsync(
                    It.IsAny<DirectoryRequiredCredentialChangeRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(RecoveryProofOutcome.Success);
            var model = new RequiredPasswordChangeModel(
                UserManager, _migration.Object, _audit.Object, _policy, Db, _localizer.Object,
                directoryChangeService: _directoryChange.Object);
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<ISessionFeature>(new SessionFeature { Session = new MemorySession() });
            model.PageContext = new PageContext(new ActionContext(
                httpContext, new RouteData(), new ActionDescriptor(), new ModelStateDictionary()));
            RequiredPasswordChangeSession.BeginDirectory(
                httpContext.Session, User, directoryObjectId, "/connect/authorize", DateTimeOffset.UtcNow);
            return model;
        }

        public async ValueTask DisposeAsync()
        {
            _scope.Dispose();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private sealed class SessionFeature : ISessionFeature
        {
            public ISession Session { get; set; } = default!;
        }
    }
}
