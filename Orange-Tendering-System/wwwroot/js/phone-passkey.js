// "Use my phone" for fingerprint / passkey. Needs biometric.js first (postJson, createPasskey, getPasskey, ...).
// Server side: Controllers/PhonePasskeyController.cs and Services/Biometric/PhoneLink.cs.
//
// Part 1  Shared QR helpers   showPhoneView(), countdown, asking the server for the QR code status
// Part 2  Method choice       "Face Recognition" / "Fingerprint / Phone" cards on the enrollment pages
// Part 3  Computer            register a phone with a QR code (#phoneSetup in Views/Shared/_PhonePasskeySetup.cshtml)
// Part 4  Phone               the page opened from the QR code: /Biometrics/Phone#t=... (Views/PhonePasskey/Index.cshtml)
// The Login page's "sign in with my phone" QR code is in supplier-login.js (it uses Part 1).
//
// Nothing here decides anything: every step is checked on the server.

// =====================================================================================================
// Part 1: shared QR helpers (one countdown and one status check per page at a time)
// =====================================================================================================

var PHONE_STATUS_EVERY_MS = 2000;

// A box contains several views marked data-pp-view="name"; this shows one of them and hides the others.
function showPhoneView(box, viewName) {
    var views = box.querySelectorAll('[data-pp-view]');
    for (var i = 0; i < views.length; i++) {
        views[i].hidden = views[i].getAttribute('data-pp-view') !== viewName;
    }
}

// 75 -> "1:15"
function formatMinutesSeconds(totalSeconds) {
    var seconds = Math.max(0, Math.floor(totalSeconds));
    var minutes = Math.floor(seconds / 60);
    var rest = seconds % 60;
    if (rest < 10) {
        return minutes + ':0' + rest;
    }
    return minutes + ':' + rest;
}

// ---- The countdown on the page (the server's own expiry is what really counts) ----

var phoneCountdownTimer = null;
var phoneCountdownSeconds = 0;
var phoneCountdownLabel = null;
var phoneCountdownFinished = null;

function startPhoneCountdown(label, seconds, onFinished) {
    stopPhoneCountdown();
    phoneCountdownLabel = label;
    phoneCountdownSeconds = seconds;
    phoneCountdownFinished = onFinished;
    if (label) {
        label.textContent = formatMinutesSeconds(seconds);
    }
    phoneCountdownTimer = setInterval(phoneCountdownTick, 1000);
}

function phoneCountdownTick() {
    phoneCountdownSeconds = phoneCountdownSeconds - 1;
    if (phoneCountdownLabel) {
        phoneCountdownLabel.textContent = formatMinutesSeconds(phoneCountdownSeconds);
    }
    if (phoneCountdownSeconds <= 0) {
        stopPhoneCountdown();
        if (phoneCountdownFinished) {
            phoneCountdownFinished();
        }
    }
}

// The server sends the real seconds left with every status answer; a difference of more than 3 seconds is corrected.
function correctPhoneCountdown(serverSeconds) {
    if (typeof serverSeconds === 'number' && Math.abs(serverSeconds - phoneCountdownSeconds) > 3) {
        phoneCountdownSeconds = serverSeconds;
    }
}

function stopPhoneCountdown() {
    if (phoneCountdownTimer !== null) {
        clearInterval(phoneCountdownTimer);
        phoneCountdownTimer = null;
    }
}

// ---- Asking the server every 2 seconds how this computer's QR code is doing ----

var phoneStatusTimer = null;
var phoneStatusHandler = null;   // a function(data) that returns true to keep asking, false to stop

function startWatchingPhoneLink(handler) {
    stopWatchingPhoneLink();
    phoneStatusHandler = handler;
    phoneStatusTimer = setTimeout(askPhoneLinkStatus, PHONE_STATUS_EVERY_MS);
}

async function askPhoneLinkStatus() {
    if (phoneStatusHandler === null) {
        return;
    }
    var keepAsking = true;
    try {
        var response = await fetch('/Biometrics/Phone/Status', { credentials: 'same-origin', cache: 'no-store', headers: { 'Accept': 'application/json' } });
        var data = await response.json();
        if (phoneStatusHandler !== null) {
            correctPhoneCountdown(data.expiresIn);
            keepAsking = phoneStatusHandler(data);
        }
    } catch (e) {
        // A network hiccup: simply ask again (the code expires by itself anyway).
    }

    if (keepAsking === false) {
        stopWatchingPhoneLink();
    } else if (phoneStatusHandler !== null) {
        phoneStatusTimer = setTimeout(askPhoneLinkStatus, PHONE_STATUS_EVERY_MS);
    }
}

