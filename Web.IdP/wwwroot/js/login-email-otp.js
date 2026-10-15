// LoginEmailOtp page - Send code button handler
document.addEventListener('DOMContentLoaded', function() {
    const btn = document.getElementById('sendEmailCodeBtn');
    if (!btn) return;
    
    // Get localized strings from data attributes
    const i18n = {
        sendCode: btn.dataset.textSendCode,
        resend: btn.dataset.textResend,
        emailCodeSent: btn.dataset.textEmailCodeSent,
        pleaseWaitThenRetry: btn.dataset.textPleaseWait,
        sending: btn.dataset.textSending,
        sendFailed: btn.dataset.textSendFailed,
        codePending: btn.dataset.textCodePending,
        codeNeeded: btn.dataset.textCodeNeeded,
        codeUnavailable: btn.dataset.textCodeUnavailable,
        sendCodeUrl: btn.dataset.sendCodeUrl
    };

    const btnText = btn.querySelector('.btn-text');
    const countdownSpan = btn.querySelector('.countdown');
    const msg = document.getElementById('emailCodeSentMsg');
    const form = btn.closest('form');
    const codeInput = form?.querySelector('[name="Input.EmailCode"]');
    const verifyButton = form?.querySelector('[data-test-id="mfa-submit"]');
    const guidance = document.getElementById('emailCodeGuidance');
    let codeSent = form?.dataset.emailCodeSent === 'true';
    let remainingSeconds = 0;
    let countdownInterval;
    let messageTimeout;

    function updateVerifyButton() {
        if (verifyButton) verifyButton.disabled = form.dataset.submitting === 'true' || !codeSent || !/^\d{6}$/.test(codeInput?.value || '');
        if (guidance) guidance.textContent = codeSent ? i18n.codePending
            : remainingSeconds > 0 ? i18n.codeUnavailable : i18n.codeNeeded;
    }
    function syncCodeInput() {
        codeInput.value = codeInput.value.replace(/\D/g, '').slice(0, 6);
        updateVerifyButton();
    }
    ['input', 'change', 'focus'].forEach(event => codeInput?.addEventListener(event, syncCodeInput));
    window.addEventListener('pageshow', () => { if (codeInput) syncCodeInput(); });
    updateVerifyButton();

    function showMessage(text, tone) {
        if (messageTimeout) clearTimeout(messageTimeout);

        msg.textContent = text;
        msg.classList.remove('hidden', 'text-green-600', 'text-amber-600', 'text-red-600');
        msg.classList.add(tone);
        msg.setAttribute('role', tone === 'text-red-600' ? 'alert' : 'status');

        messageTimeout = setTimeout(() => msg.classList.add('hidden'), 5000);
    }

    async function sendCode() {
        if (btn.disabled || form?.dataset.submitting === 'true') return;
        
        btn.disabled = true;
        btn.setAttribute('aria-busy', 'true');
        const originalText = btnText.textContent;
        btnText.textContent = i18n.sending;
        
        try {
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            const response = await fetch(i18n.sendCodeUrl, { 
                method: 'POST',
                headers: { 
                    'X-XSRF-TOKEN': token,
                    'Content-Type': 'application/x-www-form-urlencoded'
                }
            });
            
            const data = await response.json();
            codeSent = data.hasPendingCode === true;
            if (form) form.dataset.emailCodeSent = String(codeSent);
            updateVerifyButton();
            if (response.ok && data.success === true && codeSent) {
                
                // Show success message
                showMessage(i18n.emailCodeSent, 'text-green-600');
                
                // Start countdown
                startCountdown(data.remainingSeconds || 60);
            } else {
                // Handle error (e.g. rate limit hit)
                if (data.remainingSeconds) {
                    startCountdown(data.remainingSeconds);
                    showMessage(codeSent ? i18n.pleaseWaitThenRetry : i18n.codeUnavailable, 'text-amber-600');
                } else {
                    btn.disabled = false;
                    btnText.textContent = originalText;
                    showMessage(i18n.sendFailed, 'text-red-600');
                }
            }
        } catch (e) { 
            console.error(e); 
            btn.disabled = false;
            btnText.textContent = originalText;
            showMessage(i18n.sendFailed, 'text-red-600');
        } finally {
            btn.removeAttribute('aria-busy');
        }
    }

    function startCountdown(seconds) {
        remainingSeconds = Math.max(0, Math.ceil(Number(seconds) || 0));
        
        // Update UI state
        btn.disabled = true;
        btn.classList.add('opacity-50', 'cursor-not-allowed');
        btnText.textContent = codeSent ? i18n.resend : i18n.sendCode;
        countdownSpan.textContent = `(${remainingSeconds})`;
        countdownSpan.classList.remove('hidden');
        updateVerifyButton();
        
        if (countdownInterval) clearInterval(countdownInterval);
        
        countdownInterval = setInterval(() => {
            remainingSeconds--;
            countdownSpan.textContent = `(${remainingSeconds})`;
            
            if (remainingSeconds <= 0) {
                clearInterval(countdownInterval);
                resetButton();
            }
        }, 1000);
    }

    function resetButton() {
        remainingSeconds = 0;
        btn.disabled = form?.dataset.submitting === 'true';
        btn.classList.remove('opacity-50', 'cursor-not-allowed');
        btnText.textContent = codeSent ? i18n.resend : i18n.sendCode;
        countdownSpan.classList.add('hidden');
        updateVerifyButton();
    }

    btn.addEventListener('click', sendCode);
    const initialRemainingSeconds = Number(btn.dataset.remainingSeconds) || 0;
    if (initialRemainingSeconds > 0) startCountdown(initialRemainingSeconds);
    else resetButton();
});
