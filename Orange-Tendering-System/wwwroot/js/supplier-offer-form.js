// Supplier offer form: live totals preview, conditional cost and tax fields, attachment list, and the confirmation dialog.
// Display only: the server recalculates and validates every amount (Services/SupplierOfferSubmission.cs).
(function () {
    'use strict';
    var form = document.querySelector('[data-offer-form]'); if (!form) return;
    var currency = form.querySelector('[data-currency]');
    var MAX_FILE = 10 * 1024 * 1024, MAX_TOTAL = 40 * 1024 * 1024, MAX_FILES = 15;
    function decimals() { return currency.value === 'JOD' ? 3 : 2; }
    function num(input) { var v = parseFloat(input && input.value); return isFinite(v) ? v : 0; }
    function round(v) { var f = Math.pow(10, decimals()); return Math.round((v + Number.EPSILON) * f) / f; }
    function money(v) { return v.toLocaleString('en-US', { minimumFractionDigits: decimals(), maximumFractionDigits: decimals() }) + ' ' + currency.value; }
    function precise(input) {
        // Refuse (do not round) amounts with more decimals than the currency allows.
        var v = input.value.trim(), d = decimals();
        var ok = v === '' || new RegExp('^\\d+(\\.\\d{1,' + d + '})?$').test(v);
        input.setCustomValidity(ok ? '' : 'Use at most ' + d + ' decimal places for ' + currency.value + '.');
    }
    function sync() {
        var step = decimals() === 3 ? '0.001' : '0.01', items = 0, charges = 0;
        form.querySelectorAll('[data-currency-label]').forEach(function (label) { label.textContent = currency.value; });
        form.querySelectorAll('[data-price],[data-cost-amount],[name="Input.TaxAmount"]').forEach(function (i) { i.step = step; precise(i); });
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
        var mode = (form.querySelector('[data-tax-mode]:checked') || {}).value || 'Rate', tax, label = 'Tax';
        var rateBox = form.querySelector('[data-tax-rate]'), amountBox = form.querySelector('[data-tax-amount]');
        rateBox.hidden = mode !== 'Rate'; amountBox.hidden = mode !== 'Amount';
        rateBox.querySelectorAll('input').forEach(function (x) { x.disabled = mode !== 'Rate'; });
        amountBox.querySelectorAll('input').forEach(function (x) { x.disabled = mode !== 'Amount'; x.required = mode === 'Amount'; });
        var rateInput = rateBox.querySelector('[name="Input.TaxRate"]'); rateInput.required = mode === 'Rate';
        if (mode === 'Rate') {
            var base = items + (rateBox.querySelector('[name="Input.TaxIncludesCharges"]').checked ? charges : 0);
            tax = round(base * num(rateInput) / 100); label = 'Tax (' + (rateInput.value || '0') + '%)';
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
        var original = preview.innerHTML, objectUrl;
        var empty = preview.querySelector('[data-image-empty]').cloneNode(true); empty.hidden = false;
        var savedRemove = box.querySelector('[data-remove-saved-image]'), savedLink = box.querySelector('[data-saved-image]');
        function showImage() {
            if (objectUrl) URL.revokeObjectURL(objectUrl);
            var file = input.files[0];
            if (!file) { preview.innerHTML = savedRemove && savedRemove.checked ? empty.outerHTML : original; return; }
            objectUrl = URL.createObjectURL(file);
            preview.textContent = '';
            var image = document.createElement('img'); image.src = objectUrl; image.alt = 'Preview of ' + file.name;
            preview.appendChild(image);
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
            var remove = document.createElement('button'); remove.type = 'button'; remove.textContent = 'Remove'; remove.setAttribute('aria-label', 'Remove ' + f.name);
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
                if (!/\.(png|jpe?g)$/i.test(file.name)) problems.push(file.name + ': product images must be PNG or JPEG.');
                if (file.size > MAX_FILE) problems.push(file.name + ': larger than 10 MB.');
            });
        });
        form.querySelectorAll('[data-doc]').forEach(function (card) {
            var input = card.querySelector('[data-doc-input]');
            Array.prototype.forEach.call(input.files, function (f) {
                count++; total += f.size;
                if (card.dataset.key === 'BankGuarantee' && !/\.pdf$/i.test(f.name))
                    problems.push(f.name + ': the bank guarantee must be a PDF.');
                else if (card.dataset.key === 'ReferenceImage' ? !/\.(png|jpe?g)$/i.test(f.name) : !/\.(pdf|png|jpe?g)$/i.test(f.name))
                    problems.push(f.name + (card.dataset.key === 'ReferenceImage' ? ': only PNG or JPEG images.' : ': only PDF, PNG or JPEG.'));
                if (f.size > MAX_FILE) problems.push(f.name + ': larger than 10 MB.');
            });
            var kept = card.querySelectorAll('[data-existing] input:not(:checked)').length;
            if (card.dataset.key === 'BankGuarantee' && input.files.length + kept > 1)
                problems.push('Remove the saved bank guarantee before replacing its PDF.');
            if (card.dataset.required === 'true' && input.files.length + kept + (card.dataset.key === 'ReferenceImage' ? imageCount + savedImageCount : 0) === 0) problems.push(card.querySelector('.soffer-doc-name strong').textContent + ' is required.');
        });
        if (count > MAX_FILES) problems.push('Attach at most ' + MAX_FILES + ' files.');
        if (total > MAX_TOTAL) problems.push('The attachments exceed 40 MB in total.');
        docError.hidden = problems.length === 0; docError.textContent = problems.join(' ');
        return problems.length === 0;
    }
    form.querySelectorAll('[data-doc]').forEach(function (card) { card.querySelector('[data-doc-input]').addEventListener('change', function () { renderDocs(card); checkDocs(); }); });
    form.addEventListener('change', function (e) { if (e.target.closest('[data-existing]')) checkDocs(); });

    // Submit only through the confirmation dialog; disable the button after confirming to prevent duplicates.
    var dialog = form.querySelector('[data-confirm-offer]'), button = form.querySelector('[data-submit-offer]');
    button.addEventListener('click', function () {
        sync();
        var docsOk = checkDocs();
        if (!form.reportValidity() || !docsOk) { if (!docsOk) docError.scrollIntoView({ block: 'center' }); return; }
        dialog.querySelector('[data-confirm-total]').textContent = 'Grand total: ' + form.querySelector('[data-sum="grand"]').textContent;
        dialog.showModal();
    });
    dialog.querySelector('[data-cancel]').addEventListener('click', function () { dialog.close(); });
    dialog.querySelector('[data-ok]').addEventListener('click', function () {
        dialog.close(); button.disabled = true; button.textContent = 'Submitting…';
        form.submit();
    });
    form.addEventListener('input', sync); form.addEventListener('change', sync); sync();
})();