function stopWatchingPhoneLink() {
    phoneStatusHandler = null;
    if (phoneStatusTimer !== null) {
        clearTimeout(phoneStatusTimer);
        phoneStatusTimer = null;
    }
}

function isWatchingPhoneLink() {
    return phoneStatusHandler !== null;
}

// Ends this computer's QR code on the server (it also expires by itself, so errors are ignored).
function cancelPhoneLink() {
    postJson('/Biometrics/Phone/Cancel', {}).catch(function () { });
}

// =====================================================================================================
// Part 2: choose Face Recognition or Fingerprint / Phone (enrollment pages)
// =====================================================================================================

function setUpMethodChoice() {
    var choices = document.querySelectorAll('[data-method-choice]');
    for (var i = 0; i < choices.length; i++) {
        choices[i].addEventListener('click', function (event) {
            chooseEnrollmentMethod(event.currentTarget);
        });
    }
}

// Marks the clicked choice as selected and shows only its card.
function chooseEnrollmentMethod(selectedChoice) {
    var choices = document.querySelectorAll('[data-method-choice]');
    for (var i = 0; i < choices.length; i++) {
        var selected = choices[i] === selectedChoice;
        choices[i].setAttribute('aria-pressed', String(selected));
        var card = document.getElementById(choices[i].getAttribute('data-method-choice'));
        if (card) {
            card.hidden = !selected;
        }
    }
}

// =====================================================================================================
// Part 3: computer - register a phone with a QR code (#phoneSetup)
// =====================================================================================================

async function setUpPhoneEnrollment() {
    var box = document.getElementById('phoneSetup');
    if (!box) {
        return;
    }

    document.getElementById('btnPhoneQr').addEventListener('click', startPhoneEnrollment);
    document.getElementById('btnPhoneRetry').addEventListener('click', startPhoneEnrollment);
    document.getElementById('btnPhoneCancel').addEventListener('click', cancelPhoneEnrollment);
    document.getElementById('btnPhoneEmail').addEventListener('click', emailPhoneEnrollmentLink);
    window.addEventListener('pagehide', stopPhoneEnrollmentTimers);

    // On a smartphone "this device" is the phone: it registers itself, no QR code is needed.
    // On a computer the QR code is the main way; the computer's own fingerprint reader is offered when it has one.
    var qrButton = document.getElementById('btnPhoneQr');
    var deviceButton = document.getElementById('btnPasskeyRegister');
    var deviceNote = document.getElementById('ppDeviceNote');
    var hasOwnReader = await hasDeviceAuthenticator();
    if (isMobileDevice()) {
        box.setAttribute('data-mode', 'phone');
        qrButton.hidden = true;
        deviceButton.hidden = false;
        deviceButton.className = 'pp-btn pp-btn--primary';
        deviceButton.textContent = 'Register this phone';
        if (hasOwnReader === false) {
            deviceNote.textContent = 'This phone has no screen lock set up for passkeys. Turn on a screen lock or fingerprint, then try again.';
        } else {
            deviceNote.textContent = 'Your phone asks for its fingerprint, face unlock or screen lock.';
        }
    } else {
        deviceButton.hidden = hasOwnReader !== true;
    }
}

