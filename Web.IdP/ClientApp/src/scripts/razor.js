import '../styles/main.css';
import '../styles/google-style.css';

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

    // Turnstile Logic (replaces turnstile.js)
    const loginText = document.getElementById('loginBtnText') || document.getElementById('registerBtnText');
    const loginLoading = document.getElementById('loginBtnLoading') || document.getElementById('registerBtnLoading');

    if (loginText && loginLoading) {
        // Initial state is loading if turnstile is present but not yet verified
        // But we want to ensure the loading spinner is visible initially IF configured
        loginText.classList.add('hidden');
        loginLoading.classList.remove('hidden');
    }
});

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

    // Method 2: Check if any Vue app container has content (start checking after 200ms)
    setTimeout(function () {
        var checkInterval = setInterval(function () {
            // Check both #app and elements ending with -app
            var vueApps = document.querySelectorAll('#app, [id$="-app"]');
            for (var i = 0; i < vueApps.length; i++) {
                if (vueApps[i].children.length > 0) {
                    clearInterval(checkInterval);
                    hideLoadingBar();
                    return;
                }
            }
        }, 100);

        // Method 3: Timeout fallback (max 8 seconds)
        setTimeout(function () {
            clearInterval(checkInterval);
            hideLoadingBar();
        }, 8000);
    }, 200); // Start checking after 200ms delay
}
