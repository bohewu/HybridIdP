using Core.Domain.Enums;

namespace Core.Application.Ports;

public interface IForgotPasswordRoutingEvaluator
{
    ForgotPasswordRoutingDecision Evaluate(
        ForgotPasswordMode runtimeMode,
        string? customExternalUrl);
}

public sealed record ForgotPasswordRoutingDecision(
    ForgotPasswordMode RuntimeMode,
    ForgotPasswordMode DeploymentCeiling,
    ForgotPasswordMode PermittedMode,
    ForgotPasswordMode AvailableMode,
    string? ExternalUrl);