async function startPhoneEnrollment() {
    var box = document.getElementById('phoneSetup');
    var passwordInput = document.getElementById('bioPassword');
    var password = enrollmentPassword();
    if (!password) {
        showBiometricStatus('Enter your current password to continue.', 'danger');
        if (passwordInput) {
            passwordInput.focus();
        }
        return;
    }

    stopPhoneEnrollmentTimers();
    showBiometricStatus('');
    var qrButton = document.getElementById('btnPhoneQr');
    var retryButton = document.getElementById('btnPhoneRetry');
    qrButton.disabled = true;
    retryButton.disabled = true;
    try {
        // The server checks the password, then creates the QR code (PhonePasskeyController.EnrollStart).
        var data = await postJson('/Biometrics/Phone/EnrollStart', { password: password });
        document.getElementById('ppQr').src = data.qr;

        var emailButton = document.getElementById('btnPhoneEmail');
        emailButton.hidden = !data.email;
        emailButton.disabled = false;
        document.getElementById('ppEmailNote').textContent = '';
        document.getElementById('ppWaitText').textContent = 'Waiting for your phone…';
        showPhoneView(box, 'qr');
        tellIllustration('authstudio:face', { state: 'requesting', method: 'passkey' });

        startPhoneCountdown(document.getElementById('ppTimer'), data.expiresIn, function () {
            endPhoneEnrollment('This QR code expired before a phone was registered. Create a new one to try again.');
        });
        startWatchingPhoneLink(phoneEnrollmentStatusChanged);
    } catch (error) {
        showBiometricStatus(error.message, 'danger');
    } finally {
        qrButton.disabled = false;
        retryButton.disabled = false;
    }
}

// Called every 2 seconds with the server's answer. Returns true to keep asking.
function phoneEnrollmentStatusChanged(data) {
    var waitText = document.getElementById('ppWaitText');
    if (data.status === 'waiting') {
        waitText.textContent = 'Waiting for your phone…';
        return true;
    }
    if (data.status === 'opened' || data.status === 'verifying') {
        var device = '';
        if (data.device) {
            device = ' (' + data.device + ')';
        }
        waitText.textContent = 'Your phone opened the link' + device + '. Confirm on the phone.';
        return true;
    }
    if (data.status === 'completed') {
        stopPhoneEnrollmentTimers();
        document.getElementById('ppDevice').textContent = data.device || 'Your phone';
        showPhoneView(document.getElementById('phoneSetup'), 'done');
        showBiometricStatus('Phone registered. You can now sign in with it.', 'success');
        tellIllustration('authstudio:face', { state: 'confirmed', method: 'passkey' });
        // Show the server's own confirmation once the illustration has finished.
        var delay = 1900;
        if (prefersReducedMotion()) {
            delay = 600;
        }
        setTimeout(function () { window.location.reload(); }, delay);
        return false;
    }
    if (data.status === 'expired') {
        endPhoneEnrollment('This QR code expired before a phone was registered. Create a new one to try again.');
    } else if (data.status === 'cancelled') {
        endPhoneEnrollment('The setup was cancelled on the phone.');
    } else if (data.status === 'failed') {
        endPhoneEnrollment('The phone could not be registered after several tries. Create a new QR code to try again.');
    } else {
        endPhoneEnrollment('This QR code is no longer active. Create a new one to try again.');
    }
    return false;
}

function endPhoneEnrollment(message) {
    stopPhoneEnrollmentTimers();
    document.getElementById('ppEndedText').textContent = message;
    showPhoneView(document.getElementById('phoneSetup'), 'ended');
    tellIllustration('authstudio:face', { state: 'failed', method: 'passkey' });
}

function cancelPhoneEnrollment() {
    stopPhoneEnrollmentTimers();
    cancelPhoneLink();
    showPhoneView(document.getElementById('phoneSetup'), 'start');
    showBiometricStatus('');
    tellIllustration('authstudio:face', { state: 'cancelled', method: 'passkey' });
}

function stopPhoneEnrollmentTimers() {
    stopWatchingPhoneLink();
    stopPhoneCountdown();
}

// The same setup as a link in the account's verified mailbox.
async function emailPhoneEnrollmentLink() {
    var emailButton = document.getElementById('btnPhoneEmail');
    var emailNote = document.getElementById('ppEmailNote');
    emailButton.disabled = true;
    emailNote.textContent = 'Sending…';
    try {
        var data = await postJson('/Biometrics/Phone/EnrollEmail', {});
        emailNote.textContent = 'Sent to ' + data.to + '. Open the email on your phone and tap the link.';
        // One message is enough for a while; the button comes back after 30 seconds.
        setTimeout(function () { emailButton.disabled = false; }, 30000);
    } catch (error) {
        emailNote.textContent = error.message;
        emailButton.disabled = false;
    }
}

// =====================================================================================================
// Part 4: the phone page (/Biometrics/Phone#t=...)
// =====================================================================================================

var phoneToken = null;            // from the address after "#t=" (never sent to the server in the address)
var phoneWebAuthnOptions = null;  // the options of the next WebAuthn step (each one can be used once)

