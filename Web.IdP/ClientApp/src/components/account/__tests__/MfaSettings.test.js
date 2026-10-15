import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import MfaSettings from '../MfaSettings.vue';
import AccountRecoveryCodes from '../AccountRecoveryCodes.vue';
import ProfileApp from '../../../profile/ProfileApp.vue';
import { useWebAuthn } from '../../../composables/useWebAuthn';
import enMfa from '../../../i18n/locales/en-US/mfa.json';
import zhMfa from '../../../i18n/locales/zh-TW/mfa.json';
import { saveMfaContinuation } from '../../../composables/mfaContinuation';

// Mock vue-i18n
const i18nState = vi.hoisted(() => ({ locale: 'en-US' }));
vi.mock('vue-i18n', () => ({
    useI18n: () => ({
        t: (key, params) => {
            if (['mfa.otpSummaryEnabled', 'mfa.otpSummaryDisabled', 'mfa.authenticatorOptional', 'mfa.disableWarning', 'mfa.disableWarningWithEmail',
                'mfa.disableReauthenticationDescription', 'mfa.disablePasswordConfirmation', 'mfa.disableTotpConfirmation', 'mfa.addAnotherMethodBeforeRemoval'].includes(key)) {
                return (i18nState.locale === 'zh-TW' ? zhMfa : enMfa)[key.slice(4)];
            }
            if (['mfa.errors.freshAuthenticationRequired', 'mfa.errors.alternateSignInRequired'].includes(key)) {
                return (i18nState.locale === 'zh-TW' ? zhMfa : enMfa).errors[key.split('.').pop()];
            }
            if (['mfa.passkey.noneRegistered', 'mfa.passkey.sessionExpired', 'mfa.passkey.signInAgain', 'mfa.passkey.signInMethodTitle'].includes(key)) {
                return (i18nState.locale === 'zh-TW' ? zhMfa : enMfa).passkey[key.split('.').pop()];
            }
            if (params?.action) return `${key}:${params.action}`;
            return params?.seconds === undefined ? key : `${key}:${params.seconds}`;
        }
    })
}));

// Mock useWebAuthn
vi.mock('../../../composables/useWebAuthn', () => ({
    useWebAuthn: vi.fn()
}));

// Mock fetch
vi.stubGlobal('fetch', vi.fn());

