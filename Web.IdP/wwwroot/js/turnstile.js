// Turnstile CAPTCHA handler for login/register forms
(function () {
    'use strict';

    function onTurnstileSuccess(token) {
        var btn = document.getElementById('loginSubmitBtn') || document.getElementById('registerSubmitBtn');
        var btnText = document.getElementById('loginBtnText') || document.getElementById('registerBtnText');
        var btnLoading = document.getElementById('loginBtnLoading') || document.getElementById('registerBtnLoading');
        if (btn) {
            btn.disabled = false;
            if (btnText) btnText.classList.remove('d-none');
            if (btnLoading) btnLoading.classList.add('d-none');
        }
    }

    function onTurnstileExpired() {
        var btn = document.getElementById('loginSubmitBtn') || document.getElementById('registerSubmitBtn');
        var btnText = document.getElementById('loginBtnText') || document.getElementById('registerBtnText');
        var btnLoading = document.getElementById('loginBtnLoading') || document.getElementById('registerBtnLoading');
        if (btn) {
            btn.disabled = true;
            if (btnText) btnText.classList.add('d-none');
            if (btnLoading) btnLoading.classList.remove('d-none');
        }
    }

    // Initial state: show loading text
    document.addEventListener('DOMContentLoaded', function () {
        var btnText = document.getElementById('loginBtnText') || document.getElementById('registerBtnText');
        var btnLoading = document.getElementById('loginBtnLoading') || document.getElementById('registerBtnLoading');
        if (btnText && btnLoading) {
            btnText.classList.add('d-none');
            btnLoading.classList.remove('d-none');
        }
    });

    // Expose to global scope for Turnstile callbacks
    window.onTurnstileSuccess = onTurnstileSuccess;
    window.onTurnstileExpired = onTurnstileExpired;
})();
