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
        sendCodeUrl: btn.dataset.sendCodeUrl
    };

    const btnText = btn.querySelector('.btn-text');
    const countdownSpan = btn.querySelector('.countdown');
    const msg = document.getElementById('emailCodeSentMsg');
    let countdownInterval;

    async function sendCode() {
        if (btn.disabled) return;
        
        btn.disabled = true;
        const originalText = btnText.textContent;
        btnText.textContent = '...';
        
        try {
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            const response = await fetch(i18n.sendCodeUrl, { 
                method: 'POST',
                headers: { 
                    'RequestVerificationToken': token,
                    'Content-Type': 'application/x-www-form-urlencoded'
                }
            });
            
            if (response.ok) {
                const data = await response.json();
                
                // Show success message
                msg.classList.remove('hidden');
                setTimeout(() => msg.classList.add('hidden'), 5000);
                
                // Start countdown
                startCountdown(data.remainingSeconds || 60);
            } else {
                // Handle error (e.g. rate limit hit)
                const data = await response.json();
                if (data.remainingSeconds) {
                    startCountdown(data.remainingSeconds);
                    msg.classList.remove('hidden');
                    msg.textContent = i18n.pleaseWaitThenRetry;
                    msg.classList.replace('text-green-600', 'text-amber-600');
                    setTimeout(() => {
                        msg.classList.add('hidden');
                        msg.textContent = i18n.emailCodeSent;
                        msg.classList.replace('text-amber-600', 'text-green-600');
                    }, 3000);
                } else {
                    btn.disabled = false;
                    btnText.textContent = originalText;
                }
            }
        } catch (e) { 
            console.error(e); 
            btn.disabled = false;
            btnText.textContent = originalText;
        }
    }

    function startCountdown(seconds) {
        let remaining = seconds;
        
        // Update UI state
        btn.disabled = true;
        btn.classList.add('opacity-50', 'cursor-not-allowed');
        btnText.textContent = i18n.resend;
        countdownSpan.textContent = `(${remaining})`;
        countdownSpan.classList.remove('hidden');
        
        if (countdownInterval) clearInterval(countdownInterval);
        
        countdownInterval = setInterval(() => {
            remaining--;
            countdownSpan.textContent = `(${remaining})`;
            
            if (remaining <= 0) {
                clearInterval(countdownInterval);
                resetButton();
            }
        }, 1000);
    }

    function resetButton() {
        btn.disabled = false;
        btn.classList.remove('opacity-50', 'cursor-not-allowed');
        btnText.textContent = i18n.sendCode;
        countdownSpan.classList.add('hidden');
    }

    btn.addEventListener('click', sendCode);
});
