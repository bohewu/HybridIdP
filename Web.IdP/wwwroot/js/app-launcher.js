// App Launcher - Disconnect functionality
document.addEventListener('DOMContentLoaded', function() {
    const launcher = document.querySelector('.app-launcher');
    if (!launcher) return;
    
    // Get localized messages from data attributes
    const messages = {
        confirmDisconnect: launcher.dataset.disconnectConfirm || 'Are you sure you want to disconnect this application?',
        failed: launcher.dataset.disconnectFailed || 'Failed to disconnect application',
        error: launcher.dataset.disconnectError || 'An error occurred'
    };
    
    // Handle disconnect button clicks
    document.querySelectorAll('.app-disconnect-btn').forEach(btn => {
        btn.addEventListener('click', async function(e) {
            e.preventDefault();
            e.stopPropagation();
            
            const appId = this.dataset.appId;
            const appName = this.dataset.appName;
            
            // Show confirmation dialog with app name
            const confirmMessage = messages.confirmDisconnect.replace('{0}', appName);
            if (!confirm(confirmMessage)) {
                return;
            }
            
            try {
                const response = await fetch('/?handler=Disconnect', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded'
                    },
                    body: `applicationId=${encodeURIComponent(appId)}`
                });
                
                if (response.ok) {
                    // Remove the card with animation
                    const wrapper = this.closest('.app-card-wrapper');
                    wrapper.style.opacity = '0';
                    wrapper.style.transform = 'scale(0.8)';
                    
                    setTimeout(() => {
                        wrapper.remove();
                        
                        // Check if there are no more apps
                        const appGrid = document.querySelector('.app-grid');
                        if (appGrid && appGrid.children.length === 0) {
                            window.location.reload();
                        }
                    }, 300);
                } else {
                    alert(messages.failed);
                }
            } catch (error) {
                console.error('Error disconnecting app:', error);
                alert(messages.error);
            }
        });
    });
});
