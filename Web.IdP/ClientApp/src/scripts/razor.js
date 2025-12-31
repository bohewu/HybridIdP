import '../styles/main.css';
import '../styles/google-style.css';
import '../utils/csrfInterceptor.js';  // Enable automatic CSRF token for all fetch requests
import { useWebAuthn } from '../composables/useWebAuthn.js';

document.addEventListener('DOMContentLoaded', () => {
    // Mobile Menu Toggle
    const mobileMenuButton = document.querySelector('[aria-controls="mobile-menu"]');
    const mobileMenu = document.getElementById('mobile-menu');

    if (mobileMenuButton && mobileMenu) {
        mobileMenuButton.addEventListener('click', () => {
            const isExpanded = mobileMenuButton.getAttribute('aria-expanded') === 'true';
            mobileMenuButton.setAttribute('aria-expanded', !isExpanded);
            mobileMenu.classList.toggle('hidden');
        });
    }

    // Language Selector
    const languageSelects = document.querySelectorAll('.language-select');
    languageSelects.forEach(select => {
        select.addEventListener('change', (e) => {
            const culture = e.target.value;
            const url = new URL(window.location.href);
            url.searchParams.set('culture', culture);
            window.location.href = url.toString();
        });
    });

    // Impersonation Stop Button (Handling both layouts)
    // First, look for ID (shared layout)
    const stopImpersonationBtn = document.getElementById('stopImpersonationBtn');
    if (stopImpersonationBtn) {
        stopImpersonationBtn.addEventListener('click', handleStopImpersonation);
    }
    // Also look for global function replacement if used in onclick
    window.revertImpersonation = handleStopImpersonation;

    async function handleStopImpersonation() {
        try {
            const response = await fetch('/api/admin/users/stop-impersonation', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                }
            });

            if (response.ok) {
                window.location.reload();
            } else {
                const data = await response.json();
                alert(data.error || 'Failed to stop impersonation');
            }
        } catch (error) {
            console.error('Error stopping impersonation:', error);
            alert('An error occurred');
        }
    }

    // User Dropdown (for desktop)
    const userMenuButton = document.getElementById('user-menu-button');
    const userDropdown = userMenuButton?.parentElement.nextElementSibling; // The dropdown div

    if (userMenuButton && userDropdown) {
        userMenuButton.addEventListener('click', () => {
            const isExpanded = userMenuButton.getAttribute('aria-expanded') === 'true';
            userMenuButton.setAttribute('aria-expanded', !isExpanded);
            userDropdown.classList.toggle('hidden');
        });

        // Close when clicking outside
        document.addEventListener('click', (event) => {
            if (!userMenuButton.contains(event.target) && !userDropdown.contains(event.target)) {
                userDropdown.classList.add('hidden');
                userMenuButton.setAttribute('aria-expanded', 'false');
            }
        });
    }

    // Admin Sidebar Mobile Toggle
    const adminSidebar = document.querySelector('.sidebar');
    const adminOverlay = document.getElementById('mobile-overlay');
    const adminToggleBtn = document.getElementById('sidebar-toggle');

    if (adminToggleBtn && adminSidebar) {
        adminToggleBtn.addEventListener('click', () => {
            adminSidebar.classList.add('show');
            if (adminOverlay) adminOverlay.classList.add('show');
        });
    }

    if (adminOverlay && adminSidebar) {
        adminOverlay.addEventListener('click', () => {
            adminSidebar.classList.remove('show');
            adminOverlay.classList.remove('show');
        });
    }

    // Vue Loading Bar - Auto-hide when Vue apps are mounted
    initVueLoadingBar();

    // Turnstile Logic - Disable submit button until verification completes
    initTurnstile();

    // Passkey Login Logic
    initPasskeyLogin();
});

// Turnstile callback functions (must be global for Cloudflare Turnstile API)
function initTurnstile() {
    const submitBtn = document.getElementById('loginSubmitBtn') || document.getElementById('registerSubmitBtn');
    const turnstileWidget = document.querySelector('.cf-turnstile');
    
    // Only disable if Turnstile is enabled on page
    if (submitBtn && turnstileWidget) {
        submitBtn.disabled = true;
        submitBtn.classList.add('opacity-50', 'cursor-not-allowed');
    }
}

// Global callback for Turnstile success
window.onTurnstileSuccess = function(token) {
    const submitBtn = document.getElementById('loginSubmitBtn') || document.getElementById('registerSubmitBtn');
    if (submitBtn) {
        submitBtn.disabled = false;
        submitBtn.classList.remove('opacity-50', 'cursor-not-allowed');
    }
};

// Global callback for Turnstile expiration
window.onTurnstileExpired = function() {
    const submitBtn = document.getElementById('loginSubmitBtn') || document.getElementById('registerSubmitBtn');
    if (submitBtn) {
        submitBtn.disabled = true;
        submitBtn.classList.add('opacity-50', 'cursor-not-allowed');
    }
};

