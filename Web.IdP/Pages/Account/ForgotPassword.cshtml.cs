using System.Security.Cryptography;
using System.Text;
using Core.Application;
using Core.Application.Ports;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Web.IdP.Pages.Account;

[EnableRateLimiting("login")]
[ValidateAntiForgeryToken]
public sealed class ForgotPasswordModel : PageModel
{
    private const string ContextKey = "native-recovery.context";
    private const string CsrfKey = "native-recovery.csrf";
    private const string RequestIdKey = "native-recovery.request-id";
    private const string ProofKey = "native-recovery.proof";
    private const string AdministrativeApprovalKey = "native-recovery.admin-approval";

    private readonly INativePasswordRecoveryProofService _proofService;
    private readonly INativePasswordRecoveryResetService _resetService;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly IForgotPasswordRoutingEvaluator _routingEvaluator;
    private readonly IDataProtector _proofProtector;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly INativeRecoveryAssistanceService? _assistanceService;

    public ForgotPasswordModel(
        INativePasswordRecoveryProofService proofService,
        INativePasswordRecoveryResetService resetService,
        ISecurityPolicyService securityPolicyService,
        IForgotPasswordRoutingEvaluator routingEvaluator,
        IDataProtectionProvider dataProtectionProvider,
        IStringLocalizer<SharedResource> localizer,
        INativeRecoveryAssistanceService? assistanceService = null)
    {
        _proofService = proofService;
        _resetService = resetService;
        _securityPolicyService = securityPolicyService;
        _routingEvaluator = routingEvaluator;
        _proofProtector = dataProtectionProvider.CreateProtector(
            "HybridIdP.NativePasswordRecovery.WebProof.v1");
        _localizer = localizer;
        _assistanceService = assistanceService;
    }

    [BindProperty]
    public IdentifierInput Identifier { get; set; } = new();

    [BindProperty]
    public CodeInput Verification { get; set; } = new();

    [BindProperty]
    public PasswordInput Password { get; set; } = new();

    public bool AwaitingCode { get; private set; }
    public bool AwaitingPassword { get; private set; }
    public bool RecoverySucceeded { get; private set; }
    public SecurityPolicy? CurrentPolicy { get; private set; }
    public IReadOnlyList<ClientPasswordRule> ClientPasswordRules { get; private set; } = [];

    public sealed record ClientPasswordRule(
        string Name,
        string ResourceKey,
        int? RequiredValue = null);

    public sealed class IdentifierInput
    {
        public string? Value { get; set; }
    }

    public sealed class CodeInput
    {
        public string? Code { get; set; }
    }

    public sealed class PasswordInput
    {
        public string? NewPassword { get; set; }
        public string? ConfirmPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await HttpContext.Session.LoadAsync();
        if (!await IsNativeAvailableAsync())
        {
            ClearRecoveryState();
            return NotFound();
        }

        RestorePhase();
        return Page();
    }

