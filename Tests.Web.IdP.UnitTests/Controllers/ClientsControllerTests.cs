using System.Security.Claims;
using System.Text.Json;
using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Domain.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Moq;
using OpenIddict.Abstractions;
using Web.IdP.Controllers.Admin;

namespace Tests.Web.IdP.UnitTests.Controllers;

public class ClientsControllerTests
{
    private static readonly Guid TargetClientId = Guid.NewGuid();

    private readonly Mock<IClientService> _clientService = new();
    private readonly Mock<IClientAllowedScopesService> _allowedScopesService = new();

    [Theory]
    [InlineData(MutationOperation.Update, CallerKind.SameOwner)]
    [InlineData(MutationOperation.RegenerateSecret, CallerKind.SameOwner)]
    [InlineData(MutationOperation.SetAllowedScopes, CallerKind.SameOwner)]
    [InlineData(MutationOperation.SetRequiredScopes, CallerKind.SameOwner)]
    [InlineData(MutationOperation.Update, CallerKind.Admin)]
    [InlineData(MutationOperation.RegenerateSecret, CallerKind.Admin)]
    [InlineData(MutationOperation.SetAllowedScopes, CallerKind.Admin)]
    [InlineData(MutationOperation.SetRequiredScopes, CallerKind.Admin)]
    [InlineData(MutationOperation.Update, CallerKind.TrustedAutomation)]
    [InlineData(MutationOperation.RegenerateSecret, CallerKind.TrustedAutomation)]
    [InlineData(MutationOperation.SetAllowedScopes, CallerKind.TrustedAutomation)]
    [InlineData(MutationOperation.SetRequiredScopes, CallerKind.TrustedAutomation)]
    public async Task Mutation_AuthorizedCaller_InvokesExpectedServiceAndPreservesSuccessShape(
        MutationOperation operation,
        CallerKind callerKind)
    {
        var personId = Guid.NewGuid();
        var controller = CreateController(callerKind, personId);
        SetupExistingTarget();
        SetupOwnership(callerKind, personId);
        _clientService
            .Setup(service => service.RegenerateSecretAsync(
                TargetClientId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("[redacted]");

        var result = await InvokeMutationAsync(controller, operation);

        var okResult = Assert.IsType<OkObjectResult>(result);
        AssertSuccessShape(operation, okResult.Value);
        VerifyExpectedMutation(operation, Times.Once());
        VerifyUnexpectedMutations(operation);

        if (callerKind == CallerKind.SameOwner)
        {
            _clientService.Verify(
                service => service.IsClientOwnedByPersonAsync(
                    TargetClientId,
                    personId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        else
        {
            _clientService.Verify(
                service => service.IsClientOwnedByPersonAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Theory]
    [InlineData(MutationOperation.Update, CallerKind.CrossOwner)]
    [InlineData(MutationOperation.RegenerateSecret, CallerKind.CrossOwner)]
    [InlineData(MutationOperation.SetAllowedScopes, CallerKind.CrossOwner)]
    [InlineData(MutationOperation.SetRequiredScopes, CallerKind.CrossOwner)]
    [InlineData(MutationOperation.Update, CallerKind.Unowned)]
    [InlineData(MutationOperation.RegenerateSecret, CallerKind.Unowned)]
    [InlineData(MutationOperation.SetAllowedScopes, CallerKind.Unowned)]
    [InlineData(MutationOperation.SetRequiredScopes, CallerKind.Unowned)]
    [InlineData(MutationOperation.Update, CallerKind.NoPerson)]
    [InlineData(MutationOperation.RegenerateSecret, CallerKind.NoPerson)]
    [InlineData(MutationOperation.SetAllowedScopes, CallerKind.NoPerson)]
    [InlineData(MutationOperation.SetRequiredScopes, CallerKind.NoPerson)]
    [InlineData(MutationOperation.Update, CallerKind.UnrecognizedAutomation)]
    [InlineData(MutationOperation.RegenerateSecret, CallerKind.UnrecognizedAutomation)]
    [InlineData(MutationOperation.SetAllowedScopes, CallerKind.UnrecognizedAutomation)]
    [InlineData(MutationOperation.SetRequiredScopes, CallerKind.UnrecognizedAutomation)]
    [InlineData(MutationOperation.Update, CallerKind.SameSubjectUntrustedAutomation)]
    [InlineData(MutationOperation.RegenerateSecret, CallerKind.SameSubjectUntrustedAutomation)]
    [InlineData(MutationOperation.SetAllowedScopes, CallerKind.SameSubjectUntrustedAutomation)]
    [InlineData(MutationOperation.SetRequiredScopes, CallerKind.SameSubjectUntrustedAutomation)]
    [InlineData(MutationOperation.Update, CallerKind.SameSubjectProductionAutomation)]
    [InlineData(MutationOperation.RegenerateSecret, CallerKind.SameSubjectProductionAutomation)]
    [InlineData(MutationOperation.SetAllowedScopes, CallerKind.SameSubjectProductionAutomation)]
    [InlineData(MutationOperation.SetRequiredScopes, CallerKind.SameSubjectProductionAutomation)]
    [InlineData(MutationOperation.Update, CallerKind.AppRoleAdmin)]
    [InlineData(MutationOperation.RegenerateSecret, CallerKind.AppRoleAdmin)]
    [InlineData(MutationOperation.SetAllowedScopes, CallerKind.AppRoleAdmin)]
    [InlineData(MutationOperation.SetRequiredScopes, CallerKind.AppRoleAdmin)]
    public async Task Mutation_RestrictedCaller_ReturnsForbiddenBeforeMutationService(
        MutationOperation operation,
        CallerKind callerKind)
    {
        var personId = Guid.NewGuid();
        var controller = CreateController(callerKind, personId);
        SetupExistingTarget();
        SetupOwnership(callerKind, personId);

        var result = await InvokeMutationAsync(controller, operation);

        Assert.IsType<ForbidResult>(result);
        VerifyNoMutationServices();
    }

    [Theory]
    [InlineData(MutationOperation.Update)]
    [InlineData(MutationOperation.RegenerateSecret)]
    [InlineData(MutationOperation.SetAllowedScopes)]
    [InlineData(MutationOperation.SetRequiredScopes)]
    public async Task Mutation_GenuinelyMissingTarget_ReturnsNotFoundBeforeOwnershipOrMutation(
        MutationOperation operation)
    {
        var controller = CreateController(CallerKind.NoPerson, Guid.NewGuid());
        _clientService
            .Setup(service => service.GetClientByIdAsync(
                TargetClientId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClientDetail?)null);

        var result = await InvokeMutationAsync(controller, operation);

        Assert.IsType<NotFoundObjectResult>(result);
        _clientService.Verify(
            service => service.IsClientOwnedByPersonAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoMutationServices();
    }

    [Theory]
    [InlineData(MutationOperation.Update)]
    [InlineData(MutationOperation.RegenerateSecret)]
    [InlineData(MutationOperation.SetAllowedScopes)]
    [InlineData(MutationOperation.SetRequiredScopes)]
    public async Task Mutation_HardeningEnabled_ReturnsLockedBeforeLookupOrMutation(
        MutationOperation operation)
    {
        var controller = CreateController(
            CallerKind.Admin,
            Guid.NewGuid(),
            disableClientWriteEndpoints: true);

        var result = await InvokeMutationAsync(controller, operation);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status423Locked, objectResult.StatusCode);
        _clientService.Verify(
            service => service.GetClientByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoMutationServices();
    }

    [Theory]
    [InlineData(MutationOperation.Update)]
    [InlineData(MutationOperation.RegenerateSecret)]
    [InlineData(MutationOperation.SetAllowedScopes)]
    [InlineData(MutationOperation.SetRequiredScopes)]
    public async Task Mutation_InvalidClientId_ReturnsBadRequestBeforeLookupOrMutation(
        MutationOperation operation)
    {
        var controller = CreateController(CallerKind.Admin, Guid.NewGuid());

        var result = await InvokeMutationAsync(controller, operation, "not-a-guid");

        Assert.IsType<BadRequestObjectResult>(result);
        _clientService.Verify(
            service => service.GetClientByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoMutationServices();
    }

    [Theory]
    [InlineData(MutationOperation.SetAllowedScopes)]
    [InlineData(MutationOperation.SetRequiredScopes)]
    public async Task ScopeMutation_NullScopes_ReturnsBadRequestBeforeLookupOrMutation(
        MutationOperation operation)
    {
        var controller = CreateController(CallerKind.Admin, Guid.NewGuid());

        var result = operation switch
        {
            MutationOperation.SetAllowedScopes => await controller.SetAllowedScopes(
                TargetClientId.ToString(),
                new SetAllowedScopesRequest { Scopes = null }),
            MutationOperation.SetRequiredScopes => await controller.SetRequiredScopes(
                TargetClientId.ToString(),
                new SetRequiredScopesRequest { Scopes = null }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        Assert.IsType<BadRequestObjectResult>(result);
        _clientService.Verify(
            service => service.GetClientByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoMutationServices();
    }

    private ClientsController CreateController(
        CallerKind callerKind,
        Guid personId,
        bool disableClientWriteEndpoints = false)
    {
        var claims = new List<Claim>
        {
            new("permission", Permissions.Clients.Update)
        };

        switch (callerKind)
        {
            case CallerKind.SameOwner:
            case CallerKind.CrossOwner:
            case CallerKind.Unowned:
            case CallerKind.AppRoleAdmin:
                claims.Add(new Claim(AuthConstants.Claims.PersonId, personId.ToString()));
                if (callerKind == CallerKind.AppRoleAdmin)
                {
                    claims.Add(new Claim("app_role", AuthConstants.Roles.Admin));
                    claims.Add(new Claim(ClaimTypes.Role, AuthConstants.Roles.Admin));
                }
                else
                {
                    claims.Add(new Claim(ClaimTypes.Role, AuthConstants.Roles.ApplicationManager));
                }
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
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment
            .SetupGet(environment => environment.EnvironmentName)
            .Returns(
                callerKind == CallerKind.SameSubjectProductionAutomation
                    ? Environments.Production
                    : Environments.Development);
        var controller = new ClientsController(
            _clientService.Object,
            _allowedScopesService.Object,
            Microsoft.Extensions.Options.Options.Create(new ClientAdminApiHardeningOptions
            {
                DisableClientWriteEndpoints = disableClientWriteEndpoints
            }),
            Microsoft.Extensions.Options.Options.Create(new PrivilegedTestAdminBootstrapOptions
            {
                Enabled = callerKind is CallerKind.TrustedAutomation
                    or CallerKind.SameSubjectProductionAutomation
            }),
            hostEnvironment.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    private void SetupExistingTarget()
    {
        _clientService
            .Setup(service => service.GetClientByIdAsync(
                TargetClientId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientDetail
            {
                Id = TargetClientId.ToString(),
                ClientId = "target-client"
            });
    }

    private void SetupOwnership(CallerKind callerKind, Guid personId)
    {
        if (callerKind is CallerKind.SameOwner)
        {
            _clientService
                .Setup(service => service.IsClientOwnedByPersonAsync(
                    TargetClientId,
                    personId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }
        else if (callerKind is CallerKind.CrossOwner or CallerKind.Unowned or CallerKind.AppRoleAdmin)
        {
            _clientService
                .Setup(service => service.IsClientOwnedByPersonAsync(
                    TargetClientId,
                    personId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }
    }

    private static async Task<IActionResult> InvokeMutationAsync(
        ClientsController controller,
        MutationOperation operation,
        string? id = null,
        List<string>? scopes = default)
    {
        id ??= TargetClientId.ToString();
        scopes ??= ["profile"];

        return operation switch
        {
            MutationOperation.Update => await controller.UpdateClient(
                id,
                new UpdateClientRequest(
                    "updated-client",
                    "[redacted]",
                    "Updated client",
                    "confidential",
                    "explicit",
                    ["https://client.example/callback"],
                    ["https://client.example/signout"],
                    ["ept:token", "scp:profile"],
                    ["operator"])),
            MutationOperation.RegenerateSecret => await controller.RegenerateSecret(id),
            MutationOperation.SetAllowedScopes => await controller.SetAllowedScopes(
                id,
                new SetAllowedScopesRequest { Scopes = scopes }),
            MutationOperation.SetRequiredScopes => await controller.SetRequiredScopes(
                id,
                new SetRequiredScopesRequest { Scopes = scopes }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static void AssertSuccessShape(
        MutationOperation operation,
        object? value)
    {
        var payload = JsonSerializer.SerializeToElement(value);

        switch (operation)
        {
            case MutationOperation.Update:
                Assert.Equal(
                    TargetClientId.ToString(),
                    payload.GetProperty("id").GetString());
                Assert.Equal(
                    "Client updated successfully.",
                    payload.GetProperty("message").GetString());
                break;
            case MutationOperation.RegenerateSecret:
                Assert.Equal(
                    "Client secret regenerated successfully.",
                    payload.GetProperty("message").GetString());
                Assert.Equal(
                    JsonValueKind.String,
                    payload.GetProperty("clientSecret").ValueKind);
                Assert.False(string.IsNullOrEmpty(
                    payload.GetProperty("clientSecret").GetString()));
                break;
            case MutationOperation.SetAllowedScopes:
                Assert.Equal(
                    "Allowed scopes updated successfully.",
                    payload.GetProperty("message").GetString());
                break;
            case MutationOperation.SetRequiredScopes:
                Assert.Equal(
                    "Required scopes updated successfully.",
                    payload.GetProperty("message").GetString());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private void VerifyExpectedMutation(
        MutationOperation operation,
        Times times)
    {
        switch (operation)
        {
            case MutationOperation.Update:
                _clientService.Verify(
                    service => service.UpdateClientAsync(
                        TargetClientId,
                        It.IsAny<UpdateClientRequest>(),
                        It.IsAny<CancellationToken>()),
                    times);
                break;
            case MutationOperation.RegenerateSecret:
                _clientService.Verify(
                    service => service.RegenerateSecretAsync(
                        TargetClientId,
                        It.IsAny<CancellationToken>()),
                    times);
                break;
            case MutationOperation.SetAllowedScopes:
                _allowedScopesService.Verify(
                    service => service.SetAllowedScopesAsync(
                        TargetClientId,
                        It.IsAny<IEnumerable<string>>()),
                    times);
                break;
            case MutationOperation.SetRequiredScopes:
                _allowedScopesService.Verify(
                    service => service.SetRequiredScopesAsync(
                        TargetClientId,
                        It.IsAny<IEnumerable<string>>()),
                    times);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private void VerifyUnexpectedMutations(MutationOperation expectedOperation)
    {
        foreach (var operation in Enum.GetValues<MutationOperation>())
        {
            if (operation != expectedOperation)
            {
                VerifyExpectedMutation(operation, Times.Never());
            }
        }
    }

    private void VerifyNoMutationServices()
    {
        foreach (var operation in Enum.GetValues<MutationOperation>())
        {
            VerifyExpectedMutation(operation, Times.Never());
        }
    }

    public enum MutationOperation
    {
        Update,
        RegenerateSecret,
        SetAllowedScopes,
        SetRequiredScopes
    }

    public enum CallerKind
    {
        SameOwner,
        Admin,
        TrustedAutomation,
        CrossOwner,
        Unowned,
        NoPerson,
        UnrecognizedAutomation,
        SameSubjectUntrustedAutomation,
        SameSubjectProductionAutomation,
        AppRoleAdmin
    }
}
