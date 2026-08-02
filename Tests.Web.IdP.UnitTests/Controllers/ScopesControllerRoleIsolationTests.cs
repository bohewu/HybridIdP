using System.Security.Claims;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Controllers.Admin;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class ScopesControllerRoleIsolationTests
{
    private const string TargetScopeId = "scope-id";
    private const string TargetScopeName = "custom-scope";

    private readonly Mock<IScopeService> _scopeService = new();

    [Theory]
    [InlineData(ScopeMutation.Update, CallerKind.SameOwner)]
    [InlineData(ScopeMutation.Delete, CallerKind.SameOwner)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.SameOwner)]
    [InlineData(ScopeMutation.Update, CallerKind.Admin)]
    [InlineData(ScopeMutation.Delete, CallerKind.Admin)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.Admin)]
    [InlineData(ScopeMutation.Update, CallerKind.TrustedAutomation)]
    [InlineData(ScopeMutation.Delete, CallerKind.TrustedAutomation)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.TrustedAutomation)]
    public async Task Mutation_AuthorizedCaller_InvokesExpectedService(
        ScopeMutation mutation,
        CallerKind callerKind)
    {
        var personId = Guid.NewGuid();
        var controller = CreateController(callerKind, personId);
        SetupExistingScope();
        SetupOwnership(callerKind, personId);
        SetupMutationResults();

        var result = await InvokeMutationAsync(controller, mutation);

        Assert.IsType<OkObjectResult>(result);
        VerifyExpectedMutation(mutation, Times.Once());
        VerifyExpectedLookup(mutation, callerKind == CallerKind.Admin ? Times.Never() : Times.Once());
    }

    [Theory]
    [InlineData(ScopeMutation.Update, CallerKind.CrossOwner)]
    [InlineData(ScopeMutation.Delete, CallerKind.CrossOwner)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.CrossOwner)]
    [InlineData(ScopeMutation.Update, CallerKind.NoPerson)]
    [InlineData(ScopeMutation.Delete, CallerKind.NoPerson)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.NoPerson)]
    [InlineData(ScopeMutation.Update, CallerKind.UnrecognizedAutomation)]
    [InlineData(ScopeMutation.Delete, CallerKind.UnrecognizedAutomation)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.UnrecognizedAutomation)]
    [InlineData(ScopeMutation.Update, CallerKind.SameSubjectUntrustedAutomation)]
    [InlineData(ScopeMutation.Delete, CallerKind.SameSubjectUntrustedAutomation)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.SameSubjectUntrustedAutomation)]
    [InlineData(ScopeMutation.Update, CallerKind.SameSubjectProductionAutomation)]
    [InlineData(ScopeMutation.Delete, CallerKind.SameSubjectProductionAutomation)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.SameSubjectProductionAutomation)]
    [InlineData(ScopeMutation.Update, CallerKind.AppRoleAdmin)]
    [InlineData(ScopeMutation.Delete, CallerKind.AppRoleAdmin)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.AppRoleAdmin)]
    public async Task Mutation_RestrictedCaller_ReturnsForbiddenWithoutInvokingMutation(
        ScopeMutation mutation,
        CallerKind callerKind)
    {
        var personId = Guid.NewGuid();
        var controller = CreateController(callerKind, personId);
        SetupExistingScope();
        SetupOwnership(callerKind, personId);
        SetupMutationResults();

        var result = await InvokeMutationAsync(controller, mutation);

        Assert.IsType<ForbidResult>(result);
        VerifyExpectedLookup(mutation, Times.Once());
        VerifyNoMutationServices();
    }

    [Theory]
    [InlineData(ScopeMutation.Update, CallerKind.SameOwner)]
    [InlineData(ScopeMutation.Delete, CallerKind.SameOwner)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.SameOwner)]
    [InlineData(ScopeMutation.Update, CallerKind.TrustedAutomation)]
    [InlineData(ScopeMutation.Delete, CallerKind.TrustedAutomation)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.TrustedAutomation)]
    [InlineData(ScopeMutation.Update, CallerKind.AppRoleAdmin)]
    [InlineData(ScopeMutation.Delete, CallerKind.AppRoleAdmin)]
    [InlineData(ScopeMutation.UpdateClaims, CallerKind.AppRoleAdmin)]
    public async Task Mutation_NonIdpAdminStandardScope_ReturnsForbidden(
        ScopeMutation mutation,
        CallerKind callerKind)
    {
        var personId = Guid.NewGuid();
        var controller = CreateController(callerKind, personId);
        SetupExistingScope(OpenIddictConstants.Scopes.OpenId);
        SetupOwnership(callerKind, personId);
        SetupMutationResults();

        var result = await InvokeMutationAsync(
            controller,
            mutation,
            deleteIdentifier: OpenIddictConstants.Scopes.OpenId);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        VerifyExpectedLookup(mutation, Times.Once(), OpenIddictConstants.Scopes.OpenId);
        _scopeService.Verify(
            service => service.IsScopeOwnedByPersonAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoMutationServices();
    }

    [Theory]
    [InlineData(ScopeMutation.Update)]
    [InlineData(ScopeMutation.Delete)]
    [InlineData(ScopeMutation.UpdateClaims)]
    public async Task Mutation_MissingTarget_PreservesExistingErrorSemantics(
        ScopeMutation mutation)
    {
        var controller = CreateController(CallerKind.CrossOwner, Guid.NewGuid());
        _scopeService
            .Setup(service => service.DeleteScopeAsync(
                TargetScopeName,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await InvokeMutationAsync(controller, mutation);

        if (mutation == ScopeMutation.Delete)
        {
            Assert.IsType<BadRequestObjectResult>(result);
            VerifyExpectedMutation(mutation, Times.Once());
        }
        else
        {
            Assert.IsType<NotFoundObjectResult>(result);
            VerifyNoMutationServices();
        }

        VerifyExpectedLookup(mutation, Times.Once());
    }

    private ScopesController CreateController(CallerKind callerKind, Guid personId)
    {
        var claims = new List<Claim>
        {
            new("permission", Permissions.Scopes.Update),
            new("permission", Permissions.Scopes.Delete)
        };

        switch (callerKind)
        {
            case CallerKind.SameOwner:
            case CallerKind.CrossOwner:
                claims.Add(new Claim(AuthConstants.Claims.PersonId, personId.ToString()));
                claims.Add(new Claim(ClaimTypes.Role, AuthConstants.Roles.ApplicationManager));
                break;
            case CallerKind.Admin:
                claims.Add(new Claim(AuthConstants.Claims.PersonId, personId.ToString()));
                claims.Add(new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin));
                break;
            case CallerKind.TrustedAutomation:
            case CallerKind.SameSubjectUntrustedAutomation:
            case CallerKind.SameSubjectProductionAutomation:
                claims.Add(new Claim(
                    OpenIddictConstants.Claims.Subject,
                    "testclient-admin"));
                break;
            case CallerKind.UnrecognizedAutomation:
                claims.Add(new Claim(
                    OpenIddictConstants.Claims.Subject,
                    "testclient-admin-shadow"));
                break;
            case CallerKind.AppRoleAdmin:
                claims.Add(new Claim(AuthConstants.Claims.PersonId, personId.ToString()));
                claims.Add(new Claim("app_role", AuthConstants.Roles.Admin));
                claims.Add(new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin));
                break;
            case CallerKind.NoPerson:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(callerKind));
        }

        var identity = new ClaimsIdentity(
            claims,
            authenticationType: "test",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);
        var environment = new Mock<IWebHostEnvironment>();
        environment
            .SetupGet(value => value.EnvironmentName)
            .Returns(callerKind == CallerKind.SameSubjectProductionAutomation
                ? Environments.Production
                : Environments.Development);

        var controller = new ScopesController(
            _scopeService.Object,
            Microsoft.Extensions.Options.Options.Create(
                new PrivilegedTestAdminBootstrapOptions
                {
                    Enabled = callerKind is CallerKind.TrustedAutomation
                        or CallerKind.UnrecognizedAutomation
                        or CallerKind.SameSubjectProductionAutomation
                }),
            environment.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    private void SetupExistingScope(string scopeName = TargetScopeName)
    {
        var scope = new ScopeSummary
        {
            Id = TargetScopeId,
            Name = scopeName,
            DisplayName = "Target scope"
        };
        _scopeService
            .Setup(service => service.GetScopeByIdAsync(
                TargetScopeId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        _scopeService
            .Setup(service => service.GetScopeByNameAsync(
                scopeName,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
    }

    private void SetupOwnership(CallerKind callerKind, Guid personId)
    {
        _scopeService
            .Setup(service => service.IsScopeOwnedByPersonAsync(
                TargetScopeId,
                personId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerKind == CallerKind.SameOwner);
    }

    private void SetupMutationResults()
    {
        _scopeService
            .Setup(service => service.UpdateScopeAsync(
                TargetScopeId,
                It.IsAny<UpdateScopeRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _scopeService
            .Setup(service => service.DeleteScopeAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _scopeService
            .Setup(service => service.UpdateScopeClaimsAsync(
                TargetScopeId,
                It.IsAny<UpdateScopeClaimsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TargetScopeId, TargetScopeName, Array.Empty<ScopeClaimDto>()));
    }

    private static async Task<IActionResult> InvokeMutationAsync(
        ScopesController controller,
        ScopeMutation mutation,
        string deleteIdentifier = TargetScopeName)
    {
        return mutation switch
        {
            ScopeMutation.Update => await controller.Update(
                TargetScopeId,
                new UpdateScopeRequest(null, "Updated", null, null)),
            ScopeMutation.Delete => await controller.Delete(deleteIdentifier),
            ScopeMutation.UpdateClaims => await controller.UpdateScopeClaims(
                TargetScopeId,
                new UpdateScopeClaimsRequest([])),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
    }

    private void VerifyExpectedLookup(
        ScopeMutation mutation,
        Times times,
        string deleteIdentifier = TargetScopeName)
    {
        if (mutation == ScopeMutation.Delete)
        {
            _scopeService.Verify(
                service => service.GetScopeByNameAsync(
                    deleteIdentifier,
                    It.IsAny<CancellationToken>()),
                times);
            return;
        }

        _scopeService.Verify(
            service => service.GetScopeByIdAsync(
                TargetScopeId,
                It.IsAny<CancellationToken>()),
            times);
    }

    private void VerifyExpectedMutation(ScopeMutation mutation, Times times)
    {
        switch (mutation)
        {
            case ScopeMutation.Update:
                _scopeService.Verify(
                    service => service.UpdateScopeAsync(
                        TargetScopeId,
                        It.IsAny<UpdateScopeRequest>(),
                        It.IsAny<CancellationToken>()),
                    times);
                break;
            case ScopeMutation.Delete:
                _scopeService.Verify(
                    service => service.DeleteScopeAsync(
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()),
                    times);
                break;
            case ScopeMutation.UpdateClaims:
                _scopeService.Verify(
                    service => service.UpdateScopeClaimsAsync(
                        TargetScopeId,
                        It.IsAny<UpdateScopeClaimsRequest>(),
                        It.IsAny<CancellationToken>()),
                    times);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    private void VerifyNoMutationServices()
    {
        _scopeService.Verify(
            service => service.UpdateScopeAsync(
                It.IsAny<string>(),
                It.IsAny<UpdateScopeRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _scopeService.Verify(
            service => service.DeleteScopeAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _scopeService.Verify(
            service => service.UpdateScopeClaimsAsync(
                It.IsAny<string>(),
                It.IsAny<UpdateScopeClaimsRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    public enum ScopeMutation
    {
        Update,
        Delete,
        UpdateClaims
    }

    public enum CallerKind
    {
        SameOwner,
        CrossOwner,
        Admin,
        TrustedAutomation,
        NoPerson,
        UnrecognizedAutomation,
        SameSubjectUntrustedAutomation,
        SameSubjectProductionAutomation,
        AppRoleAdmin
    }
}
