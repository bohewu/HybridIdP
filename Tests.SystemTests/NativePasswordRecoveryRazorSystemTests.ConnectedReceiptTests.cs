using System.Text.Json;

namespace Tests.SystemTests;

public sealed partial class NativePasswordRecoveryRazorSystemTests
{
    [Fact]
    public void EnterUnknownWriteBarrier_ShouldPersistReceiptBeforeDisablingKnownCleanup()
    {
        var root = CreateReceiptTestDirectory();
        try
        {
            var anchorPath = Path.Combine(root, "identity-anchor.json");
            var account = CreateReceiptBarrierAccount(root, anchorPath, cleanupAllowed: true);

            account.EnterUnknownWriteBarrier();

            using var anchor = JsonDocument.Parse(File.ReadAllText(anchorPath));
            Assert.Equal("write-outcome-unresolved", anchor.RootElement.GetProperty("stage").GetString());
            Assert.False(account.CleanupAllowedForTesting);
            Assert.False(File.Exists(anchorPath + ".tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnterUnknownWriteBarrier_ShouldPreserveKnownCleanupEligibility_WhenReceiptWriteFails()
    {
        var root = CreateReceiptTestDirectory();
        try
        {
            var anchorPath = Path.Combine(root, "identity-anchor.json");
            Directory.CreateDirectory(anchorPath);
            var account = CreateReceiptBarrierAccount(root, anchorPath, cleanupAllowed: true);
            var subsequentDispatchReached = false;

            var exception = Record.Exception(() =>
            {
                account.EnterUnknownWriteBarrier();
                subsequentDispatchReached = true;
            });

            Assert.NotNull(exception);
            Assert.False(subsequentDispatchReached);
            Assert.True(account.CleanupAllowedForTesting);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnterUnknownWriteBarrier_ShouldKeepCleanupDisabled_WhenPriorStateIsUnknown()
    {
        var root = CreateReceiptTestDirectory();
        try
        {
            var anchorPath = Path.Combine(root, "identity-anchor.json");
            Directory.CreateDirectory(anchorPath);
            var account = CreateReceiptBarrierAccount(root, anchorPath, cleanupAllowed: false);

            Assert.NotNull(Record.Exception(() => account.EnterUnknownWriteBarrier()));
            Assert.False(account.CleanupAllowedForTesting);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateReceiptTestDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "hybrididp-connected-receipt", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ConnectedDirectoryAccount CreateReceiptBarrierAccount(
        string root,
        string anchorPath,
        bool cleanupAllowed)
    {
        var phasePath = Path.Combine(root, "phase.json");
        var settings = new ConnectedDirectorySettings(
            "offline.invalid",
            1,
            "DC=offline",
            "OU=Managed,DC=offline",
            "OU=Active,OU=Managed,DC=offline",
            "OU=Quarantine,OU=Managed,DC=offline",
            "OFFLINE",
            "not-used",
            "not-used",
            "smoke-cred-offline",
            phasePath,
            anchorPath);
        return ConnectedDirectoryAccount.CreateForReceiptTesting(
            settings,
            new ConnectedReceiptStore(phasePath, anchorPath),
            cleanupAllowed);
    }
}
