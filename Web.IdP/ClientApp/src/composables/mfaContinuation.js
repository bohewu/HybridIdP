const storageKey = 'mfa.continuation';
const lifetimeMs = 5 * 60 * 1000;
const actions = new Set(['email-enable', 'email-disable', 'passkey-register', 'totp-disable', 'passkey-delete', 'recovery-codes']);

// UI intent only. Every resumed operation still requires server-side fresh proof.
export function clearMfaContinuation() {
  sessionStorage.removeItem(storageKey);
}

export function saveMfaContinuation(userId, action, targetId = null) {
  clearMfaContinuation();
  if (!userId || !actions.has(action)) return;
  sessionStorage.setItem(storageKey, JSON.stringify({ userId, action, targetId, expiresAt: Date.now() + lifetimeMs }));
}

export function takeMfaContinuation(userId) {
  const raw = sessionStorage.getItem(storageKey);
  clearMfaContinuation();
  try {
    const intent = JSON.parse(raw);
    return intent && intent.userId === userId && actions.has(intent.action) &&
      Number.isFinite(intent.expiresAt) && intent.expiresAt > Date.now() &&
      intent.expiresAt <= Date.now() + lifetimeMs ? intent : null;
  } catch { return null; }
}
