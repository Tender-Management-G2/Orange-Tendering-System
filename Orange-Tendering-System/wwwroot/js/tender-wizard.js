(() => {
    const byId = id => document.getElementById(id);
    const dialog = byId('tc-dialog');
    if (!dialog) return;
    const form = byId('tc-form');
    const { requests, offerCurrencies, strings } = JSON.parse(byId('tw-data').textContent);
    const panels = [...form.querySelectorAll('[data-tw-panel]')];
    const tabs = [...form.querySelectorAll('[data-tw-step]')];
    const seats = [...form.querySelectorAll('[data-seat]')];
    const docs = [...form.querySelectorAll('[name=RequiredDocuments]')];
    const requestField = byId('tc-pr');
    const title = byId('Title');
    const description = byId('Description');
    const currency = byId('Currency');
    const alert = byId('tw-error');
    const count = byId('tw-count');
    const readiness = byId('tw-readiness');
    const readinessTitle = byId('tw-readiness-title');
    const defaultReadiness = readinessTitle.textContent;
    const submit = byId('tc-submit');
    const next = byId('tw-next');
    const back = byId('tw-back');
    const labels = ['Title', 'Description', 'EstimatedValue', 'Currency', 'CommercialConditions',
        'DeliveryExpectations', 'MandatoryCompliance'];
    const format = (pattern, ...values) => pattern.replace(/\{(\d+)\}/g, (_, i) => values[i]);
    const displayNumber = new Intl.NumberFormat(dialog.lang, { maximumFractionDigits: 3 });
    const serverInvalid = new Set();
    let step = 0;
    let furthest = 0;
    let dirty = false;
    let sending = false;

    function request() {
        return requests.find(row => String(row.id) === requestField.value);
    }

    function node(tag, value, className) {
        const element = document.createElement(tag);
        element.textContent = value ?? '';
        if (className) element.className = className;
        return element;
    }

    function updateRequest() {
        const row = request();
        const subtitle = byId('tw-subtitle');
        subtitle.textContent = row ? row.number + ' · ' + row.title : '';
        subtitle.hidden = !row;
        title.value = row?.title ?? '';
        description.value = row?.description ?? '';
        byId('tw-source').textContent = row ? format(strings.from, row.number) : '';
        byId('tw-request-details').hidden = !row;
        const items = byId('tw-request-items');
        items.replaceChildren();
        if (!row) return;
        const facts = { ...row, itemCount: displayNumber.format(row.items.length),
            total: row.total == null ? '' : displayNumber.format(row.total) + ' ' + row.items[0].unit };
        form.querySelectorAll('[data-pr]').forEach(element => {
            const value = facts[element.dataset.pr];
            element.textContent = value ?? '';
            const optional = element.closest('[data-tw-optional]');
            if (optional) optional.hidden = !value;
        });
        for (const item of row.items) {
            const tr = node('tr');
            tr.append(node('th', item.name), node('td', displayNumber.format(item.quantity) + ' ' + item.unit),
                node('td', item.specifications, 'tw-specifications'));
            tr.firstElementChild.scope = 'row';
            items.append(tr);
        }
    }

    function updateEligibility(requestChanged = false) {
        const row = request();
        for (const option of currency.options) {
            option.disabled = !!option.value && !!row?.lowestPrice && !offerCurrencies.includes(option.value);
        }
        if (currency.selectedOptions[0]?.disabled) currency.value = '';
        for (const seat of seats) {
            const field = byId(seat.dataset.seat);
            const manager = field.id === 'SourcingManagerId';
            field.disabled = !!row?.lowestPrice && !manager;
            seat.hidden = field.disabled;
            for (const option of field.options) {
                option.hidden = !!option.value && field.id === 'DepartmentRepresentativeId'
                    && option.dataset.dept !== String(row?.departmentId);
                option.disabled = option.hidden;
            }
            if (field.selectedOptions[0]?.hidden || (requestChanged && field.disabled)) field.value = '';
        }
        const active = seats.filter(seat => !seat.hidden).map(seat => byId(seat.dataset.seat));
        for (const field of active) {
            for (const option of field.options) {
                option.disabled = option.hidden || (!!option.value && active.some(other =>
                    other !== field && other.value === option.value));
            }
            const seat = field.closest('[data-seat]');
            const valid = assignmentValid(field, active) && !serverInvalid.has(field);
            seat.querySelector('[data-seat-department]').textContent = valid
                ? field.selectedOptions[0].dataset.deptName || '—' : '—';
            const status = seat.querySelector('[data-seat-status]');
            status.classList.toggle('tw-positive', valid);
            status.textContent = valid ? '✓ ' + strings.assigned : strings.missing;
        }
    }

    function assignmentValid(field, active) {
        return !!field.value && !field.selectedOptions[0]?.disabled
            && active.filter(other => other.value === field.value).length === 1;
    }

    function fieldLabel(field) {
        return field.labels?.[0]?.querySelector('span')?.textContent.trim() ?? '';
    }

    function checklist() {
        const active = seats.filter(seat => !seat.hidden).map(seat => byId(seat.dataset.seat));
        const groups = [
            [{ field: requestField, label: fieldLabel(requestField), valid: !!request() }],
            [...labels.map(id => {
                const field = byId(id);
                return { field, label: fieldLabel(field), valid: !!field.value.trim()
                    && field.validity.valid && !field.selectedOptions?.[0]?.disabled };
            }), { field: byId('tw-documents'), label: byId('tw-documents-label').textContent,
                valid: docs.some(doc => doc.checked) }],
            active.map(field => ({ field, label: field.closest('[data-seat]').dataset.label,
                valid: assignmentValid(field, active) }))
        ];
        for (const check of groups.flat()) if (serverInvalid.has(check.field)) check.valid = false;
        return groups;
    }

    function updateDocuments() {
        const container = byId('tw-selected-documents');
        container.replaceChildren();
        for (const doc of docs.filter(doc => doc.checked)) {
            const chip = node('button', doc.dataset.label + ' ×', 'tw-chip tw-remove');
            chip.type = 'button';
            chip.setAttribute('aria-label', format(strings.removeDocument, doc.dataset.label));
            chip.addEventListener('click', () => {
                doc.checked = false;
                doc.dispatchEvent(new Event('change', { bubbles: true }));
                container.nextElementSibling.querySelector('summary').focus();
            });
            container.append(chip);
        }
    }

    function updateReview() {
        const row = request();
        if (!row) return;
        const card = key => form.querySelector('[data-review=' + key + ']');
        const labeled = (label, value) => {
            const wrapper = node('div', '', 'tw-review-line');
            wrapper.append(node('small', label), node('div', value));
            return wrapper;
        };
        const line = (key, ...values) => {
            card(key).replaceChildren(...values.filter(Boolean).map(value => node('div', value, 'tw-review-line')));
        };
        line('request', row.number + ' · ' + row.title,
            [row.department, row.status, row.needed, row.method].filter(Boolean).join(' · '),
            row.items.map(item => item.name + ' × ' + displayNumber.format(item.quantity) + ' ' + item.unit).join(' · '));
        card('general').replaceChildren(labeled(fieldLabel(title), title.value), labeled(fieldLabel(description), description.value));
        const estimate = byId('EstimatedValue');
        card('financial').replaceChildren(labeled(fieldLabel(estimate), displayNumber.format(estimate.value) + ' ' + currency.value),
            labeled(fieldLabel(byId('CommercialConditions')), byId('CommercialConditions').value));
        if (byId('IsCritical')?.checked)
            card('financial').append(node('small', byId('IsCritical').closest('label').innerText.trim()));
        line('delivery', byId('DeliveryExpectations').value);
        const chips = node('div', '', 'tw-chips');
        chips.append(...docs.filter(doc => doc.checked).map(doc => node('span', doc.dataset.label, 'tw-chip')));
        card('documents').replaceChildren(chips, node('p', byId('MandatoryCompliance').value, 'tw-review-line'));
        card('committee').replaceChildren(...seats.filter(seat => !seat.hidden).map(seat => {
            const field = byId(seat.dataset.seat);
            return node('div', seat.dataset.label + ' · ' + (field.value ? field.selectedOptions[0].textContent : strings.missing));
        }));
    }

    function render() {
        const checks = checklist();
        const invalid = checks.findIndex(group => group.some(check => !check.valid));
        const allValid = invalid === -1;
        tabs.forEach((button, index) => {
            const complete = index < furthest && index < (allValid ? 3 : invalid);
            button.disabled = sending || index === step || !complete;
            button.classList.toggle('tw-complete', complete && index !== step);
            button.classList.toggle('tw-current', index === step);
            button.querySelector('.tw-step-number').textContent = complete && index !== step ? '✓' : index + 1;
            if (index === step) button.setAttribute('aria-current', 'step');
            else button.removeAttribute('aria-current');
            panels[index].hidden = index !== step;
            panels[index].inert = sending;
        });
        readiness.hidden = step === 0;
        readinessTitle.textContent = step === 3 ? readinessTitle.dataset.reviewTitle : defaultReadiness;
        const visibleChecks = step === 3 ? checks.flat() : checks[step];
        const done = visibleChecks.filter(check => check.valid).length;
        const progress = byId('tw-progress');
        progress.max = visibleChecks.length;
        progress.value = done;
        count.textContent = format(strings.count, done, visibleChecks.length);
        byId('tw-checklist').replaceChildren(...visibleChecks.map(check => {
            const item = node('li');
            item.append(node('span', check.valid ? '✓' : '', check.valid ? 'tw-ok' : 'tw-pending'),
                document.createTextNode(check.label));
            item.firstElementChild.setAttribute('aria-hidden', 'true');
            item.setAttribute('aria-label', check.label + ': ' + (check.valid ? strings.complete : strings.missing));
            return item;
        }));
        byId('tw-step-count').textContent = format(strings.step, step + 1, panels.length);
        back.hidden = step === 0;
        back.disabled = sending;
        next.hidden = step === 3;
        submit.hidden = step !== 3;
        submit.disabled = sending || !allValid;
        if (step === 3) updateReview();
    }

    function go(index, focus = true) {
        step = index;
        render();
        dialog.querySelector('.tw-body').scrollTop = 0;
        if (focus) panels[index].querySelector('h3').focus();
    }

    function clearError(field) {
        if (!field?.hasAttribute('aria-invalid')) return;
        byId(field.id + '-error')?.remove();
        field.removeAttribute('aria-invalid');
        field.removeAttribute('aria-describedby');
    }

    function showFieldError(field, message) {
        clearError(field);
        const error = node('p', message, 'tw-field-error');
        error.id = field.id + '-error';
        field.after(error);
        field.setAttribute('aria-invalid', 'true');
        field.setAttribute('aria-describedby', error.id);
    }

    function focusField(field) {
        (field.matches('input,textarea,select') ? field : field.querySelector('summary'))?.focus();
    }

    const fieldMessages = {
        'tc-pr': strings.chooseRequest,
        EstimatedValue: strings.enterAmount,
        Currency: strings.chooseCurrency,
        'tw-documents': strings.chooseDocuments,
        Title: strings.missingSource,
        Description: strings.missingSource
    };

    function validateThrough(index) {
        const checks = checklist();
        for (let i = 0; i <= index; i++) {
            const invalid = checks[i].filter(check => !check.valid);
            if (!invalid.length) continue;
            go(i, false);
            for (const { field } of invalid) {
                if (!serverInvalid.has(field))
                    showFieldError(field, fieldMessages[field.id] ?? (field.closest('[data-seat]') ? strings.chooseEmployee : strings.invalid));
            }
            focusField(invalid[0].field);
            return false;
        }
        return true;
    }

    next.addEventListener('click', () => {
        if (!validateThrough(step)) return;
        furthest = Math.max(furthest, step + 1);
        go(step + 1);
    });
    back.addEventListener('click', () => go(step - 1));
    for (const tab of tabs) tab.addEventListener('click', () => go(Number(tab.dataset.twStep)));
    form.querySelectorAll('[data-tw-edit]').forEach(button => {
        button.addEventListener('click', () => go(Number(button.dataset.twEdit)));
    });

    function changed(event) {
        dirty = true;
        alert.hidden = true;
        if (event.target === requestField) {
            updateRequest();
            furthest = 0;
            serverInvalid.clear();
            form.querySelectorAll('[aria-invalid]').forEach(clearError);
        }
        const field = event.target.name === 'RequiredDocuments' ? byId('tw-documents') : event.target;
        serverInvalid.delete(field);
        clearError(field);
        updateEligibility(event.target === requestField);
        if (field.id === 'tw-documents') updateDocuments();
        render();
    }
    form.addEventListener('input', event => {
        if (event.target.matches('input:not([type=checkbox]), textarea')) changed(event);
    });
    form.addEventListener('change', event => {
        if (event.target.id === 'tw-delivery-template') {
            if (!event.target.value) return;
            const delivery = byId('DeliveryExpectations');
            delivery.value = event.target.value;
            event.target.value = '';
            delivery.dispatchEvent(new Event('input', { bubbles: true }));
            delivery.focus();
            return;
        }
        if (event.target.matches('select, input[type=checkbox]')) changed(event);
    });

    function showServerErrors(data) {
        const fields = [];
        const messages = [...(data.errors ?? [])];
        for (const [name, errors] of Object.entries(data.fields ?? {})) {
            const field = name === 'PurchaseRequestId' ? requestField
                : name.startsWith('RequiredDocuments') ? byId('tw-documents') : byId(name);
            const index = field ? panels.findIndex(panel => panel.contains(field)) : -1;
            if (index < 0 || field.disabled) messages.push(...errors);
            else fields.push({ field, index, message: errors.join(' ') });
        }
        if (fields.length) {
            fields.sort((a, b) => a.index - b.index);
            for (const error of fields) {
                serverInvalid.add(error.field);
                showFieldError(error.field, error.message);
            }
            updateEligibility();
            go(fields[0].index, false);
            focusField(fields[0].field);
        }
        if (messages.length || !fields.length) {
            alert.textContent = messages.join(' ') || strings.failed;
            alert.hidden = false;
            alert.focus();
        }
    }

    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (sending || step !== 3 || !validateThrough(2)) return;
        sending = true;
        alert.hidden = true;
        render();
        const motion = window.OrangeMotion?.begin({ mode: 'popup', title: byId('tc-title').textContent });
        try {
            const response = await fetch(form.action, {
                method: 'POST', body: new FormData(form), credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest', Accept: 'application/json' }
            });
            const data = await response.json().catch(() => ({}));
            if (response.ok && data.id) {
                dirty = false;
                if (motion && data.feedback) await Promise.resolve(motion.finish(data.feedback)).catch(() => {});
                window.location.href = form.dataset.listUrl + '?created=' + data.id + '&open=' + data.id;
                return;
            }
            sending = false;
            motion?.cancel();
            showServerErrors(data);
        } catch {
            sending = false;
            motion?.cancel();
            showServerErrors({ errors: [strings.network] });
        }
        render();
    });

    function open(id) {
        if (dialog.open) return;
        if (id) requestField.value = String(id);
        updateRequest();
        updateEligibility();
        updateDocuments();
        go(0, false);
        dialog.showModal();
        requestField.focus();
    }

    function close() {
        if (sending || (dirty && !window.confirm(strings.discard))) return;
        dialog.close();
        form.reset();
        form.querySelectorAll('[aria-invalid]').forEach(clearError);
        form.querySelectorAll('details[open]').forEach(details => { details.open = false; });
        alert.hidden = true;
        dirty = false;
        furthest = 0;
        serverInvalid.clear();
    }

    document.querySelectorAll('[data-open-tender]').forEach(button => button.addEventListener('click', () => open()));
    dialog.querySelectorAll('[data-tc-close]').forEach(button => button.addEventListener('click', close));
    dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
    dialog.addEventListener('click', event => {
        if (event.target === dialog) {
            const rect = dialog.getBoundingClientRect();
            if (event.clientX < rect.left || event.clientX > rect.right ||
                event.clientY < rect.top || event.clientY > rect.bottom) close();
        }
    });
    const query = new URLSearchParams(window.location.search);
    if (query.has('request') || query.has('new')) open(query.get('request'));
})();
