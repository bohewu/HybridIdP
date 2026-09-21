using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Core.Application.Ports;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.SystemTests;

public sealed partial class NativePasswordRecoveryRazorSystemTests
{
    [Theory]
    [InlineData("en-US", true)]
    [InlineData("zh-TW", true)]
    [InlineData("en-US", false)]
    [InlineData("zh-TW", false)]
    public async Task Guidance_RazorPhases_PreserveCoreOrderAndExcludeReminderUntilSuccess(
        string culture, bool configured)
    {
        await using var factory = await NativeRecoveryKestrelFactory.CreateGuidanceTestServerAsync(options =>
        {
            if (configured) ConfigureLiteralGuidance(options);
        });
        factory.ResetService.Outcome = NativeRecoveryResetOutcome.PasswordRejected;
        var path = $"/Account/ForgotPassword?culture={culture}&ui-culture={culture}";
        var html = await factory.Client.GetStringAsync(path);
        AssertGuidancePhase(html, "start", configured);
        var postStep = 0;

        async Task<string> PostAsync(string handler, params (string Name, string Value)[] fields)
        {
            postStep++;
            Console.WriteLine(
                $"[native-recovery-guidance] culture={culture} configured={configured} post-step={postStep} handler={handler} phase=send");
            using var content = CreateForm(ExtractAntiforgeryToken(html), fields);
            using var response = await factory.Client.PostAsync(
                path + "&handler=" + handler, content);
            Console.WriteLine(
                $"[native-recovery-guidance] culture={culture} configured={configured} post-step={postStep} handler={handler} phase=headers status={(int)response.StatusCode}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            html = await response.Content.ReadAsStringAsync();
            Console.WriteLine(
                $"[native-recovery-guidance] culture={culture} configured={configured} post-step={postStep} handler={handler} phase=body-complete");
            return html;
        }

        await PostAsync("Start", ("Identifier.Value", " "));
        Assert.Contains("native-recovery-error", html);
        AssertGuidancePhase(html, "start", configured);

        await PostAsync("Start", ("Identifier.Value", SyntheticIdentifier));
        AssertGuidancePhase(html, "code", configured);
        await PostAsync("Verify", ("Verification.Code", "000000"));
        Assert.Contains("native-recovery-error", html);
        AssertGuidancePhase(html, "code", configured);
        await PostAsync("Verify", ("Verification.Code", SyntheticCode));
        AssertGuidancePhase(html, "password", configured);
        await PostAsync("Reset", ("Password.NewPassword", "New!Password123"), ("Password.ConfirmPassword", "different"));
        Assert.Contains("native-recovery-error", html);
        AssertGuidancePhase(html, "password", configured);
        await PostAsync("Reset", ("Password.NewPassword", "New!Password123"), ("Password.ConfirmPassword", "New!Password123"));
        Assert.Contains("native-recovery-error", html);
        AssertGuidancePhase(html, "password", configured);

        // Denied is also the public result for uncertain backend operations.
        factory.ResetService.Outcome = NativeRecoveryResetOutcome.Denied;
        await PostAsync("Reset", ("Password.NewPassword", "New!Password123"), ("Password.ConfirmPassword", "New!Password123"));
        Assert.Contains("native-recovery-error", html);
        AssertGuidancePhase(html, "start", configured);

        await PostAsync("Start", ("Identifier.Value", "unknown@example.invalid"));
        await PostAsync("Verify", ("Verification.Code", SyntheticCode));
        factory.ResetService.Outcome = NativeRecoveryResetOutcome.Succeeded;
        await PostAsync("Reset", ("Password.NewPassword", "New!Password123"), ("Password.ConfirmPassword", "New!Password123"));
        AssertGuidancePhase(html, "success", configured);
    }

    [Theory]
    [InlineData("https://help.example.invalid/guide?a=1&b=2", "Help", true)]
    [InlineData("http://help.example.invalid/guide", "Help", true)]
    [InlineData("javascript:alert(1)", "Help", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", "Help", false)]
    [InlineData("//help.example.invalid", "Help", false)]
    [InlineData("/help", "Help", false)]
    [InlineData("https://user:password@help.example.invalid", "Help", false)]
    [InlineData("https://user@help.example.invalid", "Help", false)]
    [InlineData("https://help.example.invalid", " ", false)]
    [InlineData("https://help.example.invalid", "@missing", false)]
    [InlineData("@SupportUrl", "Help", false)]
    [InlineData(" ", "Help", false)]
    public async Task Guidance_SupportLink_RequiresSafeIndependentUrlAndResolvedLabel(
        string url, string label, bool expectedLink)
    {
        await using var factory = await NativeRecoveryKestrelFactory.CreateGuidanceTestServerAsync(options =>
        {
            options.SupportText = "Independent support text";
            options.SupportUrl = url;
            options.SupportLabel = label;
        });
        var html = await factory.Client.GetStringAsync("/Account/ForgotPassword");
        Assert.Contains("Independent support text", html);
        Assert.Equal(expectedLink, html.Contains("data-test-id=\"native-recovery-support-link\""));
        if (expectedLink)
        {
            Assert.Contains($"href=\"{HtmlEncoder.Default.Encode(new Uri(url).AbsoluteUri)}\"", html);
        }
    }

    [Fact]
    public async Task Guidance_Resources_UseEnabledExactThenEnglishAndRefreshOnNextRequest()
    {
        await using var factory = await NativeRecoveryKestrelFactory.CreateGuidanceTestServerAsync(options =>
        {
            options.TopNotice = "@ Guidance.Top ";
            options.SupportText = "@Guidance.Support";
            options.SupportLabel = "@Guidance.Label";
            options.SupportUrl = "https://help.example.invalid";
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var exact = new Resource { Key = "Guidance.Top", Culture = "zh-TW", Value = "Exact <script>alert('x')</script> & text" };
        var fallback = new Resource { Key = "Guidance.Top", Culture = "en-US", Value = "English fallback" };
        db.Resources.AddRange(exact, fallback,
            new Resource { Key = "Guidance.Support", Culture = "zh-TW", Value = "Disabled support", IsEnabled = false },
            new Resource { Key = "Guidance.Support", Culture = "en-US", Value = "Support fallback" },
            new Resource { Key = "Guidance.Label", Culture = "en-US", Value = "<b>Help</b>" });
        await db.SaveChangesAsync();
        const string path = "/Account/ForgotPassword?culture=zh-TW&ui-culture=zh-TW";
        var html = await factory.Client.GetStringAsync(path);
        Assert.Contains(HtmlEncoder.Default.Encode(exact.Value), html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("Support fallback", html);
        Assert.Contains("&lt;b&gt;Help&lt;/b&gt;", html);
        Assert.DoesNotContain("English fallback", html);

        exact.Value = "Updated on next request";
        await db.SaveChangesAsync();
        Assert.Contains(exact.Value, await factory.Client.GetStringAsync(path));
        exact.IsEnabled = false;
        await db.SaveChangesAsync();
        Assert.Contains("English fallback", await factory.Client.GetStringAsync(path));
        fallback.IsEnabled = false;
        await db.SaveChangesAsync();
        Assert.DoesNotContain("native-recovery-top-notice", await factory.Client.GetStringAsync(path));
        exact.IsEnabled = true;
        exact.Value = " \t ";
        fallback.IsEnabled = true;
        await db.SaveChangesAsync();
        html = await factory.Client.GetStringAsync(path);
        Assert.DoesNotContain("native-recovery-top-notice", html);
        Assert.DoesNotContain("English fallback", html);
    }

    [Fact]
    public async Task Guidance_Literals_AreEncodedAndIdenticalForEveryIdentifier()
    {
        await using var factory = await NativeRecoveryKestrelFactory.CreateGuidanceTestServerAsync(ConfigureLiteralGuidance);
        string? baseline = null;
        foreach (var identifier in new[] { SyntheticIdentifier, "missing@example.invalid", "staff@example.invalid", "student@example.invalid" })
        {
            using var client = factory.CreateBrowserClient();
            var start = await client.GetStringAsync("/Account/ForgotPassword");
            using var response = await client.PostAsync("/Account/ForgotPassword?handler=Start",
                CreateForm(ExtractAntiforgeryToken(start), ("Identifier.Value", identifier)));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            AssertGuidancePhase(html, "code", true);
            Assert.DoesNotContain(identifier, html);
            Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
            var renderedGuidance = string.Join("\n", Regex.Matches(html,
                "<(?:p|a)[^>]+data-test-id=\"native-recovery-(?:top-notice|verification-tip|support-text|support-link)\"[^>]*>.*?</(?:p|a)>")
                .Select(match => match.Value));
            baseline ??= renderedGuidance;
            Assert.Equal(baseline, renderedGuidance);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("@ ")]
    [InlineData("@missing")]
    public async Task Guidance_EmptyOrUnresolvedSettings_RenderNoContainers(string setting)
    {
        await using var factory = await NativeRecoveryKestrelFactory.CreateGuidanceTestServerAsync(options =>
        {
            options.TopNotice = options.VerificationTip = options.ResetTip = options.SuccessReminder =
                options.SupportText = options.SupportLabel = setting;
            options.SupportUrl = "https://help.example.invalid";
        });
        AssertGuidancePhase(await factory.Client.GetStringAsync("/Account/ForgotPassword"), "start", false);
    }

    [Fact]
    [Trait("Category", "ExplicitLocalE2E")]
    public async Task Guidance_RealRazorSyntheticServices_BrowserCheck()
    {
        if (Environment.GetEnvironmentVariable("RUN_RECOVERY_GUIDANCE_BROWSER_E2E") != "1") return;
        var readyFile = RequireEnvironmentPath("NATIVE_RECOVERY_BROWSER_READY_FILE");
        var stopFile = RequireEnvironmentPath("NATIVE_RECOVERY_BROWSER_STOP_FILE");
        await using var factory = await NativeRecoveryKestrelFactory.CreateGuidanceAsync(options =>
        {
            if (Environment.GetEnvironmentVariable("RECOVERY_GUIDANCE_BROWSER_EMPTY") == "1") return;
            options.TopNotice = "@Guidance.Top";
            options.VerificationTip = "@Guidance.Verify";
            options.ResetTip = "@Guidance.Reset";
            options.SuccessReminder = "@Guidance.Success";
            options.SupportText = "@Guidance.Support";
            options.SupportLabel = "@Guidance.Label";
            options.SupportUrl = "https://help.example.invalid/recovery";
        });
        factory.ResetService.Outcome = NativeRecoveryResetOutcome.Succeeded;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var values = new[]
            {
                ("Top", "Have access to your recovery mailbox before you begin.", "開始之前，請確認您可以使用復原信箱。"),
                ("Verify", "Check your junk mail folder if the code has not arrived.", "如果尚未收到驗證碼，請檢查垃圾郵件資料夾。"),
                ("Reset", "Choose a unique password and follow the requirements below.", "請使用不重複的密碼，並遵循下方的密碼要求。"),
                ("Success", "Use your new password the next time you sign in.", "下次登入時，請使用您剛設定的新密碼。"),
                ("Support", "For general recovery guidance, contact your support team.", "若需要一般帳號復原說明，請聯絡您的支援團隊。"),
                ("Label", "Recovery help", "帳號復原說明")
            };
            foreach (var (key, en, zh) in values)
            {
                db.Resources.Add(new Resource { Key = "Guidance." + key, Culture = "en-US", Value = en });
                db.Resources.Add(new Resource { Key = "Guidance." + key, Culture = "zh-TW", Value = zh });
            }
            await db.SaveChangesAsync();
        }
        try
        {
            await File.WriteAllTextAsync(readyFile, new Uri(factory.Client.BaseAddress!, "/Account/ForgotPassword").AbsoluteUri);
            Assert.True(await WaitForFileAsync(stopFile, TimeSpan.FromMinutes(5)));
        }
        finally
        {
            TryDelete(readyFile);
            TryDelete(stopFile);
        }
    }

    private static void ConfigureLiteralGuidance(ForgotPasswordRecoveryOptions options)
    {
        options.TopNotice = "Top <img src=x onerror=alert(1)> & guidance";
        options.VerificationTip = "Verification guidance";
        options.ResetTip = "Reset guidance";
        options.SuccessReminder = "Success reminder";
        options.SupportText = "Support text";
        options.SupportLabel = "Support label";
        options.SupportUrl = "https://help.example.invalid";
    }

    private static void AssertGuidancePhase(string html, string phase, bool configured)
    {
        var activeMarker = phase switch
        {
            "code" => "native-recovery-code",
            "password" => "native-recovery-password",
            "success" => "native-recovery-success",
            _ => "native-recovery-identifier"
        };
        Assert.Contains($"data-test-id=\"{activeMarker}\"", html);
        foreach (var (slot, expected) in new[]
        {
            ("top-notice", configured), ("support", configured), ("support-link", configured),
            ("verification-tip", configured && phase == "code"),
            ("reset-tip", configured && phase == "password"),
            ("success-reminder", configured && phase == "success")
        })
        {
            Assert.Equal(expected, html.Contains($"data-test-id=\"native-recovery-{slot}\""));
        }
        if (!configured) return;
        var top = html.IndexOf("data-test-id=\"native-recovery-top-notice\"", StringComparison.Ordinal);
        var support = html.IndexOf("data-test-id=\"native-recovery-support\"", StringComparison.Ordinal);
        Assert.True(top < support);
        foreach (var marker in new[] { "native-recovery-error", "native-recovery-sent", "native-recovery-success" })
        {
            var index = html.IndexOf($"data-test-id=\"{marker}\"", StringComparison.Ordinal);
            if (index >= 0) Assert.True(index < top);
        }
        if (phase != "success")
        {
            Assert.True(top < html.IndexOf("<form", StringComparison.Ordinal));
            Assert.True(support > html.LastIndexOf("</form>", StringComparison.Ordinal));
        }
    }
}
