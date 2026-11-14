(function () {
    'use strict';
    var dialog = document.getElementById('supplier-tender-dialog');
    if (!dialog) return;
    var body = dialog.querySelector('[data-tender-details-body]');
    var request;
    document.querySelectorAll('[data-tender-details]').forEach(function (button) {
        button.addEventListener('click', async function () {
            if (request) request.abort();
            request = new AbortController();
            body.textContent = 'Loading tender details…';
            dialog.showModal();
            try {
                var response = await fetch(button.dataset.url, {
                    credentials: 'same-origin', signal: request.signal,
                    headers: { 'X-Requested-With': 'XMLHttpRequest' }
                });
                if (!response.ok || response.redirected) throw new Error('Unavailable');
                var html = await response.text();
                if (!dialog.open) return;
                body.innerHTML = html;
                body.scrollTop = 0;
                body.querySelectorAll('time[data-tender-local]').forEach(function (time) {
                    var date = new Date(time.dataset.tenderLocal);
                    if (time.dataset.tenderLocal && !isNaN(date)) time.textContent = date.toLocaleString();
                });
            } catch (error) {
                if (error.name !== 'AbortError') body.textContent = 'Tender details could not be loaded. Please close and try again.';
            }
        });
    });
    dialog.addEventListener('click', function (event) {
        if (event.target.closest('[data-tender-close]')) dialog.close();
        var workflow = event.target.closest('[data-tender-workflow]');
        if (workflow) {
            dialog.close();
            var original = document.querySelector('[data-open-workflow][data-id="' + workflow.dataset.id + '"]');
            if (original) original.click();
        }
        if (event.target === dialog) {
            var bounds = dialog.getBoundingClientRect();
            if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
        }
    });
    dialog.addEventListener('close', function () { if (request) request.abort(); });
})();