// A message under the buttons. kind: null (normal) or 'danger'.
function sayOnPhone(text, kind) {
    var message = document.getElementById('ppMessage');
    message.textContent = text || '';
    message.className = 'pp-message';
    if (kind) {
        message.className = 'pp-message pp-message--' + kind;
    }
}

// Shows the final screen: view 'done' (success) or 'ended' (the code cannot be used).
function finishOnPhone(view, title, text) {
    stopPhoneCountdown();
    var expires = document.querySelector('#phonePage .pp-expires');
    if (expires) {
        expires.hidden = true;
    }
    if (view === 'done') {
        document.getElementById('ppDoneTitle').textContent = title;
        document.getElementById('ppDoneText').textContent = text;
    } else {
        document.getElementById('ppEndTitle').textContent = title;
        document.getElementById('ppEndText').textContent = text;
    }
    sayOnPhone('');
    showPhoneView(document.getElementById('phonePage'), view);
}

function setPhoneButtonsDisabled(disabled) {
    var buttons = document.querySelectorAll('#phonePage button');
    for (var i = 0; i < buttons.length; i++) {
        buttons[i].disabled = disabled;
    }
}

async function setUpPhonePage() {
    var page = document.getElementById('phonePage');
    if (!page) {
        return;
    }

    phoneToken = new URLSearchParams(window.location.hash.substring(1)).get('t');
    // A new QR code opened in the same tab changes only the "#..." part: start again with the new code.
    window.addEventListener('hashchange', function () { window.location.reload(); });

    if (!phoneToken) {
        finishOnPhone('ended', 'Link incomplete', 'Scan the QR code on your computer again, or open the link from the email.');
        return;
    }

    // 1. Ask the server what this code is for (it also marks the code as opened).
    var info;
    try {
        info = await postJson('/Biometrics/Phone/Info', { token: phoneToken });
    } catch (error) {
        finishOnPhone('ended', 'This code cannot be used', error.message);
        return;
    }

    var nameFields = page.querySelectorAll('[data-pp-name]');
    for (var i = 0; i < nameFields.length; i++) {
        nameFields[i].textContent = info.name || info.account;
    }
    var accountFields = page.querySelectorAll('[data-pp-account]');
    for (var j = 0; j < accountFields.length; j++) {
        accountFields[j].textContent = info.account;
    }
    startPhoneCountdown(document.getElementById('ppExpires'), info.expiresIn, function () {
        finishOnPhone('ended', 'Code expired', 'Create a new QR code on your computer and scan it again.');
    });

    // 2. Register this phone, or approve a sign-in.
    if (info.kind === 'enroll') {
        await showPhoneEnrollment();
    } else {
        showPhoneSignIn(info);
    }
}

// ---- Register this phone ----

async function loadPhoneEnrollOptions() {
    phoneWebAuthnOptions = await postJson('/Biometrics/Phone/EnrollOptions', { token: phoneToken });
}

async function showPhoneEnrollment() {
    try {
        await loadPhoneEnrollOptions();
    } catch (error) {
        finishOnPhone('ended', 'This code cannot be used', error.message);
        return;
    }
    showPhoneView(document.getElementById('phonePage'), 'enroll');
    document.getElementById('btnPhoneEnroll').addEventListener('click', registerThisPhone);
    document.getElementById('btnPhoneEnrollCancel').addEventListener('click', function () {
        denyOnPhone('Setup cancelled', 'No device was added to your account. You can close this page.');
    });
}

async function registerThisPhone() {
    setPhoneButtonsDisabled(true);
    sayOnPhone('Confirm with your fingerprint, face or screen lock when your phone asks.');
    try {
        if (phoneWebAuthnOptions === null) {
            await loadPhoneEnrollOptions();
        }
        var options = phoneWebAuthnOptions;
        phoneWebAuthnOptions = null; // each challenge can be answered once
        var credentialJson = await createPasskey(options, ['client-device']);
        sayOnPhone('Checking…');
        var result = await postJson('/Biometrics/Phone/EnrollFinish', { token: phoneToken, credentialJson: credentialJson });

        var deviceText = '';
        if (result.device) {
            deviceText = result.device + ' can now be used to sign in. ';
        }
        finishOnPhone('done', 'This phone is registered', deviceText + 'You can close this page: your computer updates by itself.');
    } catch (error) {
        if (error.restart) {
            finishOnPhone('ended', 'This code cannot be used', error.message);
            return;
        }
        if (error.code === 'cancelled') {
            sayOnPhone(error.message, null);
        } else {
            sayOnPhone(error.message, 'danger');
        }
        // Prepare a fresh challenge for the next try.
        loadPhoneEnrollOptions().catch(function () { });
    } finally {
        setPhoneButtonsDisabled(false);
    }
}

