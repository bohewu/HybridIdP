import { ref } from 'vue';

export function useWebAuthn() {
    // Keep track of the abort controller for conditional UI requests
    let conditionalAbortController = null;

    const isSupported = () => {
        return window.PublicKeyCredential !== undefined &&
            navigator.credentials !== undefined;
    };

    // Base64url decode
    const base64ToArrayBuffer = (base64) => {
        // Handle both base64 and base64url
        const base64url = base64.replace(/-/g, '+').replace(/_/g, '/');
        // Add padding if needed
        const padding = '='.repeat((4 - (base64url.length % 4)) % 4);
        const binaryString = window.atob(base64url + padding);
        const bytes = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
            bytes[i] = binaryString.charCodeAt(i);
        }
        return bytes.buffer;
    };

    // Base64url encode
    const arrayBufferToBase64 = (buffer) => {
        const bytes = new Uint8Array(buffer);
        let binary = '';
        for (let i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        const base64 = window.btoa(binary);
        // Convert to base64url
        return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
    };

    // Generate device name automatically
    const generateDeviceName = () => {
        const browser = getBrowserName();
        const os = getOSName();
        const date = new Date().toLocaleDateString('zh-TW');
        return `${browser} on ${os} - ${date}`;
    };

    const getBrowserName = () => {
        const ua = navigator.userAgent;
        if (ua.indexOf('Chrome') > -1) return 'Chrome';
        if (ua.indexOf('Safari') > -1) return 'Safari';
        if (ua.indexOf('Firefox') > -1) return 'Firefox';
        if (ua.indexOf('Edge') > -1) return 'Edge';
        return 'Browser';
    };

    const getOSName = () => {
        const ua = navigator.userAgent;
        if (ua.indexOf('Win') > -1) return 'Windows';
        if (ua.indexOf('Mac') > -1) return 'macOS';
        if (ua.indexOf('Linux') > -1) return 'Linux';
        if (ua.indexOf('Android') > -1) return 'Android';
        if (ua.indexOf('iOS') > -1 || ua.indexOf('iPhone') > -1) return 'iOS';
        return 'Unknown';
    };

    const registerPasskey = async () => {
        if (!isSupported()) {
            throw new Error('mfa.errors.webAuthnNotSupported');
        }

        // 1. Get options from server
        const optionsResp = await fetch('/api/passkey/register-options', {
            method: 'POST',
            credentials: 'include'
        });

        if (!optionsResp.ok) {
            throw new Error('mfa.errors.registrationOptionsFailed');
        }

        const options = await optionsResp.json();

        // 2. Convert base64 to ArrayBuffer
        options.challenge = base64ToArrayBuffer(options.challenge);
        options.user.id = base64ToArrayBuffer(options.user.id);

        if (options.excludeCredentials) {
            options.excludeCredentials = options.excludeCredentials.map(cred => ({
                ...cred,
                id: base64ToArrayBuffer(cred.id)
            }));
        }

        try {
            // 3. Call WebAuthn API
            const credential = await navigator.credentials.create({
                publicKey: options
            });

            if (!credential) {
                throw new Error('No credential created');
            }

            // 4. Prepare response
            const attestationResponse = {
                id: credential.id,
                rawId: arrayBufferToBase64(credential.rawId),
                type: credential.type,
                response: {
                    clientDataJSON: arrayBufferToBase64(credential.response.clientDataJSON),
                    attestationObject: arrayBufferToBase64(credential.response.attestationObject)
                },
                deviceName: generateDeviceName() // Auto-generate device name
            };

            // 5. Send to server
            const registerResp = await fetch('/api/passkey/register', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify(attestationResponse)
            });

            if (!registerResp.ok) {
                const error = await registerResp.json();
                throw new Error(error.error || 'Registration failed');
            }

            return await registerResp.json();
        } catch (err) {
            if (err.name === 'NotAllowedError') {
                throw new Error('mfa.errors.userCanceled');
            }
            if (err.name === 'SecurityError') {
                throw new Error('mfa.errors.securityError');
            }
            throw err;
        }
    };

    const authenticateWithPasskey = async (username, mediation = 'optional') => {
        if (!isSupported()) {
            throw new Error('mfa.errors.webAuthnNotSupported');
        }

        // 1. Get assertion options
        const optionsResp = await fetch('/api/passkey/login-options', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username })
        });

        if (!optionsResp.ok) {
            throw new Error('mfa.errors.loginOptionsFailed');
        }

        const options = await optionsResp.json();

        // 2. Convert base64 to ArrayBuffer
        options.challenge = base64ToArrayBuffer(options.challenge);

        if (options.allowCredentials) {
            options.allowCredentials = options.allowCredentials.map(cred => ({
                ...cred,
                id: base64ToArrayBuffer(cred.id)
            }));
        }

        // If starting an explicit (modal) authentication, abort any pending conditional UI request first
        // to prevent "request already pending" errors (especially on mobile/Safari).
        if (mediation === 'optional' && conditionalAbortController) {
            try {
                conditionalAbortController.abort();
            } catch (e) {
                // Ignore abort errors
            }
            conditionalAbortController = null;
        }

        // Create a new controller for this request
        const controller = new AbortController();
        if (mediation === 'conditional') {
            conditionalAbortController = controller;
        }

        try {
            // 3. Call WebAuthn API
            const assertion = await navigator.credentials.get({
                publicKey: options,
                mediation: mediation, // 'optional' (default) or 'conditional' (auto-fill)
                signal: controller.signal
            });

            if (!assertion) {
                throw new Error('No assertion created');
            }

            // 4. Prepare response
            const assertionResponse = {
                id: assertion.id,
                rawId: arrayBufferToBase64(assertion.rawId),
                type: assertion.type,
                response: {
                    clientDataJSON: arrayBufferToBase64(assertion.response.clientDataJSON),
                    authenticatorData: arrayBufferToBase64(assertion.response.authenticatorData),
                    signature: arrayBufferToBase64(assertion.response.signature),
                    userHandle: assertion.response.userHandle
                        ? arrayBufferToBase64(assertion.response.userHandle)
                        : null
                }
            };

            // 5. Send to server
            const loginResp = await fetch('/api/passkey/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(assertionResponse)
            });

            if (!loginResp.ok) {
                const error = await loginResp.json();
                throw new Error(error.error || 'Authentication failed');
            }

            return await loginResp.json();
        } catch (err) {
            if (err.name === 'NotAllowedError') {
                throw new Error('mfa.errors.userCanceled');
            }
            throw err;
        }
    };

    return {
        isSupported,
        registerPasskey,
        authenticateWithPasskey
    };
}
