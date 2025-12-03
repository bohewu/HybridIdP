// Handle account switching in LinkedAccounts page
document.addEventListener('DOMContentLoaded', function() {
    const switchButtons = document.querySelectorAll('.switch-account-btn');
    
    switchButtons.forEach(button => {
        button.addEventListener('click', async function(e) {
            e.preventDefault();
            
            const accountId = this.dataset.accountId;
            const accountEmail = this.dataset.accountEmail;
            const confirmMessage = this.dataset.confirmMessage;
            const errorMessage = this.dataset.errorMessage;
            const failedMessage = this.dataset.failedMessage;
            
            if (!confirm(confirmMessage.replace('{0}', accountEmail))) {
                return;
            }
            
            try {
                const response = await fetch('/api/my/switch-account', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                    },
                    body: JSON.stringify({
                        targetAccountId: accountId,
                        reason: 'User requested account switch from UI'
                    })
                });
                
                const data = await response.json();
                
                if (data.success) {
                    // Reload page to show new account
                    window.location.reload();
                } else {
                    alert(data.error || failedMessage);
                }
            } catch (error) {
                console.error('Error switching account:', error);
                alert(errorMessage);
            }
        });
    });
});