describe('MfaSettings.vue', () => {
    function mockPolicyAccount(factor, mandatory, overrides = {}) {
        fetch.mockImplementation(url => Promise.resolve(jsonResponse(
            url === '/api/account/mfa/status' ? {
                twoFactorEnabled: factor === 'totp', emailMfaEnabled: factor === 'email', hasPassword: true,
                enableTotpMfa: true, enableEmailMfa: true, enablePasskey: true,
                enforceMandatoryMfaEnrollment: mandatory, ...overrides
            } : url === '/api/passkey/list' ? factor === 'passkey' ? [{ id: 7, deviceName: 'Security key' }] : []
            : url === '/api/account/security-policy' ? { requireMfaForPasskey: false }
            : url === '/api/account/recovery-codes' ? { hasBatch: true, remainingCodes: 10, legacyRemainingCodes: 0, totalRemainingCodes: 10 }
            : {})));
    }

    it.each(['en-US', 'zh-TW'].flatMap(locale => ['totp', 'email', 'passkey'].flatMap(factor => [false, true].map(mandatory => [locale, factor, mandatory]))))('explains final factor removal in %s for %s under global mandatory policy (%s)', async (locale, factor, mandatory) => {
        i18nState.locale = locale;
        const copy = locale === 'zh-TW' ? zhMfa : enMfa;
        mockPolicyAccount(factor, mandatory, { requireMfa: true });
        const wrapper = mount(MfaSettings);
        await flushPromises();
        const selector = factor === 'totp' ? 'totp-disable' : factor === 'email' ? 'email-mfa-disable' : 'passkey-delete-7';
        const button = wrapper.get(`[data-testid="${selector}"]`);
        expect(button.element.disabled).toBe(mandatory);
        if (factor === 'passkey') expect(button.attributes('aria-label')).toBe('mfa.passkey.delete');
        if (mandatory) {
            expect(button.attributes('title')).toBe(copy.addAnotherMethodBeforeRemoval);
            const reason = wrapper.get(`#${button.attributes('aria-describedby')}`);
            expect(reason.text()).toBe(copy.addAnotherMethodBeforeRemoval);
            expect(reason.isVisible()).toBe(true);
            expect(wrapper.find('[data-testid="account-recovery-status"]').exists()).toBe(true);
            await button.trigger('click');
            expect(wrapper.find('[data-testid="mfa-reauthentication-notice"]').exists()).toBe(false);
            expect(wrapper.find('.delete-target').exists()).toBe(false);
        } else {
            expect(button.attributes('aria-describedby')).toBeUndefined();
            expect(button.attributes('title')).toBe(factor === 'passkey' ? 'mfa.passkey.delete' : undefined);
            await button.trigger('click');
            expect(wrapper.find('[data-testid="mfa-reauthentication-notice"]').exists()).toBe(factor !== 'passkey');
        }
        expect(fetch.mock.calls.every(([, options]) => options?.method !== 'POST' && options?.method !== 'DELETE')).toBe(true);
        wrapper.unmount();
    });

    it.each(['totp', 'email', 'passkey'])('keeps the %s loading state separate from the mandatory-removal reason', async factor => {
        mockPolicyAccount(factor, false);
        const wrapper = mount(MfaSettings);
        await flushPromises();
        wrapper.vm.totpSetupLoading = true;
        await flushPromises();
        const selector = factor === 'totp' ? 'totp-disable' : factor === 'email' ? 'email-mfa-disable' : 'passkey-delete-7';
        const button = wrapper.get(`[data-testid="${selector}"]`);
        expect(button.element.disabled).toBe(true);
        expect(button.attributes('aria-describedby')).toBeUndefined();
        expect(button.attributes('title')).toBe(factor === 'passkey' ? 'mfa.passkey.delete' : undefined);
        wrapper.vm.totpSetupLoading = false;
        await flushPromises();
        expect(button.element.disabled).toBe(false);
        wrapper.unmount();
    });

    it('allows deleting one of two passkeys and disables the remaining key under mandatory MFA', async () => {
        mockPolicyAccount('passkey', true);
        const normal = fetch.getMockImplementation();
        let keys = [{ id: 7, deviceName: 'First key' }, { id: 8, deviceName: 'Second key' }];
        fetch.mockImplementation((url, options) => {
            if (url === '/api/passkey/list') return Promise.resolve(jsonResponse([...keys]));
            if (url === '/api/passkey/7' && options?.method === 'DELETE') {
                keys = keys.filter(key => key.id !== 7);
                return Promise.resolve(jsonResponse({ success: true }));
            }
            return normal(url, options);
        });
        const wrapper = mount(MfaSettings);
        await flushPromises();
        expect(wrapper.get('[data-testid="passkey-delete-7"]').element.disabled).toBe(false);
        expect(wrapper.get('[data-testid="passkey-delete-8"]').element.disabled).toBe(false);
        wrapper.vm.confirmDeletePasskey(keys[0]);
        await wrapper.vm.deletePasskey();
        await flushPromises();
        expect(wrapper.find('[data-testid="passkey-delete-7"]').exists()).toBe(false);
        expect(wrapper.get('[data-testid="passkey-delete-8"]').element.disabled).toBe(true);
        expect(wrapper.get('#passkey-removal-policy-8').isVisible()).toBe(true);
        wrapper.unmount();
    });

    it('keeps removal available when another policy-enabled factor remains', async () => {
        mockPolicyAccount('totp', true, { emailMfaEnabled: true });
        const wrapper = mount(MfaSettings);
        await flushPromises();
        expect(wrapper.get('[data-testid="totp-disable"]').element.disabled).toBe(false);
        expect(wrapper.get('[data-testid="email-mfa-disable"]').element.disabled).toBe(false);
        wrapper.unmount();
    });

    it.each(['en-US', 'zh-TW'].flatMap(locale => ['totp', 'email', 'passkey'].map(factor => [locale, factor])))('explains alternate sign-in rejection in the %s %s removal surface', async (locale, factor) => {
        i18nState.locale = locale;
        mockPolicyAccount(factor, false, { hasPassword: false });
        const normal = fetch.getMockImplementation();
        const endpoint = factor === 'passkey' ? '/api/passkey/7' : factor === 'totp' ? '/api/account/mfa/disable' : '/api/account/mfa/email/disable';
        fetch.mockImplementation(url => url === endpoint
            ? Promise.resolve(jsonResponse({ error: 'alternateSignInRequired' }, false)) : normal(url));
        const wrapper = mount(MfaSettings);
        try {
            await flushPromises();
            const selector = factor === 'passkey' ? 'passkey-delete-7' : factor === 'totp' ? 'totp-disable' : 'email-mfa-disable';
            expect(wrapper.get(`[data-testid="${selector}"]`).element.disabled).toBe(false);
            if (factor === 'passkey') {
                wrapper.vm.confirmDeletePasskey({ id: 7, deviceName: 'Security key' });
                await wrapper.vm.deletePasskey();
            } else if (factor === 'totp') {
                wrapper.vm.showDisableModal = true;
                wrapper.vm.disableTotpCode = '123456';
                await wrapper.vm.disableMfa();
            } else await wrapper.vm.disableEmailMfa();
            await flushPromises();
            const error = wrapper.get(factor === 'email' ? '.email-mfa-section .error-message' : '.modal-content .error-message');
            const copy = locale === 'zh-TW' ? zhMfa : enMfa;
            expect(error.text()).toBe(copy.errors.alternateSignInRequired);
            expect(error.isVisible()).toBe(true);
            expect(wrapper.find('[data-testid="mfa-reauthentication-notice"]').exists()).toBe(false);
            expect(fetch.mock.calls.filter(([url]) => url === endpoint)).toHaveLength(1);
        } finally { wrapper.unmount(); }
    });

    it('does not count a policy-disabled OTP method as an alternative', async () => {
        mockPolicyAccount('totp', true, { emailMfaEnabled: true, enableEmailMfa: false });
        const wrapper = mount(MfaSettings);
        await flushPromises();
        expect(wrapper.get('[data-testid="totp-disable"]').element.disabled).toBe(true);
        wrapper.unmount();
    });

    it.each([false, true])('accounts for the passkey cascade when removing the last OTP (%s)', async requireMfaForPasskey => {
        mockPolicyAccount('totp', true);
        const normal = fetch.getMockImplementation();
        fetch.mockImplementation(url => url === '/api/account/security-policy'
            ? Promise.resolve(jsonResponse({ requireMfaForPasskey }))
            : url === '/api/passkey/list' ? Promise.resolve(jsonResponse([{ id: 7 }])) : normal(url));
        const wrapper = mount(MfaSettings);
        await flushPromises();
        expect(wrapper.get('[data-testid="totp-disable"]').element.disabled).toBe(requireMfaForPasskey);
        wrapper.unmount();
    });

    it.each(['totp', 'email'])('explains legacy-only codes before final %s removal and makes regeneration optional', async factor => {
        mockPolicyAccount(factor, false);
        const normal = fetch.getMockImplementation();
        fetch.mockImplementation(url => url === '/api/account/recovery-codes'
            ? Promise.resolve(jsonResponse({ hasBatch: true, remainingCodes: 0, legacyRemainingCodes: 4, totalRemainingCodes: 4 })) : normal(url));
        const wrapper = mount(MfaSettings);
        await flushPromises();
        await wrapper.get(`[data-testid="${factor === 'totp' ? 'totp-disable' : 'email-mfa-disable'}"]`).trigger('click');
        expect(wrapper.get('[data-testid="legacy-recovery-removal-warning"]').text()).toContain('mfa.accountRecoveryCodes.lastOtpRemovalWarning');
        expect(wrapper.get('[data-testid="mfa-reauthentication-continue"]').element.disabled).toBe(false);
        expect(wrapper.find('[data-testid="account-recovery-saved"]').exists()).toBe(false);
        await wrapper.get('[data-testid="legacy-recovery-regenerate-first"]').trigger('click');
        expect(wrapper.vm.reauthenticationIntent.action).toBe('account-recovery-codes');
        expect(wrapper.find('[data-testid="legacy-recovery-removal-warning"]').exists()).toBe(false);
        expect(fetch.mock.calls.every(([, options]) => options?.method !== 'POST')).toBe(true);
        wrapper.unmount();
    });

    it('discloses first Email account codes in the common component and retains them through a status refresh', async () => {
        mockPolicyAccount('totp', false);
        const normal = fetch.getMockImplementation();
        fetch.mockImplementation(url => url === '/api/account/mfa/email/verify'
            ? Promise.resolve(jsonResponse({ success: true, accountRecoveryCodes: ['email-first-code'] })) : normal(url));
        const wrapper = mount(MfaSettings);
        await flushPromises();
        wrapper.vm.openEmailMfaSetup();
        wrapper.vm.emailCodeSent = true;
        await flushPromises();
        await wrapper.get('#email-mfa-code').setValue('123456');
        await wrapper.vm.verifyEmailMfa();
        await flushPromises();
        expect(wrapper.findAll('[data-testid="account-recovery-disclosure"]')).toHaveLength(1);
        expect(wrapper.get('[data-testid="account-recovery-disclosure"]').text()).toBe('email-first-code');
        await wrapper.vm.loadMfaStatus();
        await flushPromises();
        expect(wrapper.get('[data-testid="account-recovery-disclosure"]').text()).toBe('email-first-code');
        expect(wrapper.find('[data-testid="regenerate-password"]').exists()).toBe(false);
        expect(fetch.mock.calls.some(([url]) => url === '/api/account/mfa/recovery-codes')).toBe(false);
        await wrapper.get('[data-testid="account-recovery-saved"]').setValue(true);
        await wrapper.get('[data-testid="account-recovery-done"]').trigger('click');
        expect(wrapper.text()).not.toContain('email-first-code');
        wrapper.unmount();
    });

    it.each([false, true])('refreshes the actual Profile parent only after first Email codes are saved (%s)', async hasIssuedCodes => {
        mockPolicyAccount('email', false, { emailMfaEnabled: false });
        const normal = fetch.getMockImplementation();
        const profileRequest = vi.fn().mockResolvedValue(jsonResponse({ userId: 'account-1', email: 'test@example.com' }));
        fetch.mockImplementation(url => url === '/api/profile' ? profileRequest()
            : url === '/api/account/mfa/email/verify' ? Promise.resolve(jsonResponse({
                success: true, accountRecoveryCodes: hasIssuedCodes ? ['parent-remount-code'] : null
            })) : normal(url));
        const wrapper = mount(ProfileApp, {
            global: {
                stubs: { ProfileInfoCard: true, EditProfileForm: true, ChangePasswordForm: true, RecoveryEmailSettings: true },
                directives: { loading: () => {} }
            }
        });
        try {
            await flushPromises();
            const settings = wrapper.getComponent(MfaSettings);
            const recovery = settings.getComponent(AccountRecoveryCodes);
            settings.vm.openEmailMfaSetup();
            settings.vm.emailCodeSent = true;
            await flushPromises();
            await settings.get('#email-mfa-code').setValue('123456');
            await settings.vm.verifyEmailMfa();
            await flushPromises();
            if (hasIssuedCodes) {
                expect(profileRequest).toHaveBeenCalledTimes(1);
                expect(wrapper.getComponent(MfaSettings).vm.$.uid).toBe(settings.vm.$.uid);
                expect(settings.emitted('status-changed')).toBeUndefined();
                expect(wrapper.get('[data-testid="account-recovery-disclosure"]').text()).toBe('parent-remount-code');
                expect(wrapper.get('[data-testid="account-recovery-done"]').element.disabled).toBe(true);
                expect(sessionStorage.length).toBe(0);
                expect(localStorage.length).toBe(0);
                await wrapper.get('[data-testid="account-recovery-saved"]').setValue(true);
                await wrapper.get('[data-testid="account-recovery-done"]').trigger('click');
                await flushPromises();
                expect(recovery.vm.codes).toEqual([]);
            }
            expect(profileRequest).toHaveBeenCalledTimes(2);
            expect(settings.emitted('status-changed')).toHaveLength(1);
            expect(wrapper.getComponent(MfaSettings).vm.$.uid).not.toBe(settings.vm.$.uid);
            expect(wrapper.find('[data-testid="account-recovery-disclosure"]').exists()).toBe(false);
        } finally { wrapper.unmount(); }
    });

    it('discloses initial account backup codes returned by passkey registration once', async () => {
        fetch.mockImplementation(url => Promise.resolve(jsonResponse(url === '/api/passkey/list' ? []
            : url === '/api/account/recovery-codes' ? { hasBatch: true, remainingCodes: 2 }
            : { enablePasskey: true })));
        mockRegisterPasskey.mockResolvedValueOnce({ success: true, accountRecoveryCodes: ['initial-code-a', 'initial-code-b'] });
        const wrapper = mount(MfaSettings);
        await flushPromises();
        await wrapper.vm.registerNewPasskey();
        await flushPromises();
        expect(wrapper.get('[data-testid="account-recovery-disclosure"]').text()).toContain('initial-code-a');
        expect(sessionStorage.length).toBe(0);
        await wrapper.get('[data-testid="account-recovery-saved"]').setValue(true);
        await wrapper.get('[data-testid="account-recovery-done"]').trigger('click');
        await wrapper.vm.registerNewPasskey();
        await flushPromises();
        expect(wrapper.find('[data-testid="account-recovery-disclosure"]').exists()).toBe(false);
        expect(fetch.mock.calls.filter(([url]) => url === '/api/account/recovery-codes')).toHaveLength(3);
        wrapper.unmount();
    });

    it('confirms the distinct backup-code purpose before reauthentication and resumes without generating', async () => {
        const assign = vi.spyOn(window.location, 'assign').mockImplementation(() => {});
        fetch.mockImplementation(url => Promise.resolve(jsonResponse(url === '/api/passkey/list' ? []
            : url === '/api/account/recovery-codes' ? { hasBatch: false, remainingCodes: 0 }
            : url.startsWith('/api/account/mfa/reauthenticate') ? { loginUrl: '/Account/Login?reauthenticate=true' }
            : { enablePasskey: true })));
        const props = { profile: { userId: 'account-backup', email: 'fixture@example.test' }, csrfToken: 'csrf-fixture' };
        const wrapper = mount(MfaSettings, { props });
        let resumed;
        try {
            await flushPromises();
            await wrapper.get('[data-testid="account-recovery-generate"]').trigger('click');
            expect(wrapper.get('[data-testid="mfa-reauthentication-notice"]').text()).toContain('mfa.reauthenticationActions.account-recovery-codes');
            expect(fetch.mock.calls.some(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toBe(false);
            await wrapper.get('[data-testid="mfa-reauthentication-continue"]').trigger('click');
            await flushPromises();
            expect(fetch).toHaveBeenCalledWith('/api/account/mfa/reauthenticate?returnToProfile=true&action=account-recovery-codes', { method: 'POST', credentials: 'include' });
            expect(assign).toHaveBeenCalledWith('/Account/Login?reauthenticate=true');
            expect(JSON.parse(sessionStorage.getItem('mfa.continuation')).action).toBe('account-recovery-codes');
            wrapper.unmount();
            resumed = mount(MfaSettings, { props });
            await flushPromises();
            expect(resumed.get('[data-testid="account-recovery-confirm"]').exists()).toBe(true);
            expect(sessionStorage.getItem('mfa.continuation')).toBeNull();
            expect(fetch.mock.calls.filter(([url, options]) => url === '/api/account/recovery-codes' && options.method === 'POST')).toHaveLength(0);
            fetch.mockResolvedValueOnce({ ok: false, status: 403, json: async () => ({ error: 'freshAuthenticationRequired' }) });
            await resumed.get('[data-testid="account-recovery-confirm"]').trigger('click');
            await flushPromises();
            expect(resumed.get('[data-testid="mfa-reauthentication-notice"]').text()).toContain('mfa.reauthenticationActions.account-recovery-codes');
            expect(fetch.mock.calls.filter(([url, options]) => url === '/api/account/recovery-codes' && options.method === 'POST')).toHaveLength(1);
            expect(fetch.mock.calls.filter(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toHaveLength(1);
            await resumed.get('[data-testid="mfa-reauthentication-cancel"]').trigger('click');
            expect(resumed.find('[data-testid="mfa-reauthentication-notice"]').exists()).toBe(false);
        } finally {
            resumed?.unmount();
            if (!resumed) wrapper.unmount();
            assign.mockRestore();
        }
    });

    it.each(['en-US', 'zh-TW'])('shows an empty inventory without asking for verification in %s', async locale => {
        i18nState.locale = locale;
        fetch.mockImplementation(url => Promise.resolve(jsonResponse(
            url === '/api/passkey/list' ? [] : { enablePasskey: true })));
        const wrapper = mount(MfaSettings);
        await flushPromises();

        expect(wrapper.get('[data-testid="passkey-list-empty"]').text()).toBe(
            (locale === 'zh-TW' ? zhMfa : enMfa).passkey.noneRegistered);
        expect(wrapper.get('.passkey-section .section-action button').element.disabled).toBe(false);
        expect(wrapper.find('[data-testid="passkey-list-session-expired"]').exists()).toBe(false);
        expect(wrapper.find('[data-testid="mfa-reauthentication-notice"]').exists()).toBe(false);
        expect(fetch.mock.calls.some(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toBe(false);
        expect(sessionStorage.getItem('mfa.continuation')).toBeNull();
        wrapper.unmount();
    });

    it('does not turn a denied TOTP setup into an inventory login redirect', async () => {
        const assign = vi.spyOn(window.location, 'assign').mockImplementation(() => {});
        const wrapper = mount(MfaSettings);
        await flushPromises();
        await wrapper.vm.startSetup();
        fetch.mockResolvedValue({ ok: false, status: 401, json: async () => ({}) });
        await wrapper.get('[data-testid="mfa-reauthentication-continue"]').trigger('click');
        await flushPromises();
        expect(assign).not.toHaveBeenCalled();
        expect(wrapper.vm.totpSetupError).toBe('mfa.errors.setupFailed');
        wrapper.unmount();
        assign.mockRestore();
    });

    it.each(['en-US', 'zh-TW'])('shows a direct sign-in link only for an expired inventory session in %s', async locale => {
        i18nState.locale = locale;
        const assign = vi.spyOn(window.location, 'assign').mockImplementation(() => {});
        fetch.mockImplementation(url => Promise.resolve(url === '/api/passkey/list'
            ? { ok: false, status: 401, json: async () => ({ error: 'sessionExpired' }) }
            : jsonResponse({ enablePasskey: true })));
        const wrapper = mount(MfaSettings);
        await flushPromises();
        const inventory = wrapper.get('[data-testid="passkey-list-session-expired"]');
        expect(inventory.text()).toContain((locale === 'zh-TW' ? zhMfa : enMfa).passkey.sessionExpired);
        expect(inventory.get('a').attributes('href')).toBe('/Account/Login?returnUrl=%2FAccount%2FProfile');
        expect(wrapper.find('[data-testid="passkey-list-empty"]').exists()).toBe(false);
        expect(fetch.mock.calls.some(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toBe(false);
        expect(assign).not.toHaveBeenCalled();
        expect(wrapper.find('[data-testid="mfa-reauthentication-notice"]').exists()).toBe(false);
        expect(mockRegisterPasskey).not.toHaveBeenCalled();
        expect(sessionStorage.getItem('mfa.continuation')).toBeNull();
        wrapper.unmount();
        assign.mockRestore();
    });

    it('shows registered passkeys after a successful inventory reload', async () => {
        let authorized = false;
        fetch.mockImplementation(url => Promise.resolve({ ok: url !== '/api/passkey/list' || authorized, status: authorized ? 200 : 401, json: async () =>
            url === '/api/passkey/list' ? authorized ? [{ id: 1, deviceName: 'Existing passkey' }]
                : { error: 'freshAuthenticationRequired' } : { enablePasskey: true } }));
        const wrapper = mount(MfaSettings);
        await flushPromises();
        expect(wrapper.find('[data-testid="passkey-list-session-expired"]').exists()).toBe(true);
        authorized = true;
        await wrapper.vm.loadPasskeys();
        await flushPromises();
        expect(wrapper.find('[data-testid="passkey-list-session-expired"]').exists()).toBe(false);
        expect(wrapper.find('[data-testid="passkey-list-empty"]').exists()).toBe(false);
        expect(wrapper.text()).toContain('Existing passkey');
        wrapper.unmount();
    });

    it.each([403, 500])('does not represent a failed inventory request as empty or ask for verification (%s)', async status => {
        fetch.mockImplementation(url => Promise.resolve(url === '/api/passkey/list'
            ? { ok: false, status, json: async () => ({ error: 'accessDenied' }) }
            : jsonResponse({ enablePasskey: true })));
        const wrapper = mount(MfaSettings);
        await flushPromises();
        expect(wrapper.find('[data-testid="passkey-list-empty"]').exists()).toBe(false);
        expect(wrapper.find('[data-testid="passkey-list-session-expired"]').exists()).toBe(false);
        expect(wrapper.get('.passkey-section [role="alert"]').text()).toBe('mfa.errors.listPasskeysFailed');
        expect(fetch.mock.calls.some(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toBe(false);
        wrapper.unmount();
    });
    it.each(['email-send', 'email-verify'].flatMap(operation => [true, false].map(confirm => [operation, confirm])))('explains denied %s management before confirmation (%s)', async (operation, confirm) => {
        const wrapper = mount(MfaSettings);
        await flushPromises();
        const endpoint = operation === 'email-send' ? '/api/account/mfa/email/send'
            : '/api/account/mfa/email/verify';
        fetch.mockImplementation((url) => Promise.resolve({ ok: false,
            json: async () => url === endpoint ? { error: 'freshAuthenticationRequired' } : {} }));
        if (operation.startsWith('email')) wrapper.vm.openEmailMfaSetup();
        await flushPromises();
        if (operation === 'email-verify') {
            await wrapper.get('#email-mfa-code').setValue('123456');
            wrapper.vm.emailCodeSent = true;
        }
        await (operation === 'email-send' ? wrapper.vm.sendEmailMfaCode()
            : wrapper.vm.verifyEmailMfa());
        await flushPromises();
        expect(fetch.mock.calls.filter(([url]) => url === endpoint)).toHaveLength(1);
        expect(wrapper.get('[data-testid="mfa-reauthentication-notice"]').text()).toContain('mfa.reauthenticationActions.email-enable');
        expect(wrapper.findAll('.modal-overlay')).toHaveLength(1);
        expect(wrapper.vm.emailMfaCode).toBe('');
        expect(fetch.mock.calls.some(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toBe(false);
        expect(sessionStorage.getItem('mfa.continuation')).toBeNull();
        await wrapper.get(`[data-testid="mfa-reauthentication-${confirm ? 'continue' : 'cancel'}"]`).trigger('click');
        await flushPromises();
        if (confirm) {
            expect(fetch).toHaveBeenCalledWith('/api/account/mfa/reauthenticate?returnToProfile=true&action=email-enable',
                { method: 'POST', credentials: 'include' });
        } else {
            expect(fetch.mock.calls.some(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toBe(false);
            expect(wrapper.find('[data-testid="mfa-reauthentication-notice"]').exists()).toBe(false);
        }
        wrapper.unmount();
    });

    it.each(['totp', 'email', 'passkey'].flatMap(factor => [true, false].map(confirm => [factor, confirm])))('explains denied %s removal and handles confirmation (%s) without retrying the mutation', async (factor, confirm) => {
        const wrapper = mount(MfaSettings);
        await flushPromises();
        const endpoint = factor === 'totp' ? '/api/account/mfa/disable'
            : factor === 'email' ? '/api/account/mfa/email/disable' : '/api/passkey/7';
        fetch.mockImplementation((url) => Promise.resolve(url === endpoint
            ? { ok: false, json: async () => ({ error: 'freshAuthenticationRequired' }) }
            : { ok: false, json: async () => ({}) }));
        if (factor === 'passkey') wrapper.vm.confirmDeletePasskey({ id: 7 });
        if (factor === 'totp') { wrapper.vm.showDisableModal = true; wrapper.vm.disablePassword = 'confirmation'; }
        await (factor === 'totp' ? wrapper.vm.disableMfa()
            : factor === 'email' ? wrapper.vm.disableEmailMfa() : wrapper.vm.deletePasskey());
        await flushPromises();
        expect(fetch.mock.calls.filter(([url]) => url === endpoint)).toHaveLength(1);
        expect(wrapper.get('[data-testid="mfa-reauthentication-notice"]').text()).toContain(factor === 'totp'
            ? enMfa.disableReauthenticationDescription
            : `mfa.reauthenticationActions.${factor === 'passkey' ? 'passkey-delete' : `${factor}-disable`}`);
        expect(wrapper.findAll('.modal-overlay')).toHaveLength(1);
        expect(wrapper.vm.disablePassword).toBe('');
        expect(fetch.mock.calls.some(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toBe(false);
        await wrapper.get(`[data-testid="mfa-reauthentication-${confirm ? 'continue' : 'cancel'}"]`).trigger('click');
        await flushPromises();
        expect(fetch.mock.calls.filter(([url]) => url === endpoint)).toHaveLength(1);
        if (confirm) expect(fetch).toHaveBeenCalledWith(`/api/account/mfa/reauthenticate?forRemoval=true&action=${factor === 'passkey' ? 'passkey-delete' : `${factor}-disable`}`, { method: 'POST', credentials: 'include' });
        else expect(fetch.mock.calls.some(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toBe(false);
        wrapper.unmount();
    });

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

            if (url === '/api/account/recovery-codes') {
                return Promise.resolve(jsonResponse({ hasBatch: true, remainingCodes: 0, legacyRemainingCodes: 4, totalRemainingCodes: 4 }));
            }

            return Promise.resolve(jsonResponse({}));
        });
    }

    it.each(['en-US', 'zh-TW'].flatMap(locale => [true, false].flatMap(hasPassword =>
        [true, false].map(emailMfaEnabled => [locale, hasPassword, emailMfaEnabled]))))(
        'explains TOTP disable before and after reauthentication and preserves confirmation (%s, password=%s, email=%s)',
        async (locale, hasPassword, emailMfaEnabled) => {
            i18nState.locale = locale;
            const copy = locale === 'zh-TW' ? zhMfa : enMfa;
            const error = hasPassword ? 'invalidPassword' : 'invalidCode';
            const disableHandler = vi.fn()
                .mockResolvedValueOnce(jsonResponse({ error }, false))
                .mockResolvedValueOnce(jsonResponse({}));
            fetch.mockImplementation(url => {
                if (url === '/api/account/mfa/disable') return disableHandler();
                return Promise.resolve(jsonResponse(url === '/api/account/mfa/status'
                    ? { twoFactorEnabled: true, hasPassword, emailMfaEnabled, enableTotpMfa: true, enableEmailMfa: true }
                    : url === '/api/passkey/list' ? []
                    : url.startsWith('/api/account/mfa/reauthenticate') ? { loginUrl: '/Account/Login?reauthenticate=true' }
                    : {}));
            });
            const assign = vi.spyOn(window.location, 'assign').mockImplementation(() => {});
            const props = { profile: { userId: 'account-1', email: 'test@example.com' } };
            const wrapper = mount(MfaSettings, { props });
            let resumed;
            try {
                await flushPromises();
                await wrapper.get('.mfa-content > .mfa-status.mfa-enabled .btn-danger').trigger('click');
                expect(wrapper.get('#mfa-reauthentication-purpose').text()).toBe(copy.disableReauthenticationDescription);
                expect(wrapper.find('.modal-content input').exists()).toBe(false);
                expect(fetch.mock.calls.some(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toBe(false);
                expect(disableHandler).not.toHaveBeenCalled();

                await wrapper.get('[data-testid="mfa-reauthentication-continue"]').trigger('click');
                await flushPromises();
                expect(fetch).toHaveBeenCalledWith('/api/account/mfa/reauthenticate?forRemoval=true&action=totp-disable',
                    { method: 'POST', credentials: 'include' });
                expect(assign).toHaveBeenCalledWith('/Account/Login?reauthenticate=true');
                wrapper.unmount();

                resumed = mount(MfaSettings, { props });
                await flushPromises();
                expect(sessionStorage.getItem('mfa.continuation')).toBeNull();
                expect(resumed.find('[data-testid="mfa-reauthentication-notice"]').exists()).toBe(false);
                expect(resumed.get('.modal-content > p').text()).toBe(emailMfaEnabled ? copy.disableWarningWithEmail : copy.disableWarning);
                expect(resumed.get('.modal-content .field-hint').text()).toBe(hasPassword ? copy.disablePasswordConfirmation : copy.disableTotpConfirmation);
                expect(disableHandler).not.toHaveBeenCalled();
                if (hasPassword) {
                    expect(resumed.get('.password-section input').attributes('type')).toBe('password');
                } else {
                    expect(resumed.get('.password-section input').attributes('type')).toBe('text');
                }
                await resumed.get('.password-section input').setValue(hasPassword ? 'dummy' : '123456');
                await resumed.get('.modal-content .btn-danger').trigger('click');
                await flushPromises();
                expect(fetch).toHaveBeenCalledWith('/api/account/mfa/disable', {
                    method: 'POST', headers: { 'Content-Type': 'application/json' }, credentials: 'include',
                    body: JSON.stringify(hasPassword ? { password: 'dummy' } : { totpCode: '123456' })
                });
                expect(resumed.get('.modal-content .error-message').text()).toBe(`mfa.errors.${error}`);
                expect(resumed.emitted('status-changed')).toBeUndefined();
                await resumed.get('.modal-content .btn-danger').trigger('click');
                await flushPromises();
                expect(disableHandler).toHaveBeenCalledTimes(2);
                expect(resumed.find('.modal-content').exists()).toBe(false);
                expect(resumed.emitted('status-changed')).toHaveLength(1);
                expect(fetch.mock.calls.filter(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toHaveLength(1);
                expect(fetch.mock.calls.some(([url]) => url === '/api/account/mfa/email/disable')).toBe(false);
            } finally {
                resumed?.unmount();
                if (!resumed) wrapper.unmount();
                assign.mockRestore();
            }
        });


    beforeEach(() => {
        sessionStorage.clear();
        i18nState.locale = 'en-US';
        vi.useRealTimers();
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

    afterEach(() => {
        vi.useRealTimers();
    });

    function mockContinuationAccount(emailMfaEnabled = true, denyRemoval = false) {
        fetch.mockImplementation((url) => Promise.resolve(jsonResponse(
            url === '/api/profile' ? { userId: 'account-a', email: 'test@example.com' }
            : url === '/api/passkey/list' ? []
            : url === '/api/account/mfa/status' ? { emailMfaEnabled, enableEmailMfa: true, enableTotpMfa: true }
            : url === '/api/account/mfa/email/disable' && denyRemoval ? { error: 'freshAuthenticationRequired' }
            : {}, !(url === '/api/account/mfa/email/disable' && denyRemoval))));
    }

    it('resumes Email MFA removal once for the same account and does not repeat on remount', async () => {
        mockContinuationAccount();
        saveMfaContinuation('account-a', 'email-disable');
        const wrapper = mount(MfaSettings);
        await flushPromises();
        expect(fetch.mock.calls.filter(([url]) => url === '/api/account/mfa/email/disable')).toHaveLength(1);
        wrapper.unmount();
        const reloaded = mount(MfaSettings);
        await flushPromises();
        expect(fetch.mock.calls.filter(([url]) => url === '/api/account/mfa/email/disable')).toHaveLength(1);
        reloaded.unmount();
    });

    it('does not replay removal when a different account signs in', async () => {
        mockContinuationAccount();
        saveMfaContinuation('account-b', 'email-disable');
        const wrapper = mount(MfaSettings);
        await flushPromises();
        expect(fetch.mock.calls.some(([url]) => url === '/api/account/mfa/email/disable')).toBe(false);
        wrapper.unmount();
    });

    it('does not start a reauthentication loop if the resumed removal is denied', async () => {
        mockContinuationAccount(true, true);
        saveMfaContinuation('account-a', 'email-disable');
        const wrapper = mount(MfaSettings);
        await flushPromises();
        expect(fetch.mock.calls.filter(([url]) => url === '/api/account/mfa/email/disable')).toHaveLength(1);
        expect(fetch.mock.calls.some(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toBe(false);
        expect(wrapper.find('.email-mfa-section .error-message').exists()).toBe(true);
        wrapper.unmount();
    });

    it('starts TOTP setup without treating the click event as a removal request', async () => {
        mockContinuationAccount(false);
        const wrapper = mount(MfaSettings);
        await flushPromises();
        await wrapper.get('.mfa-status.mfa-disabled > .btn-enable').trigger('click');
        await flushPromises();
        expect(wrapper.get('[data-testid="mfa-reauthentication-notice"]').text()).toContain('mfa.reauthenticationActions.totp-add');
        expect(fetch.mock.calls.some(([url]) => url.includes('reauthenticate'))).toBe(false);
        await wrapper.get('[data-testid="mfa-reauthentication-cancel"]').trigger('click');
        expect(fetch.mock.calls.some(([url]) => url.includes('reauthenticate'))).toBe(false);
        await wrapper.get('[data-testid="totp-add"]').trigger('click');
        await wrapper.get('[data-testid="mfa-reauthentication-continue"]').trigger('click');
        await flushPromises();
        expect(fetch).toHaveBeenCalledWith('/api/account/mfa/reauthenticate?action=totp-add', { method: 'POST', credentials: 'include' });
        expect(fetch.mock.calls.some(([url]) => url.includes('forRemoval'))).toBe(false);
        wrapper.unmount();
    });

    it.each(['en-US', 'zh-TW'])('reports Email-only enrollment as enabled and the authenticator as not enabled in %s without changing passkey policy', async locale => {
        i18nState.locale = locale;
        const copy = locale === 'zh-TW' ? zhMfa : enMfa;
        mockContinuationAccount(true);
        const wrapper = mount(MfaSettings);
        await flushPromises();
        expect(wrapper.get('[data-testid="mfa-overall-status"]').text()).toBe(copy.otpSummaryEnabled);
        expect(wrapper.get('.mfa-status.mfa-disabled h3').text()).toBe('mfa.authenticatorTitle');
        expect(wrapper.get('.mfa-status.mfa-disabled p').text()).toContain(
            locale === 'zh-TW' ? '驗證器 App 尚未啟用' : 'Authenticator app is not enabled');
        expect(wrapper.get('[data-testid="totp-add"]').text()).toBe('mfa.addAuthenticator');
        wrapper.vm.requireMfaForPasskey = true;
        await flushPromises();
        expect(wrapper.vm.passkeyBlockedByPolicy).toBe(false);
        wrapper.unmount();
    });

    it.each(['en-US', 'zh-TW'])('reports TOTP-only enrollment in the OTP summary in %s', async locale => {
        i18nState.locale = locale;
        const copy = locale === 'zh-TW' ? zhMfa : enMfa;
        mockMfaAccount({ hasPassword: true, recoveryHandler: () => jsonResponse({}) });
        const wrapper = mount(MfaSettings);
        await flushPromises();

        expect(wrapper.get('[data-testid="mfa-overall-status"]').text()).toBe(copy.otpSummaryEnabled);
        expect(wrapper.get('.mfa-status.mfa-enabled h3').text()).toBe('mfa.authenticatorTitle');
        expect(wrapper.get('.mfa-status.mfa-enabled p').text()).toBe('mfa.authenticatorEnabled');
        wrapper.unmount();
    });

    it('confirms Email Enable purpose before enrollment and cancels without a request or storage change', async () => {
        mockContinuationAccount(false);
        const wrapper = mount(MfaSettings);
        await flushPromises();
        const beforeCalls = fetch.mock.calls.length;
        const beforeStorage = sessionStorage.getItem('mfa.continuation');
        await wrapper.get('[data-testid="email-mfa-enable"]').trigger('click');
        expect(wrapper.get('[data-testid="mfa-reauthentication-notice"]').text()).toContain('mfa.reauthenticationActions.email-enable');
        expect(wrapper.find('.email-mfa-modal').exists()).toBe(false);
        expect(fetch).toHaveBeenCalledTimes(beforeCalls);
        expect(sessionStorage.getItem('mfa.continuation')).toBe(beforeStorage);
        await wrapper.get('[data-testid="mfa-reauthentication-cancel"]').trigger('click');
        expect(fetch).toHaveBeenCalledTimes(beforeCalls);
        expect(sessionStorage.getItem('mfa.continuation')).toBe(beforeStorage);
        expect(wrapper.find('.email-mfa-modal').exists()).toBe(false);
        wrapper.unmount();
    });

    it('starts Email identity confirmation once, then resumes the same setup without sending a code', async () => {
        mockContinuationAccount(false);
        const wrapper = mount(MfaSettings);
        await flushPromises();
        fetch.mockResolvedValueOnce(jsonResponse({ loginUrl: '/Account/Login?reauthenticate=true' }));
        await wrapper.get('[data-testid="email-mfa-enable"]').trigger('click');
        await wrapper.get('[data-testid="mfa-reauthentication-continue"]').trigger('click');
        await wrapper.vm.continueReauthentication();
        await flushPromises();
        expect(fetch.mock.calls.filter(([url]) => url.startsWith('/api/account/mfa/reauthenticate'))).toEqual([
            ['/api/account/mfa/reauthenticate?returnToProfile=true&action=email-enable', { method: 'POST', credentials: 'include' }]
        ]);
        expect(wrapper.find('.email-mfa-modal').exists()).toBe(false);
        expect(JSON.parse(sessionStorage.getItem('mfa.continuation')).action).toBe('email-enable');
        wrapper.unmount();
        mockContinuationAccount(false);
        const resumed = mount(MfaSettings);
        await flushPromises();
        expect(resumed.find('.email-mfa-modal').exists()).toBe(true);
        expect(resumed.find('[data-testid="mfa-reauthentication-notice"]').exists()).toBe(false);
        expect(fetch.mock.calls.some(([url]) => url.endsWith('/email/send'))).toBe(false);
        expect(sessionStorage.getItem('mfa.continuation')).toBeNull();
        resumed.unmount();
    });

    it('cancels reauthentication without a request, storage change or navigation', async () => {
        mockContinuationAccount(false);
        const wrapper = mount(MfaSettings);
        await flushPromises();
        saveMfaContinuation('account-a', 'email-disable');
        const storedIntent = sessionStorage.getItem('mfa.continuation');
        const previousUrl = window.location.href;
        const previousCalls = fetch.mock.calls.length;
        await wrapper.get('[data-testid="totp-add"]').trigger('click');
        await wrapper.get('[data-testid="mfa-reauthentication-cancel"]').trigger('click');
        expect(sessionStorage.getItem('mfa.continuation')).toBe(storedIntent);
        expect(fetch).toHaveBeenCalledTimes(previousCalls);
        expect(window.location.href).toBe(previousUrl);
        expect(wrapper.find('[data-testid="mfa-reauthentication-notice"]').exists()).toBe(false);
        wrapper.unmount();
    });

    it.each([true, false])('explains passkey registration reauthentication before confirmation (%s)', async (confirm) => {
        mockContinuationAccount(false);
        mockRegisterPasskey.mockRejectedValueOnce(new Error('mfa.errors.freshAuthenticationRequired'));
        const wrapper = mount(MfaSettings);
        await flushPromises();
        await wrapper.vm.registerNewPasskey();
        await flushPromises();
        expect(wrapper.get('[data-testid="mfa-reauthentication-notice"]').text()).toContain('mfa.reauthenticationActions.passkey-register');
        expect(fetch.mock.calls.some(([url]) => url.includes('/reauthenticate'))).toBe(false);
        expect(sessionStorage.getItem('mfa.continuation')).toBeNull();
        await wrapper.get(`[data-testid="mfa-reauthentication-${confirm ? 'continue' : 'cancel'}"]`).trigger('click');
        await flushPromises();
        expect(fetch.mock.calls.filter(([url]) => url.includes('/reauthenticate'))).toHaveLength(confirm ? 1 : 0);
        expect(mockRegisterPasskey).toHaveBeenCalledOnce();
        wrapper.unmount();
    });

    it('saves only the requested action after confirmation and blocks duplicate starts and cancellation while pending', async () => {
        mockContinuationAccount(false);
        const wrapper = mount(MfaSettings);
        await flushPromises();
        let release;
        const pending = new Promise(resolve => { release = resolve; });
        fetch.mockImplementation(() => pending);
        await wrapper.vm.startSetup(false, 'email-enable');
        expect(sessionStorage.getItem('mfa.continuation')).toBeNull();
        const request = wrapper.vm.continueReauthentication();
        await wrapper.vm.continueReauthentication();
        wrapper.vm.cancelReauthentication();
        await flushPromises();
        expect(fetch.mock.calls.filter(([url]) => url.includes('/reauthenticate'))).toHaveLength(1);
        expect(wrapper.get('[data-testid="mfa-reauthentication-cancel"]').element.disabled).toBe(true);
        expect(wrapper.get('[data-testid="mfa-reauthentication-continue"]').element.disabled).toBe(true);
        expect(JSON.parse(sessionStorage.getItem('mfa.continuation'))).toEqual({
            userId: 'account-a', action: 'email-enable', targetId: null, expiresAt: expect.any(Number)
        });
        release(jsonResponse({}, false));
        await request;
        expect(sessionStorage.getItem('mfa.continuation')).toBeNull();
        wrapper.unmount();
    });

    it.each([true, false, undefined])('uses authoritative pending state (%s) during Email cooldown, then allows a new send', async (hasPendingCode) => {
        vi.useFakeTimers();
        mockContinuationAccount(false);
        const wrapper = mount(MfaSettings);
        await flushPromises();
        const send = vi.fn().mockResolvedValueOnce(jsonResponse({ remainingSeconds: 12, hasPendingCode }, false))
            .mockResolvedValueOnce(jsonResponse({ success: true, hasPendingCode: true, remainingSeconds: 60 }));
        fetch.mockImplementation(url => url.endsWith('/email/send') ? send() : Promise.resolve(jsonResponse({ error: 'invalidOrExpiredCode' }, false)));
        wrapper.vm.openEmailMfaSetup();
        await flushPromises();
        await wrapper.get('[data-testid="email-mfa-send"]').trigger('click');
        await flushPromises();
        const input = wrapper.get('#email-mfa-code');
        input.element.value = '123456';
        await input.trigger('change');
        expect(wrapper.get('[data-testid="email-mfa-verify"]').element.disabled).toBe(hasPendingCode !== true);
        expect(wrapper.get('[data-testid="email-mfa-code-guidance"]').text()).toBe(hasPendingCode === true ? 'mfa.emailCodeHelp' : 'mfa.emailCodeUnavailable');
        expect(wrapper.get('[data-testid="email-mfa-send"]').element.disabled).toBe(true);
        await wrapper.vm.verifyEmailMfa();
        expect(fetch.mock.calls.filter(([url]) => url.endsWith('/email/verify'))).toHaveLength(hasPendingCode === true ? 1 : 0);
        await vi.advanceTimersByTimeAsync(12000);
        expect(wrapper.get('[data-testid="email-mfa-send"]').element.disabled).toBe(false);
        await wrapper.get('[data-testid="email-mfa-send"]').trigger('click');
        await flushPromises();
        expect(send).toHaveBeenCalledTimes(2);
        expect(wrapper.get('[data-testid="email-mfa-verify"]').element.disabled).toBe(false);
        input.element.value = '654321';
        window.dispatchEvent(new Event('pageshow'));
        await flushPromises();
        expect(wrapper.vm.emailMfaCode).toBe('654321');
        wrapper.unmount();
    });

    it('synchronizes restored Email input on focus and keeps pending verification locked until retry', async () => {
        mockContinuationAccount(false);
        const wrapper = mount(MfaSettings);
        await flushPromises();
        wrapper.vm.openEmailMfaSetup();
        wrapper.vm.emailCodeSent = true;
        await flushPromises();
        const input = wrapper.get('#email-mfa-code');
        input.element.value = '12 3456';
        await input.trigger('focus');
        expect(input.element.value).toBe('123456');
        expect(wrapper.get('[data-testid="email-mfa-verify"]').element.disabled).toBe(false);
        let release;
        fetch.mockImplementation(() => new Promise(resolve => { release = resolve; }));
        const request = wrapper.vm.verifyEmailMfa();
        await wrapper.vm.verifyEmailMfa();
        wrapper.vm.closeEmailMfaSetup();
        await flushPromises();
        expect(wrapper.get('[data-testid="email-mfa-verify"]').element.disabled).toBe(true);
        expect(wrapper.get('#email-mfa-code').element.disabled).toBe(true);
        window.dispatchEvent(new Event('pageshow'));
        await flushPromises();
        expect(wrapper.get('[data-testid="email-mfa-verify"]').element.disabled).toBe(true);
        expect(wrapper.find('.email-mfa-modal').exists()).toBe(true);
        expect(fetch.mock.calls.filter(([url]) => url.endsWith('/email/verify'))).toHaveLength(1);
        release(jsonResponse({ error: 'invalidOrExpiredCode' }, false));
        await request;
        await flushPromises();
        expect(wrapper.get('[data-testid="email-mfa-verify"]').element.disabled).toBe(false);
        wrapper.unmount();
    });

    it.each([true, false])('uses remaining Email challenge state after verification denial (%s)', async (hasPendingCode) => {
        vi.useFakeTimers();
        mockContinuationAccount(false);
        const wrapper = mount(MfaSettings);
        await flushPromises();
        wrapper.vm.openEmailMfaSetup();
        await flushPromises();
        fetch.mockImplementation(url => Promise.resolve(url.endsWith('/email/send')
            ? jsonResponse({ success: true, hasPendingCode: true, remainingSeconds: 60 })
            : jsonResponse({ error: 'invalidOrExpiredCode', hasPendingCode }, false)));
        await wrapper.vm.sendEmailMfaCode();
        await wrapper.get('#email-mfa-code').setValue('123456');
        await wrapper.vm.verifyEmailMfa();
        await flushPromises();
        expect(wrapper.get('[data-testid="email-mfa-verify"]').element.disabled).toBe(!hasPendingCode);
        expect(wrapper.get('[data-testid="email-mfa-code-guidance"]').text()).toBe(hasPendingCode ? 'mfa.emailCodeHelp' : 'mfa.emailCodeUnavailable');
        await wrapper.vm.verifyEmailMfa();
        expect(fetch.mock.calls.filter(([url]) => url.endsWith('/email/verify'))).toHaveLength(hasPendingCode ? 2 : 1);
        wrapper.unmount();
    });

    it('blocks repeated TOTP removal and cancellation until the first response completes', async () => {
        const wrapper = mount(MfaSettings);
        await flushPromises();
        wrapper.vm.showDisableModal = true;
        wrapper.vm.disablePassword = 'confirmation';
        let release;
        const pending = new Promise(resolve => { release = resolve; });
        fetch.mockImplementation(url => url === '/api/account/mfa/disable'
            ? pending : Promise.resolve(jsonResponse({})));
        const first = wrapper.vm.disableMfa();
        await wrapper.vm.disableMfa();
        await flushPromises();
        expect(fetch.mock.calls.filter(([url]) => url === '/api/account/mfa/disable')).toHaveLength(1);
        expect(wrapper.get('.modal-content .btn-danger').element.disabled).toBe(true);
        wrapper.vm.cancelDisable();
        expect(wrapper.vm.showDisableModal).toBe(true);
        release(jsonResponse({ error: 'invalidPassword' }, false));
        await first;
        await flushPromises();
        expect(wrapper.get('.modal-content .btn-danger').element.disabled).toBe(false);
        wrapper.vm.cancelDisable();
        expect(wrapper.vm.showDisableModal).toBe(false);
        wrapper.unmount();
    });

    it('renders passkey section', async () => {
        const wrapper = mount(MfaSettings);
        await flushPromises();
        
        expect(wrapper.find('.passkey-section').exists()).toBe(true);
        expect(wrapper.find('.passkey-section h3').text()).toBe(enMfa.passkey.signInMethodTitle);
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

    it('does not start a second registration while the browser credential request is pending', async () => {
        let release;
        mockRegisterPasskey.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
        const wrapper = mount(MfaSettings);
        await flushPromises();
        const first = wrapper.vm.registerNewPasskey();
        await wrapper.vm.registerNewPasskey();
        await flushPromises();
        expect(mockRegisterPasskey).toHaveBeenCalledOnce();
        expect(wrapper.get('.passkey-section .section-action button').element.disabled).toBe(true);
        release({ success: true });
        await first;
        await flushPromises();
        expect(wrapper.get('.passkey-section .section-action button').element.disabled).toBe(false);
        wrapper.unmount();
    });

    it('removes the confirmed deleted key even when stamp rotation denies the next list refresh', async () => {
        const wrapper = mount(MfaSettings);
        await flushPromises();
        wrapper.vm.passkeys = [{ id: 1 }, { id: 2 }];
        wrapper.vm.confirmDeletePasskey({ id: 1 });
        fetch.mockImplementation(url => Promise.resolve(url === '/api/passkey/1'
            ? jsonResponse({ success: true }) : { ...jsonResponse({ error: 'sessionExpired' }, false), status: 401 }));
        await wrapper.vm.deletePasskey();
        await flushPromises();
        expect(wrapper.findAll('.passkey-item')).toHaveLength(0);
        expect(wrapper.vm.passkeys).toHaveLength(0);
        expect(wrapper.find('.modal-content').exists()).toBe(false);
        expect(wrapper.get('[data-testid="passkey-list-session-expired"]').text()).toContain(enMfa.passkey.sessionExpired);
        expect(wrapper.find('[data-testid="passkey-list-empty"]').exists()).toBe(false);
        wrapper.unmount();
    });

    it.each(['en-US', 'zh-TW'])('translates fresh authentication errors in %s', async (locale) => {
        i18nState.locale = locale;
        mockRegisterPasskey.mockRejectedValueOnce(new Error('mfa.errors.freshAuthenticationRequired'));
        const wrapper = mount(MfaSettings);
        await flushPromises();

        await wrapper.vm.registerNewPasskey();
        await flushPromises();

        const message = (locale === 'zh-TW' ? zhMfa : enMfa).errors.freshAuthenticationRequired;
        expect(wrapper.find('.passkey-section .error-message').text()).toBe(message);
        expect(message).not.toContain('freshAuthenticationRequired');
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
        await wrapper.get('[data-testid="mfa-reauthentication-continue"]').trigger('click');
        await flushPromises();

        expect(fetch).toHaveBeenCalledWith('/api/account/mfa/reauthenticate?action=totp-add', {
            method: 'POST',
            credentials: 'include'
        });
        expect(wrapper.find('[role="alert"]').text()).toBe('mfa.errors.setupFailed');
        expect(wrapper.find('.btn-enable').attributes('disabled')).toBeUndefined();
    });

    it('explains reauthentication before starting TOTP enrollment', async () => {
        fetch.mockResolvedValue({
            ok: true,
            json: () => Promise.resolve({
                email: 'test@example.com',
                twoFactorEnabled: false,
                enableTotpMfa: true,
                enableEmailMfa: false,
                enablePasskey: false
            })
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();

        expect(wrapper.find('.btn-enable').text()).toBe('mfa.addAuthenticator');
        await wrapper.get('[data-testid="totp-add"]').trigger('click');
        expect(wrapper.get('[data-testid="mfa-reauthentication-notice"]').text()).toContain('mfa.reauthenticationDescription');
    });

    it.each(['en-US', 'zh-TW'])('scopes the disabled summary to OTP for Passkey-only enrollment in %s without changing registration policy', async locale => {
        i18nState.locale = locale;
        const copy = locale === 'zh-TW' ? zhMfa : enMfa;
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

        expect(wrapper.get('[data-testid="mfa-overall-status"]').text()).toBe(copy.otpSummaryDisabled);
        expect(wrapper.get('[data-testid="mfa-overall-status"]').text()).not.toBe(copy.notEnabled);
        expect(wrapper.get('.passkey-section h3').text()).toBe(copy.passkey.signInMethodTitle);
        expect(wrapper.get('.passkey-list h4').text()).toBe('mfa.passkey.registered');
        expect(wrapper.get('.pk-name').text()).toBe('My Passkey');
        expect(wrapper.vm.hasMfaEnabled).toBe(false);
        expect(wrapper.vm.passkeyBlockedByPolicy).toBe(true);
        expect(wrapper.get('.passkey-section .section-action button').element.disabled).toBe(true);
        expect(wrapper.get('.passkey-warning').text()).toBe('mfa.passkey.nonCompliantWarning');

        wrapper.vm.requireMfaForPasskey = false;
        await flushPromises();
        expect(wrapper.vm.passkeyBlockedByPolicy).toBe(false);
        expect(wrapper.get('.passkey-section .section-action button').element.disabled).toBe(false);
        expect(wrapper.find('.passkey-warning').exists()).toBe(false);
        expect(wrapper.get('.pk-name').text()).toBe('My Passkey');
        wrapper.unmount();
    });

    it('requires an emailed code before enabling Email MFA', async () => {
        fetch.mockImplementation((url) => {
            if (url === '/api/account/mfa/status') {
                return Promise.resolve(jsonResponse({
                    twoFactorEnabled: false,
                    emailMfaEnabled: false,
                    enableTotpMfa: true,
                    enableEmailMfa: true,
                    enablePasskey: false
                }));
            }
            if (url === '/api/profile') {
                return Promise.resolve(jsonResponse({ email: 'test@example.com' }));
            }
            if (url === '/api/account/mfa/email/send') {
                return Promise.resolve(jsonResponse({ success: true, hasPendingCode: true, remainingSeconds: 60 }));
            }
            if (url === '/api/account/mfa/email/verify') {
                return Promise.resolve(jsonResponse({ success: true }));
            }
            if (url === '/api/passkey/list') {
                return Promise.resolve(jsonResponse([]));
            }
            if (url === '/api/account/security-policy') {
                return Promise.resolve(jsonResponse({ requireMfaForPasskey: false }));
            }
            return Promise.resolve(jsonResponse({}));
        });

        const wrapper = mount(MfaSettings, {
            props: { csrfToken: 'test-csrf' }
        });
        await flushPromises();
        await wrapper.vm.openEmailMfaSetup();
        await flushPromises();

        expect(wrapper.find('.email-mfa-modal').exists()).toBe(true);
        expect(fetch.mock.calls.some(
            ([url]) => url === '/api/account/mfa/email/send')).toBe(false);

        await wrapper.get('[data-testid="email-mfa-send"]').trigger('click');
        await flushPromises();

        expect(fetch).toHaveBeenCalledWith('/api/account/mfa/email/send', {
            method: 'POST',
            headers: { 'X-XSRF-TOKEN': 'test-csrf' },
            credentials: 'include'
        });

        await wrapper.get('#email-mfa-code').setValue('123456');
        await wrapper.vm.verifyEmailMfa();
        await flushPromises();

        const verifyRequest = fetch.mock.calls.find(
            ([url]) => url === '/api/account/mfa/email/verify');
        expect(verifyRequest).toBeTruthy();
        expect(verifyRequest[1].headers).toEqual({
            'Content-Type': 'application/json',
            'X-XSRF-TOKEN': 'test-csrf'
        });
        expect(JSON.parse(verifyRequest[1].body)).toEqual({ code: '123456' });
        expect(fetch.mock.calls.some(
            ([url]) => url === '/api/account/mfa/email/enable')).toBe(false);
        expect(wrapper.find('.email-mfa-modal').exists()).toBe(false);

        wrapper.vm.openEmailMfaSetup();
        await flushPromises();
        const sendButton = wrapper.get('[data-testid="email-mfa-send"]');
        expect(sendButton.attributes('disabled')).toBeUndefined();
        expect(sendButton.text()).toBe('mfa.sendCode');
        wrapper.unmount();
    });

    it('keeps Email MFA verification open when the code is invalid', async () => {
        fetch.mockImplementation((url) => {
            if (url === '/api/account/mfa/status') {
                return Promise.resolve(jsonResponse({
                    twoFactorEnabled: false,
                    emailMfaEnabled: false,
                    enableTotpMfa: true,
                    enableEmailMfa: true,
                    enablePasskey: false
                }));
            }
            if (url === '/api/profile') {
                return Promise.resolve(jsonResponse({ email: 'test@example.com' }));
            }
            if (url === '/api/account/mfa/email/send') {
                return Promise.resolve(jsonResponse({ success: true, hasPendingCode: true, remainingSeconds: 60 }));
            }
            if (url === '/api/account/mfa/email/verify') {
                return Promise.resolve(jsonResponse({
                    success: false,
                    error: 'invalidOrExpiredCode'
                }));
            }
            return Promise.resolve(jsonResponse({}));
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();
        await wrapper.vm.openEmailMfaSetup();
        await flushPromises();
        await wrapper.get('[data-testid="email-mfa-send"]').trigger('click');
        await flushPromises();
        await wrapper.get('#email-mfa-code').setValue('000000');
        await wrapper.vm.verifyEmailMfa();
        await flushPromises();

        expect(wrapper.find('.email-mfa-modal').exists()).toBe(true);
        expect(wrapper.get('.email-mfa-modal [role="alert"]').text())
            .toBe('mfa.errors.invalidOrExpiredCode');
        wrapper.unmount();
    });

    it('starts a live resend countdown only after send is requested', async () => {
        vi.useFakeTimers();
        fetch.mockImplementation((url) => {
            if (url === '/api/account/mfa/status') {
                return Promise.resolve(jsonResponse({
                    twoFactorEnabled: false,
                    emailMfaEnabled: false,
                    enableTotpMfa: true,
                    enableEmailMfa: true,
                    enablePasskey: false
                }));
            }
            if (url === '/api/profile') {
                return Promise.resolve(jsonResponse({ email: 'test@example.com' }));
            }
            if (url === '/api/account/mfa/email/send') {
                return Promise.resolve(jsonResponse({ success: true, hasPendingCode: true, remainingSeconds: 60 }));
            }
            return Promise.resolve(jsonResponse({}));
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();
        wrapper.vm.openEmailMfaSetup();
        await flushPromises();

        const sendButton = wrapper.get('[data-testid="email-mfa-send"]');
        expect(sendButton.text()).toBe('mfa.sendCode');
        expect(sendButton.attributes('disabled')).toBeUndefined();

        await sendButton.trigger('click');
        await flushPromises();

        expect(sendButton.text()).toBe('mfa.resendCodeIn:60');
        expect(sendButton.attributes('disabled')).toBeDefined();

        await vi.advanceTimersByTimeAsync(1000);
        expect(sendButton.text()).toBe('mfa.resendCodeIn:59');

        wrapper.vm.closeEmailMfaSetup();
        wrapper.vm.openEmailMfaSetup();
        await vi.advanceTimersByTimeAsync(1000);
        expect(wrapper.get('[data-testid="email-mfa-send"]').text())
            .toBe('mfa.resendCodeIn:58');
        wrapper.unmount();
    });

    it('turns a server cooldown into a localized live countdown', async () => {
        vi.useFakeTimers();
        fetch.mockImplementation((url) => {
            if (url === '/api/account/mfa/status') {
                return Promise.resolve(jsonResponse({
                    emailMfaEnabled: false,
                    enableTotpMfa: true,
                    enableEmailMfa: true,
                    enablePasskey: false
                }));
            }
            if (url === '/api/profile') {
                return Promise.resolve(jsonResponse({ email: 'test@example.com' }));
            }
            if (url === '/api/account/mfa/email/send') {
                return Promise.resolve(jsonResponse({
                    remainingSeconds: 48,
                    hasPendingCode: true,
                    message: 'Please wait 48 seconds.'
                }, false));
            }
            return Promise.resolve(jsonResponse({}));
        });

        const wrapper = mount(MfaSettings);
        await flushPromises();
        wrapper.vm.openEmailMfaSetup();
        await flushPromises();
        await wrapper.get('[data-testid="email-mfa-send"]').trigger('click');
        await flushPromises();

        expect(wrapper.find('.email-mfa-modal [role="alert"]').exists()).toBe(false);
        expect(wrapper.get('[data-testid="email-mfa-send"]').text())
            .toBe('mfa.resendCodeIn:48');

        await vi.advanceTimersByTimeAsync(1000);
        expect(wrapper.get('[data-testid="email-mfa-send"]').text())
            .toBe('mfa.resendCodeIn:47');
        wrapper.unmount();
    });

    it('provides matching regeneration keys in both supported MFA locales', () => {
        const topLevelKeys = [
            'authenticatorTitle',
            'authenticatorOptional',
            'addAuthenticator',
            'reauthenticationTitle',
            'reauthenticationDescription',
            'disableWarningWithEmail',
            'disableReauthenticationDescription',
            'disablePasswordConfirmation',
            'disableTotpConfirmation',
            'continueReauthentication',
            'manageMethodsTitle',
            'optionalSetupTitle',
            'additionalMethodDescription',
            'emailCodeNeeded',
            'emailCodeUnavailable',
            'emailReauthenticationHint',
            'addAnotherMethodBeforeRemoval'
        ];

        for (const key of topLevelKeys) {
            expect(typeof enMfa[key]).toBe('string');
            expect(enMfa[key].length).toBeGreaterThan(0);
            expect(typeof zhMfa[key]).toBe('string');
            expect(zhMfa[key].length).toBeGreaterThan(0);
        }

        expect(Object.keys(enMfa.reauthenticationActions)).toEqual(Object.keys(zhMfa.reauthenticationActions));
        for (const action of Object.keys(enMfa.reauthenticationActions)) {
            expect(enMfa.reauthenticationActions[action].length).toBeGreaterThan(0);
            expect(zhMfa.reauthenticationActions[action].length).toBeGreaterThan(0);
        }

        expect(typeof enMfa.errors.regenerateFailed).toBe('string');
        expect(enMfa.errors.regenerateFailed.length).toBeGreaterThan(0);
        expect(typeof zhMfa.errors.regenerateFailed).toBe('string');
        expect(zhMfa.errors.regenerateFailed.length).toBeGreaterThan(0);
    });

    it('provides matching Email MFA possession-proof keys in both supported locales', () => {
        const topLevelKeys = [
            'emailSetupTitle',
            'emailSetupDescription',
            'emailCodeSent',
            'emailCodeLabel',
            'emailCodeHelp',
            'sendCode',
            'sendingCode',
            'resendCode',
            'resendCodeIn',
            'verifyAndEnable'
        ];

        for (const key of topLevelKeys) {
            expect(typeof enMfa[key]).toBe('string');
            expect(enMfa[key].length).toBeGreaterThan(0);
            expect(typeof zhMfa[key]).toBe('string');
            expect(zhMfa[key].length).toBeGreaterThan(0);
        }

        expect(typeof enMfa.errors.sendCodeFailed).toBe('string');
        expect(typeof zhMfa.errors.sendCodeFailed).toBe('string');
        expect(typeof enMfa.errors.invalidOrExpiredCode).toBe('string');
        expect(typeof zhMfa.errors.invalidOrExpiredCode).toBe('string');
    });
});

