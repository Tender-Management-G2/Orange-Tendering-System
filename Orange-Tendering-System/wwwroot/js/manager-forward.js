(function () {
    'use strict';
    var form = document.querySelector('[data-mf-form]');
    if (!form) return;
    var dialog = form.querySelector('[data-mf-dialog]'), openBtn = form.querySelector('[data-mf-open]'), sendBtn = form.querySelector('[data-mf-send]');
    function closeDialog() { if (dialog.close) dialog.close(); else dialog.removeAttribute('open'); }
    if (openBtn && dialog) openBtn.addEventListener('click', function () { if (typeof dialog.showModal === 'function') dialog.showModal(); else dialog.setAttribute('open', ''); });
    form.querySelectorAll('[data-mf-cancel]').forEach(function (b) { b.addEventListener('click', closeDialog); });
    // Inline PDF preview: loading it is what records that the Manager opened this exact report version.
    var preview = form.querySelector('[data-mf-preview]'), box = form.querySelector('[data-mf-preview-box]');
    if (preview && box) {
        var openLabel = preview.textContent;
        preview.addEventListener('click', function () {
            if (box.hidden) { box.innerHTML = '<iframe title="Evaluation report" src="' + preview.dataset.mfPreview + '"></iframe>'; box.hidden = false; preview.textContent = '×'; preview.setAttribute('aria-label', 'Hide'); }
            else { box.hidden = true; box.innerHTML = ''; preview.textContent = openLabel; preview.removeAttribute('aria-label'); }
        });
    }
    // A summary field that fails validation sits in the collapsed section: close the signature modal and open it so the problem is visible.
    form.addEventListener('invalid', function (event) {
        var details = form.querySelector('details.mf-summary'); if (details) details.open = true;
        if (dialog && dialog.open) closeDialog();
        event.target.scrollIntoView({ block: 'center' });
    }, true);
    // One submission only. report-signature.js runs first (capture phase) and blocks an empty signature, so this only sees valid sends.
    form.addEventListener('submit', function (event) {
        var intent = event.submitter && event.submitter.name === 'intent' ? event.submitter.value : '';
        if (intent !== 'submit') return;
        if (form.dataset.sent) { event.preventDefault(); return; }
        form.dataset.sent = 'true';
        setTimeout(function () { if (sendBtn) { sendBtn.disabled = true; sendBtn.setAttribute('aria-busy', 'true'); } }, 0);
    });
}());
