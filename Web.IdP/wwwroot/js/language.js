// Language switcher functionality
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

    // Event delegation for language links
    document.addEventListener('click', function(e) {
        const target = e.target.closest('[data-language]');
        if (target) {
            e.preventDefault();
            const culture = target.getAttribute('data-language');
            setLanguage(culture);
        }
    });

    // Handle language select dropdown
    document.addEventListener('change', function(e) {
        if (e.target.matches('.language-select')) {
            const culture = e.target.value;
            setLanguage(culture);
        }
    });

})();
