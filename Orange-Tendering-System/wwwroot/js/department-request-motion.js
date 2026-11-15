(() => {
    const dialog = document.querySelector('[data-request-motion-success]');
    if (!dialog || typeof dialog.showModal !== 'function') return;

    let closing = false;
    let timer;
    const close = () => {
        if (closing) return;
        closing = true;
        clearTimeout(timer);
        dialog.classList.add('is-exiting');
        window.setTimeout(() => dialog.close(), 240);
    };

    dialog.querySelector('[data-request-motion-close]').addEventListener('click', close);
    dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
    dialog.addEventListener('close', () => { clearTimeout(timer); dialog.remove(); });

    dialog.showModal();
    timer = window.setTimeout(close, 4200);
})();
