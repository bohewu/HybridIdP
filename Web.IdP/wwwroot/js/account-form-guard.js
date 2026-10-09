// Install before styles or module startup can delay account-page interaction.
(function () {
    document.addEventListener('submit', function (event) {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-account-form')) return;
        const submitter = event.submitter || form.querySelector('button[type="submit"]');
        if (form.dataset.submitting === 'true' || submitter?.disabled) {
            event.preventDefault();
            return;
        }
        form.dataset.submitting = 'true';
        form.setAttribute('aria-busy', 'true');
        form.querySelectorAll('button').forEach(button => {
            button.dataset.preSubmitDisabled = String(button.disabled);
            button.disabled = true;
        });
    }, true);

    window.addEventListener('pageshow', function (event) {
        if (!event.persisted) return;
        document.querySelectorAll('form[data-account-form]').forEach(form => {
            delete form.dataset.submitting;
            form.removeAttribute('aria-busy');
            form.querySelectorAll('[data-pre-submit-disabled]').forEach(button => {
                button.disabled = button.dataset.preSubmitDisabled === 'true';
                delete button.dataset.preSubmitDisabled;
            });
        });
    });
})();