    public async Task<IActionResult> OnPostStartAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);
        if (!await IsNativeAvailableAsync())
        {
            ClearRecoveryState();
            return NotFound();
        }

        ClearRecoveryState();
        var identifier = Identifier.Value;
        Identifier = new IdentifierInput();
        ModelState.Clear();
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 256)
        {
            ModelState.AddModelError(string.Empty, _localizer["NativeRecovery.IdentifierRequired"]);
            return Page();
        }

        var context = CreateContext();
        var result = await _proofService.StartAsync(
            new NativeRecoveryStartRequest(identifier, context),
            cancellationToken);
        HttpContext.Session.SetString(RequestIdKey, result.RequestId.ToString("D"));
        AwaitingCode = true;
        return Page();
    }

    public async Task<IActionResult> OnPostVerifyAsync(CancellationToken cancellationToken)
    {
        var code = Verification.Code;
        Verification = new CodeInput();
        Password = new PasswordInput();
        ModelState.Clear();
        await HttpContext.Session.LoadAsync(cancellationToken);
        if (!await IsNativeAvailableAsync())
        {
            ClearRecoveryState();
            return NotFound();
        }

        if (!TryGetRequest(out var requestId, out var context) ||
            code?.Length != 6 || code.Any(character => !char.IsAsciiDigit(character)))
        {
            return VerificationFailed();
        }

        var result = await _proofService.VerifyAsync(
            new NativeRecoveryVerificationRequest(requestId, code, context),
            cancellationToken);
        if (result.Outcome != NativeRecoveryVerificationOutcome.Verified ||
            string.IsNullOrWhiteSpace(result.Proof))
        {
            return VerificationFailed();
        }

        HttpContext.Session.SetString(ProofKey, _proofProtector.Protect(result.Proof));
        AwaitingPassword = true;
        return Page();
    }

    public async Task<IActionResult> OnPostResetAsync(CancellationToken cancellationToken)
    {
        var newPassword = Password.NewPassword;
        var confirmPassword = Password.ConfirmPassword;
        Password = new PasswordInput();
        ModelState.Clear();
        await HttpContext.Session.LoadAsync(cancellationToken);
        if (!await IsNativeAvailableAsync())
        {
            ClearRecoveryState();
            return NotFound();
        }

        var useAdministrativeApproval =
            HttpContext.Session.GetString(AdministrativeApprovalKey) == "available";
        var proof = string.Empty;
        if (!TryGetRequest(out var requestId, out var context) ||
            !useAdministrativeApproval && !TryGetProof(out proof))
        {
            return ResetDenied();
        }

        if (string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(confirmPassword))
        {
            AwaitingPassword = true;
            ModelState.AddModelError(string.Empty, _localizer["NativeRecovery.PasswordRequired"]);
            return Page();
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            AwaitingPassword = true;
            ModelState.AddModelError(string.Empty, _localizer["NativeRecovery.PasswordMismatch"]);
            return Page();
        }

        var result = await _resetService.ResetAsync(
            new NativeRecoveryResetRequest(
                requestId,
                proof,
                newPassword,
                context,
                useAdministrativeApproval),
            cancellationToken);

        if (result.Outcome == NativeRecoveryResetOutcome.PasswordRejected)
        {
            AwaitingPassword = true;
            AddPasswordResetErrors(result.ErrorCodes);
            return Page();
        }

        ClearRecoveryState();
        if (result.Outcome != NativeRecoveryResetOutcome.Succeeded)
        {
            return ResetDenied();
        }

        RecoverySucceeded = true;
        return Page();
    }

    public async Task<IActionResult> OnPostStartOverAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);
        if (!await IsNativeAvailableAsync())
        {
            ClearRecoveryState();
            return NotFound();
        }

        ClearRecoveryState();
        ModelState.Clear();
        return Page();
    }

    public async Task<IActionResult> OnPostVerifyReplacementAsync(CancellationToken cancellationToken)
    {
        var code = Verification.Code;
        Verification = new CodeInput();
        ModelState.Clear();
        await HttpContext.Session.LoadAsync(cancellationToken);
        if (!await IsNativeAvailableAsync() || _assistanceService is null ||
            !TryGetRequest(out var requestId, out var context) || code?.Length != 6 ||
            code.Any(character => !char.IsAsciiDigit(character)))
        {
            return BadRequest(new { outcome = "unavailable" });
        }

        var outcome = await _assistanceService.VerifyReplacementAsync(
            new NativeRecoveryReplacementVerificationRequest(requestId, code, context),
            cancellationToken);
        if (outcome != RecoveryProofOutcome.Success)
        {
            return BadRequest(new { outcome = "unavailable" });
        }

        ClearRecoveryState();
        return new JsonResult(new { outcome = "success" });
    }

    public async Task<IActionResult> OnPostUseApprovalAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);
        if (!await IsNativeAvailableAsync() || _assistanceService is null ||
            !TryGetRequest(out var requestId, out var context))
        {
            return BadRequest(new { outcome = "unavailable" });
        }

        var outcome = await _assistanceService.GetApprovalStatusAsync(
            new NativeRecoveryApprovalStatusRequest(requestId, context),
            cancellationToken);
        if (outcome != RecoveryProofOutcome.Success)
        {
            return BadRequest(new { outcome = "unavailable" });
        }

        HttpContext.Session.SetString(AdministrativeApprovalKey, "available");
        return new JsonResult(new { outcome = "success" });
    }

    private async Task<bool> IsNativeAvailableAsync()
    {
        CurrentPolicy = await _securityPolicyService.GetCurrentPolicyAsync();
        ClientPasswordRules = BuildClientPasswordRules(CurrentPolicy);
        var decision = _routingEvaluator.Evaluate(
            CurrentPolicy.ForgotPasswordMode,
            CurrentPolicy.CustomForgotPasswordUrl);
        return decision.AvailableMode == ForgotPasswordMode.Native;
    }

    private static IReadOnlyList<ClientPasswordRule> BuildClientPasswordRules(SecurityPolicy policy)
    {
        var rules = new List<ClientPasswordRule>();
        if (policy.MinPasswordLength > 0)
        {
            rules.Add(new(
                "minimum-length",
                "NativeRecovery.Policy.MinimumLength",
                policy.MinPasswordLength));
        }

        if (policy.RequireUppercase)
        {
            rules.Add(new("uppercase", "NativeRecovery.Policy.Uppercase"));
        }

        if (policy.RequireLowercase)
        {
            rules.Add(new("lowercase", "NativeRecovery.Policy.Lowercase"));
        }

        if (policy.RequireDigit)
        {
            rules.Add(new("digit", "NativeRecovery.Policy.Digit"));
        }

        if (policy.RequireNonAlphanumeric)
        {
            rules.Add(new("symbol", "NativeRecovery.Policy.Symbol"));
        }

        if (policy.MinCharacterTypes > 0)
        {
            rules.Add(new(
                "character-types",
                "NativeRecovery.Policy.CharacterTypes",
                policy.MinCharacterTypes));
        }

        return rules;
    }

    private void AddPasswordResetErrors(IReadOnlyList<string>? errorCodes)
    {
        if (CurrentPolicy is null || errorCodes is null || errorCodes.Count == 0 ||
            errorCodes.Any(code => !IsAllowedPasswordError(code)))
        {
            ModelState.AddModelError(string.Empty, _localizer["NativeRecovery.RequestFailed"]);
            return;
        }

        foreach (var code in errorCodes.Distinct(StringComparer.Ordinal))
        {
            var message = code switch
            {
                "PasswordTooShort" => _localizer[
                    "NativeRecovery.Error.MinimumLength",
                    CurrentPolicy.MinPasswordLength],
                "PasswordRequiresUpper" => _localizer["NativeRecovery.Error.Uppercase"],
                "PasswordRequiresLower" => _localizer["NativeRecovery.Error.Lowercase"],
                "PasswordRequiresDigit" => _localizer["NativeRecovery.Error.Digit"],
                "PasswordRequiresNonAlphanumeric" => _localizer["NativeRecovery.Error.Symbol"],
                "PasswordTooSimple" => _localizer[
                    "NativeRecovery.Error.CharacterTypes",
                    CurrentPolicy.MinCharacterTypes],
                "PasswordReuse" => _localizer[
                    "NativeRecovery.Error.PasswordReuse",
                    CurrentPolicy.PasswordHistoryCount],
                _ => _localizer["NativeRecovery.RequestFailed"]
            };
            ModelState.AddModelError(string.Empty, message);
        }
    }

    private static bool IsAllowedPasswordError(string code) =>
        code is
            "PasswordTooShort" or
            "PasswordRequiresUpper" or
            "PasswordRequiresLower" or
            "PasswordRequiresDigit" or
            "PasswordRequiresNonAlphanumeric" or
            "PasswordTooSimple" or
            "PasswordReuse";

    private NativeRecoveryContext CreateContext()
    {
        var browserContext = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var csrfContext = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        HttpContext.Session.SetString(ContextKey, browserContext);
        HttpContext.Session.SetString(CsrfKey, csrfContext);
        return new NativeRecoveryContext(Hash(browserContext), Hash(csrfContext));
    }

    private bool TryGetRequest(out Guid requestId, out NativeRecoveryContext context)
    {
        var requestValue = HttpContext.Session.GetString(RequestIdKey);
        var browserContext = HttpContext.Session.GetString(ContextKey);
        var csrfContext = HttpContext.Session.GetString(CsrfKey);
        if (!Guid.TryParse(requestValue, out requestId) ||
            string.IsNullOrWhiteSpace(browserContext) ||
            string.IsNullOrWhiteSpace(csrfContext))
        {
            context = default!;
            return false;
        }

        context = new NativeRecoveryContext(Hash(browserContext), Hash(csrfContext));
        return true;
    }

    private bool TryGetProof(out string proof)
    {
        proof = string.Empty;
        var protectedProof = HttpContext.Session.GetString(ProofKey);
        if (string.IsNullOrWhiteSpace(protectedProof))
        {
            return false;
        }

        try
        {
            proof = _proofProtector.Unprotect(protectedProof);
            return !string.IsNullOrWhiteSpace(proof);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private void RestorePhase()
    {
        AwaitingPassword = TryGetProof(out _);
        AwaitingCode = !AwaitingPassword &&
            Guid.TryParse(HttpContext.Session.GetString(RequestIdKey), out _);
    }

    private IActionResult VerificationFailed()
    {
        AwaitingCode = Guid.TryParse(HttpContext.Session.GetString(RequestIdKey), out _);
        if (!AwaitingCode)
        {
            ClearRecoveryState();
        }

        ModelState.AddModelError(string.Empty, _localizer["NativeRecovery.VerificationFailed"]);
        return Page();
    }

    private IActionResult ResetDenied()
    {
        ClearRecoveryState();
        ModelState.AddModelError(string.Empty, _localizer["NativeRecovery.RequestFailed"]);
        return Page();
    }

    private void ClearRecoveryState()
    {
        HttpContext.Session.Remove(ContextKey);
        HttpContext.Session.Remove(CsrfKey);
        HttpContext.Session.Remove(RequestIdKey);
        HttpContext.Session.Remove(ProofKey);
        HttpContext.Session.Remove(AdministrativeApprovalKey);
        AwaitingCode = false;
        AwaitingPassword = false;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
