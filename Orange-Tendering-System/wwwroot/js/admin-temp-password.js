(() => {
    const box = document.querySelector('[data-temp-password]');
    if (!box) return;

    const input = box.querySelector('input[name="Password"]');
    const generate = box.querySelector('[data-password-generate]');
    const toggle = box.querySelector('[data-password-toggle]');
    const copy = box.querySelector('[data-password-copy]');
    const status = document.querySelector('[data-password-status]');
    const groups = [
        'ABCDEFGHJKLMNPQRSTUVWXYZ',
        'abcdefghjkmnpqrstuvwxyz',
        '23456789',
        '!@#$%&*_-?'
    ];
    const alphabet = groups.join('');

    generate.addEventListener('click', () => {
        if (!window.crypto?.getRandomValues) {
            status.textContent = 'Secure generation is unavailable. Enter a temporary password manually.';
            return;
        }

        const random = new Uint32Array(64);
        window.crypto.getRandomValues(random);
        const characters = groups.map((group, index) => group[random[index] % group.length]);
        for (let index = characters.length; index < 18; index++) {
            characters.push(alphabet[random[index] % alphabet.length]);
        }
        for (let index = characters.length - 1, offset = 18; index > 0; index--, offset++) {
            const swap = random[offset] % (index + 1);
            [characters[index], characters[swap]] = [characters[swap], characters[index]];
        }

        input.value = characters.join('');
        input.dispatchEvent(new Event('input', { bubbles: true }));
        status.textContent = 'Temporary password generated. The user will replace it through the emailed link.';
    });

    toggle.addEventListener('click', () => {
        const visible = input.type === 'password';
        input.type = visible ? 'text' : 'password';
        toggle.textContent = visible ? 'Hide' : 'Show';
        toggle.setAttribute('aria-label', visible ? 'Hide temporary password' : 'Show temporary password');
        toggle.setAttribute('aria-pressed', String(visible));
    });

    copy.addEventListener('click', async () => {
        if (!input.value) {
            status.textContent = 'Generate or enter a temporary password first.';
            return;
        }
        try {
            if (!navigator.clipboard?.writeText) throw new Error('Clipboard unavailable');
            await navigator.clipboard.writeText(input.value);
            status.textContent = 'Temporary password copied. Keep it secure; the user will replace it through the emailed link.';
        } catch {
            input.type = 'text';
            toggle.textContent = 'Hide';
            toggle.setAttribute('aria-label', 'Hide temporary password');
            toggle.setAttribute('aria-pressed', 'true');
            input.focus();
            input.select();
            status.textContent = 'Password selected. Press Ctrl+C to copy it.';
        }
    });
})();
