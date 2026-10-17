using Xunit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Core.Application;
using Core.Application.DTOs;
using OpenIddict.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Constants;
using Infrastructure;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Web.IdP.Services;
using System.Threading.Tasks;
using Core.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System;
using System.Net;
using System.Text.Json;
using OpenIddict.Server.AspNetCore;
using System.Threading;
using System.Collections.Immutable;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Application.UnitTests
{
    public class AuthorizationServiceTests
    {
        [Fact]
        public async Task RequiredMfa_ShouldChallengeRemainingAccountCodeInsteadOfInitialEnrollment()
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true };
            SetupMockUsers(user);
            var application = new object();
            _mockApplicationManager.Setup(value => value.FindByClientIdAsync("client", It.IsAny<CancellationToken>())).ReturnsAsync(application);
            _mockApplicationManager.Setup(value => value.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableDictionary<string, JsonElement>.Empty);
            var codes = Mock.Of<IAccountRecoveryCodeService>(value => value.GetStatusAsync(user.Id, It.IsAny<CancellationToken>()) ==
                Task.FromResult(new AccountRecoveryCodeStatusDto(true, 1)));
            using var services = new ServiceCollection().AddSingleton(codes).BuildServiceProvider();
            _mockHttpContextAccessor.Setup(value => value.HttpContext).Returns(new DefaultHttpContext { RequestServices = services });
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.Id.ToString()), new Claim("amr", "pwd")], "Test"));
            var result = Assert.IsType<RedirectResult>(await _authorizationService.HandleAuthorizeRequestAsync(principal,
                new OpenIddictRequest { ClientId = "client", AcrValues = "mfa" }, null));
            Assert.StartsWith("/Account/LoginMfa?", result.Url);
        }

        [Theory]
        [InlineData("fresh", true)]
        [InlineData("cookie-only", false)]
        [InlineData("expired", false)]
        [InlineData("other-subject", false)]
        [InlineData("retired-stamp", false)]
        [InlineData("history-only", false)]
        public async Task RequiredMfa_ShouldContinueOnlyFreshFirstPasswordEnrollment(string condition, bool directSetup)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true, SecurityStamp = "current" };
            SetupMockUsers(user);
            SetupAuthorizationClient(new object(), requireMfa: true);
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var values = new Dictionary<string, byte[]>();
            var session = new Mock<ISession>();
            session.Setup(value => value.Set(It.IsAny<string>(), It.IsAny<byte[]>()))
                .Callback<string, byte[]>((key, bytes) => values[key] = bytes);
            session.Setup(value => value.Remove(It.IsAny<string>())).Callback<string>(key => values.Remove(key));
            session.Setup(value => value.TryGetValue(It.IsAny<string>(), out It.Ref<byte[]?>.IsAny))
                .Returns((string key, out byte[]? bytes) => values.TryGetValue(key, out bytes));
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("amr", "pwd"),
                new Claim(_mockUserManager.Object.Options.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp)], IdentityConstants.ApplicationScheme));
            var auth = new Mock<IAuthenticationService>();
            auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
            auth.Setup(value => value.AuthenticateAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme))
                .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme)));
            var codes = new Mock<IAccountRecoveryCodeService>();
            codes.Setup(value => value.GetStatusAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new AccountRecoveryCodeStatusDto(false, 0));
            using var services = new ServiceCollection().AddSingleton<IApplicationDbContext>(db).AddSingleton(auth.Object)
                .AddSingleton(_mockUserManager.Object).AddSingleton(codes.Object).AddSingleton(_mockPasskeyService.Object).BuildServiceProvider();
            var http = new DefaultHttpContext { Session = session.Object, RequestServices = services, User = principal };
            http.Request.Path = "/connect/authorize";
            http.Request.QueryString = new QueryString("?client_id=client&state=preserved");
            _mockHttpContextAccessor.Setup(value => value.HttpContext).Returns(http);
            if (condition != "cookie-only")
            {
                Web.IdP.Helpers.AuthenticationMethodSession.Replace(session.Object, user, "pwd");
                Assert.True(await Web.IdP.Helpers.MfaEnrollmentSession.TryBeginInitialAfterPasswordAsync(http, user));
                Web.IdP.Helpers.AuthenticationMethodSession.Consume(session.Object);
                var initial = System.Text.Json.Nodes.JsonNode.Parse(session.Object.GetString("MfaEnrollment:Initial")!)!;
                if (condition == "expired") initial["ExpiresUtc"] = DateTimeOffset.UtcNow.AddSeconds(-1);
                if (condition == "other-subject") initial["UserId"] = Guid.NewGuid();
                if (condition == "retired-stamp") initial["SecurityStamp"] = "retired";
                session.Object.SetString("MfaEnrollment:Initial", initial.ToJsonString());
            }
            if (condition == "history-only") codes.Setup(value => value.GetStatusAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new AccountRecoveryCodeStatusDto(true, 0));
            var result = Assert.IsType<RedirectResult>(await _authorizationService.HandleAuthorizeRequestAsync(principal,
                new OpenIddictRequest { ClientId = "client", ResponseType = "code" }, null));
            if (directSetup)
            {
                Assert.StartsWith("/Account/MfaSetup?", result.Url);
                Assert.Contains("state%3Dpreserved", result.Url);
                Assert.True(Web.IdP.Helpers.MfaEnrollmentSession.HasFreshProof(session.Object, user.Id));
                auth.Verify(value => value.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()), Times.Never);
            }
            else Assert.StartsWith("/Account/Login?", result.Url);
            Assert.False(await Web.IdP.Helpers.MfaEnrollmentSession.TryContinueInitialAfterPasswordAsync(http, user));
        }

        [Theory]
        [InlineData("login", null)]
        [InlineData(null, 0L)]
        public async Task AnonymousAuthorize_ShouldBeginCeremonyBeforeInitialForcedLogin(string? prompt, long? maxAge)
        {
            var session = new Mock<ISession>();
            var context = new DefaultHttpContext { Session = session.Object };
            _mockHttpContextAccessor.Setup(accessor => accessor.HttpContext).Returns(context);
            Assert.IsType<ChallengeResult>(await _authorizationService.HandleAuthorizeRequestAsync(null,
                new OpenIddictRequest { ClientId = "client", Prompt = prompt, MaxAge = maxAge }, prompt));
            session.Verify(value => value.Set("Authorization:FreshAuthentication", It.IsAny<byte[]>()), Times.Once());
        }

        [Theory]
        [InlineData("login", null)]
        [InlineData("login consent", null)]
        [InlineData(null, 0L)]
        [InlineData(null, 60L)]
        [InlineData("none", 60L)]
        public async Task HandleAuthorizeRequestAsync_ShouldRequireActualFreshAuthentication(string? prompt, long? maxAge)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("auth_time", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds().ToString())
            ], IdentityConstants.ApplicationScheme));
            var authentication = new Mock<IAuthenticationService>();
            authentication.Setup(a => a.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask);
            var context = new DefaultHttpContext { Session = new Mock<ISession>().Object,
                RequestServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
                    .AddSingleton(authentication.Object).AddSingleton<Core.Application.IAccountRecoveryCodeService>(Moq.Mock.Of<Core.Application.IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<CancellationToken>()) == Task.FromResult(new Core.Application.DTOs.AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider() };
            _mockHttpContextAccessor.Setup(a => a.HttpContext).Returns(context);

            var result = await _authorizationService.HandleAuthorizeRequestAsync(principal,
                new OpenIddictRequest { ClientId = "client", Prompt = prompt, MaxAge = maxAge }, prompt);

            if (prompt == "none")
            {
                Assert.Equal(OpenIddictConstants.Errors.LoginRequired, Assert.IsType<ForbidResult>(result)
                    .Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
                authentication.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()), Times.Never());
            }
            else
            {
                Assert.IsType<ChallengeResult>(result);
                authentication.Verify(a => a.SignOutAsync(context, IdentityConstants.ApplicationScheme, It.IsAny<AuthenticationProperties>()), Times.Once());
            }
        }

        [Theory]
        [InlineData(true, null, "pwd")]
        [InlineData(true, null, "hwk")]
        [InlineData(false, "mfa", "pwd")]
        [InlineData(true, null, "mfa")]
        public async Task HandleAuthorizeSubmitAsync_ShouldRejectDirectConsentWithoutRequiredMfa(bool clientRequiresMfa, string? acr, string amr)
        {
            var client = new object();
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>())).ReturnsAsync(client);
            _mockApplicationManager.Setup(m => m.GetIdAsync(client, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid().ToString());
            _mockApplicationManager.Setup(m => m.GetPropertiesAsync(client, It.IsAny<CancellationToken>())).ReturnsAsync(
                ImmutableDictionary<string, JsonElement>.Empty.Add(AuthConstants.Properties.RequireMfa, JsonSerializer.SerializeToElement(clientRequiresMfa)));
            var user = new ApplicationUser { Id = Guid.NewGuid(), SecurityStamp = "current" };
            SetupMockUsers(user);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.Id.ToString()),
                new Claim("amr", amr), new Claim(Web.IdP.Helpers.AuthorizationAuthenticationSession.MfaSecurityStampClaim, "retired")], IdentityConstants.ApplicationScheme));
            var result = await _authorizationService.HandleAuthorizeSubmitAsync(principal,
                new OpenIddictRequest { ClientId = "test-client", AcrValues = acr }, "accept", []);
            var denied = Assert.IsType<ForbidResult>(result);
            Assert.Equal("unmet_authentication_requirements", denied.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        }

    private readonly Mock<global::Web.IdP.Services.ICurrentUserLifecycleEligibility> _lifecycle = new();

        private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager;
        private readonly Mock<IOpenIddictAuthorizationManager> _mockAuthorizationManager;
        private readonly Mock<IOpenIddictScopeManager> _mockScopeManager;
        private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
        private readonly Mock<RoleManager<ApplicationRole>> _mockRoleManager;
        private readonly Mock<IApplicationDbContext> _mockDb;
        private readonly Mock<IApiResourceService> _mockApiResourceService;
        private readonly Mock<ILocalizationService> _mockLocalizationService;
        private readonly Mock<IScopeService> _mockScopeService;
        private readonly Mock<IAuditService> _mockAuditService;
        private readonly Mock<IClientAllowedScopesService> _mockClientAllowedScopesService;
        private readonly Mock<IClientScopeRequestProcessor> _mockClientScopeProcessor;
        private readonly Mock<ILogger<AuthorizationService>> _mockLogger;
        private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
        private readonly Mock<IClaimsEnrichmentService> _mockClaimsEnricher;
        private readonly Mock<ISecurityPolicyService> _mockSecurityPolicyService;
        private readonly Mock<IPasskeyService> _mockPasskeyService;
        private readonly Mock<ISessionService> _mockSessionService;
        private readonly AuthorizationService _authorizationService;

        public AuthorizationServiceTests()
        {
        _lifecycle.Setup(policy => policy.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

            _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
            _mockAuthorizationManager = new Mock<IOpenIddictAuthorizationManager>();
            _mockScopeManager = new Mock<IOpenIddictScopeManager>();
            _mockUserManager = MockUserManager();
            _mockRoleManager = MockRoleManager();
            _mockDb = new Mock<IApplicationDbContext>();
            _mockApiResourceService = new Mock<IApiResourceService>();
            _mockLocalizationService = new Mock<ILocalizationService>();
            _mockScopeService = new Mock<IScopeService>();
            _mockAuditService = new Mock<IAuditService>();
            _mockClientAllowedScopesService = new Mock<IClientAllowedScopesService>();
            _mockClientScopeProcessor = new Mock<IClientScopeRequestProcessor>();
            _mockLogger = new Mock<ILogger<AuthorizationService>>();
            _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
            _mockClaimsEnricher = new Mock<IClaimsEnrichmentService>();
            _mockSecurityPolicyService = new Mock<ISecurityPolicyService>();
            _mockSecurityPolicyService.Setup(s => s.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());
            _mockPasskeyService = new Mock<IPasskeyService>();
            _mockSessionService = new Mock<ISessionService>();
            _mockPasskeyService
                .Setup(service => service.GetUserPasskeysAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            // Default setup
            _mockClaimsEnricher.Setup(x => x.AddScopeMappedClaimsAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
                .Returns(Task.CompletedTask);
            _mockClaimsEnricher.Setup(x => x.AddPermissionClaimsAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<ApplicationUser>()))
                .Returns(Task.CompletedTask);

            _authorizationService = new AuthorizationService(
                _lifecycle.Object,
                _mockApplicationManager.Object,
                _mockAuthorizationManager.Object,
                _mockScopeManager.Object,
                _mockUserManager.Object,
                _mockRoleManager.Object,
                _mockDb.Object,
                _mockApiResourceService.Object,
                _mockLocalizationService.Object,
                _mockScopeService.Object,
                _mockAuditService.Object,
                _mockClientAllowedScopesService.Object,
                _mockClientScopeProcessor.Object,
                _mockLogger.Object,
                _mockHttpContextAccessor.Object,
                _mockClaimsEnricher.Object,
                _mockSecurityPolicyService.Object,
                _mockPasskeyService.Object,
                _mockSessionService.Object
            );
        }

        private static Mock<UserManager<ApplicationUser>> MockUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(store.Object, null, null, null, null, null, null, null, null);
        }

        [Theory]
        [InlineData(false, "missing")]
        [InlineData(false, "insufficient")]
        [InlineData(false, "covering")]
        [InlineData(true, "missing")]
        [InlineData(true, "insufficient")]
        [InlineData(true, "covering")]
        public async Task ExternalConsent_ShouldRequireCoveringApprovalAndNeverCreateGrant(bool submit, string approval)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true };
            SetupMockUsers(user);
            SetupMockScopeExtensions();
            using var sessionDb = SetupSessionPersistence();
            var application = new object();
            var grant = new object();
            var applicationId = Guid.NewGuid().ToString();
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
                new Claim(OpenIddictConstants.Claims.AuthenticationTime, "1700000000")], "Test"));
            _mockHttpContextAccessor.Setup(value => value.HttpContext).Returns(new DefaultHttpContext());
            _mockApplicationManager.Setup(value => value.FindByClientIdAsync("client", It.IsAny<CancellationToken>())).ReturnsAsync(application);
            _mockApplicationManager.Setup(value => value.GetIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(applicationId);
            _mockApplicationManager.Setup(value => value.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync([OpenIddictConstants.Permissions.ResponseTypes.Code]);
            _mockApplicationManager.Setup(value => value.GetConsentTypeAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OpenIddictConstants.ConsentTypes.External);
            _mockUserManager.Setup(value => value.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
            _mockUserManager.Setup(value => value.GetRolesAsync(user)).ReturnsAsync([]);
            _mockClientScopeProcessor.Setup(value => value.EnforceAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                .ReturnsAsync(new ClientScopeEvaluationResult { AllowedScopes = ["openid", "profile"] });
            _mockClientAllowedScopesService.Setup(value => value.GetRequiredScopesAsync(It.IsAny<Guid>())).ReturnsAsync([]);
            _mockScopeService.Setup(value => value.ClassifyScopes(It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<ScopeSummary>>(), It.IsAny<IEnumerable<string>>()))
                .Returns(new ScopeClassificationResult { Allowed = ["openid"] });
            _mockApiResourceService.Setup(value => value.GetAudiencesByScopesAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync([]);
            _mockAuthorizationManager.Setup(value => value.FindAsync(user.Id.ToString(), applicationId,
                OpenIddictConstants.Statuses.Valid, OpenIddictConstants.AuthorizationTypes.Permanent,
                It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
                .Returns(approval == "missing" ? ToAsyncEnumerable() : ToAsyncEnumerable(grant));
            _mockAuthorizationManager.Setup(value => value.GetScopesAsync(grant, It.IsAny<CancellationToken>()))
                .ReturnsAsync(approval == "covering" ? ["openid", "profile"] : ["openid"]);
            _mockAuthorizationManager.Setup(value => value.GetIdAsync(grant, It.IsAny<CancellationToken>())).ReturnsAsync("approved-grant");
            var request = new OpenIddictRequest { ClientId = "client", Scope = "openid profile", ResponseType = "code", Prompt = "consent" };
            var result = submit
                ? await _authorizationService.HandleAuthorizeSubmitAsync(principal, request, "accept", ["openid"])
                : await _authorizationService.HandleAuthorizeRequestAsync(principal, request, "consent");
            if (approval == "covering")
                Assert.Equal("approved-grant", Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal.GetAuthorizationId());
            else
                Assert.Equal(OpenIddictConstants.Errors.ConsentRequired, Assert.IsType<ForbidResult>(result)
                    .Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
            _mockAuthorizationManager.Verify(value => value.CreateAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(OpenIddictConstants.ConsentTypes.Explicit, null, false)]
        [InlineData(OpenIddictConstants.ConsentTypes.Explicit, "none", false)]
        [InlineData(OpenIddictConstants.ConsentTypes.Systematic, null, false)]
        [InlineData(OpenIddictConstants.ConsentTypes.Systematic, "none", false)]
        [InlineData(OpenIddictConstants.ConsentTypes.Implicit, null, true)]
        [InlineData(OpenIddictConstants.ConsentTypes.Implicit, "none", true)]
        [InlineData(OpenIddictConstants.ConsentTypes.Implicit, "consent", false)]
        [InlineData(OpenIddictConstants.ConsentTypes.External, "none", true)]
        public async Task ExistingPermanentGrant_ShouldHonorCurrentConsentPolicy(string consentType, string? prompt, bool reuseGrant)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true };
            SetupMockUsers(user);
            SetupMockScopeExtensions();
            using var sessionDb = SetupSessionPersistence();
            var application = new object();
            var grant = new object();
            var applicationId = Guid.NewGuid().ToString();
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
                new Claim(OpenIddictConstants.Claims.AuthenticationTime, "1700000000")], "Test"));
            _mockHttpContextAccessor.Setup(value => value.HttpContext).Returns(new DefaultHttpContext());
            _mockApplicationManager.Setup(value => value.FindByClientIdAsync("client", It.IsAny<CancellationToken>())).ReturnsAsync(application);
            _mockApplicationManager.Setup(value => value.GetIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(applicationId);
            _mockApplicationManager.Setup(value => value.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync([OpenIddictConstants.Permissions.ResponseTypes.Code]);
            _mockApplicationManager.Setup(value => value.GetConsentTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(consentType);
            _mockUserManager.Setup(value => value.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
            _mockUserManager.Setup(value => value.GetRolesAsync(user)).ReturnsAsync([]);
            _mockClientAllowedScopesService.Setup(value => value.GetRequiredScopesAsync(It.IsAny<Guid>())).ReturnsAsync([]);
            _mockClientScopeProcessor.Setup(value => value.EnforceAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                .ReturnsAsync(new ClientScopeEvaluationResult { AllowedScopes = ["openid"] });
            _mockAuthorizationManager.Setup(value => value.FindAsync(user.Id.ToString(), applicationId,
                OpenIddictConstants.Statuses.Valid, OpenIddictConstants.AuthorizationTypes.Permanent,
                It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>())).Returns(ToAsyncEnumerable(grant));
            _mockAuthorizationManager.Setup(value => value.GetScopesAsync(grant, It.IsAny<CancellationToken>())).ReturnsAsync(["openid"]);
            _mockAuthorizationManager.Setup(value => value.GetIdAsync(grant, It.IsAny<CancellationToken>())).ReturnsAsync("historical-grant");
            _mockApiResourceService.Setup(value => value.GetAudiencesByScopesAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync([]);

            var request = new OpenIddictRequest { ClientId = "client", Scope = "openid", ResponseType = "code", Prompt = prompt };
            var result = await _authorizationService.HandleAuthorizeRequestAsync(principal, request, prompt);

            if (reuseGrant)
                Assert.Equal("historical-grant", Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal.GetAuthorizationId());
            else if (prompt == "none")
                Assert.Equal(OpenIddictConstants.Errors.ConsentRequired, Assert.IsType<ForbidResult>(result)
                    .Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
            else
                Assert.IsType<OkResult>(result);
            _mockAuthorizationManager.Verify(value => value.CreateAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()), Times.Never);
            if (!reuseGrant)
                _mockAuthorizationManager.Verify(value => value.GetIdAsync(grant, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(true, false, false, false)]
        [InlineData(false, true, false, false)]
        [InlineData(false, false, true, false)]
        [InlineData(false, false, true, true)]
        [InlineData(true, false, true, false)]
        [InlineData(false, true, true, false)]
        [InlineData(true, false, true, true)]
        [InlineData(false, true, true, true)]
        public async Task Consent_ShouldUseOnlyRequestedRequiredScopesBeforeClassification(
            bool clientRequired, bool globalRequired, bool requestProfile, bool grantProfile)
        {
            var (principal, _) = SetupConsentScopes(clientRequired, globalRequired);
            var request = new OpenIddictRequest
            {
                ClientId = "client", ResponseType = "code",
                Scope = requestProfile ? "openid profile" : "openid"
            };

            Assert.IsType<OkResult>(await _authorizationService.HandleAuthorizeRequestAsync(principal, request, null));
            var profile = _authorizationService.ScopeInfos.SingleOrDefault(scope => scope.Name == "profile");
            Assert.Equal(requestProfile, profile != null);
            if (profile != null) Assert.Equal(clientRequired || globalRequired, profile.IsRequired);

            // POST reloads metadata independently, as it does in a new HTTP request.
            var result = await _authorizationService.HandleAuthorizeSubmitAsync(principal, request, "allow",
                grantProfile ? ["openid", "profile"] : ["openid"]);
            if (requestProfile && (clientRequired || globalRequired) && !grantProfile)
            {
                Assert.IsType<BadRequestObjectResult>(result);
                _mockScopeService.Verify(service => service.ClassifyScopes(It.IsAny<IEnumerable<string>>(),
                    It.IsAny<IEnumerable<ScopeSummary>>(), It.IsAny<IEnumerable<string>>()), Times.Never);
                _mockAuthorizationManager.Verify(manager => manager.CreateAsync(It.IsAny<ClaimsIdentity>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ImmutableArray<string>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
                return;
            }

            var issued = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal;
            var expected = requestProfile && grantProfile ? new[] { "openid", "profile" } : ["openid"];
            Assert.Equal(expected, issued.GetScopes());
            Assert.Equal(requestProfile && grantProfile, issued.HasClaim(claim => claim.Type == "name"));
            Assert.Equal(principal.GetClaim("sub"), issued.GetClaim("sub"));
            _mockAuthorizationManager.Verify(manager => manager.CreateAsync(It.IsAny<ClaimsIdentity>(),
                principal.GetClaim("sub")!, It.IsAny<string>(), OpenIddictConstants.AuthorizationTypes.Permanent,
                It.Is<ImmutableArray<string>>(scopes => scopes.SequenceEqual(expected)), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Consent_ShouldExcludeInjectedScopesOutsideEffectiveRequest(bool requestDisallowedScope)
        {
            var (principal, _) = SetupConsentScopes(false, false);
            var request = new OpenIddictRequest { ClientId = "client", ResponseType = "code",
                Scope = requestDisallowedScope ? "openid email" : "openid" };

            var result = await _authorizationService.HandleAuthorizeSubmitAsync(principal, request, "allow",
                ["openid", "profile", "email", "unregistered"]);

            Assert.Equal(new[] { "openid" }, Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal.GetScopes());
            _mockAuthorizationManager.Verify(manager => manager.CreateAsync(It.IsAny<ClaimsIdentity>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.Is<ImmutableArray<string>>(scopes => scopes.SequenceEqual(new[] { "openid" })),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData("openid", "openid", true, null, "implicit", true)]
        [InlineData("openid profile", "openid", true, null, "implicit", false)]
        [InlineData("openid profile", "openid", true, "none", "implicit", false)]
        [InlineData("openid", "openid profile", true, null, "implicit", true)]
        [InlineData("openid", "openid profile", false, null, "implicit", false)]
        [InlineData("openid profile", "openid", true, null, "external", false)]
        public async Task Consent_ShouldRespectCurrentGrantCoverageAndValidity(string requested, string priorScopes,
            bool valid, string? prompt, string policy, bool reuse)
        {
            var (principal, applicationId) = SetupConsentScopes(true, false);
            var grant = new object();
            var prior = priorScopes.Split(' ').ToImmutableArray();
            _mockApplicationManager.Setup(manager => manager.GetConsentTypeAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(policy);
            _mockAuthorizationManager.Setup(manager => manager.FindAsync(principal.GetClaim("sub"), applicationId,
                    OpenIddictConstants.Statuses.Valid, OpenIddictConstants.AuthorizationTypes.Permanent,
                    It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
                .Returns((string subject, string client, string status, string type, ImmutableArray<string> scopes, CancellationToken ct) =>
                    ToAsyncEnumerable(valid && scopes.All(scope => prior.Contains(scope)) ? [grant] : []));
            _mockAuthorizationManager.Setup(manager => manager.GetScopesAsync(grant, It.IsAny<CancellationToken>())).ReturnsAsync(prior);
            _mockAuthorizationManager.Setup(manager => manager.GetIdAsync(grant, It.IsAny<CancellationToken>())).ReturnsAsync("existing");

            var result = await _authorizationService.HandleAuthorizeRequestAsync(principal,
                new OpenIddictRequest { ClientId = "client", ResponseType = "code", Scope = requested, Prompt = prompt }, prompt);

            if (reuse)
            {
                var issued = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal;
                Assert.Equal(requested.Split(' '), issued.GetScopes());
                Assert.False(issued.HasClaim(claim => claim.Type == "name"));
            }
            else if (prompt == "none" || policy == "external")
                Assert.Equal(OpenIddictConstants.Errors.ConsentRequired, Assert.IsType<ForbidResult>(result)
                    .Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
            else Assert.IsType<OkResult>(result);
            _mockAuthorizationManager.Verify(manager => manager.FindAsync(principal.GetClaim("sub"), applicationId,
                OpenIddictConstants.Statuses.Valid, OpenIddictConstants.AuthorizationTypes.Permanent,
                It.Is<ImmutableArray<string>>(scopes => scopes.SequenceEqual(requested.Split(' ', StringSplitOptions.None))),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(OpenIddictConstants.ClientTypes.Public, true)]
        [InlineData(OpenIddictConstants.ClientTypes.Confidential, true)]
        [InlineData(OpenIddictConstants.ClientTypes.Confidential, false)]
        public async Task V143Descriptor_ShouldKeepConsentAndScopesWithoutNewClientSettings(string clientType, bool requirePkce)
        {
            var (principal, applicationId) = SetupConsentScopes(false, false);
            var application = (await _mockApplicationManager.Object.FindByClientIdAsync("client"))!;
            // v1.4.3 (0062e557) ClientService.CreateClientAsync defaults: explicit/web,
            // these seven permissions, optional Properties absent, and PKCE in Requirements.
            // Required was already a separate ClientRequiredScopes table, not a descriptor property.
            var legacy = new OpenIddictApplicationDescriptor
            {
                ClientId = "client", ClientType = clientType,
                ApplicationType = OpenIddictConstants.ApplicationTypes.Web,
                ConsentType = OpenIddictConstants.ConsentTypes.Explicit,
                RedirectUris = { new Uri("https://legacy.example/callback") },
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    OpenIddictConstants.Permissions.Scopes.Email,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "openid"
                }
            };
            if (requirePkce) legacy.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
            _mockApplicationManager.Setup(manager => manager.FindByIdAsync(applicationId, It.IsAny<CancellationToken>())).ReturnsAsync(application);
            _mockApplicationManager.Setup(manager => manager.GetClientTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(legacy.ClientType);
            _mockApplicationManager.Setup(manager => manager.GetApplicationTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(legacy.ApplicationType);
            _mockApplicationManager.Setup(manager => manager.GetConsentTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(legacy.ConsentType);
            _mockApplicationManager.Setup(manager => manager.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableDictionary<string, JsonElement>.Empty);
            _mockApplicationManager.Setup(manager => manager.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => legacy.Permissions.ToImmutableArray());
            _mockApplicationManager.Setup(manager => manager.GetRedirectUrisAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(["https://legacy.example/callback"]);
            _mockApplicationManager.Setup(manager => manager.GetPostLogoutRedirectUrisAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            _mockApplicationManager.Setup(manager => manager.PopulateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), application, It.IsAny<CancellationToken>()))
                .Callback((OpenIddictApplicationDescriptor target, object _, CancellationToken _) => target.Requirements.UnionWith(legacy.Requirements))
                .Returns(default(ValueTask));

            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            _mockScopeManager.Setup(manager => manager.GetResourcesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            var usage = new global::Infrastructure.Authorization.ApiScopeUsagePolicy(db, _mockScopeManager.Object,
                _mockApplicationManager.Object, Mock.Of<global::Infrastructure.Authorization.IAdministrativeAuthorizationBoundary>(),
                Mock.Of<Microsoft.AspNetCore.Authorization.IAuthorizationService>());
            var allowed = new global::Infrastructure.Services.ClientAllowedScopesService(
                _mockApplicationManager.Object, _mockScopeManager.Object, db, usage);
            var processor = new global::Infrastructure.Services.ClientScopeRequestProcessor(allowed, _mockAuditService.Object);
            _mockClientAllowedScopesService.Setup(service => service.GetRequiredScopesAsync(It.IsAny<Guid>()))
                .Returns((Guid id) => allowed.GetRequiredScopesAsync(id));
            _mockClientScopeProcessor.Setup(service => service.EnforceAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                .Returns((Guid id, IEnumerable<string> scopes, bool audit) => processor.EnforceAsync(id, scopes, audit));

            var clients = new global::Infrastructure.Services.ClientService(_mockApplicationManager.Object,
                Mock.Of<Core.Domain.Events.IDomainEventPublisher>(), db, _mockScopeManager.Object,
                Microsoft.Extensions.Options.Options.Create(new global::Infrastructure.Options.RedirectUriSecurityPolicyOptions()), usage);
            var detail = (await clients.GetClientByIdAsync(Guid.Parse(applicationId)))!;
            Assert.Equal(clientType, detail.Type);
            Assert.Equal(legacy.ConsentType, detail.ConsentType);
            Assert.Equal(requirePkce, detail.RequirePkce);
            Assert.False(detail.RequireMfa);
            Assert.False(detail.DisableExternalProviders);
            Assert.False(detail.EnableTurnstile);
            Assert.Empty(await allowed.GetRequiredScopesAsync(Guid.Parse(applicationId)));

            var request = new OpenIddictRequest { ClientId = "client", ResponseType = "code", Scope = "openid profile" };
            Assert.IsType<OkResult>(await _authorizationService.HandleAuthorizeRequestAsync(principal, request, null));
            var initial = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(await _authorizationService.HandleAuthorizeSubmitAsync(
                principal, request, "allow", ["openid", "profile"])).Principal;
            Assert.Equal(new[] { "openid", "profile" }, initial.GetScopes());
            Assert.NotNull(initial.GetClaim("name"));

            // Adding admin metadata does not append either newly allowed phone or unrequested Required profile.
            legacy.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Phone);
            db.ClientRequiredScopes.Add(new ClientRequiredScope { ClientId = applicationId, ScopeId = "profile" });
            await db.SaveChangesAsync();
            var profileScope = (await _mockScopeManager.Object.FindByNameAsync("profile"))!;
            _mockScopeManager.Setup(manager => manager.ListAsync(null, null, It.IsAny<CancellationToken>())).Returns(() => ToAsyncEnumerable(profileScope));
            _mockScopeManager.Setup(manager => manager.GetNameAsync(profileScope, It.IsAny<CancellationToken>())).ReturnsAsync("profile");
            request.Scope = "openid";
            Assert.IsType<OkResult>(await _authorizationService.HandleAuthorizeRequestAsync(principal, request, null));
            Assert.Equal("openid", Assert.Single(_authorizationService.ScopeInfos).Name);
            var smaller = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(await _authorizationService.HandleAuthorizeSubmitAsync(
                principal, request, "allow", ["openid"])).Principal;
            Assert.Equal(new[] { "openid" }, smaller.GetScopes());
            Assert.Null(smaller.GetClaim("name"));
            Assert.Empty(legacy.Properties);
            Assert.Equal(requirePkce, legacy.Requirements.Contains(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange));
            _mockApplicationManager.Verify(manager => manager.UpdateAsync(application, It.IsAny<OpenIddictApplicationDescriptor>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private (ClaimsPrincipal principal, string applicationId) SetupConsentScopes(bool clientRequired, bool globalRequired)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true, FirstName = "Test" };
            SetupMockUsers(user);
            SetupMockScopeExtensions(new ScopeExtension { ScopeId = "profile", IsRequired = globalRequired });
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.Id.ToString())], "test"));
            var application = new object();
            var grant = new object();
            var applicationId = Guid.NewGuid().ToString();
            _mockHttpContextAccessor.Setup(accessor => accessor.HttpContext).Returns(new DefaultHttpContext());
            _mockApplicationManager.Setup(manager => manager.FindByClientIdAsync("client", It.IsAny<CancellationToken>())).ReturnsAsync(application);
            _mockApplicationManager.Setup(manager => manager.GetIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(applicationId);
            _mockApplicationManager.Setup(manager => manager.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync([OpenIddictConstants.Permissions.ResponseTypes.Code]);
            _mockApplicationManager.Setup(manager => manager.GetConsentTypeAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OpenIddictConstants.ConsentTypes.Implicit);
            _mockUserManager.Setup(manager => manager.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
            _mockUserManager.Setup(manager => manager.GetRolesAsync(user)).ReturnsAsync([]);
            _mockClientAllowedScopesService.Setup(service => service.GetRequiredScopesAsync(It.IsAny<Guid>()))
                .ReturnsAsync(clientRequired ? ["profile"] : []);
            _mockClientScopeProcessor.Setup(service => service.EnforceAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                .ReturnsAsync((Guid id, IEnumerable<string> scopes, bool audit) => new ClientScopeEvaluationResult
                    { AllowedScopes = scopes.Where(scope => scope is "openid" or "profile").ToList() });
            foreach (var name in new[] { "openid", "profile" })
            {
                var scope = new object();
                _mockScopeManager.Setup(manager => manager.FindByNameAsync(name, It.IsAny<CancellationToken>())).ReturnsAsync(scope);
                _mockScopeManager.Setup(manager => manager.GetIdAsync(scope, It.IsAny<CancellationToken>())).ReturnsAsync(name);
            }
            // Use the production classifier so its Required autofill cannot conceal a POST validation failure.
            var classifier = new global::Infrastructure.Services.ScopeService(_mockScopeManager.Object,
                _mockApplicationManager.Object, _mockDb.Object, Mock.Of<Core.Domain.Events.IDomainEventPublisher>(),
                Mock.Of<global::Infrastructure.Authorization.IApiScopeUsagePolicy>());
            _mockScopeService.Setup(service => service.ClassifyScopes(It.IsAny<IEnumerable<string>>(),
                    It.IsAny<IEnumerable<ScopeSummary>>(), It.IsAny<IEnumerable<string>>()))
                .Returns((IEnumerable<string> requested, IEnumerable<ScopeSummary> available, IEnumerable<string>? granted) =>
                    classifier.ClassifyScopes(requested, available, granted));
            _mockApiResourceService.Setup(service => service.GetAudiencesByScopesAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync([]);
            _mockAuthorizationManager.Setup(manager => manager.FindAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
                .Returns(ToAsyncEnumerable());
            _mockAuthorizationManager.Setup(manager => manager.CreateAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(grant);
            _mockAuthorizationManager.Setup(manager => manager.GetIdAsync(grant, It.IsAny<CancellationToken>())).ReturnsAsync("created");
            return (principal, applicationId);
        }

        private static Mock<RoleManager<ApplicationRole>> MockRoleManager()
        {
            var store = new Mock<IRoleStore<ApplicationRole>>();
            return new Mock<RoleManager<ApplicationRole>>(store.Object, null, null, null, null);
        }


        [Theory]
        [InlineData(0, OpenIddictConstants.ConsentTypes.Explicit)]
        [InlineData(0, OpenIddictConstants.ConsentTypes.Implicit)]
        [InlineData(1, OpenIddictConstants.ConsentTypes.Explicit)]
        [InlineData(3, OpenIddictConstants.ConsentTypes.Explicit)]
        [InlineData(4, OpenIddictConstants.ConsentTypes.Explicit)]
        public async Task Consent_ShouldCreateGrantOnlyWhenLifecycleAllows(int deniedCheck, string consentType)
        {
            var allowed = deniedCheck == 0;
            var lifecycleChecks = 0;
            var checksAtGrantCreation = 0;
            var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true };
            SetupMockUsers(user);
            SetupMockScopeExtensions();
            using var sessionDb = SetupSessionPersistence();
            var retainedSession = new UserSession { UserId = user.Id, AuthorizationId = "retained-authorization", ClientId = "client" };
            sessionDb.UserSessions.Add(retainedSession);
            await sessionDb.SaveChangesAsync();
            var retainedSnapshot = JsonSerializer.Serialize(retainedSession);
            var application = new object();
            var authorization = new object();
            var applicationId = Guid.NewGuid().ToString();
            var authorizationType = consentType == OpenIddictConstants.ConsentTypes.Explicit
                ? OpenIddictConstants.AuthorizationTypes.AdHoc
                : OpenIddictConstants.AuthorizationTypes.Permanent;
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
                new Claim(OpenIddictConstants.Claims.AuthenticationTime, "1700000000")
            ], "test"));
            _mockHttpContextAccessor.Setup(value => value.HttpContext).Returns(new DefaultHttpContext());
            _mockApplicationManager.Setup(value => value.FindByClientIdAsync("client", It.IsAny<CancellationToken>())).ReturnsAsync(application);
            _mockApplicationManager.Setup(value => value.GetIdAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(applicationId);
            _mockApplicationManager.Setup(value => value.GetConsentTypeAsync(application, It.IsAny<CancellationToken>())).ReturnsAsync(consentType);
            _mockUserManager.Setup(value => value.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
            _mockUserManager.Setup(value => value.GetRolesAsync(user)).ReturnsAsync([]);
            _mockClientScopeProcessor.Setup(value => value.EnforceAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>(), false))
                .ReturnsAsync(new ClientScopeEvaluationResult { AllowedScopes = [] });
            _mockClientAllowedScopesService.Setup(value => value.GetRequiredScopesAsync(It.IsAny<Guid>())).ReturnsAsync([]);
            _mockScopeService.Setup(value => value.ClassifyScopes(It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<ScopeSummary>>(), It.IsAny<IEnumerable<string>>()))
                .Returns(new ScopeClassificationResult());
            _mockApiResourceService.Setup(value => value.GetAudiencesByScopesAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync([]);
            _mockAuthorizationManager.Setup(value => value.CreateAsync(It.IsAny<ClaimsIdentity>(), user.Id.ToString(), applicationId,
                authorizationType, It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
                .Callback(() => checksAtGrantCreation = lifecycleChecks)
                .ReturnsAsync(authorization);
            _mockAuthorizationManager.Setup(value => value.GetIdAsync(authorization, It.IsAny<CancellationToken>())).ReturnsAsync("consent-authorization");
            _lifecycle.Setup(value => value.IsEligibleAsync(user.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ++lifecycleChecks != deniedCheck);
            var result = await _authorizationService.HandleAuthorizeSubmitAsync(principal,
                new OpenIddictRequest { ClientId = "client", ResponseType = "code" }, "accept", []);
            if (allowed)
            {
                var signIn = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
                Assert.Equal("consent-authorization", signIn.Principal.GetAuthorizationId());
                var authenticationTime = Assert.Single(signIn.Principal.FindAll(OpenIddictConstants.Claims.AuthenticationTime));
                Assert.Equal("1700000000", authenticationTime.Value);
                Assert.Equal(ClaimValueTypes.Integer64, authenticationTime.ValueType);
                Assert.Equal(4, checksAtGrantCreation);
            }
            else
            {
                var denied = Assert.IsType<ForbidResult>(result);
                Assert.Equal(OpenIddictConstants.Errors.AccessDenied,
                    denied.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
            }
            Assert.Equal(allowed ? 4 : deniedCheck, lifecycleChecks);
            _mockAuthorizationManager.Verify(value => value.CreateAsync(It.IsAny<ClaimsIdentity>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()), allowed ? Times.Once() : Times.Never());
            _mockAuthorizationManager.Verify(value => value.GetIdAsync(authorization, It.IsAny<CancellationToken>()),
                allowed ? Times.Once() : Times.Never());
            _mockAuthorizationManager.VerifyNoOtherCalls();
            _mockSessionService.Verify(value => value.EnsureCreatedAsync(user.Id, "consent-authorization", "client",
                It.IsAny<string?>(), null, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                allowed ? Times.Once() : Times.Never());
            _mockSessionService.VerifyNoOtherCalls();
            _mockAuditService.Verify(value => value.LogEventAsync("AuthorizationGrantedFull", user.Id.ToString(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                allowed ? Times.Once() : Times.Never());
            Assert.Equal(allowed ? 2 : 1, await sessionDb.UserSessions.CountAsync());
            Assert.Equal(retainedSnapshot, JsonSerializer.Serialize(
                await sessionDb.UserSessions.AsNoTracking().SingleAsync(session => session.Id == retainedSession.Id)));
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_ShouldChallenge_WhenUserNotAuthenticated()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity()); // Unauthenticated
#pragma warning disable CA2254 // Template should be a static expression
            _mockLogger.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()));
#pragma warning restore CA2254 // Template should be a static expression

            var request = new OpenIddictRequest();
            var context = new DefaultHttpContext();
            
            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

             var authService = new AuthorizationService(
                _lifecycle.Object,
                _mockApplicationManager.Object,
                _mockAuthorizationManager.Object,
                _mockScopeManager.Object,
                _mockUserManager.Object,
                _mockRoleManager.Object,
                _mockDb.Object,
                _mockApiResourceService.Object,
                _mockLocalizationService.Object,
                _mockScopeService.Object,
                _mockAuditService.Object,
                _mockClientAllowedScopesService.Object,
                _mockClientScopeProcessor.Object,
                _mockLogger.Object,
                httpContextAccessor.Object,
                _mockClaimsEnricher.Object,
                _mockSecurityPolicyService.Object,
                _mockPasskeyService.Object,
                _mockSessionService.Object
            );

            // Act
            var result = await authService.HandleAuthorizeRequestAsync(user, request, null);

            // Assert
            var challengeResult = Assert.IsType<ChallengeResult>(result);
            Assert.Contains(IdentityConstants.ApplicationScheme, challengeResult.AuthenticationSchemes);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WithPromptNoneAndUnauthenticatedUser_ReturnsLoginRequired()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity()); // Unauthenticated
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                Prompt = "none"
            };

            var context = new DefaultHttpContext();

            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var authService = new AuthorizationService(
                _lifecycle.Object,
                _mockApplicationManager.Object,
                _mockAuthorizationManager.Object,
                _mockScopeManager.Object,
                _mockUserManager.Object,
                _mockRoleManager.Object,
                _mockDb.Object,
                _mockApiResourceService.Object,
                _mockLocalizationService.Object,
                _mockScopeService.Object,
                _mockAuditService.Object,
                _mockClientAllowedScopesService.Object,
                _mockClientScopeProcessor.Object,
                _mockLogger.Object,
                httpContextAccessor.Object,
                _mockClaimsEnricher.Object,
                _mockSecurityPolicyService.Object,
                _mockPasskeyService.Object,
                _mockSessionService.Object
            );

            // Act
            var result = await authService.HandleAuthorizeRequestAsync(user, request, request.Prompt);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.NotNull(forbidResult.Properties);
            Assert.Equal(
                OpenIddictConstants.Errors.LoginRequired,
                forbidResult.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WithPromptNoneAndUnauthenticatedUser_LogsSecurityTelemetry()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity()); // Unauthenticated
            var request = new OpenIddictRequest
            {
                ClientId = "probe-client",
                Prompt = "none",
                Scope = "openid profile"
            };

            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
            context.Request.Headers["User-Agent"] = "security-probe-agent";

            _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            // Act
            var result = await _authorizationService.HandleAuthorizeRequestAsync(user, request, request.Prompt);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.Equal(
                OpenIddictConstants.Errors.LoginRequired,
                forbidResult.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);

            _mockAuditService.Verify(a => a.LogEventAsync(
                "AuthorizationPromptNoneProbe",
                null,
                It.Is<string>(details =>
                    details.Contains("\"clientId\":\"probe-client\"") &&
                    details.Contains("\"prompt\":\"none\"") &&
                    details.Contains("\"scope\":\"openid profile\"") &&
                    details.Contains("\"ip\":\"203.0.113.10\"") &&
                    details.Contains("\"userAgent\":\"security-probe-agent\"")),
                "203.0.113.10",
                "security-probe-agent",
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WithInvalidPromptCombination_ReturnsInvalidRequest()
        {
            // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity()); // Unauthenticated
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                Prompt = "none login"
            };

            var context = new DefaultHttpContext();

            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var authService = new AuthorizationService(
                _lifecycle.Object,
                _mockApplicationManager.Object,
                _mockAuthorizationManager.Object,
                _mockScopeManager.Object,
                _mockUserManager.Object,
                _mockRoleManager.Object,
                _mockDb.Object,
                _mockApiResourceService.Object,
                _mockLocalizationService.Object,
                _mockScopeService.Object,
                _mockAuditService.Object,
                _mockClientAllowedScopesService.Object,
                _mockClientScopeProcessor.Object,
                _mockLogger.Object,
                httpContextAccessor.Object,
                _mockClaimsEnricher.Object,
                _mockSecurityPolicyService.Object,
                _mockPasskeyService.Object,
                _mockSessionService.Object
            );

            // Act
            var result = await authService.HandleAuthorizeRequestAsync(user, request, request.Prompt);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
            Assert.NotNull(forbidResult.Properties);
            Assert.Equal(
                OpenIddictConstants.Errors.InvalidRequest,
                forbidResult.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_ShouldThrow_WhenRequestIsNull()
        {
             // Arrange
            var user = new ClaimsPrincipal(new ClaimsIdentity("Test"));
            var context = new DefaultHttpContext();
            // No OpenIddict feature set
            
            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

             var authService = new AuthorizationService(
                _lifecycle.Object,
                _mockApplicationManager.Object,
                _mockAuthorizationManager.Object,
                _mockScopeManager.Object,
                _mockUserManager.Object,
                _mockRoleManager.Object,
                _mockDb.Object,
                _mockApiResourceService.Object,
                _mockLocalizationService.Object,
                _mockScopeService.Object,
                _mockAuditService.Object,
                _mockClientAllowedScopesService.Object,
                _mockClientScopeProcessor.Object,
                _mockLogger.Object,
                httpContextAccessor.Object,
                _mockClaimsEnricher.Object,
                _mockSecurityPolicyService.Object,
                _mockPasskeyService.Object,
                _mockSessionService.Object
            );

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => authService.HandleAuthorizeRequestAsync(user, null!, null));
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WithCodeResponseType_MissingPermission_ReturnsForbid()
        {
            // Arrange
            // Arrange
            var userId = Guid.NewGuid();
            var identity = new ClaimsIdentity("Test");
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, userId.ToString()));
            var user = new ClaimsPrincipal(identity);

            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Code,
                Scope = "openid"
            };

            // Setup Context
            var context = new DefaultHttpContext();
            _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            // Setup Client
            var client = new object();
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("client", default)).ReturnsAsync(client);
            _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(client, default)).ReturnsAsync("TestApp");
            _mockApplicationManager.Setup(m => m.GetIdAsync(client, default)).ReturnsAsync("client-id-guid");

            // Setup Missing Permission
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(client, default))
                .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Permissions.ResponseTypes.Token)); // Has Token but needs Code

            // Setup User
            var appUser = new ApplicationUser { Id = userId };
            _mockUserManager.Setup(m => m.GetUserAsync(user)).ReturnsAsync(appUser);
            SetupMockUsers(appUser);
            _mockSecurityPolicyService.Setup(x => x.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());

            // Act
            var result = await _authorizationService.HandleAuthorizeRequestAsync(user, request, null);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WithTokenResponseType_MissingPermission_ReturnsForbid()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var identity = new ClaimsIdentity("Test");
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, userId.ToString()));
            var user = new ClaimsPrincipal(identity);

            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Token,
                Scope = "openid"
            };

            // Setup Context
            var context = new DefaultHttpContext();
            _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            // Setup Client
            var client = new object();
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("client", default)).ReturnsAsync(client);
            _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(client, default)).ReturnsAsync("TestApp");
            _mockApplicationManager.Setup(m => m.GetIdAsync(client, default)).ReturnsAsync("client-id-guid");

            // Setup Missing Permission
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(client, default))
                .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Permissions.ResponseTypes.Code)); // Has Code but needs Token

            // Setup User
            var appUser = new ApplicationUser { Id = userId };
            _mockUserManager.Setup(m => m.GetUserAsync(user)).ReturnsAsync(appUser);
            SetupMockUsers(appUser);
            _mockSecurityPolicyService.Setup(x => x.GetCurrentPolicyAsync()).ReturnsAsync(new SecurityPolicy());

            // Act
            var result = await _authorizationService.HandleAuthorizeRequestAsync(user, request, null);

            // Assert
            var forbidResult = Assert.IsType<ForbidResult>(result);
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        public async Task HandleAuthorizeRequestAsync_WhenClientGlobalOrAcrRequiresMfa_RequiresFreshLoginForEnrollment(
            bool clientRequiresMfa,
            bool globallyRequiresMfa,
            bool acrRequiresMfa)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid() };
            SetupMockUsers(user);

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString())],
                "Test"));
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Code,
                Scope = "openid",
                AcrValues = acrRequiresMfa ? "mfa" : null
            };
            var application = new object();
            _mockApplicationManager
                .Setup(manager => manager.FindByClientIdAsync("client", It.IsAny<CancellationToken>()))
                .ReturnsAsync(application);
            _mockApplicationManager
                .Setup(manager => manager.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateClientProperties(clientRequiresMfa));
            _mockSecurityPolicyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy
                {
                    EnforceMandatoryMfaEnrollment = globallyRequiresMfa
                });

            var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IAccountRecoveryCodeService>(Mock.Of<IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(new AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider() };
            context.Features.Set<ISessionFeature>(new TestSessionFeature
            {
                Session = new Mock<ISession>().Object
            });
            _mockHttpContextAccessor.Setup(accessor => accessor.HttpContext).Returns(context);

            var result = await _authorizationService.HandleAuthorizeRequestAsync(principal, request, null);

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("/Account/Login", redirect.Url);
        }

        [Fact]
        public async Task HandleAuthorizeRequestAsync_WhenNoMfaPolicyApplies_PreservesBaselineAuthorizationPath()
        {
            var user = new ApplicationUser { Id = Guid.NewGuid() };
            SetupMockUsers(user);

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString())],
                "Test"));
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Code,
                Scope = "openid"
            };
            var application = new object();
            SetupAuthorizationClient(application, false);
            _mockSecurityPolicyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy());
            _mockHttpContextAccessor
                .Setup(accessor => accessor.HttpContext)
                .Returns(new DefaultHttpContext());

            var result = await _authorizationService.HandleAuthorizeRequestAsync(principal, request, null);

            Assert.IsType<ForbidResult>(result);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task Authorization_ShouldPersistSessionOnlyAfterFinalLifecycleAcceptance(int deniedCheck)
        {
            const int assignedRoleCount = 0;
            string? claimedActiveRole = null;
            var lifecycleChecks = 0;
            _lifecycle.Setup(policy => policy.IsEligibleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ++lifecycleChecks != deniedCheck);
            var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.invalid" };
            SetupMockUsers(user);
            SetupMockScopeExtensions();
            using var sessionDb = SetupSessionPersistence();
            var retainedSession = new UserSession { UserId = user.Id, AuthorizationId = "retained-authorization", ClientId = "client" };
            sessionDb.UserSessions.Add(retainedSession);
            await sessionDb.SaveChangesAsync();
            var retainedSnapshot = JsonSerializer.Serialize(retainedSession);
            var principalClaims = new List<Claim>
            {
                new(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
                new(OpenIddictConstants.Claims.AuthenticationTime, "1700000000")
            };
            if (claimedActiveRole is not null)
            {
                principalClaims.Add(new Claim("active_role", claimedActiveRole));
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(principalClaims, "Test"));
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Code,
                Scope = "openid"
            };
            var application = new object();
            var authorization = new object();
            var applicationId = Guid.NewGuid().ToString();
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("client", It.IsAny<CancellationToken>()))
                .ReturnsAsync(application);
            _mockApplicationManager.Setup(m => m.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableDictionary<string, JsonElement>.Empty);
            _mockApplicationManager.Setup(m => m.GetIdAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(applicationId);
            _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync("Test client");
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Permissions.ResponseTypes.Code));
            _mockSecurityPolicyService.Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy());
            _mockClientScopeProcessor.Setup(service => service.EnforceAsync(
                    Guid.Parse(applicationId), It.IsAny<IEnumerable<string>>(), true))
                .ReturnsAsync(new ClientScopeEvaluationResult { AllowedScopes = ["openid"] });
            _mockScopeManager.Setup(m => m.FindByNameAsync("openid", It.IsAny<CancellationToken>()))
                .ReturnsAsync((object?)null);
            _mockClientAllowedScopesService.Setup(service => service.GetRequiredScopesAsync(Guid.Parse(applicationId)))
                .ReturnsAsync([]);
            _mockAuthorizationManager.Setup(m => m.FindAsync(
                    user.Id.ToString(), applicationId, OpenIddictConstants.Statuses.Valid,
                    OpenIddictConstants.AuthorizationTypes.Permanent,
                    It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
                .Returns(ToAsyncEnumerable(authorization));
            _mockAuthorizationManager.Setup(m => m.GetScopesAsync(authorization, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create("openid"));
            _mockAuthorizationManager.Setup(m => m.GetIdAsync(authorization, It.IsAny<CancellationToken>()))
                .ReturnsAsync("auth-existing");
            _mockUserManager.Setup(m => m.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
            _mockUserManager.Setup(m => m.GetEmailAsync(user)).ReturnsAsync(user.Email);
            _mockUserManager.Setup(m => m.GetUserNameAsync(user)).ReturnsAsync("user");
            var assignedRoles = Enumerable.Range(0, assignedRoleCount)
                .Select(index => $"Role{(char)('A' + index)}")
                .ToList();
            _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(assignedRoles);
            _mockApiResourceService.Setup(service => service.GetAudiencesByScopesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync([]);
            _mockClaimsEnricher.Setup(service => service.AddPermissionClaimsAsync(
                    It.IsAny<ClaimsIdentity>(), user, "client", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockClaimsEnricher.Setup(service => service.AddAppSpecificRolesAsync(
                    It.IsAny<ClaimsIdentity>(), user, "client", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockClaimsEnricher.Setup(service => service.AddScopeMappedClaimsAsync(
                    It.IsAny<ClaimsIdentity>(), user, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockHttpContextAccessor.Setup(accessor => accessor.HttpContext)
                .Returns(new DefaultHttpContext());

            var result = await _authorizationService.HandleAuthorizeRequestAsync(principal, request, null);

            if (deniedCheck == 0)
            {
                var signIn = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result);
                Assert.Equal("auth-existing", signIn.Principal.GetAuthorizationId());
                var authenticationTime = Assert.Single(signIn.Principal.FindAll(OpenIddictConstants.Claims.AuthenticationTime));
                Assert.Equal("1700000000", authenticationTime.Value);
                Assert.Equal(ClaimValueTypes.Integer64, authenticationTime.ValueType);
            }
            else
            {
                var denied = Assert.IsType<ForbidResult>(result);
                Assert.Equal(OpenIddictConstants.Errors.AccessDenied,
                    denied.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
            }
            Assert.Equal(deniedCheck == 0 ? 3 : deniedCheck, lifecycleChecks);
            _mockSessionService.Verify(service => service.EnsureCreatedAsync(
                user.Id,
                "auth-existing",
                "client",
                "Test client",
                It.Is<Guid?>(roleId => roleId == null),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), deniedCheck == 0 ? Times.Once() : Times.Never());
            _mockSessionService.VerifyNoOtherCalls();
            _mockAuthorizationManager.Verify(value => value.FindAsync(user.Id.ToString(), applicationId,
                OpenIddictConstants.Statuses.Valid, OpenIddictConstants.AuthorizationTypes.Permanent,
                It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()), Times.Once);
            _mockAuthorizationManager.Verify(value => value.GetScopesAsync(authorization, It.IsAny<CancellationToken>()), Times.Once);
            _mockAuthorizationManager.Verify(value => value.GetIdAsync(authorization, It.IsAny<CancellationToken>()), Times.Once);
            _mockAuthorizationManager.VerifyNoOtherCalls();
            Assert.Equal(deniedCheck == 0 ? 2 : 1, await sessionDb.UserSessions.CountAsync());
            Assert.Equal(retainedSnapshot, JsonSerializer.Serialize(
                await sessionDb.UserSessions.AsNoTracking().SingleAsync(session => session.Id == retainedSession.Id)));
        }

        [Theory]
        [InlineData(0, null, false)]
        [InlineData(2, null, false)]
        [InlineData(1, null, true)]
        [InlineData(1, "UnownedRole", false)]
        public async Task HandleAuthorizeRequestAsync_TracksExistingAuthorizationWithOnlyOwnedResolvedRole(
            int assignedRoleCount,
            string? claimedActiveRole,
            bool expectsActiveRole)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "user@example.invalid" };
            SetupMockUsers(user);
            SetupMockScopeExtensions();
            var principalClaims = new List<Claim>
            {
                new(OpenIddictConstants.Claims.Subject, user.Id.ToString())
            };
            if (claimedActiveRole is not null)
            {
                principalClaims.Add(new Claim("active_role", claimedActiveRole));
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(principalClaims, "Test"));
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Code,
                Scope = "openid"
            };
            var application = new object();
            var authorization = new object();
            var applicationId = Guid.NewGuid().ToString();
            _mockApplicationManager.Setup(m => m.FindByClientIdAsync("client", It.IsAny<CancellationToken>()))
                .ReturnsAsync(application);
            _mockApplicationManager.Setup(m => m.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableDictionary<string, JsonElement>.Empty);
            _mockApplicationManager.Setup(m => m.GetIdAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(applicationId);
            _mockApplicationManager.Setup(m => m.GetDisplayNameAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync("Test client");
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create(OpenIddictConstants.Permissions.ResponseTypes.Code));
            _mockSecurityPolicyService.Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy());
            _mockClientScopeProcessor.Setup(service => service.EnforceAsync(
                    Guid.Parse(applicationId), It.IsAny<IEnumerable<string>>(), true))
                .ReturnsAsync(new ClientScopeEvaluationResult { AllowedScopes = ["openid"] });
            _mockScopeManager.Setup(m => m.FindByNameAsync("openid", It.IsAny<CancellationToken>()))
                .ReturnsAsync((object?)null);
            _mockClientAllowedScopesService.Setup(service => service.GetRequiredScopesAsync(Guid.Parse(applicationId)))
                .ReturnsAsync([]);
            _mockAuthorizationManager.Setup(m => m.FindAsync(
                    user.Id.ToString(), applicationId, OpenIddictConstants.Statuses.Valid,
                    OpenIddictConstants.AuthorizationTypes.Permanent,
                    It.IsAny<ImmutableArray<string>>(), It.IsAny<CancellationToken>()))
                .Returns(ToAsyncEnumerable(authorization));
            _mockAuthorizationManager.Setup(m => m.GetScopesAsync(authorization, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray.Create("openid", "profile", "email"));
            _mockAuthorizationManager.Setup(m => m.GetIdAsync(authorization, It.IsAny<CancellationToken>()))
                .ReturnsAsync("auth-existing");
            _mockUserManager.Setup(m => m.GetUserIdAsync(user)).ReturnsAsync(user.Id.ToString());
            _mockUserManager.Setup(m => m.GetEmailAsync(user)).ReturnsAsync(user.Email);
            _mockUserManager.Setup(m => m.GetUserNameAsync(user)).ReturnsAsync("user");
            var assignedRoles = Enumerable.Range(0, assignedRoleCount)
                .Select(index => $"Role{(char)('A' + index)}")
                .ToList();
            _mockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(assignedRoles);
            var activeRoleId = Guid.NewGuid();
            if (expectsActiveRole)
            {
                _mockRoleManager.Setup(m => m.FindByNameAsync("RoleA"))
                    .ReturnsAsync(new ApplicationRole { Id = activeRoleId, Name = "RoleA" });
            }
            _mockApiResourceService.Setup(service => service.GetAudiencesByScopesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync([]);
            _mockClaimsEnricher.Setup(service => service.AddPermissionClaimsAsync(
                    It.IsAny<ClaimsIdentity>(), user, "client", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockClaimsEnricher.Setup(service => service.AddAppSpecificRolesAsync(
                    It.IsAny<ClaimsIdentity>(), user, "client", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockClaimsEnricher.Setup(service => service.AddScopeMappedClaimsAsync(
                    It.IsAny<ClaimsIdentity>(), user, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockHttpContextAccessor.Setup(accessor => accessor.HttpContext)
                .Returns(new DefaultHttpContext());

            var result = await _authorizationService.HandleAuthorizeRequestAsync(principal, request, null);

            var issued = Assert.IsType<Microsoft.AspNetCore.Mvc.SignInResult>(result).Principal!;
            Assert.Equal(new[] { "openid" }, issued.GetScopes());
            Assert.Null(issued.GetClaim(OpenIddictConstants.Claims.Name));
            Assert.Null(issued.GetClaim(OpenIddictConstants.Claims.Email));
            _mockSessionService.Verify(service => service.EnsureCreatedAsync(
                user.Id,
                "auth-existing",
                "client",
                "Test client",
                It.Is<Guid?>(roleId => roleId == (expectsActiveRole ? activeRoleId : null)),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(AuthConstants.Amr.Mfa, false, "current")]
        [InlineData(AuthConstants.Amr.Mfa, true, "retired")]
        [InlineData(AuthConstants.Amr.Mfa, true, null)]
        [InlineData(AuthConstants.Amr.HardwareKey, true, "current")]
        public async Task HandleAuthorizeRequestAsync_WhenClientRequiresMfaAndPrincipalHasEvidence_DoesNotRedirect(
            string amrValue, bool requiresStepUp, string? binding)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), TwoFactorEnabled = true, SecurityStamp = "current" };
            SetupMockUsers(user);

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
                    new Claim(AuthConstants.ClaimTypes.Amr, amrValue),
                    new Claim(Web.IdP.Helpers.AuthorizationAuthenticationSession.MfaSecurityStampClaim, binding ?? "")
                ],
                "Test"));
            var request = new OpenIddictRequest
            {
                ClientId = "client",
                ResponseType = OpenIddictConstants.ResponseTypes.Code,
                Scope = "openid"
            };
            var application = new object();
            SetupAuthorizationClient(application, true);
            _mockSecurityPolicyService
                .Setup(service => service.GetCurrentPolicyAsync())
                .ReturnsAsync(new SecurityPolicy());
            _mockHttpContextAccessor
                .Setup(accessor => accessor.HttpContext)
                .Returns(new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IAccountRecoveryCodeService>(Mock.Of<IAccountRecoveryCodeService>(codes => codes.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(new AccountRecoveryCodeStatusDto(false, 0)))).BuildServiceProvider() });

            var result = await _authorizationService.HandleAuthorizeRequestAsync(principal, request, null);

            if (requiresStepUp) Assert.IsType<RedirectResult>(result);
            else Assert.IsType<ForbidResult>(result);
        }

        private void SetupAuthorizationClient(object application, bool requireMfa)
        {
            _mockApplicationManager
                .Setup(manager => manager.FindByClientIdAsync("client", It.IsAny<CancellationToken>()))
                .ReturnsAsync(application);
            _mockApplicationManager
                .Setup(manager => manager.GetPropertiesAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateClientProperties(requireMfa));
            _mockApplicationManager
                .Setup(manager => manager.GetIdAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync("client-id-guid");
            _mockApplicationManager
                .Setup(manager => manager.GetDisplayNameAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync("Test client");
            _mockApplicationManager
                .Setup(manager => manager.GetPermissionsAsync(application, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ImmutableArray<string>.Empty);
        }

        private static async IAsyncEnumerable<object> ToAsyncEnumerable(params object[] items)
        {
            foreach (var item in items)
            {
                yield return item;
            }

            await Task.CompletedTask;
        }

        private static ImmutableDictionary<string, JsonElement> CreateClientProperties(bool requireMfa)
        {
            return requireMfa
                ? ImmutableDictionary<string, JsonElement>.Empty.Add(
                    AuthConstants.Properties.RequireMfa,
                    JsonSerializer.SerializeToElement(true))
                : ImmutableDictionary<string, JsonElement>.Empty;
        }

        private sealed class TestSessionFeature : ISessionFeature
        {
            public required ISession Session { get; set; }
        }

        private ApplicationDbContext SetupSessionPersistence()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var context = new ApplicationDbContext(options);
            var sessionService = new global::Infrastructure.Services.SessionService(
                _mockAuthorizationManager.Object, _mockApplicationManager.Object,
                new Mock<IOpenIddictTokenManager>().Object, context);
            _mockSessionService.Setup(service => service.EnsureCreatedAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns((Guid userId, string authorizationId, string clientId, string? clientDisplayName,
                    Guid? activeRoleId, string? ipAddress, string? userAgent, CancellationToken cancellationToken) =>
                    sessionService.EnsureCreatedAsync(userId, authorizationId, clientId, clientDisplayName,
                        activeRoleId, ipAddress, userAgent, cancellationToken));
            return context;
        }

        private void SetupMockUsers(params ApplicationUser[] users)
        {
            var usersQueryable = users.AsQueryable();
            var mockSet = new Mock<DbSet<ApplicationUser>>();
            mockSet.As<IAsyncEnumerable<ApplicationUser>>()
                .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
                .Returns(new TestAsyncEnumerator<ApplicationUser>(usersQueryable.GetEnumerator()));
            mockSet.As<IQueryable<ApplicationUser>>()
                .Setup(m => m.Provider)
                .Returns(new TestAsyncQueryProvider<ApplicationUser>(usersQueryable.Provider));
            mockSet.As<IQueryable<ApplicationUser>>()
                .Setup(m => m.Expression)
                .Returns(usersQueryable.Expression);
            mockSet.As<IQueryable<ApplicationUser>>()
                .Setup(m => m.ElementType)
                .Returns(usersQueryable.ElementType);
            mockSet.As<IQueryable<ApplicationUser>>()
                .Setup(m => m.GetEnumerator())
                .Returns(usersQueryable.GetEnumerator());

            _mockDb.Setup(c => c.Users).Returns(mockSet.Object);
        }

        private void SetupMockScopeExtensions(params ScopeExtension[] extensions)
        {
            var items = extensions.AsQueryable();
            var mockSet = new Mock<DbSet<ScopeExtension>>();
            mockSet.As<IAsyncEnumerable<ScopeExtension>>()
                .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
                .Returns(() => new TestAsyncEnumerator<ScopeExtension>(items.GetEnumerator()));
            mockSet.As<IQueryable<ScopeExtension>>().Setup(m => m.Provider)
                .Returns(new TestAsyncQueryProvider<ScopeExtension>(items.Provider));
            mockSet.As<IQueryable<ScopeExtension>>().Setup(m => m.Expression).Returns(items.Expression);
            mockSet.As<IQueryable<ScopeExtension>>().Setup(m => m.ElementType).Returns(items.ElementType);
            mockSet.As<IQueryable<ScopeExtension>>().Setup(m => m.GetEnumerator()).Returns(items.GetEnumerator());
            _mockDb.Setup(c => c.ScopeExtensions).Returns(mockSet.Object);
        }
    }

}
