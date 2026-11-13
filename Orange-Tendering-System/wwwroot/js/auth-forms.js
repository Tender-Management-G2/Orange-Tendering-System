document.querySelectorAll('.auth-card form').forEach(form => {
    form.querySelectorAll('input[type="password"]').forEach(input => {
        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'auth-password-toggle';
        toggle.textContent = 'Show';
        toggle.setAttribute('aria-label', 'Show password');
        input.insertAdjacentElement('afterend', toggle);
        toggle.addEventListener('click', () => {
            const visible = input.type === 'text';
            input.type = visible ? 'password' : 'text';
            toggle.textContent = visible ? 'Show' : 'Hide';
            toggle.setAttribute('aria-label', visible ? 'Show password' : 'Hide password');
        });
    });
    form.addEventListener('submit', event => {
        if (!form.checkValidity()) return;
        setTimeout(() => {
            if (event.defaultPrevented) return;
            form.querySelectorAll('button[type="submit"]').forEach(button => button.disabled = true);
        }, 0);
    });
});
