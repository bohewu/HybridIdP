import { test, expect } from '@playwright/test';
import { spawn } from 'child_process';
import * as path from 'path';
import { loginAsUser } from './helpers/auth-helper';

// Fix __dirname for ESM if needed, or ignore if CommonJS is target
// But for now let's use relative path from cwd or assume __dirname is available via node types
// If __dirname is missing in type check, we can define it or use process.cwd()
const __dirname = path.resolve(); 

test.describe('Device Flow E2E', () => {
    test('should complete device flow using TestClient.Device', async ({ page }) => {
        test.setTimeout(60000); // Give it time

        // __dirname is e2e/ relative to root if running from there, or e2e/tests depending on how we resolved it.
        // process.cwd() is usually c:\repos\HybridIdP\e2e when running `npx playwright test`
        // TestClient.Device is at c:\repos\HybridIdP\TestClient.Device
        // So we need to go up one level from e2e.
        const clientProject = path.resolve(process.cwd(), '../TestClient.Device');
        
        // Spawn the console client
        console.log(`Starting TestClient.Device in ${clientProject}`);
        
        // We use 'dotnet run'
        const clientProcess = spawn('dotnet', ['run', '--', '--no-browser'], {
            cwd: clientProject,
            shell: true
        });

        let userCode = '';
        let verificationUri = '';
        let accessToken = '';
        let outputBuffer = '';

        // Promise to wait for User Code
        const userCodePromise = new Promise<void>((resolve, reject) => {
            clientProcess.stdout.on('data', (data: any) => {
                const text = data.toString();
                outputBuffer += text;
                console.log('[Client]:', text.trim());

                // Parse User Code
                // Expected: "User Code:        XXXX-XXXX-XXXX"
                const codeMatch = text.match(/User Code:\s+([A-Z0-9-]+)/);
                if (codeMatch) {
                    userCode = codeMatch[1];
                    console.log('Detected User Code:', userCode);
                }

                // Check for completion/token
                if (text.includes('Access Token received')) {
                    accessToken = text;
                   // Don't resolve here, we resolve code first
                }
                
                if (userCode && !verificationUri) {
                    // Just to be safe, assume logic flows
                    resolve();
                }
            });

            clientProcess.stderr.on('data', (data: any) => {
                console.error('[Client Error]:', data.toString());
            });
            
            clientProcess.on('error', (err: any) => reject(err));
            clientProcess.on('exit', (code: any) => {
                if (code !== 0 && code !== null) console.log(`Client exited with code ${code}`);
            });
        });

        // Wait for code
        await userCodePromise;
        expect(userCode).toBeTruthy();
        
        // 2. Go to Verification URI (Verified manually as https://localhost:7035/connect/verify)
        await page.goto('https://localhost:7035/connect/verify');
        
        // 3. Enter Code
        await page.fill('input[name="user_code"]', userCode);
        await page.click('button[type="submit"]');
        
        // 4. Login if needed
        // Check if we are redirected to login
        const loginInput = page.locator('#Input_Login');
        if (await loginInput.isVisible()) {
            console.log('Login required, logging in...');
            // Use helper - use Admin account which we know exists
            await loginAsUser(page, 'admin@hybridauth.local', 'Admin@123');
        }

        // 5. Consent (if enabled)
        // Check for common consent markers
        if (await page.locator('text=Approve').isVisible() || await page.locator('text=Allow').isVisible()) {
             console.log('Waiting more for token...');
             await new Promise(r => setTimeout(r, 5000));
        }

        expect(outputBuffer).toContain('Access Token received');
        
        // Kill process
        clientProcess.kill();
    });
});
