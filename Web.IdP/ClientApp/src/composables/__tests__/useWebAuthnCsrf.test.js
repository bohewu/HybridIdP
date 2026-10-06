import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

describe('WebAuthn browser CSRF integration', () => {
    let transport;

    beforeEach(async () => {
        vi.resetModules();
        document.head.innerHTML = '<meta name="csrf-token" content="server-issued-token">';
        transport = vi.fn();
        vi.stubGlobal('fetch', transport);
        vi.stubGlobal('PublicKeyCredential', function PublicKeyCredential() {});
        vi.stubGlobal('navigator', {
            userAgent: 'Chrome on Windows',
            credentials: { create: vi.fn(), get: vi.fn() }
        });
        window.fetch = transport;
        await import('../../utils/csrfInterceptor.js');
        vi.stubGlobal('fetch', window.fetch);
    });

    afterEach(() => {
        document.head.innerHTML = '';
        vi.unstubAllGlobals();
    });

    const jsonResponse = (body) => ({
        ok: true,
        headers: { get: () => 'application/json' },
        json: async () => body
    });

    it('sends the page token on both registration requests', async () => {
        transport.mockResolvedValueOnce(jsonResponse({
            challenge: 'AQ', user: { id: 'Ag' }, rp: { name: 'HybridIdP' }
        })).mockResolvedValueOnce(jsonResponse({ success: true }));
        navigator.credentials.create.mockResolvedValueOnce({
            id: 'credential', rawId: new Uint8Array([1]).buffer, type: 'public-key',
            response: {
                clientDataJSON: new Uint8Array([2]).buffer,
                attestationObject: new Uint8Array([3]).buffer
            }
        });
        const { useWebAuthn } = await import('../useWebAuthn');

        await expect(useWebAuthn().registerPasskey()).resolves.toEqual({ success: true });

        expect(transport.mock.calls.map(([url]) => url)).toEqual(['/api/passkey/register-options', '/api/passkey/register']);
        for (const [, options] of transport.mock.calls) {
            expect(options.headers['X-XSRF-TOKEN']).toBe('server-issued-token');
            expect(options.credentials).toBe('include');
        }
    });

    it('sends the anonymous page token on both assertion requests', async () => {
        transport.mockResolvedValueOnce(jsonResponse({ challenge: 'AQ', allowCredentials: [] }))
            .mockResolvedValueOnce(jsonResponse({ success: true }));
        navigator.credentials.get.mockResolvedValueOnce({
            id: 'credential', rawId: new Uint8Array([1]).buffer, type: 'public-key',
            response: {
                clientDataJSON: new Uint8Array([2]).buffer,
                authenticatorData: new Uint8Array([3]).buffer,
                signature: new Uint8Array([4]).buffer,
                userHandle: null
            }
        });
        const { useWebAuthn } = await import('../useWebAuthn');

        await expect(useWebAuthn().authenticateWithPasskey('')).resolves.toEqual({ success: true });

        expect(transport.mock.calls.map(([url]) => url)).toEqual(['/api/passkey/login-options', '/api/passkey/login']);
        for (const [, options] of transport.mock.calls) {
            expect(options.headers['X-XSRF-TOKEN']).toBe('server-issued-token');
            expect(options.headers['Content-Type']).toBe('application/json');
            expect(options.credentials).toBe('include');
        }
    });
});
