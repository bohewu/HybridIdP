import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import AccountRecoveryCodes from '../AccountRecoveryCodes.vue';
import enMfa from '../../../i18n/locales/en-US/mfa.json';
import zhMfa from '../../../i18n/locales/zh-TW/mfa.json';

const i18nState = vi.hoisted(() => ({ locale: 'en-US' }));
vi.mock('vue-i18n', () => ({ useI18n: () => ({
  t: (key, params) => {
    const copy = i18nState.locale === 'zh-TW' ? zhMfa : enMfa;
    const value = key.startsWith('mfa.') ? key.slice(4).split('.').reduce((result, part) => result?.[part], copy) : key;
    return params?.count === undefined ? value : value.replace('{count}', params.count);
  }
}) }));

const response = (data, status = 200) => ({ ok: status === 200, status, json: async () => data });
const wrappers = [];
const render = (props = {}) => {
  const wrapper = mount(AccountRecoveryCodes, { props, attachTo: document.body });
  wrappers.push(wrapper);
  return wrapper;
};

describe('Account backup codes', () => {
  beforeEach(() => {
    i18nState.locale = 'en-US';
    sessionStorage.clear();
    localStorage.clear();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response({ hasBatch: false, remainingCodes: 0 })));
  });
  afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.unstubAllGlobals(); });

  it.each(['en-US', 'zh-TW'].flatMap(locale => [false, true].map(hasBatch => [locale, hasBatch])))('distinguishes never generated from exhausted in %s (%s)', async (locale, hasBatch) => {
    i18nState.locale = locale;
    const copy = (locale === 'zh-TW' ? zhMfa : enMfa).accountRecoveryCodes;
    fetch.mockResolvedValue(response({ hasBatch, remainingCodes: 0 }));
    const wrapper = render();
    await flushPromises();
    expect(wrapper.get('[data-testid="account-recovery-status"]').text()).toBe(hasBatch ? copy.exhausted : copy.neverGenerated);
    expect(wrapper.text()).toContain(copy.independent);
    expect(wrapper.text()).toContain(copy.adminHelp);
    expect(wrapper.get('[data-testid="account-recovery-generate"]').text()).toBe(hasBatch ? copy.replace : copy.generate);
  });

  it('shows remaining count and asks for fresh verification without generating', async () => {
    fetch.mockResolvedValue(response({ hasBatch: true, remainingCodes: 3 }));
    const wrapper = render();
    await flushPromises();
    expect(wrapper.get('[data-testid="account-recovery-status"]').text()).toBe('3 account backup codes remaining');
    await wrapper.get('[data-testid="account-recovery-generate"]').trigger('click');
    expect(wrapper.emitted('reauthenticate')).toHaveLength(1);
    expect(fetch.mock.calls.every(([, options]) => options.method === 'GET')).toBe(true);
  });

  it('shows one aggregate count for retained legacy and account codes without redisplaying codes', async () => {
    fetch.mockResolvedValue(response({ hasBatch: true, remainingCodes: 2, legacyRemainingCodes: 4, totalRemainingCodes: 6 }));
    const wrapper = render();
    await flushPromises();
    expect(wrapper.findAll('[data-testid="account-recovery-status"]')).toHaveLength(1);
    expect(wrapper.get('[data-testid="account-recovery-status"]').text()).toBe('6 account backup codes remaining');
    expect(wrapper.text()).toContain(enMfa.accountRecoveryCodes.legacyCompatibility);
    expect(wrapper.find('[data-testid="account-recovery-disclosure"]').exists()).toBe(false);
    expect(wrapper.emitted('status-changed')[0][0]).toMatchObject({ remainingCodes: 2, legacyRemainingCodes: 4 });
    expect(fetch.mock.calls.every(([, options]) => options.method === 'GET')).toBe(true);
  });

  it.each([401, 403, 500, 'malformed'])('shows unavailable status rather than an empty batch (%s)', async status => {
    fetch.mockResolvedValueOnce(response({}, typeof status === 'number' ? status : 200));
    const wrapper = render();
    await flushPromises();
    expect(wrapper.get('[role="alert"]').text()).toBe(enMfa.accountRecoveryCodes.statusFailed);
    expect(wrapper.find('[data-testid="account-recovery-status"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="account-recovery-generate"]').exists()).toBe(false);
    await wrapper.get('button').trigger('click');
    await flushPromises();
    expect(wrapper.get('[data-testid="account-recovery-status"]').text()).toBe(enMfa.accountRecoveryCodes.neverGenerated);
  });

  it('allows cancelling the post-verification confirmation without a mutation', async () => {
    const wrapper = render();
    await flushPromises();
    wrapper.vm.resumeGeneration();
    await flushPromises();
    expect(wrapper.get('[role="dialog"]').text()).toContain(enMfa.accountRecoveryCodes.generateWarning);
    await wrapper.get('[data-testid="account-recovery-cancel"]').trigger('click');
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false);
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it.each(['en-US', 'zh-TW'])('copies all disclosed codes only on request and keeps save acknowledgement separate in %s', async locale => {
    i18nState.locale = locale;
    const copy = (locale === 'zh-TW' ? zhMfa : enMfa).accountRecoveryCodes;
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    const wrapper = render({ disclosureOnly: true });
    wrapper.vm.displayIssuedCodes(['fixture-code-a', 'fixture-code-b']);
    await flushPromises();
    expect(writeText).not.toHaveBeenCalled();
    expect(wrapper.get('[data-testid="account-recovery-copy"]').text()).toBe(copy.copyAll);
    const statusChanges = wrapper.emitted('status-changed').length;
    await wrapper.get('[data-testid="account-recovery-copy"]').trigger('click');
    await flushPromises();
    expect(writeText).toHaveBeenCalledExactlyOnceWith('fixture-code-a\nfixture-code-b');
    const feedback = wrapper.get('[data-testid="account-recovery-copy-status"]');
    expect(feedback.text()).toBe(copy.copySuccess);
    expect(feedback.attributes('role')).toBe('status');
    expect(feedback.attributes('aria-live')).toBe('polite');
    expect(feedback.text()).not.toContain('fixture-code');
    expect(wrapper.get('[data-testid="account-recovery-saved"]').element.checked).toBe(false);
    expect(wrapper.get('[data-testid="account-recovery-done"]').element.disabled).toBe(true);
    expect(wrapper.emitted('saved')).toBeUndefined();
    expect(wrapper.emitted('status-changed')).toHaveLength(statusChanges);
    expect(fetch).not.toHaveBeenCalled();
    expect(sessionStorage.length).toBe(0);
    expect(localStorage.length).toBe(0);
    await wrapper.get('[data-testid="account-recovery-saved"]').setValue(true);
    await wrapper.get('[data-testid="account-recovery-done"]').trigger('click');
    expect(wrapper.vm.codes).toEqual([]);
    expect(wrapper.vm.copyFeedback).toBe('');
    expect(wrapper.vm.copying).toBe(false);
    wrapper.vm.resumeGeneration();
    await flushPromises();
    expect(wrapper.find('[data-testid="account-recovery-copy-status"]').exists()).toBe(false);
    expect(wrapper.text()).not.toContain('fixture-code');
  });

  it.each(['en-US', 'zh-TW'].flatMap(locale => ['denied', 'missing'].map(failure => [locale, failure])))('leaves codes available for manual copying when clipboard is %s (%s)', async (locale, failure) => {
    i18nState.locale = locale;
    const copy = (locale === 'zh-TW' ? zhMfa : enMfa).accountRecoveryCodes;
    const writeText = vi.fn().mockRejectedValue(new Error('clipboard denied'));
    vi.stubGlobal('navigator', { clipboard: failure === 'missing' ? undefined : { writeText } });
    const wrapper = render({ disclosureOnly: true });
    wrapper.vm.displayIssuedCodes(['manual-fixture-code']);
    await flushPromises();
    await wrapper.get('[data-testid="account-recovery-copy"]').trigger('click');
    await flushPromises();
    expect(wrapper.get('[data-testid="account-recovery-copy-status"]').text()).toBe(copy.copyFailed);
    expect(wrapper.get('[data-testid="account-recovery-disclosure"]').text()).toBe('manual-fixture-code');
    expect(wrapper.get('[data-testid="account-recovery-copy"]').element.disabled).toBe(false);
    expect(wrapper.get('[data-testid="account-recovery-saved"]').element.checked).toBe(false);
    expect(wrapper.get('[data-testid="account-recovery-done"]').element.disabled).toBe(true);
    expect(wrapper.emitted('saved')).toBeUndefined();
    expect(sessionStorage.length).toBe(0);
    expect(localStorage.length).toBe(0);
  });

  it('blocks repeated copying while pending and includes the copy action in the focus trap', async () => {
    let release;
    const writeText = vi.fn().mockImplementation(() => new Promise(resolve => { release = resolve; }));
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    const wrapper = render({ disclosureOnly: true });
    wrapper.vm.displayIssuedCodes(['focus-fixture-code']);
    await flushPromises();
    const copyButton = wrapper.get('[data-testid="account-recovery-copy"]');
    const savedInput = wrapper.get('[data-testid="account-recovery-saved"]');
    expect(document.activeElement).toBe(copyButton.element);
    await copyButton.trigger('keydown', { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(savedInput.element);
    await savedInput.trigger('keydown', { key: 'Tab' });
    expect(document.activeElement).toBe(copyButton.element);
    await copyButton.trigger('click');
    copyButton.element.dispatchEvent(new Event('click'));
    await flushPromises();
    expect(writeText).toHaveBeenCalledTimes(1);
    expect(copyButton.element.disabled).toBe(true);
    expect(copyButton.text()).toBe(enMfa.accountRecoveryCodes.copying);
    release();
    await flushPromises();
    await savedInput.setValue(true);
    copyButton.element.focus();
    await copyButton.trigger('keydown', { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(wrapper.get('[data-testid="account-recovery-done"]').element);
    await wrapper.get('[data-testid="account-recovery-done"]').trigger('keydown', { key: 'Tab' });
    expect(document.activeElement).toBe(copyButton.element);
  });

  it.each(['close', 'unmount', 'replace'].flatMap(action => [false, true].map(denied => [action, denied])))('ignores late clipboard completion after %s (denied: %s)', async (action, denied) => {
    let resolveCopy, rejectCopy;
    const writeText = vi.fn().mockImplementation(() => new Promise((resolve, reject) => { resolveCopy = resolve; rejectCopy = reject; }));
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    const wrapper = render({ disclosureOnly: true });
    wrapper.vm.displayIssuedCodes(['late-copy-fixture']);
    await flushPromises();
    await wrapper.get('[data-testid="account-recovery-copy"]').trigger('click');
    const instance = wrapper.vm;
    if (action === 'close') {
      await wrapper.get('[data-testid="account-recovery-saved"]').setValue(true);
      await wrapper.get('[data-testid="account-recovery-done"]').trigger('click');
    } else if (action === 'unmount') {
      wrapper.unmount();
      wrappers.splice(wrappers.indexOf(wrapper), 1);
    } else wrapper.vm.displayIssuedCodes(['replacement-fixture']);
    if (denied) rejectCopy(new Error('clipboard denied'));
    else resolveCopy();
    await flushPromises();
    expect(instance.codes).toEqual(action === 'replace' ? ['replacement-fixture'] : []);
    expect(instance.copyFeedback).toBe('');
    expect(instance.copying).toBe(false);
  });

  it('warns before replacement, sends CSRF once, and clears codes after save acknowledgement', async () => {
    fetch.mockResolvedValue(response({ hasBatch: true, remainingCodes: 1 }));
    const wrapper = render({ csrfToken: 'csrf-fixture' });
    await flushPromises();
    wrapper.vm.resumeGeneration();
    await flushPromises();
    expect(wrapper.get('[role="dialog"]').text()).toContain(enMfa.accountRecoveryCodes.replaceWarning);
    let release;
    fetch.mockImplementation((url, options) => options.method === 'POST'
      ? new Promise(resolve => { release = resolve; })
      : Promise.resolve(response({ hasBatch: true, remainingCodes: 2 })));
    await wrapper.get('[data-testid="account-recovery-confirm"]').trigger('click');
    wrapper.get('[data-testid="account-recovery-confirm"]').element.dispatchEvent(new Event('click'));
    await flushPromises();
    expect(fetch.mock.calls.filter(([, options]) => options.method === 'POST')).toEqual([
      ['/api/account/recovery-codes', { method: 'POST', credentials: 'include', cache: 'no-store', headers: { 'X-XSRF-TOKEN': 'csrf-fixture' } }]
    ]);
    release(response({ recoveryCodes: ['fixture-code-a', 'fixture-code-b'] }));
    await flushPromises();
    expect(wrapper.get('[data-testid="account-recovery-disclosure"]').text()).toContain('fixture-code-a');
    expect(wrapper.get('[data-testid="account-recovery-done"]').element.disabled).toBe(true);
    await wrapper.get('[role="dialog"]').trigger('keydown', { key: 'Escape' });
    expect(wrapper.find('[data-testid="account-recovery-disclosure"]').exists()).toBe(true);
    expect(sessionStorage.length).toBe(0);
    expect(localStorage.length).toBe(0);
    await wrapper.get('[data-testid="account-recovery-saved"]').setValue(true);
    await wrapper.get('[data-testid="account-recovery-done"]').trigger('click');
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false);
    expect(wrapper.vm.codes).toEqual([]);
    expect(wrapper.emitted('saved')).toHaveLength(1);
    wrapper.vm.resumeGeneration();
    await flushPromises();
    expect(wrapper.text()).not.toContain('fixture-code-a');
  });

  it.each([403, 400])('handles generation denial without silently retrying (%s)', async status => {
    const wrapper = render();
    await flushPromises();
    wrapper.vm.resumeGeneration();
    await flushPromises();
    fetch.mockResolvedValue(response({ error: 'freshAuthenticationRequired' }, status));
    await wrapper.get('[data-testid="account-recovery-confirm"]').trigger('click');
    await flushPromises();
    expect(fetch.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(1);
    if (status === 403) {
      expect(wrapper.emitted('reauthenticate')).toHaveLength(1);
      expect(wrapper.find('[role="dialog"]').exists()).toBe(false);
    } else {
      expect(wrapper.emitted('reauthenticate')).toBeUndefined();
      expect(wrapper.get('[role="dialog"] [role="alert"]').text()).toBe(enMfa.accountRecoveryCodes.generateFailed);
    }
  });

  it('displays initial registration codes without fetching status and clears them on unmount', async () => {
    const wrapper = render({ disclosureOnly: true });
    wrapper.vm.displayIssuedCodes(['initial-fixture-code']);
    await flushPromises();
    expect(fetch).not.toHaveBeenCalled();
    expect(wrapper.find('.account-recovery-codes').exists()).toBe(false);
    expect(wrapper.get('[data-testid="account-recovery-disclosure"]').text()).toBe('initial-fixture-code');
    const instance = wrapper.vm;
    wrapper.unmount();
    wrappers.splice(wrappers.indexOf(wrapper), 1);
    expect(instance.codes).toEqual([]);
    instance.displayIssuedCodes(['late-fixture-code']);
    expect(instance.codes).toEqual([]);
  });
});
