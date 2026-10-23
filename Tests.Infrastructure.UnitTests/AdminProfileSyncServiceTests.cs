using Core.Application;
using Core.Application.DTOs;
using Core.Application.Options;
using Core.Application.Ports;
using Core.Domain;
using Core.Domain.Entities;
using Infrastructure;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Tests.Infrastructure.UnitTests;

public sealed class AdminProfileSyncServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NeutralSync_ShouldBeFreshFillOnlyWithoutProofAndRejectChangedQuery(bool changeQuery)
    {
        using var f = new Fixture(neutral: true);
        await f.SeedAsync();
        f.Db.UserLogins.RemoveRange(f.Db.UserLogins);
        var user = await f.Db.Users.SingleAsync();
        user.LastName = "retained";
        f.Native.ApplyManual(user, new Dictionary<NativeField, string?> { [NativeField.FirstName] = "" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("ready", preview.Status);
        Assert.Empty(f.Db.ChangeTracker.Entries());
        Assert.Equal("manual_fixed", preview.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").Action);
        Assert.Equal("preserve", preview.Changes.Single(x => x.Target == "User" && x.Field == "LastName").Action);
        if (changeQuery)
        {
            f.QueryContext = f.QueryContext! with { Fingerprint = "changed" };
            Assert.Equal("preview_stale", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
                f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!))).Code);
            Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync()).Nickname);
        }
        else
        {
            await f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!);
            user = await f.Db.Users.AsNoTracking().SingleAsync();
            Assert.Equal("Source display", user.Nickname);
            Assert.Equal("", user.FirstName);
            Assert.Equal("retained", user.LastName);
            Assert.Contains("NeutralQuery", user.NativeFieldStateJson);
        }
        Assert.Empty(await f.Db.UserLogins.ToListAsync());
        Assert.Equal(2, f.Fetches);
    }

    [Fact]
    public async Task MappingSave_ShouldNotFetchAndShouldInvalidateUnchangedPreview()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        var count = f.Fetches;
        var config = await f.Native.GetMappingsAsync();
        await f.Native.SetMappingsAsync(new(config.Revision, config.Mappings, config.FirstNameFallback));
        Assert.Equal(count, f.Fetches);
        Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync()).Nickname);
        Assert.Equal("retained-profiles", (await f.Db.Persons.AsNoTracking().SingleAsync()).ProviderProfilesJson);
        Assert.Equal("preview_stale", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
            f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!))).Code);
        Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync()).Nickname);
    }

    [Fact]
    public async Task Mapping_ShouldRemapFreshManualPreviewAndApplyWhileRemainingFillOnly()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var user = await f.Db.Users.SingleAsync();
        user.Nickname = "retained user";
        await f.Db.SaveChangesAsync();
        var config = await f.Native.GetMappingsAsync();
        var rows = config.Mappings.Select(x => x.Field == NativeField.Nickname ? x with { SourceProperty = "givenName" } : x).ToArray();
        await f.Native.SetMappingsAsync(new(config.Revision, rows, config.FirstNameFallback));
        await f.Native.UpdateAsync(NativeFieldTarget.User, f.UserId, NativeField.Nickname, new()
        {
            Operation = NativeFieldOperation.AssignSource, SourceIdentity = f.Identity, AllowOverwrite = true,
            ExpectedRevision = Guid.Empty
        });
        f.Db.ChangeTracker.Clear();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Empty(f.Db.ChangeTracker.Entries());
        Assert.Equal("retained user", preview.Changes.Single(x => x.Target == "User" && x.Field == "Nickname").ProposedValue);
        Assert.Equal("Source first", preview.Changes.Single(x => x.Target == "Person" && x.Field == "Nickname").ProposedValue);
        await f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!);
        Assert.Equal("retained user", (await f.Db.Users.AsNoTracking().SingleAsync()).Nickname);
        Assert.Equal("Source first", (await f.Db.Persons.AsNoTracking().SingleAsync()).Nickname);
        Assert.Equal("retained-profiles", (await f.Db.Persons.AsNoTracking().SingleAsync()).ProviderProfilesJson);
    }

    [Fact]
    public async Task MappingChangeDuringFreshFetch_ShouldRejectManualApply()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        f.OnFetch = async () =>
        {
            var config = await f.Native.GetMappingsAsync();
            await f.Native.SetMappingsAsync(new(config.Revision, config.Mappings, config.FirstNameFallback));
        };
        Assert.Equal("preview_stale", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
            f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!))).Code);
        Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync()).Nickname);
    }

    [Fact]
    public async Task Preview_ShouldRemainDetachedAndNeverFlushProfileOrAudit()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("ready", preview.Status);
        Assert.NotNull(preview.PreviewToken);
        Assert.Empty(f.Db.ChangeTracker.Entries());
        Assert.Equal(0, await f.Db.SaveChangesAsync());
        var user = await f.Db.Users.AsNoTracking().SingleAsync();
        var person = await f.Db.Persons.AsNoTracking().SingleAsync();
        Assert.Null(user.Nickname);
        Assert.Null(person.FirstName);
        Assert.Null(user.NativeFieldStateJson);
        Assert.Null(person.NativeFieldStateJson);
        Assert.Empty(await f.Db.AuditEvents.ToListAsync());
        Assert.Equal(0, f.AuditCalls);
        Assert.DoesNotContain("subject", preview.PreviewToken);
    }

    [Fact]
    public async Task Apply_ShouldFillUserAndPersonIndependentlyAndPreserveSecurityData()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var user = await f.Db.Users.SingleAsync();
        user.FirstName = "Existing first";
        user.SecurityStamp = "retained-security-stamp";
        user.PasswordHash = "retained-hash";
        user.RecoveryCodes = "retained-recovery";
        var person = await f.Db.Persons.SingleAsync();
        f.Native.ApplyManual(person, new Dictionary<NativeField, string?> { [NativeField.LastName] = "" });
        await f.Db.SaveChangesAsync();
        await f.Native.UpdateAsync(NativeFieldTarget.User, f.UserId, NativeField.FirstName, new()
        {
            Operation = NativeFieldOperation.AssignSource, SourceIdentity = f.Identity, AllowOverwrite = true,
            ExpectedRevision = (await f.Native.GetTargetAsync(NativeFieldTarget.User, f.UserId)).Revision
        });
        f.Options.TrustedEmailProviderNamespaces = ["example.provider"];
        f.Source = f.Source with { EmailTrustOrigin = "PolicyTrusted" };
        f.Db.ChangeTracker.Clear();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("preserve", preview.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").Action);
        Assert.Equal("manual_fixed", preview.Changes.Single(x => x.Target == "Person" && x.Field == "LastName").Action);
        var result = await f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!);
        Assert.Equal("applied", result.Status);
        f.Db.ChangeTracker.Clear();
        user = await f.Db.Users.SingleAsync();
        person = await f.Db.Persons.SingleAsync();
        Assert.Equal("Existing first", user.FirstName);
        Assert.Equal("Source first", person.FirstName);
        Assert.Equal("", person.LastName);
        Assert.Equal("source@example.test", user.Email);
        Assert.Equal("SOURCE@EXAMPLE.TEST", user.NormalizedEmail);
        Assert.True(user.EmailConfirmed);
        Assert.Equal("account", user.UserName);
        Assert.Equal("retained-security-stamp", user.SecurityStamp);
        Assert.Equal("retained-hash", user.PasswordHash);
        Assert.Equal("retained-recovery", user.RecoveryCodes);
        Assert.Equal("retained-profiles", person.ProviderProfilesJson);
        Assert.False(user.EmailMfaEnabled);
        Assert.Equal("FirstName", Assert.Single(result.ChangedPersonFields.Where(x => x == "FirstName")));
        var entry = await f.Db.AuditEvents.SingleAsync();
        Assert.Contains("changedUserFields", entry.Details);
        Assert.Contains(f.PersonId.ToString(), entry.Details);
        Assert.DoesNotContain("Source first", entry.Details);
        Assert.DoesNotContain("source@example.test", entry.Details);
        Assert.DoesNotContain("subject", entry.Details);
        Assert.Equal("preview_stale", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
            f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!))).Code);
    }

    [Theory]
    [InlineData("missing", "binding_missing")]
    [InlineData("case", "binding_missing")]
    [InlineData("ambiguous", "binding_ambiguous")]
    [InlineData("person", "person_missing")]
    public async Task Preview_ShouldRequireExactlyOneExistingTupleAndLinkedPerson(string scenario, string reason)
    {
        using var f = new Fixture();
        await f.SeedAsync();
        if (scenario is "missing" or "case") f.Db.UserLogins.RemoveRange(await f.Db.UserLogins.ToListAsync());
        if (scenario == "case") f.Db.UserLogins.Add(new() { UserId = f.UserId, LoginProvider = "EXAMPLE.PROVIDER", ProviderKey = "subject" });
        if (scenario == "ambiguous") f.Db.UserLogins.Add(new() { UserId = f.UserId, LoginProvider = "example.provider", ProviderKey = "another" });
        if (scenario == "person") (await f.Db.Users.SingleAsync()).PersonId = null;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("blocked", preview.Status);
        Assert.Equal(reason, preview.Reason);
        Assert.Null(preview.PreviewToken);
        Assert.Equal(0, f.Fetches);
    }

    [Theory]
    [InlineData("disabled", "disabled")]
    [InlineData("proof", "source_disabled")]
    [InlineData("native", "source_disabled")]
    [InlineData("paused", "source_paused")]
    [InlineData("namespace", "source_namespace_mismatch")]
    [InlineData("endpoint", "configuration_invalid")]
    public async Task Preview_ShouldHonorIndependentDeploymentAndSourcePolicy(string scenario, string reason)
    {
        using var f = new Fixture();
        await f.SeedAsync();
        if (scenario == "disabled") f.Options.Enabled = false;
        if (scenario == "proof") f.Proof.Enabled = false;
        if (scenario == "native") f.NativeOptions.ProofEnabled = false;
        if (scenario == "paused") await f.Native.SetPausedAsync(NativeFieldSource.Proof, new(true, Guid.Empty));
        if (scenario == "namespace") f.Proof.TrustedProviderNamespace = "other.provider";
        if (scenario == "endpoint") f.Options.Endpoint = "http://example.test/native";
        var status = await f.Service.GetStatusAsync();
        Assert.False(status.Enabled);
        Assert.Equal(reason, status.Reason);
        Assert.Equal(reason, (await f.Service.PreviewAsync("actor", f.UserId)).Reason);
        Assert.Equal(0, f.Fetches);
    }

    [Fact]
    public async Task SharedPerson_ShouldRequireCompatibleExplicitOwnerAndNeverFillEmail()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var other = Guid.NewGuid();
        f.Db.Users.Add(new ApplicationUser { Id = other, PersonId = f.PersonId });
        f.Db.UserLogins.Add(new() { UserId = other, LoginProvider = "example.provider", ProviderKey = "other" });
        await f.Db.SaveChangesAsync();
        await f.Native.UpdateAsync(NativeFieldTarget.Person, f.PersonId, NativeField.Nickname, new()
        {
            Operation = NativeFieldOperation.AssignSource, SourceIdentity = f.Identity, ExpectedRevision = Guid.Empty
        });
        await f.Native.UpdateAsync(NativeFieldTarget.Person, f.PersonId, NativeField.FirstName, new()
        {
            Operation = NativeFieldOperation.AssignSource, SourceIdentity = f.Identity with { LocalAccountId = other, StableSubject = "other" },
            ExpectedRevision = (await f.Native.GetTargetAsync(NativeFieldTarget.Person, f.PersonId)).Revision
        });
        f.Db.ChangeTracker.Clear();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("fill", preview.Changes.Single(x => x.Target == "Person" && x.Field == "Nickname").Action);
        Assert.Equal("owner_mismatch", preview.Changes.Single(x => x.Target == "Person" && x.Field == "FirstName").Action);
        Assert.Equal("shared_person", preview.Changes.Single(x => x.Target == "Person" && x.Field == "Email").Action);
        await f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!);
        var person = await f.Db.Persons.AsNoTracking().SingleAsync();
        Assert.Equal("Source display", person.Nickname);
        Assert.Null(person.FirstName);
        Assert.Null(person.Email);
    }

    [Theory]
    [InlineData("Unknown", true, null, false, false, false)]
    [InlineData("SourceVerified", false, null, false, false, false)]
    [InlineData("SourceVerified", true, null, false, false, true)]
    [InlineData("PolicyTrusted", true, "source@example.test", false, false, true)]
    [InlineData("PolicyTrusted", true, "other@example.test", false, false, false)]
    [InlineData("PolicyTrusted", true, null, true, false, false)]
    [InlineData("PolicyTrusted", true, "source@example.test", true, false, false)]
    [InlineData("PolicyTrusted", true, null, false, true, true)]
    public async Task Email_ShouldRespectEvidenceLocalTrustExistingAddressAndMfa(string origin, bool locallyTrusted,
        string? email, bool mfa, bool previouslyConfirmed, bool expectedConfirmation)
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var user = await f.Db.Users.SingleAsync();
        user.Email = email;
        user.EmailMfaEnabled = mfa;
        user.EmailConfirmed = previouslyConfirmed;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.Options.TrustedEmailProviderNamespaces = locallyTrusted ? ["example.provider"] : ["EXAMPLE.PROVIDER"];
        f.Source = f.Source with { EmailTrustOrigin = origin };
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        await f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!);
        user = await f.Db.Users.AsNoTracking().SingleAsync();
        Assert.Equal(expectedConfirmation, user.EmailConfirmed);
        Assert.Equal(email ?? (mfa || previouslyConfirmed ? null : "source@example.test"), user.Email);
        Assert.Equal(mfa, user.EmailMfaEnabled);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("user")]
    [InlineData("person")]
    [InlineData("policy")]
    [InlineData("binding")]
    [InlineData("link")]
    [InlineData("shared")]
    [InlineData("mfa")]
    [InlineData("config")]
    public async Task Apply_ShouldRejectAnyReviewedSourceOrLocalStateDrift(string scenario)
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        if (scenario == "source") f.Source = f.Source with { Profile = f.Source.Profile with { GivenName = "Unseen" } };
        if (scenario == "user") (await f.Db.Users.SingleAsync()).FirstName = "Local edit";
        if (scenario == "person") (await f.Db.Persons.SingleAsync()).Email = "local@example.test";
        if (scenario == "policy") await f.Native.SetFirstNameFallbackAsync(NativeFieldSource.Proof, new("example.provider", true, Guid.Empty));
        if (scenario == "binding")
        {
            f.Db.UserLogins.RemoveRange(await f.Db.UserLogins.ToListAsync());
            f.Db.UserLogins.Add(new() { UserId = f.UserId, LoginProvider = "example.provider", ProviderKey = "replacement" });
            f.Source = f.Source with { StableSubject = "replacement" };
        }
        if (scenario == "link") (await f.Db.Users.SingleAsync()).PersonId = null;
        if (scenario == "shared") f.Db.Users.Add(new() { Id = Guid.NewGuid(), PersonId = f.PersonId });
        if (scenario == "mfa") (await f.Db.Users.SingleAsync()).EmailMfaEnabled = true;
        if (scenario == "config") f.Options.TrustedEmailProviderNamespaces = ["example.provider"];
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal("preview_stale", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
            f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!))).Code);
        Assert.Equal(0, f.AuditCalls);
        Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync(x => x.Id == f.UserId)).Nickname);
    }

    [Fact]
    public async Task Apply_ShouldRecheckLocalStateAfterUpstreamAwait()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        f.OnFetch = async () =>
        {
            var user = await f.Db.Users.SingleAsync();
            user.EmailMfaEnabled = true;
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
        };
        Assert.Equal("preview_stale", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
            f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!))).Code);
        Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync()).Email);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("expired")]
    [InlineData("tampered")]
    public async Task Apply_ShouldRejectWrongActorExpiredOrTamperedReceipt(string scenario)
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        if (scenario == "expired") f.Clock.Now = f.Clock.Now.AddMinutes(6);
        var token = scenario == "tampered" ? "not-a-receipt" : preview.PreviewToken!;
        Assert.Equal("preview_invalid", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
            f.Service.ApplyAsync(scenario == "actor" ? "different" : "actor", f.UserId, token))).Code);
        Assert.Equal(1, f.Fetches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_ShouldRollBackAndDiscardPendingWritesOnConcurrencyOrAuditFailure(bool auditFailure)
    {
        using var f = new Fixture();
        await f.SeedAsync();
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        f.FailAudit = auditFailure;
        f.Db.FailNextSave = !auditFailure;
        if (auditFailure)
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!));
        else
            Assert.Equal("preview_stale", (await Assert.ThrowsAsync<ProfileSyncException>(() =>
                f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!))).Code);
        Assert.Empty(f.Db.ChangeTracker.Entries());
        await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Db.AuditEvents.ToListAsync());
        Assert.Null((await f.Db.Users.AsNoTracking().SingleAsync()).Email);
        Assert.Null((await f.Db.Persons.AsNoTracking().SingleAsync()).FirstName);
    }

    [Fact]
    public async Task MissingSource_ShouldNotUseStoredLoginDisplayNameOrIssueApplyToken()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        f.Source = f.Source with { Profile = new() };
        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("no_changes", preview.Status);
        Assert.Null(preview.PreviewToken);
        Assert.All(preview.Changes, x => Assert.Equal("missing_source", x.Action));
    }

    [Fact]
    public async Task FirstNameFallback_ShouldUseFreshAssuredDisplayOnlyWhenConfigured()
    {
        using var f = new Fixture();
        await f.SeedAsync();
        f.Source = f.Source with { Profile = f.Source.Profile with { GivenName = null } };
        var off = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("missing_source", off.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").Action);
        await f.Native.SetFirstNameFallbackAsync(NativeFieldSource.Proof, new("example.provider", true, Guid.Empty));
        var on = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("Source display", on.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").ProposedValue);
        await f.Service.ApplyAsync("actor", f.UserId, on.PreviewToken!);
        Assert.Equal("Source display", (await f.Db.Users.AsNoTracking().SingleAsync()).FirstName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NeutralFirstNameFallback_ShouldUseOriginalAssuredDisplayOnlyWhenConfigured(string? givenName)
    {
        using var f = new Fixture(neutral: true);
        await f.SeedAsync();
        f.Source = f.Source with { Profile = f.Source.Profile with { GivenName = givenName, Surname = null } };
        var off = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("missing_source", off.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").Action);
        var config = await f.Native.GetMappingsAsync();
        var rows = config.Mappings.Select(x => x.Source == NativeFieldSource.NeutralQuery && x.Field == NativeField.Nickname
            ? x with { SourceProperty = "givenName" } : x).ToArray();
        await f.Native.SetMappingsAsync(new(config.Revision, rows, new("example.provider", true)));

        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.False(f.Db.ChangeTracker.HasChanges());
        Assert.Empty(f.Db.ChangeTracker.Entries<ApplicationUser>());
        Assert.Empty(f.Db.ChangeTracker.Entries<Person>());
        Assert.Equal("Source display", preview.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").ProposedValue);
        Assert.Equal("Source display", preview.Changes.Single(x => x.Target == "Person" && x.Field == "FirstName").ProposedValue);
        await f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!);
        var user = await f.Db.Users.AsNoTracking().SingleAsync();
        var person = await f.Db.Persons.AsNoTracking().SingleAsync();
        Assert.Equal("Source display", user.FirstName);
        Assert.Equal("Source display", person.FirstName);
        Assert.Null(user.LastName);
        Assert.Null(person.LastName);
        Assert.Null(user.Nickname);
    }

    [Fact]
    public async Task NeutralFirstNameFallback_ShouldPreferGivenNameAndPreserveExplicitMapping()
    {
        using var f = new Fixture(neutral: true);
        await f.SeedAsync();
        var config = await f.Native.GetMappingsAsync();
        await f.Native.SetMappingsAsync(new(config.Revision, config.Mappings, new("example.provider", true)));
        var given = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("Source first", given.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").ProposedValue);

        f.Source = f.Source with { Profile = f.Source.Profile with { GivenName = null } };
        config = await f.Native.GetMappingsAsync();
        var rows = config.Mappings.Select(x => x.Source == NativeFieldSource.NeutralQuery && x.Field == NativeField.FirstName
            ? x with { SourceProperty = "surname" } : x).ToArray();
        await f.Native.SetMappingsAsync(new(config.Revision, rows, config.FirstNameFallback));
        var remapped = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("Source surname", remapped.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").ProposedValue);
    }

    [Fact]
    public async Task NeutralFirstNameFallback_ShouldPreserveExistingAndManuallyFixedNames()
    {
        using var f = new Fixture(neutral: true);
        await f.SeedAsync();
        var config = await f.Native.GetMappingsAsync();
        await f.Native.SetMappingsAsync(new(config.Revision, config.Mappings, new("example.provider", true)));
        var user = await f.Db.Users.SingleAsync();
        user.FirstName = "Retained name";
        var person = await f.Db.Persons.SingleAsync();
        f.Native.ApplyManual(person, new Dictionary<NativeField, string?> { [NativeField.FirstName] = "" });
        await f.Db.SaveChangesAsync();
        await f.Native.UpdateAsync(NativeFieldTarget.User, f.UserId, NativeField.FirstName, new()
        {
            Operation = NativeFieldOperation.AssignSource, AllowOverwrite = true, ExpectedRevision = Guid.Empty,
            SourceIdentity = new(NativeFieldSource.NeutralQuery, f.UserId, "example.provider", string.Empty, QuerySourceId: "source")
        });
        f.Db.ChangeTracker.Clear();
        f.Source = f.Source with { Profile = f.Source.Profile with { GivenName = null } };

        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("preserve", preview.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").Action);
        Assert.Equal("manual_fixed", preview.Changes.Single(x => x.Target == "Person" && x.Field == "FirstName").Action);
        await f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!);
        Assert.Equal("Retained name", (await f.Db.Users.AsNoTracking().SingleAsync()).FirstName);
        Assert.Equal("", (await f.Db.Persons.AsNoTracking().SingleAsync()).FirstName);
    }

    [Fact]
    public async Task NeutralFirstNameFallback_ShouldNotClaimSharedPersonFields()
    {
        using var f = new Fixture(neutral: true);
        await f.SeedAsync();
        f.Db.Users.Add(new() { Id = Guid.NewGuid(), PersonId = f.PersonId, UserName = "other-account" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var config = await f.Native.GetMappingsAsync();
        await f.Native.SetMappingsAsync(new(config.Revision, config.Mappings, new("example.provider", true)));
        f.Source = f.Source with { Profile = f.Source.Profile with { GivenName = null } };

        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.Equal("fill", preview.Changes.Single(x => x.Target == "User" && x.Field == "FirstName").Action);
        Assert.Equal("shared_person", preview.Changes.Single(x => x.Target == "Person" && x.Field == "FirstName").Action);
        await f.Service.ApplyAsync("actor", f.UserId, preview.PreviewToken!);
        Assert.Equal("Source display", (await f.Db.Users.AsNoTracking().SingleAsync(x => x.Id == f.UserId)).FirstName);
        Assert.Null((await f.Db.Persons.AsNoTracking().SingleAsync()).FirstName);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("oversized")]
    public async Task NeutralFirstNameFallback_ShouldNotUseStoredOrOversizedDisplayName(string scenario)
    {
        using var f = new Fixture(neutral: true);
        await f.SeedAsync();
        var user = await f.Db.Users.SingleAsync();
        user.Nickname = "Stored name";
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var config = await f.Native.GetMappingsAsync();
        await f.Native.SetMappingsAsync(new(config.Revision, config.Mappings, new("example.provider", true)));
        f.Source = f.Source with { Profile = f.Source.Profile with
        {
            GivenName = null, DisplayName = scenario == "missing" ? null : new string('x', 101)
        } };

        var preview = await f.Service.PreviewAsync("actor", f.UserId);
        Assert.All(preview.Changes.Where(x => x.Field == "FirstName"), change => Assert.Equal("missing_source", change.Action));
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("paused")]
    public async Task NeutralFirstNameFallback_ShouldRequireAnAvailableExactNamespace(string scenario)
    {
        using var f = new Fixture(neutral: true);
        await f.SeedAsync();
        if (scenario == "paused") await f.Native.SetPausedAsync(NativeFieldSource.NeutralQuery, new(true, Guid.Empty));
        var config = await f.Native.GetMappingsAsync();
        var providerNamespace = scenario == "namespace" ? "EXAMPLE.PROVIDER" : "example.provider";
        var error = await Assert.ThrowsAsync<NativeFieldPolicyException>(() =>
            f.Native.SetMappingsAsync(new(config.Revision, config.Mappings, new(providerNamespace, true))));
        Assert.Equal("source_unavailable", error.Code);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class SyncDb(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        public bool FailNextSave { get; set; }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!FailNextSave) return base.SaveChangesAsync(cancellationToken);
            FailNextSave = false;
            throw new DbUpdateConcurrencyException();
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public SyncDb Db { get; }
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PersonId { get; } = Guid.NewGuid();
        public ProviderNativeProfileOptions Options { get; } = new()
        {
            Enabled = true, TrustedProviderNamespace = "example.provider", Endpoint = "https://example.test/native", SharedSecret = "fixture-secret"
        };
        public ProviderProofOptions Proof { get; } = new() { Enabled = true, TrustedProviderNamespace = "example.provider" };
        public NativeFieldPolicyOptions NativeOptions { get; } = new();
        public NativeFieldPolicyService Native { get; }
        public AdminProfileSyncService Service { get; }
        public ProviderNativeProfileResponse Source { get; set; } = new("1.0", "example.provider", "subject", new()
        {
            DisplayName = "Source display", GivenName = "Source first", Surname = "Source surname", Email = "source@example.test",
            Department = "Source department", Title = "Source title", EmployeeId = "employee-example",
            AssuredFields = Enum.GetValues<AssuredProfileField>()
        }, "Unknown");
        public NativeFieldSourceIdentity Identity => new(NativeFieldSource.Proof, UserId, "example.provider", "subject");
        public int Fetches { get; private set; }
        public int AuditCalls { get; private set; }
        public bool FailAudit { get; set; }
        public Func<Task>? OnFetch { get; set; }
        public Clock Clock { get; } = new();
        public NeutralQueryContext? QueryContext { get; set; }
        public Fixture(bool neutral = false)
        {
            _connection.Open();
            Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
            Db.Database.EnsureCreated();
            var queries = new Mock<INeutralQueryService>();
            if (neutral)
            {
                Proof.Enabled = false;
                QueryContext = new(UserId, PersonId, "source", new NeutralQueryRequest
                {
                    ContractType = "native-profile", ProviderNamespace = "example.provider",
                    Query = new(new Dictionary<string, string> { ["employeeId"] = "SYNTH-001" })
                }, "revision", Guid.NewGuid(), new(new Uri("https://example.test/native"), "synthetic-secret", TimeSpan.FromSeconds(5)), []);
                queries.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new QueryConfigurationStateDto(Guid.NewGuid(),
                    [new("native-profile", "source", "neutral-v2", "example.provider", [new("employeeId", "user.employeeId")])],
                    [new("native-profile", "source", "example.provider", true, null, [], [], null)]));
                queries.Setup(x => x.ResolveAsync("native-profile", UserId, null, null, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => new NeutralQueryResolution("Ready", QueryContext));
                queries.Setup(x => x.IsCurrentAsync(It.IsAny<NeutralQueryContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            }
            Native = new(Db, Microsoft.Extensions.Options.Options.Create(NativeOptions),
                Microsoft.Extensions.Options.Options.Create(Proof), Microsoft.Extensions.Options.Options.Create(new DirectoryIntegrationOptions()),
                neutral ? () => queries.Object : null);
            var client = new Mock<IProviderNativeProfileClient>();
            client.Setup(x => x.FetchAsync(It.IsAny<ProviderNativeProfileRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async () => { Fetches++; if (OnFetch is not null) await OnFetch(); return Source; });
            client.Setup(x => x.FetchNeutralAsync(It.IsAny<NeutralQueryContext>(), It.IsAny<CancellationToken>()))
                .Returns(async () => { Fetches++; if (OnFetch is not null) await OnFetch(); return new NeutralNativeProfileResult(Source.Profile, Source.EmailTrustOrigin); });
            var audit = new Mock<IAuditService>();
            audit.Setup(x => x.LogAdministrativeEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns<string, string, string, string?, CancellationToken>(
                async (eventType, _, _, details, ct) =>
                {
                    AuditCalls++;
                    Db.AuditEvents.Add(new() { EventType = eventType, UserId = "actor", Details = details });
                    await Db.SaveChangesAsync(ct);
                    if (FailAudit) throw new InvalidOperationException("fixture audit failure");
                });
            Service = new(Db, Native, client.Object, Microsoft.Extensions.Options.Options.Create(Options),
                new EphemeralDataProtectionProvider(), new UpperInvariantLookupNormalizer(), audit.Object, Clock, neutral ? queries.Object : null);
        }
        public async Task SeedAsync()
        {
            Db.Persons.Add(new() { Id = PersonId, ProviderProfilesJson = "retained-profiles" });
            Db.Users.Add(new() { Id = UserId, PersonId = PersonId, UserName = "account" });
            Db.UserLogins.Add(new() { UserId = UserId, LoginProvider = "example.provider", ProviderKey = "subject", ProviderDisplayName = "stale-binding-name" });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }
        public void Dispose() { Db.Dispose(); _connection.Dispose(); }
    }
}
