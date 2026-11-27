// The sign-in form of Areas/Identity/Pages/Account/Login.cshtml.
// Needs biometric.js and phone-passkey.js first.
//
// The person chooses a method first:
//   Email & Password -> POST Login?handler=Credentials           (Login.cshtml.cs OnPostCredentialsAsync)
//   Face ID          -> POST Login?handler=IdentityStart (email)  then runFaceLogin()        (biometric.js -> /Biometrics/FaceLogin)
//   Fingerprint      -> POST Login?handler=IdentityStart (email)  then runFingerprintLogin() (biometric.js -> /Biometrics/PasskeyLogin)
//                       or the QR code for a phone               startPhoneSignIn()         (/Biometrics/Phone/SignInStart ...)
// Without JavaScript the form posts normally (OnPostAsync) and only Email & Password works.
//
// The illustration beside the form (wwwroot/js/auth-studio/login-page.js) receives only harmless facts through
// tellIllustration(): the email FORMAT, "a password was typed", which field has focus, and what happened. Never a value.

var EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
var REQUEST_TIMEOUT_MS = 20000;
var REMEMBERED_ACCOUNT_TYPE = 'orange.signin.accountType';
var REMEMBERED_METHOD = 'orange.signin.method';

var LOGIN_TEXT = {
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

var loginBusy = false;            // a request is running: ignore a second click
var loginFocusedField = null;     // 'email', 'password' or null (for the illustration)
var emailWasTouched = false;      // the person already left the email field once
var stepExpiryTimer = null;       // ends the Face ID / Fingerprint step on the page when the server ends it
var browserOffersPhone = null;    // does this browser offer its own "use a phone" prompt? (null = unknown)
var onMobileDevice = false;

// =====================================================================================================
// Small helpers
// =====================================================================================================

function loginForm() {
    return document.getElementById('account');
}

function chosenMethod() {
    var checked = document.querySelector('#account input[name="method"]:checked');
    if (checked) {
        return checked.value;
    }
    return 'password';
}

function chosenAccountType() {
    var checked = document.querySelector('#account input[name="Input.AccountType"]:checked');
    if (checked) {
        return checked.value;
    }
    return null;
}

function loginReturnUrl() {
    return document.getElementById('bio-root').getAttribute('data-return-url') || '';
}

// The big message above the form.
function setLoginMessage(text) {
    var message = document.getElementById('signInMessage');
    message.textContent = text || '';
    if (text) {
        message.classList.add('is-error');
    } else {
        message.classList.remove('is-error');
    }
    // A message the server rendered after a normal post is replaced by this one.
    var serverSummary = document.querySelector('#account .validation-summary-errors');
    if (serverSummary) {
        serverSummary.remove();
    }
}

// The red text under one field (the same look as the server's validation messages).
function setLoginFieldError(input, text) {
    var slot = document.querySelector('#account [data-valmsg-for="' + input.name + '"]');
    if (text) {
        input.classList.add('input-validation-error');
        input.setAttribute('aria-invalid', 'true');
    } else {
        input.classList.remove('input-validation-error');
        input.setAttribute('aria-invalid', 'false');
    }
    if (slot) {
        slot.textContent = text || '';
        slot.classList.toggle('field-validation-error', Boolean(text));
        slot.classList.toggle('field-validation-valid', !text);
        if (!slot.id) {
            slot.id = input.id + '-error';
        }
        if (text) {
            input.setAttribute('aria-describedby', slot.id);
        } else {
            input.removeAttribute('aria-describedby');
        }
    }
}

function remember(key, value) {
    try {
        window.localStorage.setItem(key, value);
    } catch (e) {
        // not essential (private window, storage blocked)
    }
}

// Ticks the radio button whose value was remembered last time.
function restoreRemembered(key, inputName) {
    try {
        var remembered = window.localStorage.getItem(key);
        var inputs = document.querySelectorAll('#account input[name="' + inputName + '"]');
        for (var i = 0; i < inputs.length; i++) {
            if (inputs[i].value === remembered) {
                inputs[i].checked = true;
            }
        }
    } catch (e) {
        // storage unavailable: keep the default
    }
}

// =====================================================================================================
// What the illustration is told (never a value, only a description)
// =====================================================================================================

function emailState() {
    var text = document.getElementById('Input_Email').value.trim();
    if (!text) {
        return 'empty';
    }
    if (EMAIL_PATTERN.test(text)) {
        return 'valid';
    }
    if (emailWasTouched || loginFocusedField !== 'email') {
        return 'invalid';
    }
    if (text.indexOf('@') >= 0) {
        return 'domain';
    }
    return 'typing';
}

function publishLoginState() {
    var state = {
        email: emailState(),
        hasPassword: document.getElementById('Input_Password').value.length > 0,
        focus: loginFocusedField,
        accountType: chosenAccountType()
    };
    // The illustration starts after this script; it reads the last state from here.
    document.getElementById('authStudio').authState = state;
    tellIllustration('authstudio:state', state);
}

// =====================================================================================================
// The chosen method decides what the form shows
// =====================================================================================================

function renderLoginMethod() {
    var method = chosenMethod();
    var text = LOGIN_TEXT[method];
    var password = document.getElementById('Input_Password');
    var submitButton = document.getElementById('login-submit');
    var fine = document.getElementById('methodFine');

    document.getElementById('passwordFields').hidden = method !== 'password';
    password.disabled = method !== 'password';
    document.getElementById('signInLedeText').textContent = text.lede;
    // The button is "Sign in <span>→</span>": firstChild is only the text before the arrow.
    submitButton.firstChild.textContent = text.button + ' ';
    fine.textContent = text.fine;
    fine.hidden = !text.fine;

    var phoneOffer = document.getElementById('phoneQrOffer');
    if (phoneOffer) {
        phoneOffer.hidden = true;
    }
    showFaceHint('');
    showBiometricStatus('');
    publishLoginState();
}

function setLoginBusy(busy, label) {
    var submitButton = document.getElementById('login-submit');
    loginBusy = busy;
    submitButton.disabled = busy;
    submitButton.setAttribute('aria-busy', String(busy));
    if (busy) {
        submitButton.firstChild.textContent = label || 'Checking… ';
    } else {
        submitButton.firstChild.textContent = LOGIN_TEXT[chosenMethod()].button + ' ';
    }
}

// Checks the fields in the browser first (the server checks everything again).
function validateLoginFields(needPassword) {
    var email = document.getElementById('Input_Email');
    var password = document.getElementById('Input_Password');
    var firstInvalid = null;

    var emailText = email.value.trim();
    if (!emailText) {
        setLoginFieldError(email, 'Enter your email address.');
        firstInvalid = email;
    } else if (!EMAIL_PATTERN.test(emailText)) {
        setLoginFieldError(email, 'Enter a valid email address, for example name@company.com.');
        firstInvalid = email;
    } else {
        setLoginFieldError(email, '');
    }

    if (needPassword && !password.value) {
        setLoginFieldError(password, 'Enter your password.');
        if (firstInvalid === null) {
            firstInvalid = password;
        }
    } else {
        setLoginFieldError(password, '');
    }

    emailWasTouched = true;
    publishLoginState();
    if (firstInvalid !== null) {
        firstInvalid.focus();
        return false;
    }
    return true;
}

// =====================================================================================================
// Requests to the Login page handlers (Login.cshtml.cs)
// =====================================================================================================

// The anti-forgery token goes stale when the app restarts or another tab signs in or out. The server then
// refuses the post (400) without JSON. This reads a fresh token from the page once and tries again.
async function renewAntiForgeryToken() {
    try {
        var response = await fetch(window.location.pathname + window.location.search, { credentials: 'same-origin' });
        if (!response.ok) {
            return false;
        }
        var page = new DOMParser().parseFromString(await response.text(), 'text/html');
        var freshInput = page.querySelector('#account input[name="__RequestVerificationToken"]');
        var currentInput = document.querySelector('#account input[name="__RequestVerificationToken"]');
        if (!freshInput || !currentInput || !freshInput.value) {
            return false;
        }
        currentInput.value = freshInput.value;
        return true;
    } catch (e) {
        return false;
    }
}

// POST to Login?handler=<handler>. Returns { response, data } or { error: 'timeout' | 'unavailable' }.
async function postLoginHandler(handler, formData, isRetry) {
    var url = new URL(window.location.href);
    url.searchParams.set('handler', handler);

    // Stop waiting after 20 seconds.
    var abort = new AbortController();
    var timer = setTimeout(function () { abort.abort(); }, REQUEST_TIMEOUT_MS);
    try {
        var response = await fetch(url.pathname + url.search, {
            method: 'POST',
            credentials: 'same-origin',
            body: formData,
            signal: abort.signal,
            headers: { 'Accept': 'application/json', 'RequestVerificationToken': getAntiForgeryToken() }
        });
        var data = null;
        try {
            data = await response.json();
        } catch (e) {
            data = null;
        }

        // Only the two steps that START a sign-in may renew the token (reloading would end an open step).
        var canRenew = handler === 'Credentials' || handler === 'IdentityStart';
        if (data === null && (response.status === 400 || response.status === 405) && !isRetry && canRenew) {
            if (await renewAntiForgeryToken()) {
                if (formData.has('__RequestVerificationToken')) {
                    formData.set('__RequestVerificationToken', getAntiForgeryToken());
                }
                return await postLoginHandler(handler, formData, true);
            }
        }
        return { response: response, data: data };
    } catch (error) {
        if (error && error.name === 'AbortError') {
            return { error: 'timeout' };
        }
        return { error: 'unavailable' };
    } finally {
        clearTimeout(timer);
    }
}

function showServerUnreachable(kind) {
    if (kind === 'timeout') {
        setLoginMessage('The server took too long to answer. Check your connection and try again.');
    } else {
        setLoginMessage('The sign-in service could not be reached. Check your connection and try again.');
    }
    tellIllustration('authstudio:signal', { phase: kind });
}

// =====================================================================================================
// Email & Password
// =====================================================================================================

async function signInWithPasswordOnPage() {
    var password = document.getElementById('Input_Password');
    setLoginBusy(true);
    tellIllustration('authstudio:signal', { phase: 'submitted' });

    // new FormData(form) sends every field of the form, like a normal post: Input.Email, Input.Password, Input.RememberMe, Input.AccountType, the token.
    var answer = await postLoginHandler('Credentials', new FormData(loginForm()), false);
    if (answer.error) {
        setLoginBusy(false);
        showServerUnreachable(answer.error);
        return;
    }
    var response = answer.response;
    var data = answer.data;

    if (response.ok && data && data.outcome === 'signedIn' && data.redirect) {
        tellIllustration('authstudio:signal', { phase: 'accepted' });
        document.getElementById('login-submit').firstChild.textContent = 'Signed in ';
        goToAfterSignIn(data.redirect);
        return;
    }

    setLoginBusy(false);
    if (response.status === 409 && data && data.outcome === 'mismatch') {
        // The password was right but the account type was not: nobody was signed in.
        setLoginMessage(data.error);
        tellIllustration('authstudio:signal', { phase: 'mismatch' });
        var other = document.querySelector('#account input[name="Input.AccountType"]:not(:checked)');
        if (other) {
            other.focus();
        }
        return;
    }
    if (response.status === 423 && data && data.outcome === 'locked') {
        // Too many wrong passwords: even the right one is refused until the lockout ends.
        setLoginMessage(data.error);
        tellIllustration('authstudio:signal', { phase: 'rejected' });
        password.value = '';
        publishLoginState();
        return;
    }
    if (response.status === 401 || (data && data.outcome === 'rejected')) {
        var message = 'Invalid email or password. Please try again.';
        if (data && data.error) {
            message = data.error;
        }
        setLoginMessage(message);
        tellIllustration('authstudio:signal', { phase: 'rejected' });
        password.value = '';
        publishLoginState();
        password.focus();
        return;
    }
    // A server error is not a wrong password: say so and keep what was typed.
    setLoginMessage('Sign-in is temporarily unavailable (' + response.status + '). Please try again in a moment.');
    tellIllustration('authstudio:signal', { phase: 'unavailable' });
}

// =====================================================================================================
// Face ID and Fingerprint: the identity step
// =====================================================================================================

async function startIdentityStep(method) {
    var email = document.getElementById('Input_Email');
    if (method === 'face') {
        setLoginBusy(true, 'Opening Face ID… ');
    } else {
        setLoginBusy(true, 'Opening Fingerprint… ');
    }
    showBiometricStatus('');
    tellIllustration('authstudio:signal', { phase: 'submitted' });

    // Only the email and the method are sent: no password.
    var formData = new FormData();
    formData.append('Input.Email', email.value.trim());
    formData.append('method', method);
    var answer = await postLoginHandler('IdentityStart', formData, false);
    setLoginBusy(false);

    if (answer.error) {
        showServerUnreachable(answer.error);
        return;
    }
    if (!answer.response.ok || !answer.data || answer.data.outcome !== 'ready') {
        var message = 'Sign-in is temporarily unavailable (' + answer.response.status + '). Please try again in a moment.';
        if (answer.data && answer.data.error) {
            message = answer.data.error;
        }
        setLoginMessage(message);
        tellIllustration('authstudio:signal', { phase: 'rejected' });
        email.focus();
        return;
    }

    // The server opened the identity step for this one account (SupplierPreAuth).
    document.getElementById('authStudio').dataset.step = 'biometric';
    tellIllustration('authstudio:signal', { phase: 'biometric' });
    tellIllustration('authstudio:step', { step: 'biometric', face: method === 'face', passkey: method === 'fingerprint' });
    startStepExpiryTimer();

    if (method === 'face') {
        preloadFaceModels();
        showFaceHint('Look straight at the camera and hold still for a few seconds.');
        runFaceLogin();
    } else {
        var phoneOffer = document.getElementById('phoneQrOffer');
        if (phoneOffer && !onMobileDevice) {
            phoneOffer.hidden = false;
        }
        runFingerprintLogin();
    }
}

async function runFaceLogin() {
    setBiometricButtonsDisabled(true);
    try {
        // FaceChallenge gives a one-time token for the account of the identity step; FaceLogin compares and signs in.
        var result = await runFaceCheck('/Biometrics/FaceChallenge', {}, '/Biometrics/FaceLogin', { returnUrl: loginReturnUrl() });
        showBiometricStatus('Signed in. Redirecting...', 'success');
        goToAfterSignIn(result.redirect);
    } catch (error) {
        loginBiometricFailed(error, 'face');
    } finally {
        showCameraPanel(false);
        setBiometricButtonsDisabled(false);
    }
}

// This device's fingerprint reader / Windows Hello first. The browser may also offer a phone through its own
// QR code (Bluetooth) when it supports that. A phone registered on the public address uses this site's QR code instead.
function runFingerprintLogin() {
    if (onMobileDevice) {
        signInWithBrowserPasskey(['client-device']);
    } else if (browserOffersPhone === true) {
        signInWithBrowserPasskey(['client-device', 'hybrid']);
    } else {
        signInWithBrowserPasskey(['client-device']);
    }
}

async function signInWithBrowserPasskey(hints) {
    closePhoneSignIn();
    showBiometricStatus('Touch your fingerprint reader when your device asks.');
    tellIllustration('authstudio:face', { state: 'requesting', method: 'passkey' });
    setBiometricButtonsDisabled(true);
    try {
        var result = await signInWithPasskey(loginReturnUrl(), hints);
        tellIllustration('authstudio:face', { state: 'confirmed', method: 'passkey' });
        showBiometricStatus('Signed in. Redirecting...', 'success');
        goToAfterSignIn(result.redirect);
    } catch (error) {
        loginBiometricFailed(error, 'passkey');
    } finally {
        setBiometricButtonsDisabled(false);
    }
}

// What the Login page does after a failed or cancelled face / fingerprint attempt.
function loginBiometricFailed(error, method) {
    showBiometricError(error, method);
    if (error.restart) {
        // The server ended the identity step (expired, locked, used): start again from the method choice.
        backToMethodChoice(error.message);
    } else {
        showTryAgainHint();
    }
}

function showTryAgainHint() {
    if (chosenMethod() === 'face') {
        showFaceHint('Press "Continue with Face ID" to try again.');
    } else if (onMobileDevice) {
        showFaceHint('Press "Continue with Fingerprint" to try again.');
    } else {
        showFaceHint('Press "Continue with Fingerprint" to try again, or use the QR code for your phone.');
    }
}

// The step lives a few minutes on the server; the page says so before the server refuses.
function startStepExpiryTimer() {
    clearTimeout(stepExpiryTimer);
    var seconds = parseInt(document.getElementById('bio-root').getAttribute('data-expires-in') || '0', 10);
    if (!seconds) {
        return;
    }
    stepExpiryTimer = setTimeout(function () {
        endIdentityStep();
        setLoginMessage('Your sign-in step expired. Choose a method and try again.');
    }, seconds * 1000);
}

// The page goes back to "choose a method" (the server already ended the step).
function backToMethodChoice(message) {
    clearTimeout(stepExpiryTimer);
    document.getElementById('authStudio').dataset.step = 'credentials';
    closePhoneSignIn();
    var phoneOffer = document.getElementById('phoneQrOffer');
    if (phoneOffer) {
        phoneOffer.hidden = true;
    }
    showFaceCancelButton(false);
    showFaceHint('');
    showBiometricStatus('');
    tellIllustration('authstudio:step', { step: 'credentials' });
    setLoginMessage(message || 'Your sign-in step ended. Choose a method and try again.');
}

// The person left the step (another method, another account type, expiry): close the camera / QR code and
// end the step on the server too (it also expires by itself).
function endIdentityStep() {
    clearTimeout(stepExpiryTimer);
    var studio = document.getElementById('authStudio');
    if (studio.dataset.step !== 'biometric') {
        return;
    }

    var cancelButtons = document.getElementById('faceActions');
    if (cancelButtons && !cancelButtons.hidden) {
        faceCaptureCancelled = true; // captureFaceSamples() stops at the next picture
    }
    var phonePanel = document.getElementById('phoneSignIn');
    if (isWatchingPhoneLink() || (phonePanel && !phonePanel.hidden)) {
        closePhoneSignIn();
        cancelPhoneLink();
    }

    studio.dataset.step = 'credentials';
    showFaceCancelButton(false);
    var phoneOffer = document.getElementById('phoneQrOffer');
    if (phoneOffer) {
        phoneOffer.hidden = true;
    }
    tellIllustration('authstudio:step', { step: 'credentials' });
    postLoginHandler('Cancel', new FormData(), false);
}

// =====================================================================================================
// Fingerprint with a phone: the QR code from this site (#phoneSignIn)
// The phone types the number shown here and confirms with its own fingerprint; then this browser gets the session.
// =====================================================================================================

async function startPhoneSignIn() {
    var panel = document.getElementById('phoneSignIn');
    stopWatchingPhoneLink();
    stopPhoneCountdown();
    panel.hidden = false;
    var phoneOffer = document.getElementById('phoneQrOffer');
    if (phoneOffer) {
        phoneOffer.hidden = true;
    }
    showPhoneView(panel, 'loading');
    showBiometricStatus('');

    try {
        var data = await postJson('/Biometrics/Phone/SignInStart', {});
        document.getElementById('ppLoginQr').src = data.qr;
        document.getElementById('ppLoginCode').textContent = data.code;
        document.getElementById('ppLoginWait').textContent = 'Waiting for your phone…';
        showPhoneView(panel, 'qr');
        tellIllustration('authstudio:face', { state: 'requesting', method: 'passkey' });
        startPhoneCountdown(document.getElementById('ppLoginTimer'), data.expiresIn, function () {
            endPhoneSignIn('This QR code expired. Create a new one to try again.');
        });
        startWatchingPhoneLink(phoneSignInStatusChanged);
    } catch (error) {
        closePhoneSignIn();
        showBiometricStatus(error.message, 'danger');
        if (error.restart) {
            tellIllustration('authstudio:face', { state: 'restart', method: 'passkey', message: error.message });
            backToMethodChoice(error.message);
        }
    }
}

// Called every 2 seconds with the server's answer. Returns true to keep asking.
function phoneSignInStatusChanged(data) {
    var waitText = document.getElementById('ppLoginWait');
    if (data.status === 'waiting') {
        waitText.textContent = 'Waiting for your phone…';
        return true;
    }
    if (data.status === 'opened' || data.status === 'verifying') {
        var device = '';
        if (data.device) {
            device = ' (' + data.device + ')';
        }
        waitText.textContent = 'Your phone opened the code' + device + '. Type the number and confirm on the phone.';
        return true;
    }
    if (data.status === 'approved') {
        stopPhoneCountdown();
        waitText.textContent = 'Approved on your phone. Signing in…';
        completePhoneSignIn();
        return false;
    }
    if (data.status === 'denied') {
        endPhoneSignIn('The sign-in was declined on your phone. Nobody was signed in.');
    } else if (data.status === 'expired') {
        endPhoneSignIn('This QR code expired. Create a new one to try again.');
    } else if (data.status === 'failed') {
        endPhoneSignIn('Too many failed attempts on the phone. Create a new QR code to try again.');
    } else {
        endPhoneSignIn('This sign-in step is no longer active. Create a new QR code, or enter your password again.');
    }
    return false;
}

// The phone approved: the server signs in only this browser, inside its own identity step, and checks the account again.
async function completePhoneSignIn() {
    setBiometricButtonsDisabled(true);
    try {
        var result = await postJson('/Biometrics/Phone/SignInComplete', { returnUrl: loginReturnUrl() });
        tellIllustration('authstudio:face', { state: 'confirmed', method: 'passkey' });
        showBiometricStatus('Signed in. Redirecting...', 'success');
        goToAfterSignIn(result.redirect);
    } catch (error) {
        loginBiometricFailed(error, 'passkey');
    } finally {
        setBiometricButtonsDisabled(false);
    }
}

function endPhoneSignIn(message) {
    stopWatchingPhoneLink();
    stopPhoneCountdown();
    document.getElementById('ppLoginEnded').textContent = message;
    showPhoneView(document.getElementById('phoneSignIn'), 'ended');
    tellIllustration('authstudio:face', { state: 'failed', method: 'passkey' });
    showTryAgainHint();
}

function closePhoneSignIn() {
    stopWatchingPhoneLink();
    stopPhoneCountdown();
    var panel = document.getElementById('phoneSignIn');
    if (panel) {
        panel.hidden = true;
    }
    var phoneOffer = document.getElementById('phoneQrOffer');
    if (phoneOffer) {
        phoneOffer.hidden = false;
    }
}

function cancelPhoneSignIn() {
    closePhoneSignIn();
    cancelPhoneLink();
    showBiometricStatus('');
    tellIllustration('authstudio:face', { state: 'cancelled', method: 'passkey' });
    showTryAgainHint();
}

// =====================================================================================================
// Start: connect the form
// =====================================================================================================

function onLoginSubmit(event) {
    event.preventDefault(); // the script sends the form itself
    if (loginBusy) {
        return;
    }
    setLoginMessage('');
    var method = chosenMethod();
    if (!validateLoginFields(method === 'password')) {
        tellIllustration('authstudio:signal', { phase: 'incomplete' });
        return;
    }
    if (method === 'password') {
        signInWithPasswordOnPage();
    } else {
        startIdentityStep(method);
    }
}

function watchLoginField(input, name) {
    input.addEventListener('focus', function () {
        loginFocusedField = name;
        publishLoginState();
    });
    input.addEventListener('blur', function () {
        if (loginFocusedField === name) {
            loginFocusedField = null;
        }
        if (name === 'email' && input.value.trim()) {
            emailWasTouched = true;
        }
        publishLoginState();
    });
    input.addEventListener('input', function () {
        if (input.classList.contains('input-validation-error')) {
            setLoginFieldError(input, '');
        }
        publishLoginState();
    });
}

async function setUpLoginPage() {
    var form = loginForm();
    if (!document.getElementById('authStudio') || !document.getElementById('bio-root') || !form) {
        return;
    }
    onMobileDevice = isMobileDevice();

    var email = document.getElementById('Input_Email');
    var password = document.getElementById('Input_Password');
    watchLoginField(email, 'email');
    watchLoginField(password, 'password');

    // "Show" / "Hide" password
    var revealButton = form.querySelector('[data-reveal]');
    revealButton.addEventListener('click', function () {
        var showText = password.type === 'password';
        if (showText) {
            password.type = 'text';
            revealButton.textContent = 'Hide';
        } else {
            password.type = 'password';
            revealButton.textContent = 'Show';
        }
        revealButton.setAttribute('aria-pressed', String(showText));
    });

    // Supplier / Other User (a preference only: the server checks the real role)
    var accountInputs = form.querySelectorAll('input[name="Input.AccountType"]');
    for (var i = 0; i < accountInputs.length; i++) {
        accountInputs[i].addEventListener('change', function (event) {
            remember(REMEMBERED_ACCOUNT_TYPE, event.target.value);
            setLoginMessage('');
            endIdentityStep();
            renderLoginMethod();
            tellIllustration('authstudio:account', { type: event.target.value });
        });
    }

    // Email & Password / Face ID / Fingerprint
    var methodInputs = form.querySelectorAll('input[name="method"]');
    for (var j = 0; j < methodInputs.length; j++) {
        methodInputs[j].addEventListener('change', function (event) {
            remember(REMEMBERED_METHOD, event.target.value);
            setLoginMessage('');
            endIdentityStep();
            renderLoginMethod();
            if (event.target.value === 'password') {
                password.focus();
            } else {
                email.focus();
            }
        });
    }

    form.addEventListener('submit', onLoginSubmit);

    // The phone QR code
    var phoneOffer = document.getElementById('phoneQrOffer');
    if (phoneOffer && onMobileDevice) {
        phoneOffer.remove(); // on a phone, "this device" is already the phone
    }
    var qrButton = document.getElementById('btnPhoneQrLogin');
    if (qrButton) {
        qrButton.addEventListener('click', startPhoneSignIn);
    }
    document.getElementById('btnPhoneLoginRetry').addEventListener('click', startPhoneSignIn);
    document.getElementById('btnPhoneLoginCancel').addEventListener('click', cancelPhoneSignIn);
    window.addEventListener('pagehide', function () {
        stopWatchingPhoneLink();
        stopPhoneCountdown();
    });

    // Restore last time's choices (not the account type after a server error, which may be about that choice).
    if (!form.querySelector('.validation-summary-errors')) {
        restoreRemembered(REMEMBERED_ACCOUNT_TYPE, 'Input.AccountType');
    }
    restoreRemembered(REMEMBERED_METHOD, 'method');
    renderLoginMethod();

    // Known before the person clicks, so the Fingerprint click decides at once.
    browserOffersPhone = await browserOffersPhonePasskeys();
}

// This script is at the end of <body>, so the form already exists: start at once (no flash of the wrong fields).
setUpLoginPage();
