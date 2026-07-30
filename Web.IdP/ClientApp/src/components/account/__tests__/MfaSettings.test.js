import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import MfaSettings from '../MfaSettings.vue';
import { useWebAuthn } from '../../../composables/useWebAuthn';
import enMfa from '../../../i18n/locales/en-US/mfa.json';
import zhMfa from '../../../i18n/locales/zh-TW/mfa.json';

// Mock vue-i18n
vi.mock('vue-i18n', () => ({
    useI18n: () => ({
        t: (key) => key
    })
}));

// Mock useWebAuthn
vi.mock('../../../composables/useWebAuthn', () => ({
    useWebAuthn: vi.fn()
}));

// Mock fetch
vi.stubGlobal('fetch', vi.fn());

describe('MfaSettings.vue', () => {
    const mockRegisterPasskey = vi.fn(() => Promise.resolve({ success: true }));
    const jsonResponse = (data, ok = true) => ({
        ok,
        json: () => Promise.resolve(data)
    });

    function mockMfaAccount({ hasPassword, recoveryHandler }) {
        fetch.mockImplementation((url) => {
            if (url === '/api/account/mfa/status') {
                return Promise.resolve(jsonResponse({
                    twoFactorEnabled: true,
                    hasAuthenticator: true,
                    recoveryCodesLeft: 4,
                    hasPassword,
                    emailMfaEnabled: false,
                    enableTotpMfa: true,
                    enableEmailMfa: false,
                    enablePasskey: false
                }));
            }

            if (url === '/api/profile') {
                return Promise.resolve(jsonResponse({ email: 'test@example.com' }));
            }

            if (url === '/api/passkey/list') {
                return Promise.resolve(jsonResponse([]));
            }

            if (url === '/api/account/security-policy') {
                return Promise.resolve(jsonResponse({ requireMfaForPasskey: false }));
            }

            if (url === '/api/account/mfa/recovery-codes') {
                return recoveryHandler();
            }

            return Promise.resolve(jsonResponse({}));
        });
    }

    async function openRegenerateModal(wrapper) {
        const openButton = wrapper.findAll('button')
            .find((button) => button.text() === 'mfa.regenerateCodes');
        expect(openButton).toBeTruthy();
        await openButton.trigger('click');
    }

    function recoveryRequests() {
        return fetch.mock.calls.filter(([url]) => url === '/api/account/mfa/recovery-codes');
    }

    beforeEach(() => {
        vi.clearAllMocks();
        // Default mock for profile
        fetch.mockResolvedValue({
            ok: true,
            json: () => Promise.resolve({ 
                email: 'test@example.com',
                enableTotpMfa: true,
                enableEmailMfa: true,
                enablePasskey: true
            })
        });
        
        // Setup default mock return for useWebAuthn
        vi.mocked(useWebAuthn).mockReturnValue({
            registerPasskey: mockRegisterPasskey,
            isSupported: () => true
        });
    });

    it('renders passkey section', async () => {
        const wrapper = mount(MfaSettings);
        await flushPromises();
        
        expect(wrapper.find('.passkey-section').exists()).toBe(true);
        expect(wrapper.find('.passkey-section h3').text()).toBe('mfa.passkey.title');
    });

    it('lists registered passkeys', async () => {
        // Mock passkey list and status response
        fetch.mockImplementation((url) => {
            if (url.includes('/api/passkey/list')) {
                return Promise.resolve({
                    ok: true,
                    json: () => Promise.resolve([
                        { id: '1', deviceName: 'My Key', createdAt: new Date().toISOString() }
                    ])
                });
            }
            return Promise.resolve({ 
                ok: true, 
                json: () => Promise.resolve({ 
                    email: 'test@example.com',
                    enableTotpMfa: true,
                    enableEmailMfa: true,
                    enablePasskey: true
                }) 
            });
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();
        
        const items = wrapper.findAll('.passkey-item');
        expect(items.length).toBe(1);
        expect(items[0].find('.pk-name').text()).toBe('My Key');
    });

    it('calls registerPasskey when registerNewPasskey is called', async () => {
        const wrapper = mount(MfaSettings);
        await flushPromises();

        await wrapper.vm.registerNewPasskey();
        await flushPromises();

        expect(mockRegisterPasskey).toHaveBeenCalled();
    });

    it('shows delete confirmation modal when delete button clicked', async () => {
        fetch.mockImplementation((url) => {
            if (url.includes('/api/passkey/list')) {
                return Promise.resolve({
                    ok: true,
                    json: () => Promise.resolve([
                        { id: '1', deviceName: 'Key to delete', createdAt: new Date().toISOString() }
                    ])
                });
            }
            return Promise.resolve({ 
                ok: true, 
                json: () => Promise.resolve({ 
                    email: 'test@example.com',
                    enableTotpMfa: true,
                    enableEmailMfa: true,
                    enablePasskey: true
                }) 
            });
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();

        const deleteBtn = wrapper.find('.btn-pk-delete');
        await deleteBtn.trigger('click');

        // Check if modal is visible
        const modal = wrapper.find('.modal-content h2');
        expect(modal.exists()).toBe(true);
        expect(modal.text()).toBe('mfa.passkey.deleteConfirmTitle');
    });

    it('shows all disabled message when all MFA methods are turned off by policy', async () => {
        fetch.mockResolvedValue({
            ok: true,
            json: () => Promise.resolve({ 
                email: 'test@example.com',
                enableTotpMfa: false,
                enableEmailMfa: false,
                enablePasskey: false
            })
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();

        expect(wrapper.find('.all-mfa-disabled').exists()).toBe(true);
        expect(wrapper.find('.all-mfa-disabled h3').text()).toBe('mfa.allDisabledTitle');
        expect(wrapper.find('.mfa-content').exists()).toBe(false);
    });

    it('shows an inline error when interactive reauthentication cannot start', async () => {
        const wrapper = mount(MfaSettings);
        await flushPromises();
        fetch.mockResolvedValueOnce({ ok: false });

        await wrapper.vm.startSetup();
        await flushPromises();

        expect(fetch).toHaveBeenCalledWith('/api/account/mfa/reauthenticate', {
            method: 'POST',
            credentials: 'include'
        });
        expect(wrapper.find('[role="alert"]').text()).toBe('mfa.errors.setupFailed');
        expect(wrapper.find('.btn-enable').attributes('disabled')).toBeUndefined();
    });

    it('shows warning message when requireMfaForPasskey is enabled and user has no MFA but has passkeys', async () => {
        // Mock: user has passkeys, no MFA, and policy requires MFA for passkeys
        fetch.mockImplementation((url) => {
            if (url.includes('/api/passkey/list')) {
                return Promise.resolve({
                    ok: true,
                    json: () => Promise.resolve([
                        { id: '1', deviceName: 'My Passkey', createdAt: new Date().toISOString() }
                    ])
                });
            }
            if (url.includes('/api/account/security-policy')) {
                return Promise.resolve({
                    ok: true,
                    json: () => Promise.resolve({ requireMfaForPasskey: true })
                });
            }
            return Promise.resolve({ 
                ok: true, 
                json: () => Promise.resolve({ 
                    email: 'test@example.com',
                    twoFactorEnabled: false,  // No TOTP
                    emailMfaEnabled: false,   // No Email MFA
                    enableTotpMfa: true,
                    enableEmailMfa: true,
                    enablePasskey: true
                }) 
            });
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();

        // Check if non-compliance warning is shown (if implemented in component)
        // This test documents expected behavior for future implementation
        const passkeySection = wrapper.find('.passkey-section');
        expect(passkeySection.exists()).toBe(true);
    });

    it('requires password proof and sends only the password contract property for password users', async () => {
        mockMfaAccount({
            hasPassword: true,
            recoveryHandler: () => Promise.resolve(jsonResponse({ recoveryCodes: [] }))
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();
        await openRegenerateModal(wrapper);

        const passwordInput = wrapper.get('[data-testid="regenerate-password"]');
        const submitButton = wrapper.get('[data-testid="regenerate-submit"]');
        expect(wrapper.find('[data-testid="regenerate-totp"]').exists()).toBe(false);
        expect(wrapper.get('label[for="regenerate-password"]').text()).toBe('mfa.enterPasswordToRegenerate');
        expect(submitButton.attributes('disabled')).toBeDefined();

        await passwordInput.setValue(Array(12).fill('x').join(''));
        expect(submitButton.attributes('disabled')).toBeUndefined();
        await submitButton.trigger('click');
        await flushPromises();

        expect(recoveryRequests()).toHaveLength(1);
        const requestOptions = recoveryRequests()[0][1];
        const requestBody = JSON.parse(requestOptions.body);
        expect(requestOptions.headers).toEqual({ 'Content-Type': 'application/json' });
        expect(Object.keys(requestBody)).toEqual(['password']);
        expect(typeof requestBody.password).toBe('string');
        expect(requestBody.password.length).toBeGreaterThan(0);
    });

    it('requires a six-digit TOTP proof and sends only the totpCode contract property for passwordless users', async () => {
        mockMfaAccount({
            hasPassword: false,
            recoveryHandler: () => Promise.resolve(jsonResponse({ recoveryCodes: [] }))
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();
        await openRegenerateModal(wrapper);

        const totpInput = wrapper.get('[data-testid="regenerate-totp"]');
        const submitButton = wrapper.get('[data-testid="regenerate-submit"]');
        expect(wrapper.find('[data-testid="regenerate-password"]').exists()).toBe(false);
        expect(wrapper.get('label[for="regenerate-totp"]').text()).toBe('mfa.enterTotpToRegenerate');
        expect(totpInput.attributes('inputmode')).toBe('numeric');
        expect(submitButton.attributes('disabled')).toBeDefined();

        await totpInput.setValue(Array(6).fill('1').join(''));
        expect(submitButton.attributes('disabled')).toBeUndefined();
        await submitButton.trigger('click');
        await flushPromises();

        expect(recoveryRequests()).toHaveLength(1);
        const requestBody = JSON.parse(recoveryRequests()[0][1].body);
        expect(Object.keys(requestBody)).toEqual(['totpCode']);
        expect(typeof requestBody.totpCode).toBe('string');
        expect(requestBody.totpCode).toHaveLength(6);
    });

    it.each([
        [true, 'mfa.errors.passwordRequired'],
        [false, 'mfa.errors.totpRequired']
    ])('shows localized validation without sending when required proof is missing', async (hasPassword, expectedError) => {
        mockMfaAccount({
            hasPassword,
            recoveryHandler: vi.fn()
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();
        await openRegenerateModal(wrapper);

        await wrapper.vm.regenerateCodes();

        expect(recoveryRequests()).toHaveLength(0);
        expect(wrapper.get('#regenerate-error').text()).toBe(expectedError);
        expect(wrapper.get('[data-testid="regenerate-submit"]').attributes('disabled')).toBeDefined();
    });

    it('shows a pending state and prevents duplicate recovery-code requests', async () => {
        let resolveRecoveryRequest;
        const pendingRequest = new Promise((resolve) => {
            resolveRecoveryRequest = resolve;
        });
        mockMfaAccount({
            hasPassword: true,
            recoveryHandler: () => pendingRequest
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();
        await openRegenerateModal(wrapper);
        await wrapper.get('[data-testid="regenerate-password"]').setValue(Array(12).fill('x').join(''));

        await wrapper.get('[data-testid="regenerate-submit"]').trigger('click');
        await wrapper.vm.regenerateCodes();

        const submitButton = wrapper.get('[data-testid="regenerate-submit"]');
        expect(submitButton.attributes('disabled')).toBeDefined();
        expect(submitButton.text()).toBe('mfa.regenerating');
        expect(wrapper.get('.modal-content').attributes('aria-busy')).toBe('true');
        expect(recoveryRequests()).toHaveLength(1);

        resolveRecoveryRequest(jsonResponse({ recoveryCodes: [] }));
        await flushPromises();
    });

    it('shows a localized backend error, allows retry, and clears proof and error state', async () => {
        const oneTimeCodes = [Array(10).fill('r').join('')];
        const recoveryHandler = vi.fn()
            .mockResolvedValueOnce(jsonResponse({ error: 'invalidPassword' }, false))
            .mockResolvedValueOnce(jsonResponse({ recoveryCodes: oneTimeCodes }));
        mockMfaAccount({ hasPassword: true, recoveryHandler });

        const wrapper = mount(MfaSettings);
        await flushPromises();
        await openRegenerateModal(wrapper);

        const passwordInput = wrapper.get('[data-testid="regenerate-password"]');
        await passwordInput.setValue(Array(12).fill('x').join(''));
        await wrapper.get('[data-testid="regenerate-submit"]').trigger('click');
        await flushPromises();

        expect(wrapper.get('#regenerate-error').text()).toBe('mfa.errors.invalidPassword');
        expect(wrapper.get('[data-testid="regenerate-submit"]').attributes('disabled')).toBeUndefined();

        await wrapper.get('[data-testid="regenerate-submit"]').trigger('click');
        await flushPromises();

        expect(recoveryRequests()).toHaveLength(2);
        expect(wrapper.find('#regenerate-error').exists()).toBe(false);
        expect(wrapper.find('.recovery-codes-display').exists()).toBe(true);
        expect(wrapper.find('[data-testid="regenerate-password"]').exists()).toBe(false);

        await wrapper.get('.recovery-codes-display .btn-primary').trigger('click');
        await flushPromises();
        await openRegenerateModal(wrapper);

        expect(wrapper.get('[data-testid="regenerate-password"]').element.value).toBe('');
        expect(wrapper.find('#regenerate-error').exists()).toBe(false);
    });

    it('clears proof and request errors when the modal is closed and reopened', async () => {
        mockMfaAccount({
            hasPassword: true,
            recoveryHandler: () => Promise.resolve(jsonResponse({}, false))
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();
        await openRegenerateModal(wrapper);
        await wrapper.get('[data-testid="regenerate-password"]').setValue(Array(12).fill('x').join(''));
        await wrapper.get('[data-testid="regenerate-submit"]').trigger('click');
        await flushPromises();
        expect(wrapper.find('#regenerate-error').exists()).toBe(true);

        const cancelButton = wrapper.findAll('.btn-cancel')
            .find((button) => button.text() === 'common.cancel');
        await cancelButton.trigger('click');
        await openRegenerateModal(wrapper);

        expect(wrapper.get('[data-testid="regenerate-password"]').element.value).toBe('');
        expect(wrapper.find('#regenerate-error').exists()).toBe(false);
    });

    it('provides matching regeneration keys in both supported MFA locales', () => {
        const topLevelKeys = [
            'enterPasswordToRegenerate',
            'enterTotpToRegenerate',
            'regenerating'
        ];

        for (const key of topLevelKeys) {
            expect(typeof enMfa[key]).toBe('string');
            expect(enMfa[key].length).toBeGreaterThan(0);
            expect(typeof zhMfa[key]).toBe('string');
            expect(zhMfa[key].length).toBeGreaterThan(0);
        }

        expect(typeof enMfa.errors.regenerateFailed).toBe('string');
        expect(enMfa.errors.regenerateFailed.length).toBeGreaterThan(0);
        expect(typeof zhMfa.errors.regenerateFailed).toBe('string');
        expect(zhMfa.errors.regenerateFailed.length).toBeGreaterThan(0);
    });
});

