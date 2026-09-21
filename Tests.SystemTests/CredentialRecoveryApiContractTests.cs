using Core.Application.Ports;
using Core.Domain.Entities;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Web.IdP.Attributes;
using Web.IdP.Controllers.Account;
using Web.IdP.Controllers.Admin;
using Web.IdP.Pages.Account;

namespace Tests.SystemTests;

public sealed class CredentialRecoveryApiContractTests
{
    [Fact]
    public void RecoveryEmailApi_RequiresAuthenticationCsrfAndRateLimit()
    {
        var type = typeof(RecoveryEmailController);

        Assert.NotNull(type.GetCustomAttributes(typeof(ApiAuthorizeAttribute), true).SingleOrDefault());
        Assert.NotNull(type.GetCustomAttributes(typeof(ValidateCsrfForCookiesAttribute), true).SingleOrDefault());
        Assert.NotNull(type.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).SingleOrDefault());
    }

    [Fact]
    public void MigrationAndAdminRequests_DoNotExposeApprovalTokenOrMigrationEmailAddress()
    {
        Assert.Equal(
            ["Context", "Continuation", "MigrationOtpProof", "NewPassword"],
            typeof(MigrationCommitCeremonyRequest).GetProperties().Select(property => property.Name).Order());
        Assert.Equal(
            ["Code"],
            typeof(CredentialMigrationModel.ProofInput).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(
            typeof(AdminResetApprovalApiRequest).GetProperties(),
            property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(nameof(UsersController.ResendCredentialMigrationOtp))]
    [InlineData(nameof(UsersController.ReplaceCredentialRecoveryEmail))]
    [InlineData(nameof(UsersController.ApproveCredentialRecoveryReset))]
    [InlineData(nameof(UsersController.ResendNativeRecoveryOtp))]
    [InlineData(nameof(UsersController.ReplaceNativeRecoveryEmail))]
    [InlineData(nameof(UsersController.ApproveNativeRecoveryReset))]
    public void AdminRecoveryActions_UseExistingPermissionAndRateLimitGuards(string methodName)
    {
        var method = typeof(UsersController).GetMethod(methodName)!;

        Assert.Contains(method.GetCustomAttributes(true), attribute => attribute is HasPermissionAttribute);
        Assert.Contains(method.GetCustomAttributes(true), attribute => attribute is EnableRateLimitingAttribute);
    }

    [Fact]
    public void NativeApprovalAndOriginalBrowserHandlers_ExposeNoApprovalToken()
    {
        Assert.DoesNotContain(
            typeof(NativeRecoveryResetApproval).GetProperties(),
            property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(typeof(ForgotPasswordModel).GetMethod(nameof(ForgotPasswordModel.OnPostUseApprovalAsync)));
        Assert.NotNull(typeof(ForgotPasswordModel).GetMethod(nameof(ForgotPasswordModel.OnPostVerifyReplacementAsync)));
    }
}
