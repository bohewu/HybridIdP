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
            new { Key = "scope.profile.display", Culture = "en-US", Value = "Access to your profile information", Category = "Consent" },
            new { Key = "scope.profile.description", Culture = "en-US", Value = "This scope allows the application to access your basic profile information including name and email.", Category = "Consent" },
            new { Key = "scope.email.display", Culture = "en-US", Value = "Access to your email address", Category = "Consent" },
            new { Key = "scope.email.description", Culture = "en-US", Value = "This scope allows the application to access your email address for communication purposes.", Category = "Consent" },
            new { Key = "scope.phone.display", Culture = "en-US", Value = "Access to your phone number", Category = "Consent" },
            new { Key = "scope.phone.description", Culture = "en-US", Value = "This scope allows the application to access your phone number for verification and contact.", Category = "Consent" },
            
            // Traditional Chinese (zh-TW)
            new { Key = "scope.profile.display", Culture = "zh-TW", Value = "存取您的個人資料", Category = "Consent" },
            new { Key = "scope.profile.description", Culture = "zh-TW", Value = "此範圍允許應用程式存取您的基本個人資料，包括姓名和電子郵件。", Category = "Consent" },
            new { Key = "scope.email.display", Culture = "zh-TW", Value = "存取您的電子郵件地址", Category = "Consent" },
            new { Key = "scope.email.description", Culture = "zh-TW", Value = "此範圍允許應用程式存取您的電子郵件地址以進行通訊。", Category = "Consent" },
            new { Key = "scope.phone.display", Culture = "zh-TW", Value = "存取您的電話號碼", Category = "Consent" },
            new { Key = "scope.phone.description", Culture = "zh-TW", Value = "此範圍允許應用程式存取您的電話號碼以進行驗證和聯絡。", Category = "Consent" }
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
