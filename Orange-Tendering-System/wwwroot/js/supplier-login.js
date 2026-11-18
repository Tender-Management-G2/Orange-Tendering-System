// OWNER: Ahmad — Authentication.
// Sign-in form of Areas/Identity/Pages/Account/Login.cshtml.
//
// Every account first chooses one method (owner decision 2026-11-16; other users too since 2026-11-17).
//   Email & Password → Login?handler=Credentials. The server answers signedIn (go to its redirect),
//                      mismatch (wrong account type, nobody signed in) or rejected (one generic message).
//   Face ID          → Login?handler=IdentityStart (email only) opens the identity step for that account,
//                      then biometric.js runs the camera check (/Biometrics/FaceChallenge + FaceLogin).
//   Fingerprint      → the same IdentityStart, then phone-passkey.js asks this device's fingerprint reader
//                      (or a phone through a QR code) and /Biometrics/PasskeyLogin issues the session.
// Without JavaScript the form posts normally and only Email & Password works.
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
    const passwordFields = document.getElementById('passwordFields');
    const submit = document.getElementById('login-submit');
    const message = document.getElementById('signInMessage');
    const ledeText = document.getElementById('signInLedeText');
    const methodInputs = Array.from(form.querySelectorAll('input[name="method"]'));
    const faceButton = document.getElementById('btnFaceLogin');
    const fingerprintButton = document.getElementById('btnPasskeyStep');
    const faceHint = document.getElementById('faceHint');
    const faceActions = document.getElementById('faceActions');
    const bioStatus = document.getElementById('bio-status');
    const phoneOffer = document.getElementById('phoneQrOffer');
    const phonePanel = document.getElementById('phoneSignIn');
    const fine = document.getElementById('methodFine');
    const accountInputs = Array.from(form.querySelectorAll('input[name="Input.AccountType"]'));
    const ACCOUNT_KEY = 'orange.signin.accountType';
    const METHOD_KEY = 'orange.signin.method';
    const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
    const REQUEST_TIMEOUT_MS = 20000;
    const isMobile = window.OrangeBiometric && window.OrangeBiometric.isMobile ? window.OrangeBiometric.isMobile() : false;

    const TEXT = {
        password: {
            lede: 'Use the email and password for your account.',
            button: 'Sign in',
            fine: ''
        },
        face: {
            lede: 'Enter your email, then look at the camera.',
            button: 'Continue with Face ID',
            fine: 'Your camera image stays on this device; only face measurements are sent to this application. This academic face check has no liveness or spoof protection, so it is not production-grade biometric proof. Set up Face ID once from your account menu (Face recognition sign-in).'
        },
        fingerprint: {
            lede: 'Enter your email, then touch your fingerprint reader.',
            button: 'Continue with Fingerprint',
            fine: 'Your fingerprint never leaves your device: the device checks it and this application receives only a signed answer. Set up Fingerprint once from your account menu (Fingerprint sign-in), on this computer or on your phone.'
        }
    };

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
        const detail = { email: emailState(), hasPassword: password.value.length > 0, focus: focusName, accountType: accountType() };
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

    // ---------- Account type and method: remembered preferences, checked by the server ----------

    function accountType() {
        const checked = accountInputs.find(input => input.checked);
        return checked ? checked.value : null;
    }

    // Suppliers and other users both use the method chosen above.
    function method() {
        const checked = methodInputs.find(input => input.checked);
        return checked ? checked.value : 'password';
    }

    function remember(key, value) {
        try { window.localStorage.setItem(key, value); } catch (e) { /* not essential */ }
    }

    function restore(key, inputs) {
        try {
            const remembered = window.localStorage.getItem(key);
            const match = inputs.find(input => input.value === remembered);
            if (match) match.checked = true;
        } catch (e) { /* storage unavailable: keep the default */ }
    }

    if (!form.querySelector('.validation-summary-errors')) restore(ACCOUNT_KEY, accountInputs);
    restore(METHOD_KEY, methodInputs);

    // Shows the fields of the chosen method. Leaving Face ID / Fingerprint ends its step on the server.
    function render() {
        const current = method();
        const text = TEXT[current];
        passwordFields.hidden = current !== 'password';
        password.disabled = current !== 'password';
        if (ledeText) ledeText.textContent = text.lede;
        submit.firstChild.textContent = text.button + ' ';
        if (fine) { fine.textContent = text.fine; fine.hidden = !text.fine; }
        if (phoneOffer) phoneOffer.hidden = true;
        if (faceHint) faceHint.textContent = '';
        setStatus('');
        publish();
    }

    accountInputs.forEach(input => input.addEventListener('change', () => {
        remember(ACCOUNT_KEY, input.value);
        setMessage('');
        endStep();
        render();
        emit('authstudio:account', { type: input.value });
    }));

    methodInputs.forEach(input => input.addEventListener('change', () => {
        remember(METHOD_KEY, input.value);
        setMessage('');
        endStep();
        render();
        (input.value === 'password' ? password : email).focus();
    }));

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

    function setStatus(text, kind) {
        if (!bioStatus) return;
        ['info', 'success', 'danger'].forEach(k => bioStatus.classList.remove('bio-status--' + k));
        bioStatus.classList.add('bio-status--' + (kind || 'info'));
        bioStatus.textContent = text || '';
    }

    function validate(needPassword) {
        let first = null;
        const text = email.value.trim();
        if (!text) { setFieldError(email, 'Enter your email address.'); first = first || email; }
        else if (!emailPattern.test(text)) { setFieldError(email, 'Enter a valid email address, for example name@company.com.'); first = first || email; }
        else setFieldError(email, '');
        if (needPassword && !password.value) { setFieldError(password, 'Enter your password.'); first = first || password; }
        else setFieldError(password, '');
        touched = true;
        publish();
        if (first) first.focus();
        return !first;
    }

    // ---------- Requests ----------

    function setBusy(on, label) {
        busy = on;
        submit.disabled = on;
        submit.setAttribute('aria-busy', String(on));
        submit.firstChild.textContent = on ? (label || 'Checking… ') : TEXT[method()].button + ' ';
    }

    function token() {
        const input = form.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    // The anti-forgery token in this page goes stale when the app restarts or another tab signs in or out. The server
    // then refuses the post (400, shown as 405 by the status-code page) without JSON. Only the steps that start a
    // sign-in may fetch a fresh token and try once more: reloading the page ends an open identity step.
    const RENEWABLE = ['Credentials', 'IdentityStart'];

    async function renewToken() {
        try {
            const response = await fetch(window.location.pathname + window.location.search, { credentials: 'same-origin' });
            if (!response.ok) return false;
            const page = new DOMParser().parseFromString(await response.text(), 'text/html');
            const fresh = page.querySelector('#account input[name="__RequestVerificationToken"]');
            const input = form.querySelector('input[name="__RequestVerificationToken"]');
            if (!fresh || !input || !fresh.value) return false;
            input.value = fresh.value;
            return true;
        } catch (e) {
            return false;
        }
    }

    async function postHandler(handler, body, renewed) {
        const url = new URL(window.location.href);
        url.searchParams.set('handler', handler);
        const abort = new AbortController();
        const timer = window.setTimeout(() => abort.abort(), REQUEST_TIMEOUT_MS);
        try {
            const response = await fetch(url.pathname + url.search, {
                method: 'POST',
                credentials: 'same-origin',
                body,
                signal: abort.signal,
                headers: { 'Accept': 'application/json', 'RequestVerificationToken': token() }
            });
            let data = null;
            try { data = await response.json(); } catch (e) { data = null; }
            if (!data && (response.status === 400 || response.status === 405) && !renewed &&
                RENEWABLE.includes(handler) && await renewToken()) {
                if (body.has('__RequestVerificationToken')) body.set('__RequestVerificationToken', token());
                return postHandler(handler, body, true);
            }
            return { response, data };
        } catch (error) {
            return { error: error && error.name === 'AbortError' ? 'timeout' : 'unavailable' };
        } finally {
            window.clearTimeout(timer);
        }
    }

    function unreachable(kind) {
        setMessage(kind === 'timeout'
            ? 'The server took too long to answer. Check your connection and try again.'
            : 'The sign-in service could not be reached. Check your connection and try again.');
        emit('authstudio:signal', { phase: kind });
    }

    form.addEventListener('submit', event => {
        event.preventDefault();
        if (busy) return;
        setMessage('');
        const current = method();
        if (!validate(current === 'password')) {
            emit('authstudio:signal', { phase: 'incomplete' });
            return;
        }
        if (current === 'password') signInWithPassword();
        else startIdentity(current);
    });

    // ---------- Email & Password ----------

    async function signInWithPassword() {
        setBusy(true);
        emit('authstudio:signal', { phase: 'submitted' });
        const { response, data, error } = await postHandler('Credentials', new FormData(form));
        if (error) { setBusy(false); unreachable(error); return; }

        if (response.ok && data && data.outcome === 'signedIn' && data.redirect) {
            emit('authstudio:signal', { phase: 'accepted' });
            submit.firstChild.textContent = 'Signed in ';
            navigate(data.redirect);
            return;
        }
        setBusy(false);
        if (response.status === 409 && data && data.outcome === 'mismatch') {
            // The password was right, the account type was not: nothing was signed in.
            setMessage(data.error);
            emit('authstudio:signal', { phase: 'mismatch' });
            const other = accountInputs.find(input => !input.checked);
            if (other) other.focus();
            return;
        }
        if (response.status === 423 && data && data.outcome === 'locked') {
            // Too many failed attempts: the right password will not work until the lockout ends.
            setMessage(data.error);
            emit('authstudio:signal', { phase: 'rejected' });
            password.value = '';
            publish();
            return;
        }
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
    }

    // The server has issued the session. The illustration may play a short closing transition: it
    // cancels this event and navigates itself (it is given the server's redirect, never chooses one).
    function navigate(redirect) {
        const handled = !shell.dispatchEvent(new CustomEvent('authstudio:signed-in', { cancelable: true, detail: { redirect } }));
        if (!handled) window.location.href = redirect;
    }

    // ---------- Face ID and Fingerprint ----------

    async function startIdentity(current) {
        setBusy(true, current === 'face' ? 'Opening Face ID… ' : 'Opening Fingerprint… ');
        setStatus('');
        emit('authstudio:signal', { phase: 'submitted' });
        const body = new FormData();
        body.append('Input.Email', email.value.trim());
        body.append('method', current);
        const { response, data, error } = await postHandler('IdentityStart', body);
        setBusy(false);
        if (error) { unreachable(error); return; }
        if (!response.ok || !data || data.outcome !== 'ready') {
            setMessage((data && data.error) || 'Sign-in is temporarily unavailable (' + response.status + '). Please try again in a moment.');
            emit('authstudio:signal', { phase: 'rejected' });
            email.focus();
            return;
        }

        // The identity step is open for this account only; the server issues the session after the check.
        shell.dataset.step = 'biometric';
        emit('authstudio:signal', { phase: 'biometric' });
        emit('authstudio:step', { step: 'biometric', face: current === 'face', passkey: current === 'fingerprint' });
        startExpiry();
        if (current === 'face') {
            if (faceHint) faceHint.textContent = 'Look straight at the camera and hold still for a few seconds.';
            faceButton.click();
        } else {
            if (phoneOffer && !isMobile) phoneOffer.hidden = false;
            fingerprintButton.click();
        }
    }

    // The step lives a few minutes on the server; the page says so before the server refuses.
    function startExpiry() {
        window.clearTimeout(expiryTimer);
        const seconds = parseInt(root.dataset.expiresIn || '0', 10);
        if (!seconds) return;
        expiryTimer = window.setTimeout(() => {
            endStep();
            setMessage('Your sign-in step expired. Choose a method and try again.');
        }, seconds * 1000);
    }

    // Closes the camera / QR code and ends the identity step on the server (it also expires by itself).
    function endStep() {
        window.clearTimeout(expiryTimer);
        if (shell.dataset.step !== 'biometric') return;
        const cancel = document.getElementById('btnFaceCancel');
        if (cancel && faceActions && !faceActions.hidden) cancel.click();
        shell.dataset.step = 'credentials';
        if (faceActions) faceActions.hidden = true;
        if (phoneOffer) phoneOffer.hidden = true;
        emit('authstudio:step', { step: 'credentials' });
        postHandler('Cancel', new FormData());
    }

    // biometric.js reports what really happened during the face or fingerprint check; this script only keeps
    // the form in step with it (camera controls while the camera is live, a fresh start when the step ended).
    shell.addEventListener('authstudio:face', event => {
        const detail = event.detail || {};
        const state = detail.state;
        const live = ['requesting', 'camera', 'capturing', 'processing'].includes(state);
        if (faceActions && detail.method !== 'passkey') faceActions.hidden = !live;
        if (state === 'failed' || state === 'cancelled') {
            if (faceHint) faceHint.textContent = method() === 'face'
                ? 'Press "Continue with Face ID" to try again.'
                : 'Press "Continue with Fingerprint" to try again' + (isMobile ? '.' : ', or use the QR code for your phone.');
        }
        if (state === 'restart') {
            window.clearTimeout(expiryTimer);
            shell.dataset.step = 'credentials';
            if (faceActions) faceActions.hidden = true;
            if (phoneOffer) phoneOffer.hidden = true;
            if (phonePanel) phonePanel.hidden = true;
            if (faceHint) faceHint.textContent = '';
            setStatus('');
            emit('authstudio:step', { step: 'credentials' });
            setMessage(detail.message || 'Your sign-in step ended. Choose a method and try again.');
        }
    });

    render();
})();