// ---- Approve a sign-in on the computer ----

function showPhoneSignIn(info) {
    var time = new Date(info.requestedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', timeZone: 'Asia/Amman' });
    document.getElementById('ppRequestedFrom').textContent = (info.requestedFrom || 'A computer') + ' · ' + time;
    showPhoneView(document.getElementById('phonePage'), 'signin');
    document.getElementById('ppCode').focus();

    document.getElementById('ppCodeForm').addEventListener('submit', function (event) {
        event.preventDefault(); // checked with JavaScript, the form is not sent
        checkNumberOnPhone();
    });
    document.getElementById('btnPhoneApprove').addEventListener('click', approveSignInOnPhone);

    var denyButtons = document.querySelectorAll('[data-pp-deny]');
    for (var i = 0; i < denyButtons.length; i++) {
        denyButtons[i].addEventListener('click', function () {
            denyOnPhone('Sign-in declined', 'Nobody was signed in. If you did not try to sign in, change your password.');
        });
    }
}

// Only the digits the person typed.
function typedPhoneCode() {
    return (document.getElementById('ppCode').value || '').replace(/\D/g, '');
}

// Step 1: the two-digit number shown on the computer.
async function checkNumberOnPhone() {
    var code = typedPhoneCode();
    if (code.length !== 2) {
        sayOnPhone('Type the two-digit number shown on your computer.', 'danger');
        return;
    }
    setPhoneButtonsDisabled(true);
    sayOnPhone('');
    try {
        phoneWebAuthnOptions = await postJson('/Biometrics/Phone/SignInOptions', { token: phoneToken, code: code });
        showPhoneView(document.getElementById('phonePage'), 'approve');
    } catch (error) {
        if (error.restart) {
            finishOnPhone('ended', 'Sign-in not approved', error.message);
            return;
        }
        sayOnPhone(error.message, 'danger');
        document.getElementById('ppCode').select();
    } finally {
        setPhoneButtonsDisabled(false);
    }
}

// Step 2: the phone's own fingerprint / face unlock / screen lock.
async function approveSignInOnPhone() {
    setPhoneButtonsDisabled(true);
    sayOnPhone('Confirm with your fingerprint, face or screen lock when your phone asks.');
    try {
        if (phoneWebAuthnOptions === null) {
            phoneWebAuthnOptions = await postJson('/Biometrics/Phone/SignInOptions', { token: phoneToken, code: typedPhoneCode() });
        }
        var options = phoneWebAuthnOptions;
        phoneWebAuthnOptions = null;
        var credentialJson = await getPasskey(options, ['client-device']);
        sayOnPhone('Checking…');
        await postJson('/Biometrics/Phone/SignInApprove', { token: phoneToken, credentialJson: credentialJson });
        finishOnPhone('done', 'Sign-in approved', 'Return to your computer: it continues by itself. You can close this page.');
    } catch (error) {
        if (error.restart) {
            finishOnPhone('ended', 'Sign-in not approved', error.message);
            return;
        }
        if (error.code === 'cancelled') {
            sayOnPhone(error.message, null);
        } else {
            sayOnPhone(error.message, 'danger');
        }
    } finally {
        setPhoneButtonsDisabled(false);
    }
}

// "This wasn't me" / "Cancel".
async function denyOnPhone(title, text) {
    setPhoneButtonsDisabled(true);
    try {
        await postJson('/Biometrics/Phone/Deny', { token: phoneToken });
    } catch (e) {
        // the code expires anyway
    }
    setPhoneButtonsDisabled(false);
    finishOnPhone('ended', title, text);
}

// =====================================================================================================

function setUpPhonePasskeyPages() {
    setUpMethodChoice();
    setUpPhoneEnrollment();
    setUpPhonePage();
}

document.addEventListener('DOMContentLoaded', setUpPhonePasskeyPages);
