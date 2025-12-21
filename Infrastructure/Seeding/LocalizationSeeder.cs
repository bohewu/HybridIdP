using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Seeding;

public static class LocalizationSeeder
{
    public static async Task SeedAsync(ApplicationDbContext context)
    {
        var resources = new[]
        {
            // English (en-US)
            new { Key = "scope.profile.display", Culture = "en-US", Value = "Access your profile information", Category = "Consent" },
            new { Key = "scope.profile.description", Culture = "en-US", Value = "This scope allows the application to access your basic profile information including name and email.", Category = "Consent" },
            new { Key = "scope.email.display", Culture = "en-US", Value = "Access your email address", Category = "Consent" },
            new { Key = "scope.email.description", Culture = "en-US", Value = "This scope allows the application to access your email address for communication purposes.", Category = "Consent" },
            new { Key = "scope.phone.display", Culture = "en-US", Value = "Access your phone number", Category = "Consent" },
            new { Key = "scope.phone.description", Culture = "en-US", Value = "This scope allows the application to access your phone number for verification and contact.", Category = "Consent" },
            new { Key = "scope.openid.display", Culture = "en-US", Value = "Verify your identity", Category = "Consent" },
            new { Key = "scope.openid.description", Culture = "en-US", Value = "This scope allows the application to verify who you are and link your sign-in to your account.", Category = "Consent" },
            new { Key = "scope.roles.display", Culture = "en-US", Value = "Access your roles", Category = "Consent" },
            new { Key = "scope.roles.description", Culture = "en-US", Value = "This scope allows the application to see which groups or roles you belong to.", Category = "Consent" },
            
            // Traditional Chinese (zh-TW)
            new { Key = "scope.profile.display", Culture = "zh-TW", Value = "存取您的個人資料", Category = "Consent" },
            new { Key = "scope.profile.description", Culture = "zh-TW", Value = "此範圍允許應用程式存取您的基本個人資料，包括姓名和電子郵件。", Category = "Consent" },
            new { Key = "scope.email.display", Culture = "zh-TW", Value = "存取您的電子郵件地址", Category = "Consent" },
            new { Key = "scope.email.description", Culture = "zh-TW", Value = "此範圍允許應用程式存取您的電子郵件地址以進行通訊。", Category = "Consent" },
            new { Key = "scope.phone.display", Culture = "zh-TW", Value = "存取您的電話號碼", Category = "Consent" },
            new { Key = "scope.phone.description", Culture = "zh-TW", Value = "此範圍允許應用程式存取您的電話號碼以進行驗證和聯絡。", Category = "Consent" },
            new { Key = "scope.openid.display", Culture = "zh-TW", Value = "驗證您的身分", Category = "Consent" },
            new { Key = "scope.openid.description", Culture = "zh-TW", Value = "此範圍允許應用程式驗證您的身分，並將您的登入與帳號連結。", Category = "Consent" },
            new { Key = "scope.roles.display", Culture = "zh-TW", Value = "存取您的角色", Category = "Consent" },
            new { Key = "scope.roles.description", Culture = "zh-TW", Value = "此範圍允許應用程式查看您所屬的群組或角色。", Category = "Consent" }
        };

        foreach (var res in resources)
        {
            var existing = await context.Resources
                .FirstOrDefaultAsync(r => r.Key == res.Key && r.Culture == res.Culture);

            if (existing == null)
            {
                context.Resources.Add(new Resource
                {
                    Key = res.Key,
                    Culture = res.Culture,
                    Value = res.Value,
                    Category = res.Category,
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                });
            }
            else if (existing.Value != res.Value)
            {
                existing.Value = res.Value;
                existing.UpdatedUtc = DateTime.UtcNow;
            }
        }

        await context.SaveChangesAsync();
    }
}
