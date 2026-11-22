document.querySelectorAll('.document-review-form').forEach(form => {
    const status = form.elements.namedItem('Status');
    const notes = form.elements.namedItem('Notes');
    const notesField = notes.closest('.document-review-notes');
    const save = form.querySelector('button[type="submit"]');
    const result = form.querySelector('.document-review-result');
    const badge = form.closest('[data-document-row]').querySelector('.document-review-status');

    function updateNotes() {
        const rejected = status.value === 'Rejected';
        notesField.hidden = !rejected;
        notes.disabled = !rejected;
        notes.required = rejected;
    }

    status.addEventListener('change', updateNotes);
    updateNotes();

    form.addEventListener('submit', async event => {
        if (event.defaultPrevented) return;
        event.preventDefault();
        if (save.disabled) return;

        const fields = new FormData(form);
        const saveLabel = save.textContent.trim();
        const fallback = 'The review could not be saved. Try again.';
        result.textContent = '';
        result.removeAttribute('data-error');
        form.setAttribute('aria-busy', 'true');
        save.disabled = status.disabled = notes.disabled = true;
        save.textContent = 'Saving…';

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                headers: { Accept: 'application/json' },
                body: fields
            });
            if (!response.headers.get('content-type')?.includes('application/json')) {
                throw new Error(response.redirected
                    ? 'Your session may have expired. Reload the page and sign in again.'
                    : fallback);
            }

            const review = await response.json();
            if (!response.ok) throw new Error(review.message || fallback);

            badge.dataset.status = review.status;
            badge.textContent = review.status;
            result.textContent = review.message;
        } catch (error) {
            result.setAttribute('data-error', '');
            result.textContent = error instanceof TypeError ? fallback : error.message;
        } finally {
            form.removeAttribute('aria-busy');
            save.disabled = status.disabled = false;
            save.textContent = saveLabel;
            updateNotes();
        }
    });
});
