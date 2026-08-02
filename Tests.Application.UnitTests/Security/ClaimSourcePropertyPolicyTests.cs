using System.Security.Cryptography;
using Core.Application.Security;
using Core.Domain;
using Core.Domain.Entities;

namespace Tests.Application.UnitTests.Security;

public class ClaimSourcePropertyPolicyTests
{
    [Fact]
    public void TryResolve_AllowedPaths_ResolvesEveryApprovedProfileSource()
    {
        var personId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "allowed-source-user",
            Email = "allowed-source@example.test",
            EmailConfirmed = true,
            PhoneNumber = "+886900000000",
            PhoneNumberConfirmed = true,
            FirstName = "Allowed",
            MiddleName = "Profile",
            LastName = "Source",
            Nickname = "Allowlisted",
            Department = "Engineering",
            JobTitle = "Engineer",
            ProfileUrl = "https://example.test/profile",
            PictureUrl = "https://example.test/picture",
            Website = "https://example.test",
            Address = "{}",
            Birthdate = "2000-01-01",
            Gender = "unspecified",
            TimeZone = "Asia/Taipei",
            Locale = "zh-TW",
            EmployeeId = "employee-1",
            PersonId = personId,
            Person = new Person
            {
                Id = personId,
                Email = "person@example.test",
                PhoneNumber = "+886911111111",
                FirstName = "Person",
                MiddleName = "Profile",
                LastName = "Source",
                Nickname = "PersonNickname",
                EmployeeId = "person-employee-1",
                Department = "Research",
                JobTitle = "Researcher",
                ProfileUrl = "https://example.test/person/profile",
                PictureUrl = "https://example.test/person/picture",
                Website = "https://example.test/person",
                Address = "{}",
                Birthdate = "2000-02-02",
                Gender = "unspecified",
                TimeZone = "Asia/Taipei",
                Locale = "zh-TW",
                NationalId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            }
        };

        foreach (var path in ClaimSourcePropertyPolicy.AllowedPaths)
        {
            Assert.True(
                ClaimSourcePropertyPolicy.TryResolve(user, path, out var value),
                $"Approved claim source '{path}' must resolve through the policy.");
            Assert.False(
                string.IsNullOrEmpty(value),
                $"Test data for approved claim source '{path}' must be populated.");
        }
    }

    [Theory]
    [InlineData(nameof(ApplicationUser.PasswordHash))]
    [InlineData(nameof(ApplicationUser.SecurityStamp))]
    [InlineData(nameof(ApplicationUser.ConcurrencyStamp))]
    [InlineData(nameof(ApplicationUser.PasswordHistory))]
    [InlineData(nameof(ApplicationUser.LastTotpValidatedWindow))]
    [InlineData(nameof(ApplicationUser.EmailMfaCode))]
    [InlineData(nameof(ApplicationUser.EmailMfaCodeExpiry))]
    [InlineData(nameof(ApplicationUser.EmailMfaVerificationAttempts))]
    [InlineData(nameof(ApplicationUser.RecoveryCodes))]
    [InlineData("Person.Accounts")]
    public void TryResolve_SecurityOrInternalPath_IsRejected(string path)
    {
        var user = new ApplicationUser();

        var resolved = ClaimSourcePropertyPolicy.TryResolve(user, path, out var value);

        Assert.False(resolved);
        Assert.Null(value);
    }

    [Fact]
    public void TryResolve_WhitespaceAroundSegments_NormalizesApprovedPath()
    {
        var user = new ApplicationUser
        {
            Person = new Person { FirstName = "Test" }
        };

        var resolved = ClaimSourcePropertyPolicy.TryResolve(
            user,
            " Person . FirstName ",
            out var value);

        Assert.True(resolved);
        Assert.Equal("Test", value);
    }
}
