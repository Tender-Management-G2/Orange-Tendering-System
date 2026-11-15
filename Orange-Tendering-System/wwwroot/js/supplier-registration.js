(() => {
    const form = document.getElementById('registerForm');
    if (!form) return;
    const submit = document.getElementById('registerSubmit');
    const summary = document.getElementById('registrationErrors');
    let sending = false;

    function showError(message) {
        if (!summary) return;
        summary.classList.remove('validation-summary-valid');
        summary.classList.add('validation-summary-errors');
        const list = document.createElement('ul');
        const item = document.createElement('li');
        item.textContent = message;
        list.append(item);
        summary.replaceChildren(list);
        summary.scrollIntoView({ block: 'nearest' });
    }

    function showValidation(html) {
        const returned = new DOMParser().parseFromString(html, 'text/html').getElementById('registerForm');
        if (!returned) {
            showError('Registration could not be completed. Please try again.');
            return;
        }

        const returnedSummary = returned.querySelector('.validation-summary-errors');
        summary.className = returnedSummary?.className || 'validation-summary';
        summary.innerHTML = returnedSummary?.innerHTML || '';
        for (const current of form.querySelectorAll('[data-valmsg-for]')) {
            const replacement = [...returned.querySelectorAll('[data-valmsg-for]')]
                .find(element => element.getAttribute('data-valmsg-for') === current.getAttribute('data-valmsg-for'));
            if (!replacement) continue;
            current.className = replacement.className;
            current.innerHTML = replacement.innerHTML;
        }
        for (const current of form.elements) {
            if (!current.name) continue;
            const replacement = [...returned.elements].find(element => element.name === current.name);
            current.classList.toggle('input-validation-error', !!replacement?.classList.contains('input-validation-error'));
        }
        summary?.scrollIntoView({ block: 'nearest' });
    }

    form.addEventListener('submit', async event => {
        event.preventDefault();
        event.stopImmediatePropagation();
        if (sending || (window.jQuery && !window.jQuery(form).valid())) return;

        sending = true;
        submit.disabled = true;
        try {
            const response = await fetch(form.action || location.href, {
                method: 'POST',
                body: new FormData(form),
                credentials: 'same-origin'
            });
            if (response.redirected && new URL(response.url).origin === location.origin) {
                location.assign(response.url);
                return;
            }
            if (!response.ok) {
                showError('Registration could not be completed. Please try again.');
                return;
            }
            showValidation(await response.text());
        } catch {
            showError('Could not contact the registration service. Please try again.');
        } finally {
            sending = false;
            submit.disabled = false;
        }
    }, true);
})();
