// Supplier offer form: live totals preview, conditional cost and tax fields, attachment list, and the confirmation dialog.
// Display only: the server recalculates and validates every amount (Services/SupplierOfferSubmission.cs).
(function () {
    'use strict';
    var form = document.querySelector('[data-offer-form]'); if (!form) return;
    var ar = document.documentElement.lang === 'ar';
    function t(en, arabic) { return ar ? arabic : en; }
    var currency = form.querySelector('[data-currency]');
    var MAX_FILE = 10 * 1024 * 1024, MAX_TOTAL = 40 * 1024 * 1024, MAX_FILES = 15;
    function decimals() { return currency.value === 'JOD' ? 3 : 2; }
    function num(input) { var v = parseFloat(input && input.value); return isFinite(v) ? v : 0; }
    function round(v) { var f = Math.pow(10, decimals()); return Math.round((v + Number.EPSILON) * f) / f; }
    function money(v) { return v.toLocaleString(t('en-US', 'ar-JO'), { minimumFractionDigits: decimals(), maximumFractionDigits: decimals() }) + ' ' + currency.value; }
    function precise(input) {
        // Refuse (do not round) amounts with more decimals than the currency allows.
        var v = input.value.trim(), d = decimals();
        var ok = v === '' || new RegExp('^\\d+(\\.\\d{1,' + d + '})?$').test(v);
        input.setCustomValidity(ok ? '' : t('Use at most ' + d + ' decimal places for ' + currency.value + '.', 'الحد الأقصى للمنازل العشرية للعملة ' + currency.value + ' هو ' + d + '.'));
    }
    function sync() {
        var step = decimals() === 3 ? '0.001' : '0.01', items = 0, charges = 0;
        form.querySelectorAll('[data-currency-label]').forEach(function (label) { label.textContent = currency.value; });
        form.querySelectorAll('[data-price],[data-cost-amount],[name="Input.TaxAmount"]').forEach(function (i) { i.step = step; precise(i); });
        form.querySelectorAll('[data-price], [data-cost-amount]').forEach(function (i) { i.min = step; });
        form.querySelectorAll('[data-line]').forEach(function (line) {
            var total = num(line.querySelector('[data-qty]')) * num(line.querySelector('[data-price]'));
            items += total; line.querySelector('[data-line-total]').textContent = line.querySelector('[data-price]').value ? money(round(total)) : '—';
        });
        form.querySelectorAll('[data-cost]').forEach(function (row) {
            var on = row.querySelector('[data-cost-toggle]').checked, amount = row.querySelector('[data-cost-amount]');
            row.dataset.on = on ? 'true' : 'false'; amount.required = on; amount.disabled = !on;
            row.querySelectorAll('[data-cost-required]').forEach(function (x) { x.required = on; x.disabled = !on; });
            if (on) charges += num(amount);
        });
        var mode = (form.querySelector('[data-tax-mode]:checked') || {}).value || 'Rate', tax, label = t('Tax', 'الضريبة');
        var rateBox = form.querySelector('[data-tax-rate]'), amountBox = form.querySelector('[data-tax-amount]');
        rateBox.hidden = mode !== 'Rate'; amountBox.hidden = mode !== 'Amount';
        rateBox.querySelectorAll('input').forEach(function (x) { x.disabled = mode !== 'Rate'; });
        amountBox.querySelectorAll('input').forEach(function (x) { x.disabled = mode !== 'Amount'; x.required = mode === 'Amount'; });
        var rateInput = rateBox.querySelector('[name="Input.TaxRate"]'); rateInput.required = mode === 'Rate';
        if (mode === 'Rate') {
            var base = items + (rateBox.querySelector('[name="Input.TaxIncludesCharges"]').checked ? charges : 0);
            tax = round(base * num(rateInput) / 100); label += ' (' + (rateInput.value || '0') + '%)';
        } else tax = num(amountBox.querySelector('input'));
        var set = function (k, v) { form.querySelector('[data-sum="' + k + '"]').textContent = money(v); };
        set('items', round(items)); set('charges', round(charges)); set('before', round(items + charges)); set('tax', tax); set('grand', round(items + charges) + tax);
        form.querySelector('[data-sum-tax-label]').textContent = label;
    }
    // Payment terms: a preset, or "Other" with free text.
    var pick = form.querySelector('[data-terms-pick]'), terms = form.querySelector('[data-terms-value]');
    function syncTerms() { var custom = pick.value === '__custom'; terms.hidden = !custom; terms.required = custom; if (!custom) terms.value = pick.value; }
    pick.addEventListener('change', function () { if (pick.value === '__custom') terms.value = ''; syncTerms(); if (!terms.hidden) terms.focus(); });
    syncTerms();

    // Attachments: show chosen files with sizes; remove one before submission by rebuilding the input's file list.
    var docError = form.querySelector('[data-doc-error]');
    form.querySelectorAll('[data-line-image]').forEach(function (input) {
        var box = input.closest('.soffer-image-box'), preview = box.querySelector('[data-image-preview]');
        var thumb = input.closest('[data-line]').querySelector('[data-item-thumb]');
        var savedThumb = thumb.querySelector('img')?.cloneNode();
        var original = preview.innerHTML, objectUrl;
        var empty = preview.querySelector('[data-image-empty]').cloneNode(true); empty.hidden = false;
        var savedRemove = box.querySelector('[data-remove-saved-image]'), savedLink = box.querySelector('[data-saved-image]');
        function showImage() {
            if (objectUrl) URL.revokeObjectURL(objectUrl);
            var file = input.files[0];
            thumb.querySelector('img')?.remove();
            if (!file) {
                preview.innerHTML = savedRemove && savedRemove.checked ? empty.outerHTML : original;
                if (savedThumb && !savedRemove.checked) thumb.prepend(savedThumb.cloneNode());
                return;
            }
            objectUrl = URL.createObjectURL(file);
            preview.textContent = '';
            var image = document.createElement('img'); image.src = objectUrl; image.alt = t('Preview of ', 'معاينة ') + file.name;
            preview.appendChild(image);
            var thumbnail = image.cloneNode();
            thumbnail.alt = '';
            thumb.prepend(thumbnail);
        }
        input.addEventListener('change', function () {
            if (input.files.length && savedRemove) { savedRemove.checked = true; savedLink.hidden = true; }
            showImage(); checkDocs();
        });
        box.querySelector('[data-clear-image]').addEventListener('click', function () {
            input.value = '';
            if (savedRemove) { savedRemove.checked = true; savedLink.hidden = true; }
            showImage(); checkDocs();
        });
    });
    function renderDocs(card) {
        var input = card.querySelector('[data-doc-input]'), list = card.querySelector('[data-doc-list]');
        list.querySelectorAll('[data-new]').forEach(function (li) { li.remove(); });
        Array.prototype.forEach.call(input.files, function (f, index) {
            var li = document.createElement('li'); li.dataset.new = '1';
            var span = document.createElement('span'); span.textContent = f.name + ' (' + Math.max(1, Math.round(f.size / 1024)) + ' KB)';
            var remove = document.createElement('button'); remove.type = 'button'; remove.textContent = t('Remove', 'إزالة'); remove.setAttribute('aria-label', t('Remove ', 'إزالة ') + f.name);
            remove.addEventListener('click', function () {
                var keep = new DataTransfer(); Array.prototype.forEach.call(input.files, function (x, i) { if (i !== index) keep.items.add(x); });
                input.files = keep.files; renderDocs(card); checkDocs();
            });
            li.append(span, remove); list.append(li);
        });
    }
    function checkDocs() {
        var problems = [], count = 0, total = 0;
        var imageCount = 0;
        var savedImageCount = form.querySelectorAll('[data-remove-saved-image]:not(:checked)').length;
        form.querySelectorAll('[data-line-image]').forEach(function (input) {
            Array.prototype.forEach.call(input.files, function (file) {
                count++; imageCount++; total += file.size;
                if (!/\.(png|jpe?g)$/i.test(file.name)) problems.push(file.name + t(': product images must be PNG or JPEG.', ': يجب أن تكون صورة المنتج PNG أو JPEG.'));
                if (file.size > MAX_FILE) problems.push(file.name + t(': larger than 10 MB.', ': الحجم أكبر من ١٠ ميغابايت.'));
            });
        });
        form.querySelectorAll('[data-doc]').forEach(function (card) {
            var input = card.querySelector('[data-doc-input]');
            Array.prototype.forEach.call(input.files, function (f) {
                count++; total += f.size;
                if (card.dataset.key === 'ReferenceImage' ? !/\.(png|jpe?g)$/i.test(f.name) : !/\.(pdf|png|jpe?g)$/i.test(f.name))
                    problems.push(f.name + (card.dataset.key === 'ReferenceImage' ? t(': only PNG or JPEG images.', ': يُقبل PNG أو JPEG فقط.') : t(': only PDF, PNG or JPEG.', ': يُقبل PDF أو PNG أو JPEG فقط.')));
                if (f.size > MAX_FILE) problems.push(f.name + t(': larger than 10 MB.', ': الحجم أكبر من ١٠ ميغابايت.'));
            });
            var kept = card.querySelectorAll('[data-existing] input:not(:checked)').length;
            if (card.dataset.required === 'true' && input.files.length + kept + (card.dataset.key === 'ReferenceImage' ? imageCount + savedImageCount : 0) === 0) problems.push(card.querySelector('.soffer-doc-name strong').textContent + t(' is required.', ' مطلوب.'));
        });
        if (count > MAX_FILES) problems.push(t('Attach at most ' + MAX_FILES + ' files.', 'الحد الأقصى للمرفقات ' + MAX_FILES + ' ملفًا.'));
        if (total > MAX_TOTAL) problems.push(t('The attachments exceed 40 MB in total.', 'إجمالي حجم المرفقات أكبر من ٤٠ ميغابايت.'));
        docError.hidden = problems.length === 0; docError.textContent = problems.join(' ');
        return problems.length === 0;
    }
    form.querySelectorAll('[data-doc]').forEach(function (card) { card.querySelector('[data-doc-input]').addEventListener('change', function () { renderDocs(card); checkDocs(); }); });
    form.addEventListener('change', function (e) { if (e.target.closest('[data-existing]')) checkDocs(); });

    var panels = Array.from(form.querySelectorAll('[data-offer-panel]'));
    var tabs = Array.from(form.querySelectorAll('[data-offer-step]'));
    var back = form.querySelector('[data-offer-back]'), next = form.querySelector('[data-offer-next]');
    var active = 0, furthest = 0, last = panels.length - 1;
    var input = function (name) { return form.elements.namedItem('Input.' + name); };

    function review() {
        var summary = form.querySelector('[data-offer-review]');
        summary.replaceChildren();
        function card(title, pairs) {
            var entry = document.createElement('section'), h = document.createElement('h3'), list = document.createElement('dl');
            h.textContent = title;
            pairs.filter(function (pair) { return !!pair[1]; }).forEach(function (pair) {
                var dt = document.createElement('dt'), dd = document.createElement('dd');
                dt.textContent = pair[0]; dd.textContent = pair[1]; list.append(dt, dd);
            });
            if (!list.childElementCount) return;
            entry.append(h, list); summary.append(entry);
        }
        function field(element, fallback) {
            var label = element.labels?.[0]?.querySelector('.soffer-label')?.textContent.replace('*', '').trim();
            return [label || fallback, element.tagName === 'SELECT' ? (element.value ? element.selectedOptions[0]?.textContent : '') : element.value.trim()];
        }
        form.querySelectorAll('[data-line]').forEach(function (line) {
            var find = function (suffix) { return line.querySelector('[name$=".' + suffix + '"]'); };
            var image = line.querySelector('[data-line-image]').files[0]?.name;
            var saved = line.querySelector('[data-remove-saved-image]');
            var quantity = line.querySelector('[data-qty]');
            card(line.querySelector('[data-item-name]').textContent, [
                [t('Quantity', 'الكمية'), quantity.value + ' ' + quantity.dataset.unit],
                field(find('OfferedProduct')), field(find('Notes')), field(find('OfferedSpecifications')),
                [t('Unit Price', 'سعر الوحدة'), money(num(find('UnitPrice')))],
                [t('Line Total', 'إجمالي البند'), line.querySelector('[data-line-total]').textContent],
                [t('Product image', 'صورة المنتج'), image || (saved && !saved.checked ? line.querySelector('[data-saved-image]').dataset.savedImage : '')]
            ]);
        });
        card(t('Commercial Terms', 'الشروط التجارية'), [
            field(input('DeliveryDays')), field(input('ValidUntil')),
            [t('Payment Terms', 'شروط الدفع'), terms.hidden ? pick.selectedOptions[0]?.textContent : terms.value],
            field(currency)
        ]);
        var costs = Array.from(form.querySelectorAll('[data-cost][data-on=true]')).flatMap(function (row) {
            var description = row.querySelector('[data-cost-required]')?.value.trim();
            return [[row.querySelector('.soffer-cost-check span').textContent,
                money(num(row.querySelector('[data-cost-amount]'))) + (description ? ' · ' + description : '')]];
        });
        var taxLabel = form.querySelector('[data-sum-tax-label]').textContent;
        if (form.querySelector('[data-tax-rate]:not([hidden])') && input('TaxIncludesCharges').checked)
            taxLabel += t(' · includes additional costs', ' · تشمل التكاليف الإضافية');
        card(t('Additional Costs and Tax', 'التكاليف الإضافية والضريبة'), costs.concat([
            [taxLabel, form.querySelector('[data-sum="tax"]').textContent]
        ]));
        var documents = Array.from(form.querySelectorAll('[data-doc]')).map(function (row) {
            var added = Array.from(row.querySelector('[data-doc-input]').files, function (file) { return file.name; });
            var kept = Array.from(row.querySelectorAll('[data-existing] input:not(:checked)'), function (checkbox) { return checkbox.closest('li').dataset.existing; });
            return [row.querySelector('.soffer-doc-name strong').textContent, kept.concat(added).join(' · ')];
        });
        card(t('Documents and Notes', 'المستندات والملاحظات'), documents.concat([
            field(input('TechnicalProposal'), t('Additional notes', 'ملاحظات إضافية')),
            field(input('WarrantyMonths'), t('Overall warranty period', 'مدة الضمان الإجمالية')),
            field(input('WarrantyType'), t('Warranty type', 'نوع الضمان')),
            field(input('WarrantyTerms'), t('Warranty conditions', 'شروط الضمان')),
            field(input('Deviations'), t('Deviations / clarifications', 'الاختلافات والتوضيحات')),
            field(input('AfterSalesSupport'), t('After-sales support', 'دعم ما بعد البيع'))
        ]));
    }

    function render(index, focus) {
        active = index;
        panels.forEach(function (panel, n) { panel.hidden = n !== index; });
        tabs.forEach(function (tab, n) {
            tab.disabled = n > furthest;
            if (n === index) tab.setAttribute('aria-current', 'step'); else tab.removeAttribute('aria-current');
            tab.classList.toggle('is-complete', n < furthest && n !== index);
            tab.firstElementChild.textContent = n < furthest && n !== index ? '✓' : n + 1;
        });
        back.hidden = index === 0; next.hidden = index === last; button.hidden = index !== last;
        if (index === last) review();
        if (focus) panels[index].querySelector('h2').focus();
    }

    function validateThrough(end) {
        sync(); syncTerms();
        for (var n = 0; n <= end; n++) {
            var fields = Array.from(panels[n].querySelectorAll('input, select, textarea'));
            var invalid = fields.find(function (field) {
                if (!field.willValidate) return false;
                if (field.required && (field.type === 'text' || field.tagName === 'TEXTAREA'))
                    field.setCustomValidity(field.value.trim() ? '' : t('This field is required.', 'هذا الحقل مطلوب.'));
                return !field.validity.valid;
            });
            if (invalid) {
                render(n, false);
                for (var details = invalid.closest('details'); details; details = details.parentElement.closest('details')) details.open = true;
                invalid.reportValidity();
                return false;
            }
            if (n === 3 && !checkDocs()) {
                render(n, false); docError.focus();
                return false;
            }
        }
        return true;
    }

    // The same form and file inputs stay mounted across every step.
    var dialog = form.querySelector('[data-confirm-offer]'), button = form.querySelector('[data-submit-offer]');
    next.addEventListener('click', function () {
        if (!validateThrough(active)) return;
        furthest = Math.max(furthest, active + 1); render(active + 1, true);
    });
    back.addEventListener('click', function () { render(active - 1, true); });
    tabs.forEach(function (tab, index) {
        tab.addEventListener('click', function () { if (index <= active || validateThrough(index - 1)) render(index, true); });
    });
    form.addEventListener('submit', function (event) {
        if (!event.submitter?.matches('[data-save-draft]')) event.preventDefault();
    });
    button.addEventListener('click', function () {
        if (!validateThrough(last - 1)) return;
        dialog.querySelector('[data-confirm-total]').textContent = t('Grand total: ', 'الإجمالي الكلي: ') + form.querySelector('[data-sum="grand"]').textContent;
        dialog.showModal();
    });
    dialog.querySelector('[data-cancel]').addEventListener('click', function () { dialog.close(); });
    dialog.querySelector('[data-ok]').addEventListener('click', function () {
        dialog.close(); button.disabled = true; button.textContent = t('Submitting…', 'جارٍ الإرسال…');
        form.submit();
    });
    form.addEventListener('input', sync); form.addEventListener('change', sync);
    sync(); render(0, false);
    form.querySelector('[data-offer-server-error]')?.focus();
})();
