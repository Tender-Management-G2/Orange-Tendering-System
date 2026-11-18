(() => {
    const delayMs = 350;

    document.querySelectorAll('form[data-auto-filter]').forEach((form) => {
        let timer;
        const submit = () => {
            window.clearTimeout(timer);
            form.requestSubmit();
        };

        form.querySelectorAll('input[type="search"], input[type="text"]').forEach((input) => {
            input.addEventListener('input', () => {
                window.clearTimeout(timer);
                timer = window.setTimeout(submit, delayMs);
            });
        });

        form.querySelectorAll('select').forEach((select) => {
            select.addEventListener('change', submit);
        });
    });
})();