async function initPasskeyLogin() {
    const passkeyBtn = document.getElementById('passkeyLoginBtn');
    if (!passkeyBtn) return;

    const { authenticateWithPasskey, isSupported } = useWebAuthn();
    const usernameInput = document.querySelector('input[name="Input.Login"]');

    if (!isSupported()) {
        passkeyBtn.style.display = 'none';
        return;
    }

    // Safe redirect - only allow relative URLs or same-origin
    function safeRedirect(url) {
        const defaultUrl = '/';
        if (!url) return defaultUrl;
        
        // Only allow relative URLs starting with /
        if (url.startsWith('/') && !url.startsWith('//')) {
            return url;
        }
        
        // Check if same origin
        try {
            const parsed = new URL(url, window.location.origin);
            if (parsed.origin === window.location.origin) {
                return url;
            }
        } catch {
            // Invalid URL
        }
        
        return defaultUrl;
    }

    passkeyBtn.addEventListener('click', async () => {
        try {
            const username = usernameInput.value;
            const returnUrl = safeRedirect(passkeyBtn.getAttribute('data-return-url'));
            const result = await authenticateWithPasskey(username);
            if (result.success) {
                window.location.href = returnUrl;
            }
        } catch (err) {
            console.error('Passkey login failed:', err);
            // Only alert if it's not a user cancellation
            if (err.message !== 'mfa.errors.userCanceled') {
                alert(err.message || 'Passkey authentication failed');
            }
        }
    });

    // Support Conditional UI (Auto-fill) if available
    try {
        if (window.PublicKeyCredential && PublicKeyCredential.isConditionalMediationAvailable) {
            const available = await PublicKeyCredential.isConditionalMediationAvailable();
            if (available) {
                // Trigger conditional UI handshake. 
                // This won't pop up anything immediately; it will instead 
                // allow the browser to show passkeys in the autocomplete dropdown.
                authenticateWithPasskey(undefined, 'conditional')
                    .then(result => {
                        if (result.success) {
                            const returnUrl = safeRedirect(passkeyBtn.getAttribute('data-return-url'));
                            window.location.href = returnUrl;
                        }
                    })
                    .catch(err => {
                        // Silent fail for conditional UI (often ignored if aborted)
                        if (err.name !== 'AbortError') {
                            console.error('Conditional Passkey login failed:', err);
                        }
                    });
            }
        }
    } catch (e) {
        console.error('Conditional mediation check failed:', e);
    }
}

// Turnstile Callbacks (must be global)
window.onTurnstileSuccess = function (token) {
    const btn = document.getElementById('loginSubmitBtn') || document.getElementById('registerSubmitBtn');
    const btnText = document.getElementById('loginBtnText') || document.getElementById('registerBtnText');
    const btnLoading = document.getElementById('loginBtnLoading') || document.getElementById('registerBtnLoading');

    if (btn) {
        btn.disabled = false;
        if (btnText) btnText.classList.remove('hidden');
        if (btnLoading) btnLoading.classList.add('hidden');
    }
};

window.onTurnstileExpired = function () {
    const btn = document.getElementById('loginSubmitBtn') || document.getElementById('registerSubmitBtn');
    const btnText = document.getElementById('loginBtnText') || document.getElementById('registerBtnText');
    const btnLoading = document.getElementById('loginBtnLoading') || document.getElementById('registerBtnLoading');

    if (btn) {
        btn.disabled = true;
        if (btnText) btnText.classList.add('hidden');
        if (btnLoading) btnLoading.classList.remove('hidden');
    }
};

// Global toggleLoading (backwards compatibility / manual usage)
window.toggleLoading = function (isLoading) {
    const btnText = document.getElementById('loginBtnText');
    const btnLoading = document.getElementById('loginBtnLoading');
    if (!btnText || !btnLoading) return;

    if (isLoading) {
        btnText.classList.add('hidden');
        btnLoading.classList.remove('hidden');
    } else {
        btnText.classList.remove('hidden');
        btnLoading.classList.add('hidden');
    }
};

// Vue Loading Bar Handler
function initVueLoadingBar() {
    var loadingBar = document.getElementById('vue-loading-bar');
    if (!loadingBar) return;

    var hasHidden = false;
    var minDisplayTime = 300; // Minimum time to show loading bar (ms)
    var startTime = Date.now();

    // Hide loading bar function
    function hideLoadingBar() {
        if (hasHidden) return;

        // Ensure minimum display time
        var elapsed = Date.now() - startTime;
        if (elapsed < minDisplayTime) {
            setTimeout(hideLoadingBar, minDisplayTime - elapsed);
            return;
        }

        hasHidden = true;

        // Complete to 100% first, then fade out
        loadingBar.classList.add('complete');

        // Wait for width transition, then fade out
        setTimeout(function () {
            loadingBar.classList.add('hidden');
            // Remove from DOM after fade animation
            setTimeout(function () {
                if (loadingBar.parentNode) {
                    loadingBar.parentNode.removeChild(loadingBar);
                }
            }, 300);
        }, 200); // Wait for complete animation
    }

    // Method 1: Listen for custom Vue mounted event
    window.addEventListener('vue-mounted', hideLoadingBar);

    // Method 2: Check if page has Vue app container - if not, hide immediately
    var vueApps = document.querySelectorAll('#app, [id$="-app"]');
    if (vueApps.length === 0) {
        // No Vue container found - this is a pure Razor page, hide loading bar immediately
        hideLoadingBar();
        return;
    }

    // Method 3: For Vue pages, check if any Vue app container has content (start checking after 200ms)
    setTimeout(function () {
        var checkInterval = setInterval(function () {
            // Check both #app and elements ending with -app
            for (var i = 0; i < vueApps.length; i++) {
                if (vueApps[i].children.length > 0) {
                    clearInterval(checkInterval);
                    hideLoadingBar();
                    return;
                }
            }
        }, 100);

        // Method 4: Timeout fallback (max 8 seconds)
        setTimeout(function () {
            clearInterval(checkInterval);
            hideLoadingBar();
        }, 8000);
    }, 200); // Start checking after 200ms delay
}
