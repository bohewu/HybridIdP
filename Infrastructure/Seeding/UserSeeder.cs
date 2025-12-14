using Core.Application.Utilities;
using Core.Domain.Constants;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Seeding;

public static class UserSeeder
{
    public static async Task SeedAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext context,
        bool seedTestUsers)
    {
        // Seed Standard Admin
        await SeedAdminUserAsync(userManager, roleManager, context);

        if (seedTestUsers)
        {
            await SeedApplicationManagerTestUserAsync(userManager, roleManager, context);
            await SeedMultiRoleTestUserAsync(userManager, roleManager, context);
            await SeedStandardTestUserAsync(userManager, context);
            await SeedLegacyTestUserAsync(userManager, context);
            await SeedInactiveUserAsync(userManager, context);
            await SeedInactivePersonUserAsync(userManager, context);
            await SeedAbnormalLoginTestUserAsync(userManager, context);
        }
    }

    private static async Task SeedAdminUserAsync(
        UserManager<ApplicationUser> userManager, 
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext context)
    {
        var adminUser = await userManager.FindByEmailAsync(AuthConstants.DefaultAdmin.Email);
        
        if (adminUser == null)
        {
            // Phase 10.6.2: Create Person entity first with default NationalId
            var adminPerson = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "System",
                LastName = "Administrator",
                NationalId = PidHasher.Hash("A123456789"), // Default admin National ID (Taiwan format) - stored as SHA256 hash
                IdentityDocumentType = IdentityDocumentTypes.NationalId,
                IdentityVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Status = PersonStatus.Active,
                StartDate = DateTime.UtcNow
            };
            
            context.Persons.Add(adminPerson);
            await context.SaveChangesAsync();

            adminUser = new ApplicationUser
            {
                UserName = AuthConstants.DefaultAdmin.Email,
                Email = AuthConstants.DefaultAdmin.Email,
                EmailConfirmed = true,
                PersonId = adminPerson.Id // Link admin user to Person entity
            };

            var result = await userManager.CreateAsync(adminUser, AuthConstants.DefaultAdmin.Password);

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, AuthConstants.Roles.Admin);
                
                // Update Person.CreatedBy after user is created
                adminPerson.CreatedBy = adminUser.Id;
                adminPerson.IdentityVerifiedBy = adminUser.Id;
                await context.SaveChangesAsync();
            }
        }
        else
        {
            // Phase 10.6.2: Ensure Person exists and is linked
            if (adminUser.PersonId == null)
            {
                var existingPerson = await context.Persons
                    .FirstOrDefaultAsync(p => p.NationalId == PidHasher.Hash("A123456789"));
                
                if (existingPerson == null)
                {
                    // Create Person for existing admin
                    var adminPerson = new Person
                    {
                        Id = Guid.NewGuid(),
                        FirstName = "System",
                        LastName = "Administrator",
                        NationalId = PidHasher.Hash("A123456789"),
                        IdentityDocumentType = IdentityDocumentTypes.NationalId,
                        IdentityVerifiedAt = DateTime.UtcNow,
                        CreatedBy = adminUser.Id,
                        IdentityVerifiedBy = adminUser.Id,
                        CreatedAt = DateTime.UtcNow,
                        Status = PersonStatus.Active,
                        StartDate = DateTime.UtcNow
                    };
                    
                    context.Persons.Add(adminPerson);
                    adminUser.PersonId = adminPerson.Id;
                    await context.SaveChangesAsync();
                }
                else
                {
                    // Link existing Person to admin user
                    adminUser.PersonId = existingPerson.Id;
                    
                    // Fix existing person status if needed
                    if (existingPerson.Status != PersonStatus.Active)
                    {
                        existingPerson.Status = PersonStatus.Active;
                        if (!existingPerson.StartDate.HasValue) existingPerson.StartDate = DateTime.UtcNow;
                    }
                    
                    await context.SaveChangesAsync();
                }
            }
            else
            {
                // Ensure existing linked person is Active (Fix for Phase 18 migration on existing DBs)
                var person = await context.Persons.FindAsync(adminUser.PersonId);
                if (person != null && person.Status != PersonStatus.Active)
                {
                    person.Status = PersonStatus.Active;
                    if (!person.StartDate.HasValue) person.StartDate = DateTime.UtcNow;
                    await context.SaveChangesAsync();
                }
            }
            
            // CRITICAL: Ensure Admin role is assigned (handles case where AspNetUserRoles was cleared but user exists)
            if (!await userManager.IsInRoleAsync(adminUser, AuthConstants.Roles.Admin))
            {
                await userManager.AddToRoleAsync(adminUser, AuthConstants.Roles.Admin);
            }
        }
    }

    private static async Task SeedApplicationManagerTestUserAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext context)
    {
        const string email = "appmanager@hybridauth.local";
        const string password = "AppManager@123";
        
        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser != null)
        {
            return; // User already exists
        }

        // Check if Person already exists
        var nationalIdHash = PidHasher.Hash("B987654321");
        var existingPerson = await context.Persons.FirstOrDefaultAsync(p => p.NationalId == nationalIdHash);
        
        Person person;
        if (existingPerson != null)
        {
            person = existingPerson;
        }
        else
        {
            // Create Person entity first (required for ownership tracking)
            person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "App",
                LastName = "Manager",
                Email = email,
                NationalId = nationalIdHash,
                IdentityDocumentType = IdentityDocumentTypes.NationalId,
                IdentityVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Status = PersonStatus.Active,
                StartDate = DateTime.UtcNow
            };
            
            context.Persons.Add(person);
            await context.SaveChangesAsync();
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PersonId = person.Id,
            FirstName = "App",
            LastName = "Manager",
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, AuthConstants.Roles.ApplicationManager);
            
            // Update Person.CreatedBy
            person.CreatedBy = user.Id;
            person.IdentityVerifiedBy = user.Id;
            await context.SaveChangesAsync();
        }
    }

    private static async Task SeedMultiRoleTestUserAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext context)
    {
        const string email = "multitest@hybridauth.local";
        const string password = "MultiTest@123";
        
        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser != null)
        {
            return; // User already exists
        }

        // Check if Person already exists
        var nationalIdHash = PidHasher.Hash("C123456789");
        var existingPerson = await context.Persons.FirstOrDefaultAsync(p => p.NationalId == nationalIdHash);
        
        Person person;
        if (existingPerson != null)
        {
            person = existingPerson;
        }
        else
        {
            // Create Person entity
            person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "Multi",
                LastName = "Test",
                Email = email,
                NationalId = nationalIdHash,
                IdentityDocumentType = IdentityDocumentTypes.NationalId,
                IdentityVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Status = PersonStatus.Active,
                StartDate = DateTime.UtcNow
            };
            
            context.Persons.Add(person);
            await context.SaveChangesAsync();
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PersonId = person.Id,
            FirstName = "Multi",
            LastName = "Test",
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            // Assign both Admin and User roles for role switching tests
            await userManager.AddToRoleAsync(user, AuthConstants.Roles.Admin);
            await userManager.AddToRoleAsync(user, AuthConstants.Roles.User);
            
            // Update Person.CreatedBy
            person.CreatedBy = user.Id;
            person.IdentityVerifiedBy = user.Id;
            await context.SaveChangesAsync();
        }
    }

    private static async Task SeedStandardTestUserAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        const string email = "testuser@hybridauth.local";
        const string password = "Test@123";

        var existingUser = await userManager.FindByEmailAsync(email);
    if (existingUser != null)
    {
        return;
    }

        // Check if Person already exists
        var nationalIdHash = PidHasher.Hash("T123456789");
        var existingPerson = await context.Persons.FirstOrDefaultAsync(p => p.NationalId == nationalIdHash);
        
        Person person;
        if (existingPerson != null)
        {
            person = existingPerson;
        }
        else
        {
            // Create Person
            person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "Test",
                LastName = "User",
                Email = email,
                NationalId = nationalIdHash,
                IdentityDocumentType = IdentityDocumentTypes.NationalId,
                IdentityVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Status = PersonStatus.Active,
                StartDate = DateTime.UtcNow
            };

            context.Persons.Add(person);
            await context.SaveChangesAsync();
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PersonId = person.Id,
            FirstName = "Test",
            LastName = "User",
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            // Update created by
            person.CreatedBy = user.Id;
            person.IdentityVerifiedBy = user.Id;
            await context.SaveChangesAsync();
        }
    }

    private static async Task SeedLegacyTestUserAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        const string username = "legacy_test";
        const string password = "Legacy@123";

        var existingUser = await userManager.FindByNameAsync(username);
        if (existingUser != null) return;

        // Legacy user might not have a Person entity initially (legacy data simulation)
        // Check if Person already exists just in case
        var nationalIdHash = PidHasher.Hash("L123456789");
        var existingPerson = await context.Persons.FirstOrDefaultAsync(p => p.NationalId == nationalIdHash);
        
        Person person;
        if (existingPerson != null)
        {
            person = existingPerson;
        }
        else
        {
            person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "Legacy",
                LastName = "User",
                Email = "legacy@test.local",
                NationalId = nationalIdHash,
                IdentityDocumentType = IdentityDocumentTypes.NationalId,
                IdentityVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Status = PersonStatus.Active,
                StartDate = DateTime.UtcNow
            };
            context.Persons.Add(person);
            await context.SaveChangesAsync();
        }

        var user = new ApplicationUser
        {
            UserName = username,
            Email = "legacy@test.local", // Different from username to test legacy login by username
            EmailConfirmed = true,
            PersonId = person.Id,
            FirstName = "Legacy",
            LastName = "User",
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            person.CreatedBy = user.Id;
            person.IdentityVerifiedBy = user.Id;
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Seeds a test user with IsActive = false for testing deactivated account login blocking.
    /// </summary>
    private static async Task SeedInactiveUserAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        const string email = "inactive@hybridauth.local";
        const string password = "Inactive@123";

        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser != null) return;

        var nationalIdHash = PidHasher.Hash("I123456789");
        var existingPerson = await context.Persons.FirstOrDefaultAsync(p => p.NationalId == nationalIdHash);
        
        Person person;
        if (existingPerson != null)
        {
            person = existingPerson;
        }
        else
        {
            person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "Inactive",
                LastName = "User",
                Email = email,
                NationalId = nationalIdHash,
                IdentityDocumentType = IdentityDocumentTypes.NationalId,
                IdentityVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Status = PersonStatus.Active, // Person is active, but User is not
                StartDate = DateTime.UtcNow
            };
            context.Persons.Add(person);
            await context.SaveChangesAsync();
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PersonId = person.Id,
            FirstName = "Inactive",
            LastName = "User",
            IsActive = false // User account is deactivated
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            person.CreatedBy = user.Id;
            person.IdentityVerifiedBy = user.Id;
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Seeds a test user whose linked Person has Status = Suspended for testing person inactive login blocking.
    /// </summary>
    private static async Task SeedInactivePersonUserAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        const string email = "inactiveperson@hybridauth.local";
        const string password = "InactivePerson@123";

        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser != null) return;

        var nationalIdHash = PidHasher.Hash("P123456789");
        var existingPerson = await context.Persons.FirstOrDefaultAsync(p => p.NationalId == nationalIdHash);
        
        Person person;
        if (existingPerson != null)
        {
            person = existingPerson;
        }
        else
        {
            person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "InactivePerson",
                LastName = "User",
                Email = email,
                NationalId = nationalIdHash,
                IdentityDocumentType = IdentityDocumentTypes.NationalId,
                IdentityVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Status = PersonStatus.Suspended, // Person is suspended - cannot login
                StartDate = DateTime.UtcNow
            };
            context.Persons.Add(person);
            await context.SaveChangesAsync();
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PersonId = person.Id,
            FirstName = "InactivePerson",
            LastName = "User",
            IsActive = true // User is active, but Person is suspended
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            person.CreatedBy = user.Id;
            person.IdentityVerifiedBy = user.Id;
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Seeds a test user with pre-existing LoginHistory records from a "known" IP.
    /// When BlockAbnormalLogin is enabled, logging in from a "different" IP should be blocked.
    /// Note: In System Tests, the client IP is typically ::1, so we seed history with a different IP (192.168.1.100)
    /// to make ::1 appear as "abnormal".
    /// </summary>
    private static async Task SeedAbnormalLoginTestUserAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context)
    {
        const string email = "abnormal@hybridauth.local";
        const string password = "Abnormal@123";

        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser != null)
        {
            // Reset history for test user to ensure clean state (remove any ::1 from previous runs)
            var history = context.LoginHistories.Where(h => h.UserId == existingUser.Id);
            context.LoginHistories.RemoveRange(history);
            await context.SaveChangesAsync();

            // Seed LoginHistory for existing user
            await SeedAbnormalLoginHistoryAsync(context, existingUser.Id);
            return;
        }

        var nationalIdHash = PidHasher.Hash("A987654321");
        var existingPerson = await context.Persons.FirstOrDefaultAsync(p => p.NationalId == nationalIdHash);
        
        Person person;
        if (existingPerson != null)
        {
            person = existingPerson;
        }
        else
        {
            person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = "Abnormal",
                LastName = "LoginTest",
                Email = email,
                NationalId = nationalIdHash,
                IdentityDocumentType = IdentityDocumentTypes.NationalId,
                IdentityVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                Status = PersonStatus.Active,
                StartDate = DateTime.UtcNow
            };
            context.Persons.Add(person);
            await context.SaveChangesAsync();
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PersonId = person.Id,
            FirstName = "Abnormal",
            LastName = "LoginTest",
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            person.CreatedBy = user.Id;
            person.IdentityVerifiedBy = user.Id;
            await context.SaveChangesAsync();

            // Seed LoginHistory records with a "known" IP
            await SeedAbnormalLoginHistoryAsync(context, user.Id);
        }
    }

    /// <summary>
    /// Seeds LoginHistory records for abnormal login testing.
    /// All records use IP 192.168.1.100, so when test logs in from ::1, it appears as "new/unknown" IP.
    /// </summary>
    private static async Task SeedAbnormalLoginHistoryAsync(ApplicationDbContext context, Guid userId)
    {
        const string knownIp = "192.168.1.100";
        var now = DateTime.UtcNow;

        // Seed 10 successful logins from the "known" IP
        for (int i = 0; i < 10; i++)
        {
            context.LoginHistories.Add(new LoginHistory
            {
                UserId = userId,
                LoginTime = now.AddDays(-i - 1), // Past logins
                IpAddress = knownIp,
                UserAgent = "Seeded Test Agent",
                IsSuccessful = true,
                RiskScore = 0,
                IsFlaggedAbnormal = false
            });
        }
        await context.SaveChangesAsync();
    }
}
