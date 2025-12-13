import { test, expect } from '@playwright/test';

test.describe('Client-Side Password Rules Verification', () => {
  test.beforeEach(async ({ page }) => {
    // 1. Login as admin
    await page.goto('https://localhost:7035/Account/Login');
    await page.fill('#Input_Login', 'admin@hybridauth.local');
    await page.fill('#Input_Password', 'Admin@123');
    await page.click('button.auth-btn-primary');
    await page.waitForSelector('.user-name, .user-info-name', { timeout: 20000 });

    // 2. Set strict policy to ensure deterministic testing
    // MinCharacterTypes = 3
    await page.evaluate(async () => {
        const policy = {
            minPasswordLength: 6,
            minCharacterTypes: 3,
            requireUppercase: true,
            requireLowercase: true,
            requireDigit: true,
            requireNonAlphanumeric: false,
            // defaults for others
            passwordHistoryCount: 0,
            passwordExpirationDays: 0,
            minPasswordAgeDays: 0,
            maxFailedAccessAttempts: 5,
            lockoutDurationMinutes: 15,
            abnormalLoginHistoryCount: 10,
            blockAbnormalLogin: false,
            allowSelfPasswordChange: true
        };
        await fetch('/api/admin/security/policies', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(policy)
        });
    });
  });

  test('should update password requirements in real-time', async ({ page }) => {
    await page.goto('https://localhost:7035/Account/Profile');
    
    // Wait for the form
    const form = page.locator('[data-testid="change-password-form"]');
    await expect(form).toBeVisible();

    // Type a password that has 2 types (Lower + Digit) -> 'password1'
    await page.fill('#newPassword', 'password1');

    // Debug: print all requirement texts
    const allReqs = await form.locator('.grid.grid-cols-2 > div').allInnerTexts();
    console.log('Found requirements:', allReqs);

    // Find the requirement line about character types
    // Regex matches "At least \d+ character types" case insensitive
    const typeReqText = allReqs.find(t => /character types/i.test(t));
    expect(typeReqText, 'Character types requirement should be present').toBeDefined();
    
    // Determine the number required from the text
    const requiredCountMatch = typeReqText!.match(/(\d+)/);
    const requiredCount = requiredCountMatch ? parseInt(requiredCountMatch[0]) : 0;
    console.log(`Requirement text: "${typeReqText}", Required types: ${requiredCount}`);

    // Text changes between valid/invalid, so use index
    const reqIndex = allReqs.indexOf(typeReqText!);
    const reqItem = form.locator('.grid.grid-cols-2 > div').nth(reqIndex);

    // Check validity state
    // 'password1' has 2 types (lower, digit).
    if (requiredCount > 2) {
        // Should be invalid
        await expect(reqItem).toHaveClass(/text-gray-500/);
        await expect(reqItem.locator('i')).toHaveClass(/bi-circle/);
    } else {
         // If policy is 2, it should be valid
        await expect(reqItem).toHaveClass(/text-green-600/);
        await expect(reqItem.locator('i')).toHaveClass(/bi-check-circle-fill/);
    }

    // Now type a strong password to verify green state
    const strongPass = 'Password1!'; // 4 types
    await page.fill('#newPassword', strongPass);
    
    // Should become green regardless of policy (unless max types > 4 which is impossible)
    await expect(reqItem).toHaveClass(/text-green-600/);
    await expect(reqItem.locator('i')).toHaveClass(/bi-check-circle-fill/);
  });
});
