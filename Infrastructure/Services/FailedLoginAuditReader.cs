using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

/// <summary>Reads retained failed sign-in audits without requiring a local user.</summary>
public static class FailedLoginAuditReader
{
    /// <summary>Streams each supported audit once in the UTC interval [startUtc, endUtc).</summary>
    public static async IAsyncEnumerable<FailedLoginAuditResult> ReadAsync(
        ApplicationDbContext db,
        DateTime startUtc,
        DateTime endUtc,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var candidates = db.AuditEvents.AsNoTracking()
            .Where(e => e.Timestamp >= startUtc && e.Timestamp < endUtc &&
                (e.EventType == "LoginAttempt" || e.EventType == "UserLogin"))
            .Select(e => new { e.Id, e.EventType, e.Details, e.UserId, e.Timestamp, e.IPAddress, e.UserAgent });

        await foreach (var candidate in candidates.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (IsFailure(candidate.EventType, candidate.Details))
            {
                yield return new FailedLoginAuditResult(candidate.Id, candidate.UserId,
                    candidate.Timestamp, candidate.IPAddress, candidate.UserAgent);
            }
        }
    }

    public static bool IsFailure(string eventType, string? details)
    {
        if (details is null || (eventType != "LoginAttempt" && eventType != "UserLogin"))
            return false;

        if (eventType == "LoginAttempt" && IsTextFailure(details))
            return true;

        try
        {
            using var document = JsonDocument.Parse(details);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (eventType == "UserLogin")
                return root.TryGetProperty("Success", out var success) && success.ValueKind == JsonValueKind.False;

            // AuditService.AddImpersonationDetails wraps text once; it preserves JSON object fields.
            return root.TryGetProperty("impersonation", out var attribution) &&
                attribution.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("details", out var original) && original.ValueKind == JsonValueKind.String &&
                IsTextFailure(original.GetString()!);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsTextFailure(string details)
    {
        if (!details.StartsWith("Login attempt for user '", StringComparison.Ordinal))
            return false;

        // Read the outcome after the username, never a failure word inside the username.
        var separator = details.LastIndexOf("': ", StringComparison.Ordinal);
        return separator >= "Login attempt for user '".Length &&
            details.AsSpan(separator + 3).StartsWith("failed (", StringComparison.Ordinal) &&
            details.EndsWith(')');
    }
}

public sealed record FailedLoginAuditResult(
    int Id, string? UserId, DateTime Timestamp, string? IpAddress, string? UserAgent);
