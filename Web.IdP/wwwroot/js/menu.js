// Highlight active dropdown menu item based on current path
document.addEventListener('DOMContentLoaded', function() {
    const currentPath = window.location.pathname.toLowerCase();
    const dropdownItems = document.querySelectorAll('.dropdown-item[href]');
    
    dropdownItems.forEach(item => {
        const href = item.getAttribute('href');
        if (href && href !== '#' && currentPath.startsWith(href.toLowerCase())) {
            item.classList.add('active');
        }
    });
});
