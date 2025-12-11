// Admin Layout Mobile Menu Script
document.addEventListener('DOMContentLoaded', function() {
    const sidebar = document.querySelector('.sidebar');
    const overlay = document.getElementById('mobile-overlay');
    const toggleBtn = document.getElementById('sidebar-toggle');
    
    if (toggleBtn) {
        toggleBtn.addEventListener('click', function() {
            sidebar.classList.add('show');
            overlay.classList.add('show');
        });
    }
    
    if (overlay) {
        overlay.addEventListener('click', function() {
            sidebar.classList.remove('show');
            overlay.classList.remove('show');
        });
    }
    
    // Vue Loading Bar - Auto-hide when Vue apps are mounted
    initVueLoadingBar();
});

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
        setTimeout(function() {
            loadingBar.classList.add('hidden');
            // Remove from DOM after fade animation
            setTimeout(function() {
                if (loadingBar.parentNode) {
                    loadingBar.parentNode.removeChild(loadingBar);
                }
            }, 300);
        }, 200); // Wait for complete animation
    }
    
    // Method 1: Listen for custom Vue mounted event
    window.addEventListener('vue-mounted', hideLoadingBar);
    
    // Method 2: Check if any Vue app container has content (start checking after 200ms)
    setTimeout(function() {
        var checkInterval = setInterval(function() {
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
        setTimeout(function() {
            clearInterval(checkInterval);
            hideLoadingBar();
        }, 8000);
    }, 200); // Start checking after 200ms delay
}
