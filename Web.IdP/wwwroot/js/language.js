(function() {
    'use strict';

    async function setLanguage(culture) {
        try {
            const response = await fetch('/api/language/set', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({ culture: culture })
            });
            
            if (response.ok) {
                window.location.reload();
            } else {
                console.error('Failed to set language');
            }
        } catch (error) {
            console.error('Error setting language:', error);
        }
    }

    document.addEventListener('DOMContentLoaded', function() {
        const languageSelector = document.getElementById('language-selector');
        if (languageSelector) {
            languageSelector.addEventListener('change', function(e) {
                setLanguage(e.target.value ?? 'zh-TW');
            });
        }
    });

})();
