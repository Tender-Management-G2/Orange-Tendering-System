// "Use my phone" for fingerprint / phone sign-in. Needs wwwroot/js/biometric.js (window.OrangeBiometric) first.
// Server side: Controllers/PhonePasskeyController.cs and Services/Biometric/PhoneLink.cs.
//   - Registration step / device settings (#phoneSetup): a QR code (or emailed link) registers a phone; the page
//     waits and updates itself. On a smartphone the phone registers itself, no QR code.
//   - Supplier sign-in, identity step (#btnPasskeyStep, #phoneSignIn): this phone directly, the browser's own
//     phone prompt where the browser supports it, or a QR code from this site that the phone approves.
//   - Phone page (#phonePage, /Biometrics/Phone#t=...): opened from the QR code or the email link.
// Nothing here decides anything: every step is verified on the server, which only issues a session to the
// computer that started the sign-in after the phone's passkey answer was verified.
(function () {
    'use strict';

    var B = window.OrangeBiometric;
    if (!B) { return; }
    var POLL_MS = 2000;

    function $(id) { return document.getElementById(id); }
    function show(root, name) {
        root.querySelectorAll('[data-pp-view]').forEach(function (el) { el.hidden = el.getAttribute('data-pp-view') !== name; });
    }
    function clock(seconds) {
        var s = Math.max(0, seconds | 0);
        return Math.floor(s / 60) + ':' + ('0' + (s % 60)).slice(-2);
    }
    function reducedMotion() { return window.matchMedia('(prefers-reduced-motion: reduce)').matches; }

    // Counts down on the page; the server's own expiry is what really counts.
    function countdown(label, seconds, onZero) {
        var left = seconds;
        var timer = window.setInterval(tick, 1000);
        function tick() {
            left -= 1;
            if (label) { label.textContent = clock(left); }
            if (left <= 0) { window.clearInterval(timer); if (onZero) { onZero(); } }
        }
        if (label) { label.textContent = clock(left); }
        return {
            stop: function () { window.clearInterval(timer); },
            sync: function (serverSeconds) { if (typeof serverSeconds === 'number' && Math.abs(serverSeconds - left) > 3) { left = serverSeconds; } }
        };
    }

    // Asks the server how this computer's QR code is doing until onStatus returns false.
    function watch(onStatus) {
        var stopped = false;
        var timer = null;
        async function tick() {
            if (stopped) { return; }
            try {
                var response = await fetch('/Biometrics/Phone/Status', { credentials: 'same-origin', cache: 'no-store', headers: { 'Accept': 'application/json' } });
                var data = await response.json();
                if (!stopped && onStatus(data) === false) { stopped = true; }
            } catch (e) { /* a network hiccup: try again until the code expires */ }
            if (!stopped) { timer = window.setTimeout(tick, POLL_MS); }
        }
        timer = window.setTimeout(tick, POLL_MS);
        return function () { stopped = true; window.clearTimeout(timer); };
    }

    function cancelOnServer() {
        return B.post('/Biometrics/Phone/Cancel').catch(function () { /* the code also expires by itself */ });
    }

    // ---------- Registration step: choose Face Recognition or Fingerprint / Phone ----------

    function initMethodChoice() {
        var choices = Array.prototype.slice.call(document.querySelectorAll('[data-method-choice]'));
        choices.forEach(function (choice) {
            choice.addEventListener('click', function () {
                choices.forEach(function (other) {
                    var selected = other === choice;
                    other.setAttribute('aria-pressed', selected ? 'true' : 'false');
                    var card = $(other.getAttribute('data-method-choice'));
                    if (card) { card.hidden = !selected; }
                });
            });
        });
    }

    // ---------- Computer: register a phone with a QR code ----------

    function initEnrollment() {
        var root = $('phoneSetup');
        if (!root) { return; }
        var page = B.page || { setStatus: function () { }, announce: function () { } };
        var qrButton = $('btnPhoneQr');
        var deviceButton = $('btnPasskeyRegister');
        var deviceNote = $('ppDeviceNote');
        var emailButton = $('btnPhoneEmail');
        var emailNote = $('ppEmailNote');
        var waitText = $('ppWaitText');
        var stopWatch = null;
        var timer = null;

        // On a smartphone "this device" is the phone: it registers itself and no QR code is needed.
        // On a computer the QR code is the main way; the computer's own fingerprint reader is offered when it has one.
        (async function () {
            var platform = await B.platformAuthenticator();
            if (B.isMobile()) {
                root.setAttribute('data-mode', 'phone');
                if (qrButton) { qrButton.hidden = true; }
                if (deviceButton) {
                    deviceButton.hidden = false;
                    deviceButton.className = 'pp-btn pp-btn--primary';
                    deviceButton.textContent = 'Register this phone';
                }
                if (deviceNote) {
                    deviceNote.textContent = platform === false
                        ? 'This phone has no screen lock set up for passkeys. Turn on a screen lock or fingerprint, then try again.'
                        : 'Your phone asks for its fingerprint, face unlock or screen lock.';
                }
            } else if (deviceButton) {
                deviceButton.hidden = platform !== true;
            }
        })();

        function ended(message) {
            stop();
            $('ppEndedText').textContent = message;
            show(root, 'ended');
            page.announce('failed', { method: 'passkey' });
        }

        function stop() {
            if (stopWatch) { stopWatch(); stopWatch = null; }
            if (timer) { timer.stop(); timer = null; }
        }

        function onStatus(data) {
            if (timer) { timer.sync(data.expiresIn); }
            switch (data.status) {
                case 'waiting':
                    waitText.textContent = 'Waiting for your phone…';
                    return true;
                case 'opened':
                case 'verifying':
                    waitText.textContent = 'Your phone opened the link' + (data.device ? ' (' + data.device + ')' : '') + '. Confirm on the phone.';
                    return true;
                case 'completed':
                    stop();
                    $('ppDevice').textContent = data.device || 'Your phone';
                    show(root, 'done');
                    page.setStatus('Phone registered. You can now sign in with it.', 'success');
                    page.announce('confirmed', { method: 'passkey' });
                    // Show the server's own confirmation once the Identity Station has finished its sequence.
                    window.setTimeout(function () { window.location.reload(); }, reducedMotion() ? 600 : 1900);
                    return false;
                case 'expired':
                    ended('This QR code expired before a phone was registered. Create a new one to try again.');
                    return false;
                case 'cancelled':
                    ended('The setup was cancelled on the phone.');
                    return false;
                case 'failed':
                    ended('The phone could not be registered after several tries. Create a new QR code to try again.');
                    return false;
                default:
                    ended('This QR code is no longer active. Create a new one to try again.');
                    return false;
            }
        }

        async function start() {
            var passwordInput = $('bioPassword');
            var password = passwordInput ? passwordInput.value : '';
            if (!password) {
                page.setStatus('Enter your current password to continue.', 'danger');
                if (passwordInput) { passwordInput.focus(); }
                return;
            }
            stop();
            page.setStatus('');
            [qrButton, $('btnPhoneRetry')].forEach(function (b) { if (b) { b.disabled = true; } });
            try {
                var data = await B.post('/Biometrics/Phone/EnrollStart', { password: password });
                $('ppQr').src = data.qr;
                emailButton.hidden = !data.email;
                emailButton.disabled = false;
                emailNote.textContent = '';
                waitText.textContent = 'Waiting for your phone…';
                show(root, 'qr');
                page.announce('requesting', { method: 'passkey' });
                timer = countdown($('ppTimer'), data.expiresIn, function () {
                    ended('This QR code expired before a phone was registered. Create a new one to try again.');
                });
                stopWatch = watch(onStatus);
            } catch (e) {
                page.setStatus(e.message, 'danger');
            } finally {
                [qrButton, $('btnPhoneRetry')].forEach(function (b) { if (b) { b.disabled = false; } });
            }
        }

        if (qrButton) { qrButton.addEventListener('click', start); }
        var retry = $('btnPhoneRetry');
        if (retry) { retry.addEventListener('click', start); }

        $('btnPhoneCancel').addEventListener('click', function () {
            stop();
            cancelOnServer();
            show(root, 'start');
            page.setStatus('');
            page.announce('cancelled', { method: 'passkey' });
        });

        emailButton.addEventListener('click', async function () {
            emailButton.disabled = true;
            emailNote.textContent = 'Sending…';
            try {
                var data = await B.post('/Biometrics/Phone/EnrollEmail');
                emailNote.textContent = 'Sent to ' + data.to + '. Open the email on your phone and tap the link.';
                // One message is enough for a while; the button comes back if it did not arrive.
                window.setTimeout(function () { emailButton.disabled = false; }, 30000);
            } catch (e) {
                emailNote.textContent = e.message;
                emailButton.disabled = false;
            }
        });

        window.addEventListener('pagehide', stop);
    }

    // ---------- Computer: sign in with my phone (supplier identity step on the Login page) ----------

    function initSignIn() {
        var tile = $('btnPasskeyStep');
        var panel = $('phoneSignIn');
        if (!tile || !panel) { return; }
        var offer = $('phoneQrOffer');
        var waitText = $('ppLoginWait');
        var stopWatch = null;
        var timer = null;
        var mobile = B.isMobile();
        if (offer && mobile) { offer.remove(); offer = null; }
        // Known before the supplier clicks, so the click decides at once (no late prompt over a QR code already shown).
        var hybrid = null;
        B.hybridSupported().then(function (value) { hybrid = value; });

        function page() { return B.page; }

        function stop() {
            if (stopWatch) { stopWatch(); stopWatch = null; }
            if (timer) { timer.stop(); timer = null; }
        }

        function ended(message) {
            stop();
            $('ppLoginEnded').textContent = message;
            show(panel, 'ended');
            page().announce('failed', { method: 'passkey' });
        }

        function close() {
            stop();
            panel.hidden = true;
            if (offer) { offer.hidden = false; }
        }

        function onStatus(data) {
            if (timer) { timer.sync(data.expiresIn); }
            switch (data.status) {
                case 'waiting':
                    waitText.textContent = 'Waiting for your phone…';
                    return true;
                case 'opened':
                case 'verifying':
                    waitText.textContent = 'Your phone opened the code' + (data.device ? ' (' + data.device + ')' : '') + '. Type the number and confirm on the phone.';
                    return true;
                case 'approved':
                    stop();
                    waitText.textContent = 'Approved on your phone. Signing in…';
                    // The server signs in only this browser, inside its own password step, and checks the account again.
                    page().guarded(function () {
                        return B.post('/Biometrics/Phone/SignInComplete', { returnUrl: page().returnUrl });
                    }, 'Signed in.', 'passkey');
                    return false;
                case 'denied':
                    ended('The sign-in was declined on your phone. Nobody was signed in.');
                    return false;
                case 'expired':
                    ended('This QR code expired. Create a new one to try again.');
                    return false;
                case 'failed':
                    ended('Too many failed attempts on the phone. Create a new QR code to try again.');
                    return false;
                default:
                    ended('This sign-in step is no longer active. Create a new QR code, or enter your password again.');
                    return false;
            }
        }

        // fallback: when no phone link can be made here (409: no public address), use the browser's own prompt instead.
        async function startQr(fallback) {
            stop();
            panel.hidden = false;
            if (offer) { offer.hidden = true; }
            show(panel, 'loading');
            page().setStatus('');
            try {
                var data = await B.post('/Biometrics/Phone/SignInStart');
                $('ppLoginQr').src = data.qr;
                $('ppLoginCode').textContent = data.code;
                waitText.textContent = 'Waiting for your phone…';
                show(panel, 'qr');
                page().announce('requesting', { method: 'passkey' });
                timer = countdown($('ppLoginTimer'), data.expiresIn, function () { ended('This QR code expired. Create a new one to try again.'); });
                stopWatch = watch(onStatus);
            } catch (e) {
                close();
                if (fallback === true && e.status === 409) { standard(['client-device', 'hybrid']); return; }
                page().setStatus(e.message, 'danger');
                if (e.restart) { page().announce('restart', { method: 'passkey', message: e.message }); }
            }
        }

        // The browser's own passkey prompt: this device, or a phone through the browser's QR code (Bluetooth).
        function standard(hints) {
            close();
            page().setStatus('Touch your fingerprint reader when your device asks.');
            page().announce('requesting', { method: 'passkey' });
            return page().guarded(function () { return B.passkeyLogin(page().returnUrl, null, hints); }, 'Signed in.', 'passkey');
        }

        tile.addEventListener('click', function () {
            if (tile.disabled || !B.page) { return; }
            if (mobile) { standard(['client-device']); return; }
            // On a computer: its own fingerprint reader (Windows Hello / Touch ID) first. A phone registered on the
            // public address answers through this site's QR code instead (the link shown below the form).
            standard(hybrid === true ? ['client-device', 'hybrid'] : ['client-device']);
        });

        var qrLogin = $('btnPhoneQrLogin');
        if (qrLogin) { qrLogin.addEventListener('click', function () { startQr(); }); }
        $('btnPhoneLoginRetry').addEventListener('click', function () { startQr(); });
        $('btnPhoneLoginCancel').addEventListener('click', function () {
            close();
            cancelOnServer();
            page().setStatus('');
            page().announce('cancelled', { method: 'passkey' });
        });

        // Leaving the identity step ("Use a different account", expiry) ends the QR code too.
        var shell = $('authStudio');
        if (shell) {
            shell.addEventListener('authstudio:step', function (event) {
                if (event.detail && event.detail.step === 'credentials' && (stopWatch || !panel.hidden)) {
                    close();
                    cancelOnServer();
                }
                if (event.detail && event.detail.step === 'biometric' && offer) { offer.hidden = !event.detail.passkey; }
            });
        }
        window.addEventListener('pagehide', stop);
    }

    // ---------- Phone page (/Biometrics/Phone#t=...) ----------

    function initPhonePage() {
        var root = $('phonePage');
        if (!root) { return; }
        var token = new URLSearchParams(window.location.hash.slice(1)).get('t');
        // A new QR code opened in the same tab changes only the fragment: start again with the new code.
        window.addEventListener('hashchange', function () { window.location.reload(); });
        var message = $('ppMessage');
        var timer = null;

        function say(text, kind) {
            message.textContent = text || '';
            message.className = 'pp-message' + (kind ? ' pp-message--' + kind : '');
        }

        function finish(view, title, text) {
            if (timer) { timer.stop(); }
            var expires = root.querySelector('.pp-expires');
            if (expires) { expires.hidden = true; }
            $(view === 'done' ? 'ppDoneTitle' : 'ppEndTitle').textContent = title;
            $(view === 'done' ? 'ppDoneText' : 'ppEndText').textContent = text;
            say('');
            show(root, view);
        }

        function busy(on) {
            root.querySelectorAll('button').forEach(function (b) { b.disabled = on; });
        }

        async function deny(title, text) {
            busy(true);
            try { await B.post('/Biometrics/Phone/Deny', { token: token }); } catch (e) { /* the code expires anyway */ }
            busy(false);
            finish('ended', title, text);
        }

        async function enroll() {
            var options = null;
            async function load() { options = await B.post('/Biometrics/Phone/EnrollOptions', { token: token }); }
            try { await load(); } catch (e) { finish('ended', 'This code cannot be used', e.message); return; }
            show(root, 'enroll');

            $('btnPhoneEnroll').addEventListener('click', async function () {
                busy(true);
                say('Confirm with your fingerprint, face or screen lock when your phone asks.');
                try {
                    if (!options) { await load(); }
                    var current = options;
                    options = null; // each challenge answers once
                    var credential = await B.webauthnCreate(current, ['client-device']);
                    say('Checking…');
                    var result = await B.post('/Biometrics/Phone/EnrollFinish', { token: token, credentialJson: credential });
                    finish('done', 'This phone is registered',
                        (result.device ? result.device + ' can now be used to sign in. ' : '') + 'You can close this page: your computer updates by itself.');
                } catch (e) {
                    if (e.restart) { finish('ended', 'This code cannot be used', e.message); return; }
                    say(e.message, e.code === 'cancelled' ? null : 'danger');
                    load().catch(function () { });
                } finally {
                    busy(false);
                }
            });
            $('btnPhoneEnrollCancel').addEventListener('click', function () {
                deny('Setup cancelled', 'No device was added to your account. You can close this page.');
            });
        }

        function signIn(info) {
            var options = null;
            $('ppRequestedFrom').textContent = (info.requestedFrom || 'A computer') + ' · ' +
                new Date(info.requestedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
            show(root, 'signin');
            var input = $('ppCode');
            input.focus();

            $('ppCodeForm').addEventListener('submit', async function (event) {
                event.preventDefault();
                var code = (input.value || '').replace(/\D/g, '');
                if (code.length !== 2) { say('Type the two-digit number shown on your computer.', 'danger'); return; }
                busy(true);
                say('');
                try {
                    options = await B.post('/Biometrics/Phone/SignInOptions', { token: token, code: code });
                    show(root, 'approve');
                } catch (e) {
                    if (e.restart) { finish('ended', 'Sign-in not approved', e.message); return; }
                    say(e.message, 'danger');
                    input.select();
                } finally {
                    busy(false);
                }
            });

            $('btnPhoneApprove').addEventListener('click', async function () {
                busy(true);
                say('Confirm with your fingerprint, face or screen lock when your phone asks.');
                try {
                    if (!options) { options = await B.post('/Biometrics/Phone/SignInOptions', { token: token, code: (input.value || '').replace(/\D/g, '') }); }
                    var current = options;
                    options = null;
                    var credential = await B.webauthnGet(current, ['client-device']);
                    say('Checking…');
                    await B.post('/Biometrics/Phone/SignInApprove', { token: token, credentialJson: credential });
                    finish('done', 'Sign-in approved', 'Return to your computer: it continues by itself. You can close this page.');
                } catch (e) {
                    if (e.restart) { finish('ended', 'Sign-in not approved', e.message); return; }
                    say(e.message, e.code === 'cancelled' ? null : 'danger');
                } finally {
                    busy(false);
                }
            });

            root.querySelectorAll('[data-pp-deny]').forEach(function (button) {
                button.addEventListener('click', function () {
                    deny('Sign-in declined', 'Nobody was signed in. If you did not try to sign in, change your password.');
                });
            });
        }

        (async function () {
            if (!token) {
                finish('ended', 'Link incomplete', 'Scan the QR code on your computer again, or open the link from the email.');
                return;
            }
            var info;
            try {
                info = await B.post('/Biometrics/Phone/Info', { token: token });
            } catch (e) {
                finish('ended', 'This code cannot be used', e.message);
                return;
            }
            root.querySelectorAll('[data-pp-name]').forEach(function (el) { el.textContent = info.name || info.account; });
            root.querySelectorAll('[data-pp-account]').forEach(function (el) { el.textContent = info.account; });
            timer = countdown($('ppExpires'), info.expiresIn, function () {
                finish('ended', 'Code expired', 'Create a new QR code on your computer and scan it again.');
            });
            if (info.kind === 'enroll') { await enroll(); } else { signIn(info); }
        })();
    }

    function start() {
        initMethodChoice();
        initEnrollment();
        initSignIn();
        initPhonePage();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
