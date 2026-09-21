using Core.Application.Ports;

namespace Tests.Application.UnitTests;

public sealed class RecoveryProofContractTests
{
    [Fact]
    public void PublicProofRequests_DoNotAcceptCallerBooleanEvidenceOrMigrationEmailAddress()
    {
        var requestTypes = new[]
        {
            typeof(RecoveryEmailChangeRequest),
            typeof(RecoveryEmailVerificationRequest),
            typeof(MigrationRecoveryEmailVerificationRequest),
            typeof(MigrationOtpSendRequest),
            typeof(MigrationOtpVerificationRequest),
            typeof(MigrationOtpConsumptionRequest),
            typeof(AdminMigrationOtpResendRequest),
            typeof(AdminRecoveryEmailReplacementRequest),
            typeof(AdminResetApprovalRequest),
            typeof(ResetApprovalConsumptionRequest)
        };

        Assert.All(requestTypes, type =>
            Assert.DoesNotContain(type.GetProperties(), property => property.PropertyType == typeof(bool)));

        Assert.DoesNotContain(
            typeof(MigrationOtpSendRequest).GetProperties(),
            property => property.Name.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(MigrationOtpVerificationRequest).GetProperties(),
            property => property.Name.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(MigrationOtpConsumptionRequest).GetProperties(),
            property => property.Name.Contains("Email", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AdministratorRequests_SelectTargetWithoutUserCeremonySecrets()
    {
        var adminRequestTypes = new[]
        {
            typeof(AdminMigrationOtpResendRequest),
            typeof(AdminRecoveryEmailReplacementRequest),
            typeof(AdminResetApprovalRequest)
        };

        Assert.All(adminRequestTypes, type =>
        {
            Assert.Contains(type.GetProperties(), property =>
                property.Name == nameof(AdminMigrationOtpResendRequest.ActorAccountId) &&
                property.PropertyType == typeof(Guid));
            Assert.Contains(type.GetProperties(), property =>
                property.Name == nameof(AdminMigrationOtpResendRequest.TargetAccountId) &&
                property.PropertyType == typeof(Guid));
            Assert.DoesNotContain(type.GetProperties(), property =>
                property.Name.Contains("Continuation", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Context", StringComparison.OrdinalIgnoreCase));
        });

        Assert.DoesNotContain(
            typeof(ResetApprovalIssueResult).GetProperties(),
            property => property.Name.Contains("Approval", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(ResetApprovalConsumptionRequest).GetProperties(),
            property => property.Name.Contains("Approval", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProofServices_ExposeSeparateRecoveryAddressMigrationOtpAndApprovalOperations()
    {
        Assert.Contains(nameof(IRecoveryEmailService.BeginAuthenticatedChangeAsync),
            typeof(IRecoveryEmailService).GetMethods().Select(method => method.Name));
        Assert.Contains(nameof(IMigrationOtpProofService.ConsumeAsync),
            typeof(IMigrationOtpProofService).GetMethods().Select(method => method.Name));
        Assert.Contains(nameof(IRecoveryAssistanceService.ConsumeResetApprovalAsync),
            typeof(IRecoveryAssistanceService).GetMethods().Select(method => method.Name));
    }
}
