document.addEventListener('DOMContentLoaded', function() {
    // Simple auto-formatting for user code (XXXX-XXXX-XXXX)
    var input = document.getElementById('UserCode');
    if (input) {
        input.addEventListener('input', function(e) {
            var value = e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, '');
            if (value.length > 4) value = value.slice(0, 4) + '-' + value.slice(4);
            if (value.length > 9) value = value.slice(0, 9) + '-' + value.slice(9);
            e.target.value = value;
        });
    }
});
