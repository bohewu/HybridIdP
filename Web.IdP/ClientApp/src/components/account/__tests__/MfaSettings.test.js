import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import MfaSettings from '../MfaSettings.vue';
import { useWebAuthn } from '../../../composables/useWebAuthn';

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
});
