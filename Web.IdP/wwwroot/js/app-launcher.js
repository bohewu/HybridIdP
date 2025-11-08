// App Launcher - Disconnect, search, and view toggle functionality
document.addEventListener('DOMContentLoaded', function() {
    const launcher = document.querySelector('.app-launcher');
    if (!launcher) return;
    
    // Get localized messages from data attributes
    const messages = {
        confirmDisconnect: launcher.dataset.disconnectConfirm || 'Are you sure you want to disconnect this application?',
        failed: launcher.dataset.disconnectFailed || 'Failed to disconnect application',
        error: launcher.dataset.disconnectError || 'An error occurred'
    };
    
    // Search functionality
    const searchInput = document.getElementById('appSearch');
    if (searchInput) {
        searchInput.addEventListener('input', function(e) {
            const searchTerm = e.target.value.toLowerCase();
            const appCards = document.querySelectorAll('.app-card-wrapper');
            
            appCards.forEach(card => {
                const appName = card.dataset.appName || '';
                if (appName.includes(searchTerm)) {
                    card.classList.remove('hidden');
                } else {
                    card.classList.add('hidden');
                }
            });
        });
    }
    
    // View toggle functionality
    const toggleBtns = document.querySelectorAll('.toggle-btn');
    const appContainer = document.getElementById('appContainer');
    
    toggleBtns.forEach(btn => {
        btn.addEventListener('click', function() {
            const view = this.dataset.view;
            
            // Update active state
            toggleBtns.forEach(b => b.classList.remove('active'));
            this.classList.add('active');
            
            // Update grid layout
            if (appContainer) {
                if (view === 'list') {
                    appContainer.classList.add('list-view');
                } else {
                    appContainer.classList.remove('list-view');
                }
            }
        });
    });
    
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
