// OWNER: Ahmad — Authentication.
// Sign-in form of Areas/Identity/Pages/Account/Login.cshtml.
//
// Step 1 (every account): email and password are checked by Login?handler=Credentials. The server
// answers with one of three outcomes and nothing else is trusted here:
//   signedIn  → the session exists; go to the redirect the server chose
//   biometric → an approved supplier: the password opened a short pre-auth step (SupplierPreAuth);
//               the face check (biometric.js → /Biometrics/FaceLogin) or a passkey issues the session
//   rejected  → one generic message, whatever the reason
// Without JavaScript the same form posts normally and the server renders the same two steps.
//
// The illustration (wwwroot/js/auth-studio/login-page.js) only listens. It receives a sanitised
// state (email FORMAT, "a password was typed", focus) and the request phases, never a value.
(function () {
    'use strict';

    const shell = document.getElementById('authStudio');
    const root = document.getElementById('bio-root');
    const form = document.getElementById('account');
    if (!shell || !root || !form) return;

    const email = document.getElementById('Input_Email');
    const password = document.getElementById('Input_Password');
    const submit = document.getElementById('login-submit');
    const message = document.getElementById('signInMessage');
    const credentials = document.getElementById('signInCredentials');
    const biometric = document.getElementById('signInBiometric');
    const faceButton = document.getElementById('btnFaceLogin');
    const faceNote = document.getElementById('faceMethodNote');
    const faceHint = document.getElementById('faceHint');
    const faceActions = document.getElementById('faceActions');
    const passkeyButton = document.getElementById('btnPasskeyStep');
    const passkeyNote = document.getElementById('fingerprintMethodNote');
    const bioStatus = document.getElementById('bio-status');
    const back = document.getElementById('btnBiometricBack');
    const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
    const REQUEST_TIMEOUT_MS = 20000;

    let focusName = null;
    let touched = false;
    let busy = false;
    let expiryTimer = 0;

    const emit = (name, detail, cancelable) =>
        shell.dispatchEvent(new CustomEvent(name, { detail, cancelable: !!cancelable }));

    // ---------- Sanitised state for the illustration ----------

    function emailState() {
        const text = (email.value || '').trim();
        if (!text) return 'empty';
        if (emailPattern.test(text)) return 'valid';
        if (touched || focusName !== 'email') return 'invalid';
        return text.includes('@') ? 'domain' : 'typing';
    }

    function publish() {
        const detail = { email: emailState(), hasPassword: password.value.length > 0, focus: focusName };
        shell.authState = detail;
        emit('authstudio:state', detail);
    }

    [[email, 'email'], [password, 'password']].forEach(([input, name]) => {
        input.addEventListener('focus', () => { focusName = name; publish(); });
        input.addEventListener('blur', () => {
            if (focusName === name) focusName = null;
            if (name === 'email' && email.value.trim()) touched = true;
            publish();
        });
        input.addEventListener('input', () => {
            clearFieldError(input);
            publish();
        });
    });

    document.querySelectorAll('#account [data-reveal]').forEach(button => {
        button.addEventListener('click', () => {
            const input = document.getElementById(button.dataset.reveal);
            const show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            button.textContent = show ? 'Hide' : 'Show';
            button.setAttribute('aria-pressed', String(show));
        });
    });

    // ---------- Validation messages (same look as the server's) ----------

    function errorSlot(input) {
        return form.querySelector('[data-valmsg-for="' + input.name + '"]');
    }

    function setFieldError(input, text) {
        const slot = errorSlot(input);
        input.classList.toggle('input-validation-error', !!text);
        input.setAttribute('aria-invalid', text ? 'true' : 'false');
        if (slot) {
            slot.textContent = text || '';
            slot.classList.toggle('field-validation-error', !!text);
            slot.classList.toggle('field-validation-valid', !text);
            if (!slot.id) slot.id = input.id + '-error';
            if (text) input.setAttribute('aria-describedby', slot.id); else input.removeAttribute('aria-describedby');
        }
    }

    function clearFieldError(input) {
        if (input.classList.contains('input-validation-error')) setFieldError(input, '');
    }

    function setMessage(text) {
        message.textContent = text || '';
        message.classList.toggle('is-error', !!text);
        // A server-rendered summary from a plain post is replaced by the live one.
        const summary = form.querySelector('.validation-summary-errors');
        if (summary && text !== undefined) summary.remove();
    }

    function validate() {
        let first = null;
        const text = email.value.trim();
        if (!text) { setFieldError(email, 'Enter your email address.'); first = first || email; }
        else if (!emailPattern.test(text)) { setFieldError(email, 'Enter a valid email address, for example name@company.com.'); first = first || email; }
        else setFieldError(email, '');
        if (!password.value) { setFieldError(password, 'Enter your password.'); first = first || password; }
        else setFieldError(password, '');
        touched = true;
        publish();
        if (first) first.focus();
        return !first;
    }

    // ---------- Step 1: the credential request ----------

    function setBusy(on) {
        busy = on;
        submit.disabled = on;
        submit.setAttribute('aria-busy', String(on));
        submit.firstChild.textContent = on ? 'Checking… ' : 'Continue ';
    }

    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy) return;
        setMessage('');
        if (!validate()) {
            emit('authstudio:signal', { phase: 'incomplete' });
            return;
        }

        setBusy(true);
        emit('authstudio:signal', { phase: 'submitted' });
        const abort = new AbortController();
        const timer = window.setTimeout(() => abort.abort(), REQUEST_TIMEOUT_MS);
        let response;
        let data = null;
        try {
            const url = new URL(window.location.href);
            url.searchParams.set('handler', 'Credentials');
            response = await fetch(url.pathname + url.search, {
                method: 'POST',
                credentials: 'same-origin',
                body: new FormData(form),
                signal: abort.signal,
                headers: { 'Accept': 'application/json' }
            });
            try { data = await response.json(); } catch (e) { data = null; }
        } catch (error) {
            window.clearTimeout(timer);
            setBusy(false);
            const timedOut = error && error.name === 'AbortError';
            setMessage(timedOut
                ? 'The server took too long to answer. Check your connection and try again.'
                : 'The sign-in service could not be reached. Check your connection and try again.');
            emit('authstudio:signal', { phase: timedOut ? 'timeout' : 'unavailable' });
            return;
        }
        window.clearTimeout(timer);

        if (response.ok && data && data.outcome === 'signedIn' && data.redirect) {
            emit('authstudio:signal', { phase: 'accepted' });
            submit.firstChild.textContent = 'Signed in ';
            navigate(data.redirect);
            return;
        }
        if (response.ok && data && data.outcome === 'biometric') {
            setBusy(false);
            emit('authstudio:signal', { phase: 'biometric' });
            showBiometric(!!data.face, !!data.passkey);
            return;
        }

        setBusy(false);
        if (response.status === 401 || (data && data.outcome === 'rejected')) {
            setMessage((data && data.error) || 'Invalid email or password. Please try again.');
            emit('authstudio:signal', { phase: 'rejected' });
            password.value = '';
            publish();
            password.focus();
        } else {
            // A server error is not a wrong password: say so, keep what was typed.
            setMessage('Sign-in is temporarily unavailable (' + response.status + '). Please try again in a moment.');
            emit('authstudio:signal', { phase: 'unavailable' });
        }
    });

    // The server has issued the session. The illustration may play a short closing transition: it
    // cancels this event and navigates itself (it is given the server's redirect, never chooses one).
    function navigate(redirect) {
        const handled = !shell.dispatchEvent(new CustomEvent('authstudio:signed-in', { cancelable: true, detail: { redirect } }));
        if (!handled) window.location.href = redirect;
    }

    // ---------- Step 2: the supplier's biometric step ----------

    function showBiometric(face, passkey) {
        credentials.hidden = true;
        biometric.hidden = false;
        shell.dataset.step = 'biometric';
        root.dataset.face = String(face);
        root.dataset.passkey = String(passkey);
        if (passkeyButton) {
            passkeyButton.disabled = !passkey;
            passkeyButton.toggleAttribute('data-bio-disabled', !passkey);
            if (passkeyNote) passkeyNote.textContent = passkey ? "This device's fingerprint, or your phone via QR code" : "No passkey is set up for this account yet.";
        }
        if (faceButton) {
            faceButton.disabled = !face;
            faceButton.toggleAttribute('data-bio-disabled', !face);
            if (faceNote) faceNote.textContent = face ? "Verify with this device's camera" : 'No face is enrolled for this account. Use your device passkey.';
        }
        if (faceHint) faceHint.textContent = face ? 'Your camera opens only after you choose Face Recognition.' : '';
        if (bioStatus) bioStatus.textContent = '';
        const title = document.getElementById('biometricTitle');
        if (title) title.focus();
        startExpiry(face);
        emit('authstudio:step', { step: 'biometric', face, passkey });
    }

    function showCredentials(text) {
        window.clearTimeout(expiryTimer);
        biometric.hidden = true;
        credentials.hidden = false;
        shell.dataset.step = 'credentials';
        if (faceActions) faceActions.hidden = true;
        password.value = '';
        setMessage(text || '');
        publish();
        emit('authstudio:step', { step: 'credentials' });
        password.focus();
    }

    // The pre-auth step lives a few minutes on the server; the page says so before the server refuses.
    function startExpiry(face) {
        window.clearTimeout(expiryTimer);
        const seconds = parseInt(root.dataset.expiresIn || '0', 10);
        if (!face || !seconds) return;
        expiryTimer = window.setTimeout(() => {
            const cancel = document.getElementById('btnFaceCancel');
            if (cancel && faceActions && !faceActions.hidden) cancel.click();
            showCredentials('Your identity step expired. Enter your password again to continue.');
        }, seconds * 1000);
    }

    async function leaveBiometric() {
        const cancel = document.getElementById('btnFaceCancel');
        if (cancel && faceActions && !faceActions.hidden) cancel.click();
        const url = new URL(window.location.href);
        url.searchParams.set('handler', 'Cancel');
        const token = form.querySelector('input[name="__RequestVerificationToken"]');
        try {
            await fetch(url.pathname + url.search, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'RequestVerificationToken': token ? token.value : '' }
            });
        } catch (e) { /* the step also expires on the server by itself */ }
        emit('authstudio:face', { state: 'cancelled', method: 'face' });
        showCredentials('');
    }

    if (back) back.addEventListener('click', leaveBiometric);

    // biometric.js reports what really happened during the face step; this script only keeps the
    // form in step with it (camera controls shown while the camera is live, back to step 1 when
    // the server ended the pre-auth step).
    shell.addEventListener('authstudio:face', event => {
        const state = event.detail && event.detail.state;
        if (faceActions) faceActions.hidden = !['requesting', 'camera', 'capturing', 'processing'].includes(state);
        if (state === 'restart') showCredentials(event.detail.message || 'Enter your email and password again to continue.');
    });

    // A plain post may have rendered the page already on step 2.
    if (shell.dataset.step === 'biometric') {
        showBiometric(root.dataset.face === 'true', root.dataset.passkey === 'true');
    }
    publish();
})();
