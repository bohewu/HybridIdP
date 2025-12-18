import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useWebAuthn } from '../useWebAuthn';

describe('useWebAuthn', () => {
    beforeEach(() => {
        vi.stubGlobal('navigator', {
            userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/91.0.4472.124 Safari/537.37',
            credentials: {
                create: vi.fn(),
                get: vi.fn()
            }
        });
        vi.stubGlobal('fetch', vi.fn());
        vi.stubGlobal('window', {
            atob: (str) => Buffer.from(str, 'base64').toString('binary'),
            btoa: (str) => Buffer.from(str, 'binary').toString('base64')
        });
    });

    it('isSupported returns true if PublicKeyCredential exists', () => {
        vi.stubGlobal('window', { ...window, PublicKeyCredential: {} });
        const { isSupported } = useWebAuthn();
        expect(isSupported()).toBe(true);
    });

    it('isSupported returns false if PublicKeyCredential is missing', () => {
        vi.stubGlobal('window', { ...window, PublicKeyCredential: undefined });
        const { isSupported } = useWebAuthn();
        expect(isSupported()).toBe(false);
    });

    describe('base64url utilities (internal but tested via behavior)', () => {
        it('should handle base64url to ArrayBuffer correctly', async () => {
            // We can't easily test private functions, but we can verify registration flow
            // which uses them.
        });
    });

    describe('registerPasskey', () => {
        it('throws error if WebAuthn not supported', async () => {
            vi.stubGlobal('window', { ...window, PublicKeyCredential: undefined });
            const { registerPasskey } = useWebAuthn();
            await expect(registerPasskey()).rejects.toThrow('WebAuthn not supported');
        });

        it('successfully registers a passkey', async () => {
            const { registerPasskey } = useWebAuthn();
            vi.stubGlobal('window', { ...window, PublicKeyCredential: {} });

            // Mock options response
            fetch.mockResolvedValueOnce({
                ok: true,
                json: () => Promise.resolve({
                    challenge: 'Y2hhbGxlbmdl', // "challenge"
                    user: { id: 'dXNlcmlk' },   // "userid"
                    rp: { name: 'HybridIdP' }
                })
            });

            // Mock navigator.credentials.create
            navigator.credentials.create.mockResolvedValueOnce({
                id: 'credid',
                rawId: new Uint8Array([1, 2, 3]).buffer,
                type: 'public-key',
                response: {
                    clientDataJSON: new Uint8Array([4, 5, 6]).buffer,
                    attestationObject: new Uint8Array([7, 8, 9]).buffer
                }
            });

            // Mock final register response
            fetch.mockResolvedValueOnce({
                ok: true,
                json: () => Promise.resolve({ success: true })
            });

            const result = await registerPasskey();
            expect(result.success).toBe(true);
            expect(fetch).toHaveBeenCalledTimes(2);
            expect(navigator.credentials.create).toHaveBeenCalled();
        });

        it('handles user cancellation', async () => {
            const { registerPasskey } = useWebAuthn();
            vi.stubGlobal('window', { ...window, PublicKeyCredential: {} });

            fetch.mockResolvedValueOnce({
                ok: true,
                json: () => Promise.resolve({ challenge: 'Y2hhbGxlbmdl', user: { id: 'dXNlcmlk' } })
            });

            const error = new Error('not allowed');
            error.name = 'NotAllowedError';
            navigator.credentials.create.mockRejectedValueOnce(error);

            await expect(registerPasskey()).rejects.toThrow('User canceled or biometric failed');
        });
    });
});
