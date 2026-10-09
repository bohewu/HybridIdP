import { beforeEach, describe, expect, it, vi } from 'vitest';
import { saveMfaContinuation, takeMfaContinuation } from '../mfaContinuation';

describe('MFA continuation', () => {
  beforeEach(() => { sessionStorage.clear(); vi.useRealTimers(); });

  it('resumes an explicit same-account action once', () => {
    saveMfaContinuation('account-a', 'email-disable');
    expect(takeMfaContinuation('account-a')?.action).toBe('email-disable');
    expect(takeMfaContinuation('account-a')).toBeNull();
  });

  it('discards the intent after switching accounts', () => {
    saveMfaContinuation('account-a', 'passkey-register');
    expect(takeMfaContinuation('account-b')).toBeNull();
    expect(takeMfaContinuation('account-a')).toBeNull();
  });

  it('does not resume expired or unbounded intents', () => {
    vi.useFakeTimers();
    saveMfaContinuation('account-a', 'email-enable');
    vi.advanceTimersByTime(300000);
    expect(takeMfaContinuation('account-a')).toBeNull();
    sessionStorage.setItem('mfa.continuation', JSON.stringify({userId:'account-a', action:'email-disable', expiresAt:Date.now()+300001}));
    expect(takeMfaContinuation('account-a')).toBeNull();
    vi.useRealTimers();
  });

  it('discards malformed state and unsupported operations', () => {
    sessionStorage.setItem('mfa.continuation', '{');
    expect(takeMfaContinuation('account-a')).toBeNull();
    saveMfaContinuation('account-a', 'change-password');
    expect(sessionStorage.length).toBe(0);
  });
});
