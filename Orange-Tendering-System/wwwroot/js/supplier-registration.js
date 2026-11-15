// Supplier registration wizard (Areas/Identity/Pages/Account/Register.cshtml).
// Owns the four steps, client-side validation and the fetch submission; the server validates
// everything again. It also publishes a small, sanitised description of form readiness for the
// illustration (wwwroot/js/auth-studio): never the password, never an e-mail address, never the
// registration or tax number, never document contents.
(() => {
    'use strict';
    const root = document.getElementById('authStudio');
    const form = document.getElementById('registerForm');
    if (!root || !form) return;
    const steps = [...form.querySelectorAll('.as-step')];
    const progress = [...root.querySelectorAll('.as-steps li')];
    const next = document.getElementById('registrationNext');
    const back = document.getElementById('registrationBack');
    const submit = document.getElementById('registerSubmit');
    const summary = document.getElementById('registrationErrors');
    const review = document.getElementById('registrationReview');
    const rules = [...form.querySelectorAll('#passwordRules li')];
    const requiredFields = new Set([
        'Input.FullName', 'Input.Email', 'Input.Password', 'Input.ConfirmPassword',
        'Input.CompanyName', 'Input.Category', 'Input.RegistrationNumber',
        'Input.CompanyEmail', 'Input.CompanyPhone', 'Input.CompanyAddress'
    ]);
    const fileFields = ['CommercialRegistrationCertificate', 'TaxCertificate', 'LegalDocuments'];
    const sceneNames = {
        'Input.FullName': 'name', 'Input.Email': 'email', 'Input.Password': 'password',
        'Input.ConfirmPassword': 'confirm', 'Input.CompanyName': 'company', 'Input.Category': 'category'
    };
    // The same rules the server applies: StringLength(100, MinimumLength = 8) on the page model
    // plus ASP.NET Core Identity's default password options.
    const passwordRules = [
        { test: v => v.length >= 8, label: 'at least 8 characters' },
        { test: v => /[a-z]/.test(v), label: 'a lowercase letter' },
        { test: v => /[A-Z]/.test(v), label: 'an uppercase letter' },
        { test: v => /\d/.test(v), label: 'a number' },
        { test: v => /[^A-Za-z0-9]/.test(v), label: 'a symbol' }
    ];
    const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
    const maxFileBytes = 10 * 1024 * 1024;
    const attempted = new Set();
    let stage = 0;
    let sending = false;
    let submitState = 'idle';
    let focusName = null;

    const field = name => form.elements.namedItem(name);
    const value = name => (field(name)?.value || '').trim();
    const messageBox = name => form.querySelector('[data-valmsg-for="' + name + '"]');
    const filesOf = name => [...(field(name)?.files || [])];

    function label(input) {
        const element = form.querySelector('label[for="' + input.id + '"]');
        if (!element) return 'This field';
        // The visible name only: hints inside <small> are not part of it.
        return [...element.childNodes].filter(node => node.nodeType === Node.TEXT_NODE)
            .map(node => node.textContent).join(' ').trim() || 'This field';
    }

    // ---------- Messages ----------

    function error(message) {
        summary.className = 'as-summary is-error';
        const list = document.createElement('ul');
        const item = document.createElement('li');
        item.textContent = message;
        list.append(item);
        summary.replaceChildren(list);
        summary.scrollIntoView({ block: 'nearest' });
    }

    function clearError() {
        summary.className = 'as-summary';
        summary.replaceChildren();
    }

    function setFieldError(name, message) {
        const input = field(name);
        const box = messageBox(name);
        if (box) {
            box.textContent = message || '';
            box.classList.toggle('field-validation-error', !!message);
            box.classList.toggle('field-validation-valid', !message);
        }
        if (input?.classList) {
            input.classList.toggle('input-validation-error', !!message);
            if (message) input.setAttribute('aria-invalid', 'true'); else input.removeAttribute('aria-invalid');
        }
    }

    // ---------- Validation ----------

    function missingPasswordRules() {
        const password = field('Input.Password')?.value || '';
        return passwordRules.filter(rule => !rule.test(password)).map(rule => rule.label);
    }

    function fileProblem(name) {
        const files = filesOf(name);
        if (!files.length) {
            if (name === 'CommercialRegistrationCertificate') return 'Commercial registration certificate is required.';
            if (name === 'LegalDocuments') return 'At least one business license or legal document is required.';
            return value('Input.TaxNumber') ? 'Tax certificate is required when a tax number is provided.' : '';
        }
        for (const file of files) {
            if (!/\.(pdf|png|jpe?g)$/i.test(file.name)) return file.name + ' is not a PDF, PNG or JPEG file.';
            if (file.size > maxFileBytes) return file.name + ' is larger than 10 MB.';
            if (file.size === 0) return file.name + ' is empty.';
        }
        if (name === 'LegalDocuments' && fileFields.flatMap(filesOf).length > 7) return 'Upload no more than seven documents.';
        return '';
    }

    function problem(name) {
        const input = field(name);
        if (!input) return '';
        if (fileFields.includes(name)) return fileProblem(name);
        const text = (input.value || '').trim();
        if (name === 'Input.Password') {
            if (!input.value) return 'Password is required.';
            const missing = missingPasswordRules();
            return missing.length ? 'Password still needs ' + missing.join(', ') + '.' : '';
        }
        if (name === 'Input.ConfirmPassword') {
            if (!input.value) return 'Enter the password again to confirm it.';
            return input.value === field('Input.Password').value ? '' : 'The two passwords do not match.';
        }
        if (name === 'Input.Category') return text ? '' : 'Choose your main supply category.';
        if (!text) return requiredFields.has(name) ? label(input) + ' is required.' : '';
        if (name === 'Input.Email' || name === 'Input.CompanyEmail')
            return emailPattern.test(text) ? '' : 'Enter a valid email address, for example name@company.com.';
        return input.checkValidity() ? '' : 'Check ' + label(input).toLowerCase() + '.';
    }

    const namesIn = index => [...steps[index].querySelectorAll('input,select')]
        .filter(input => !input.disabled && input.type !== 'hidden' && input.name)
        .map(input => input.name);

    function check(name) {
        const message = problem(name);
        setFieldError(name, message);
        return !message;
    }

    function validateStep(index) {
        attempted.add(index);
        let firstInvalid = null;
        for (const name of namesIn(index)) {
            if (!check(name) && !firstInvalid) firstInvalid = field(name);
        }
        if (index === 2) updateFileStates();
        if (firstInvalid) {
            if (steps[index].hidden) goTo(index, false);
            firstInvalid.focus();
            publish();
            return false;
        }
        clearError();
        return true;
    }

    // ---------- Field furniture: password rules, reveal buttons, document states ----------

    function updateRules() {
        const password = field('Input.Password')?.value || '';
        rules.forEach((item, index) => {
            const met = passwordRules[index].test(password);
            item.classList.toggle('is-met', met);
            const state = item.querySelector('.sr-only');
            if (state) state.textContent = met ? '— met' : '— not met';
        });
    }

    root.querySelectorAll('[data-reveal]').forEach(button => {
        button.addEventListener('click', () => {
            const input = document.getElementById(button.dataset.reveal);
            const show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            button.textContent = show ? 'Hide' : 'Show';
            button.setAttribute('aria-pressed', String(show));
        });
    });

    // 'none' | 'accepted' | 'rejected'. Accepted means "passed the local checks", nothing more:
    // the server checks every file again and an administrator reviews the documents later.
    function fileState(name) {
        if (!filesOf(name).length) return 'none';
        return fileProblem(name) ? 'rejected' : 'accepted';
    }

    function updateFileStates() {
        for (const name of fileFields) {
            const line = form.querySelector('[data-file-state="' + name + '"]');
            if (!line) continue;
            const files = filesOf(name);
            const state = fileState(name);
            line.classList.toggle('is-accepted', state === 'accepted');
            line.classList.toggle('is-rejected', state === 'rejected');
            line.textContent = state === 'accepted'
                ? (files.length === 1 ? files[0].name : files.length + ' files') + ' selected. Checked again on the server when you submit.'
                : '';
        }
    }

    // ---------- Sanitised state for the illustration ----------

    function emailState() {
        const text = value('Input.Email');
        if (!text) return 'empty';
        if (emailPattern.test(text)) return 'valid';
        return focusName === 'email' && !attempted.has(0) ? 'typing' : 'invalid';
    }

    function confirmState() {
        const password = field('Input.Password')?.value || '';
        const confirm = field('Input.ConfirmPassword')?.value || '';
        if (!confirm) return 'empty';
        if (confirm === password) return 'match';
        return focusName === 'confirm' && password.startsWith(confirm) && !attempted.has(0) ? 'partial' : 'mismatch';
    }

    function publish() {
        const detail = {
            step: stage,
            focus: focusName,
            name: value('Input.FullName'),
            email: emailState(),
            hasPassword: (field('Input.Password')?.value || '').length > 0,
            rules: passwordRules.length - missingPasswordRules().length,
            confirm: confirmState(),
            company: value('Input.CompanyName'),
            category: value('Input.Category'),
            companyReady: steps[1] ? namesIn(1).every(name => !problem(name)) : false,
            documents: {
                registration: fileState('CommercialRegistrationCertificate'),
                tax: fileState('TaxCertificate'),
                legal: fileState('LegalDocuments')
            },
            submit: submitState
        };
        root.authState = detail;
        root.dispatchEvent(new CustomEvent('authstudio:state', { detail }));
    }

    // ---------- Steps ----------

    function updateReview() {
        const entries = [
            ['Contact', value('Input.FullName')], ['Email', value('Input.Email')],
            ['Company', value('Input.CompanyName')], ['Category', value('Input.Category')],
            ['Registration', value('Input.RegistrationNumber')],
            ['Company email', value('Input.CompanyEmail')], ['Telephone', value('Input.CompanyPhone')],
            ['Address', value('Input.CompanyAddress')],
            ['Documents', fileFields.flatMap(filesOf).map(file => file.name).join(', ')]
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
        root.dataset.step = String(index);
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
        if (focus) {
            const first = steps[index].querySelector('input,select') || steps[index].querySelector('legend');
            if (first) {
                if (first.tagName === 'LEGEND') first.setAttribute('tabindex', '-1');
                first.focus({ preventScroll: true });
            }
            steps[index].scrollIntoView({ block: 'nearest' });
        }
        publish();
    }

    next.addEventListener('click', () => { if (validateStep(stage)) goTo(stage + 1); });
    back.addEventListener('click', () => { clearError(); goTo(stage - 1); });

    form.addEventListener('input', event => {
        const name = event.target.name;
        if (name === 'Input.Password') updateRules();
        // A message that is already showing follows the field; nothing new appears while typing.
        if (name && messageBox(name)?.textContent) check(name);
        if (name === 'Input.Password' && messageBox('Input.ConfirmPassword')?.textContent) check('Input.ConfirmPassword');
        publish();
    });
    form.addEventListener('change', event => {
        const name = event.target.name;
        if (fileFields.includes(name)) {
            // A rejected file is reported beside its field at once; a missing one only on Continue.
            setFieldError(name, filesOf(name).length ? fileProblem(name) : '');
            if (name !== 'LegalDocuments' && messageBox('LegalDocuments')?.textContent) check('LegalDocuments');
            updateFileStates();
        } else if (name === 'Input.Category' && event.target.value) {
            setFieldError(name, '');
        } else if (name === 'Input.TaxNumber' && messageBox('TaxCertificate')?.textContent) {
            check('TaxCertificate');
        }
        publish();
    });
    form.addEventListener('focusin', event => {
        focusName = sceneNames[event.target.name] || null;
        publish();
    });
    form.addEventListener('focusout', event => {
        const name = event.target.name;
        focusName = null;
        // Leaving a field that has content checks it; empty fields are only reported on Continue.
        if (name && !fileFields.includes(name) && (event.target.value || attempted.has(stage))) check(name);
        publish();
    });
    requiredFields.forEach(name => field(name)?.setAttribute('aria-required', 'true'));

    // ---------- Submission ----------

    function showServerValidation(html) {
        const returned = new DOMParser().parseFromString(html, 'text/html').getElementById('registerForm');
        if (!returned) { error('Registration could not be completed. Please try again.'); return; }
        const returnedSummary = [...returned.querySelectorAll('.validation-summary-errors')]
            .find(element => element.textContent.trim());
        if (returnedSummary) {
            summary.className = 'as-summary is-error';
            summary.innerHTML = returnedSummary.innerHTML;
        } else clearError();
        let firstInvalid = null;
        for (const current of form.querySelectorAll('[data-valmsg-for]')) {
            const name = current.getAttribute('data-valmsg-for');
            const replacement = [...returned.querySelectorAll('[data-valmsg-for]')]
                .find(element => element.getAttribute('data-valmsg-for') === name);
            if (!replacement) continue;
            const message = replacement.textContent.trim();
            setFieldError(name, message);
            if (!firstInvalid && message) firstInvalid = form.elements.namedItem(name);
        }
        if (firstInvalid) {
            const invalidStep = steps.findIndex(step => step.contains(firstInvalid));
            if (invalidStep >= 0) goTo(invalidStep, false);
            firstInvalid.focus();
        } else summary.scrollIntoView({ block: 'nearest' });
    }

    function setSubmitState(state) {
        submitState = state;
        publish();
    }

    form.addEventListener('submit', async event => {
        event.preventDefault();
        event.stopImmediatePropagation();
        if (sending) return;
        for (let i = 0; i < steps.length - 1; i++) {
            if (!validateStep(i)) return;
        }
        sending = true;
        submit.disabled = true;
        submit.textContent = 'Sending…';
        setSubmitState('sending');
        try {
            const response = await fetch(form.action || location.href, {
                method: 'POST', body: new FormData(form), credentials: 'same-origin'
            });
            if (response.redirected && new URL(response.url).origin === location.origin) {
                // The server created the pending application and sent the verification code.
                setSubmitState('accepted');
                location.assign(response.url);
                return;
            }
            setSubmitState('failed');
            if (!response.ok) { error('Registration could not be completed. Please try again.'); return; }
            showServerValidation(await response.text());
        } catch {
            setSubmitState('failed');
            error('Could not contact the registration service. Please try again.');
        } finally {
            sending = false;
            submit.disabled = false;
            submit.textContent = 'Send verification code →';
        }
    }, true);

    // ---------- Start ----------

    updateRules();
    updateFileStates();
    goTo(0, false);
    const firstServerError = form.querySelector('.field-validation-error, .validation-summary-errors');
    if (firstServerError) {
        const invalidInput = form.elements.namedItem(firstServerError.getAttribute('data-valmsg-for'));
        const index = invalidInput ? steps.findIndex(step => step.contains(invalidInput)) : 0;
        goTo(Math.max(0, index), false);
    }
    // Values restored by the browser (back navigation, autofill) arrive after this script ran.
    window.addEventListener('pageshow', () => { updateRules(); updateFileStates(); publish(); });
})();
