(() => {
    'use strict';
    const root = document.getElementById('livingRegistration');
    const form = document.getElementById('registerForm');
    if (!root || !form) return;
    const steps = [...form.querySelectorAll('.ld-step')];
    const progress = [...root.querySelectorAll('.ld-progress li')];
    const next = document.getElementById('registrationNext');
    const back = document.getElementById('registrationBack');
    const submit = document.getElementById('registerSubmit');
    const summary = document.getElementById('registrationErrors');
    const review = document.getElementById('registrationReview');
    const requiredFields = new Set([
        'Input.FullName', 'Input.Email', 'Input.Password', 'Input.ConfirmPassword',
        'Input.CompanyName', 'Input.Category', 'Input.RegistrationNumber',
        'Input.CompanyEmail', 'Input.CompanyPhone', 'Input.CompanyAddress'
    ]);
    let stage = 0;
    let sending = false;

    const field = name => form.elements.namedItem(name);
    const value = name => (field(name)?.value || '').trim();
    const label = element => form.querySelector('label[for="' + element.id + '"]')?.textContent.trim() || 'This field';

    function error(message) {
        summary.className = 'validation-summary validation-summary-errors';
        const list = document.createElement('ul');
        const item = document.createElement('li');
        item.textContent = message;
        list.append(item);
        summary.replaceChildren(list);
        summary.scrollIntoView({ block: 'nearest' });
    }

    function clearError() {
        summary.className = 'validation-summary';
        summary.replaceChildren();
    }

    function validateStep(index) {
        const inputs = [...steps[index].querySelectorAll('input,select')];
        for (const input of inputs) {
            if (input.disabled || input.type === 'hidden') continue;
            const missing = requiredFields.has(input.name) && !input.value.trim();
            if (!input.checkValidity() || missing) {
                error(missing || input.validity.valueMissing ? label(input) + ' is required.' : 'Check ' + label(input).toLowerCase() + '.');
                if (steps[index].hidden) goTo(index, false);
                input.focus();
                return false;
            }
        }
        if (index === 0 && value('Input.Password').length < 8) {
            error('Use a password of at least 8 characters.');
            if (steps[index].hidden) goTo(index, false);
            field('Input.Password').focus();
            return false;
        }
        if (index === 0 && value('Input.Password') !== value('Input.ConfirmPassword')) {
            error('The confirmation password must match.');
            if (steps[index].hidden) goTo(index, false);
            field('Input.ConfirmPassword').focus(); return false;
        }
        if (index === 2) {
            const files = ['CommercialRegistrationCertificate', 'TaxCertificate', 'LegalDocuments'];
            const required = ['CommercialRegistrationCertificate', 'LegalDocuments'];
            if (value('Input.TaxNumber')) required.push('TaxCertificate');
            for (const name of required) {
                if (!field(name)?.files?.length) {
                    error(label(field(name)) + ' is required.');
                    if (steps[index].hidden) goTo(index, false);
                    field(name)?.focus(); return false;
                }
            }
            const selected = files.flatMap(name => [...(field(name)?.files || [])]);
            if (selected.length > 7) { error('Upload no more than seven documents.'); return false; }
            for (const file of selected) {
                if (file.size > 10 * 1024 * 1024 || !/\.(pdf|png|jpe?g)$/i.test(file.name)) {
                    error('Documents must be PDF, PNG, or JPEG files of at most 10 MB each.');
                    return false;
                }
            }
        }
        clearError();
        return true;
    }

    function updateDossier() {
        const set = (id, text) => { const target = document.getElementById(id); if (target) target.textContent = text; };
        const category = value('Input.Category');
        const symbols = {
            'IT & Electronics': '⌘', 'Construction & Maintenance': '⚒',
            'Medical & Safety': '✚', 'Transport & Logistics': '▰',
            'Office Supplies': '▤', 'Furniture & Fixtures': '▥',
            'Professional Services': '✦', 'Food & Hospitality': '◒',
            'Cleaning & Facilities': '✧', 'Telecommunications': '⌁'
        };
        set('dossierName', value('Input.FullName') || 'Your name');
        set('contactCardName', value('Input.FullName') || 'Your name');
        set('dossierEmail', value('Input.Email') || 'Identity card');
        set('dossierCompany', value('Input.CompanyName') || 'Your company');
        set('dossierCategory', category || 'A place for your story');
        set('dossierCategoryIcon', symbols[category] || '◈');
        root.classList.toggle('has-name', !!value('Input.FullName'));
        root.classList.toggle('has-email', !!field('Input.Email')?.checkValidity() && !!value('Input.Email'));
        root.classList.toggle('has-password', value('Input.Password').length >= 8 &&
            value('Input.Password') === value('Input.ConfirmPassword'));
        root.classList.toggle('has-company', !!value('Input.CompanyName'));
        const selected = ['CommercialRegistrationCertificate', 'TaxCertificate', 'LegalDocuments']
            .flatMap(name => [...(field(name)?.files || [])]);
        root.classList.toggle('has-documents', !!field('CommercialRegistrationCertificate')?.files?.length &&
            !!field('LegalDocuments')?.files?.length &&
            (!value('Input.TaxNumber') || !!field('TaxCertificate')?.files?.length) &&
            selected.length <= 7 && selected.every(file => file.size <= 10 * 1024 * 1024 &&
                /\.(pdf|png|jpe?g)$/i.test(file.name)));
        root.dispatchEvent(new CustomEvent('dossier:field'));
    }

    function updateReview() {
        const entries = [
            ['Contact', value('Input.FullName')], ['Email', value('Input.Email')],
            ['Company', value('Input.CompanyName')], ['Category', value('Input.Category')],
            ['Registration', value('Input.RegistrationNumber')],
            ['Company email', value('Input.CompanyEmail')], ['Telephone', value('Input.CompanyPhone')],
            ['Address', value('Input.CompanyAddress')],
            ['Documents', ['CommercialRegistrationCertificate', 'TaxCertificate', 'LegalDocuments']
                .flatMap(name => [...(field(name)?.files || [])]).map(file => file.name).join(', ')]
        ];
        review.replaceChildren();
        for (const [key, text] of entries) {
            const dt = document.createElement('dt');
            const dd = document.createElement('dd');
            dt.textContent = key; dd.textContent = text || '—';
            review.append(dt, dd);
        }
    }

    function goTo(index, focus = true) {
        stage = index;
        root.dataset.stage = String(index);
        steps.forEach((step, i) => { step.hidden = i !== index; });
        progress.forEach((item, i) => {
            item.classList.toggle('is-current', i === index);
            item.classList.toggle('is-done', i < index);
            if (i === index) item.setAttribute('aria-current', 'step'); else item.removeAttribute('aria-current');
        });
        back.hidden = index === 0;
        next.hidden = index === steps.length - 1;
        submit.hidden = index !== steps.length - 1;
        if (index === steps.length - 1) updateReview();
        root.dispatchEvent(new CustomEvent('dossier:stage'));
        if (focus) steps[index].querySelector('legend')?.scrollIntoView({ block: 'nearest' });
    }

    next.addEventListener('click', () => { if (validateStep(stage)) goTo(stage + 1); });
    back.addEventListener('click', () => { clearError(); goTo(stage - 1); });
    form.addEventListener('input', updateDossier);
    form.addEventListener('change', updateDossier);
    updateDossier();
    requiredFields.forEach(name => field(name)?.setAttribute('aria-required', 'true'));

    function showServerValidation(html) {
        const returned = new DOMParser().parseFromString(html, 'text/html').getElementById('registerForm');
        if (!returned) { error('Registration could not be completed. Please try again.'); return; }
        const returnedSummary = [...returned.querySelectorAll('.validation-summary-errors')]
            .find(element => element.textContent.trim());
        if (returnedSummary) {
            summary.className = returnedSummary.className;
            summary.innerHTML = returnedSummary.innerHTML;
        } else clearError();
        let firstInvalid = null;
        for (const current of form.querySelectorAll('[data-valmsg-for]')) {
            const replacement = [...returned.querySelectorAll('[data-valmsg-for]')]
                .find(element => element.getAttribute('data-valmsg-for') === current.getAttribute('data-valmsg-for'));
            if (!replacement) continue;
            current.className = replacement.className;
            current.innerHTML = replacement.innerHTML;
            if (!firstInvalid && replacement.textContent.trim()) {
                firstInvalid = form.elements.namedItem(current.getAttribute('data-valmsg-for'));
            }
        }
        if (firstInvalid) {
            const invalidStep = steps.findIndex(step => step.contains(firstInvalid));
            if (invalidStep >= 0) goTo(invalidStep, false);
            firstInvalid.focus();
        } else summary.scrollIntoView({ block: 'nearest' });
    }

    form.addEventListener('submit', async event => {
        event.preventDefault();
        event.stopImmediatePropagation();
        if (sending) return;
        for (let i = 0; i < steps.length - 1; i++) {
            if (!validateStep(i)) { goTo(i, false); return; }
        }
        sending = true;
        submit.disabled = true;
        submit.textContent = 'Sending…';
        try {
            const response = await fetch(form.action || location.href, {
                method: 'POST', body: new FormData(form), credentials: 'same-origin'
            });
            if (response.redirected && new URL(response.url).origin === location.origin) {
                location.assign(response.url);
                return;
            }
            if (!response.ok) { error('Registration could not be completed. Please try again.'); return; }
            showServerValidation(await response.text());
        } catch {
            error('Could not contact the registration service. Please try again.');
        } finally {
            sending = false;
            submit.disabled = false;
            submit.textContent = 'Send verification code →';
        }
    }, true);

    goTo(0, false);
    const firstServerError = form.querySelector('.field-validation-error, .validation-summary-errors');
    if (firstServerError) {
        const invalidInput = form.elements.namedItem(firstServerError.getAttribute('data-valmsg-for'));
        const index = invalidInput ? steps.findIndex(step => step.contains(invalidInput)) : 0;
        goTo(Math.max(0, index), false);
    }
})();
