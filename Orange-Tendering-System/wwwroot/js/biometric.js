// Face ID and Fingerprint (passkey) in the browser.
// Server side: Controllers/BiometricsController.cs.
//
// Part 1  postJson()                 every request to the server (JSON + anti-forgery token)
// Part 2  Fingerprint / passkey      createPasskey(), getPasskey(), registerPasskey(), signInWithPasskey()
// Part 3  Face ID                    loadFaceModels(), startCamera(), captureFaceSamples(), runFaceCheck()
// Part 4  Page helpers               status box, buttons, camera panel, messages for the illustration
// Part 5  Enrollment pages           the buttons on Biometrics/Face, Biometrics/Fingerprint, PendingApproval, EmployeeIdentity
//
// Other scripts use these functions: supplier-login.js (Login page), phone-passkey.js (phone QR code),
// biometric-demo.js (development demos). Load this file first.

// =====================================================================================================
// Part 1: requests
// =====================================================================================================

// The anti-forgery token that Razor writes into every page (@Html.AntiForgeryToken() or any <form method="post">).
function getAntiForgeryToken() {
    var input = document.querySelector('input[name="__RequestVerificationToken"]');
    if (input) {
        return input.value;
    }
    return '';
}

// Sends "body" as JSON to "url" with POST and returns the JSON answer.
// When the server answers with an error status, it throws an Error with the server's message.
// error.restart = true means: the sign-in step ended on the server, start again from the beginning.
async function postJson(url, body) {
    var response = await fetch(url, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiForgeryToken() },
        body: JSON.stringify(body || {})
    });

    var data = null;
    try {
        data = await response.json();
    } catch (e) {
        data = null; // the answer had no JSON body
    }

    if (!response.ok) {
        var message = 'The request failed (' + response.status + ').';
        if (data && data.error) {
            message = data.error;
        }
        var error = new Error(message);
        error.status = response.status;
        error.restart = Boolean(data && data.restart);
        throw error;
    }
    return data;
}

// =====================================================================================================
// Part 2: Fingerprint / passkey (WebAuthn)
// The device (Windows Hello, Touch ID, a phone) checks the fingerprint itself. The browser only gives
// this site a signed answer, which the server verifies. No fingerprint is ever sent.
// =====================================================================================================

// WebAuthn uses binary data (ArrayBuffer); the server sends and receives it as base64url text.
function base64urlToBuffer(text) {
    var base64 = text.replace(/-/g, '+').replace(/_/g, '/');
    while (base64.length % 4 !== 0) {
        base64 = base64 + '=';
    }
    var raw = atob(base64);
    var bytes = new Uint8Array(raw.length);
    for (var i = 0; i < raw.length; i++) {
        bytes[i] = raw.charCodeAt(i);
    }
    return bytes.buffer;
}

function bufferToBase64url(buffer) {
    var bytes = new Uint8Array(buffer);
    var text = '';
    for (var i = 0; i < bytes.length; i++) {
        text = text + String.fromCharCode(bytes[i]);
    }
    return btoa(text).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function checkPasskeySupport() {
    if (!window.isSecureContext) {
        throw new Error('Open this page over HTTPS to use passkeys.');
    }
    if (!window.PublicKeyCredential || !navigator.credentials) {
        throw new Error('This browser does not support fingerprint sign-in.');
    }
}

// The browser reports many different problems (closed, timed out, nothing set up) with a few error names.
// The details go to the console; the page shows a plain sentence.
function friendlyPasskeyError(error) {
    console.error('Biometric error:', error);
    if (error && (error.name === 'NotAllowedError' || error.name === 'AbortError')) {
        var cancelled = new Error('The prompt was closed or timed out. Try again when you are ready.');
        cancelled.code = 'cancelled';
        return cancelled;
    }
    if (error && error.name === 'InvalidStateError') {
        return new Error('This device is already registered for your account.');
    }
    if (error && error.name === 'NotSupportedError') {
        return new Error('This device or browser does not support this kind of sign-in.');
    }
    if (error instanceof Error) {
        return error;
    }
    return new Error(String(error));
}

// The device's answer as JSON text for the server. New browsers have credential.toJSON(); for older ones it is built by hand.
function credentialToJson(credential) {
    if (!credential) {
        throw new Error('The device did not return a credential.');
    }
    if (credential.toJSON) {
        return JSON.stringify(credential.toJSON());
    }

    var answer = credential.response;
    var response = { clientDataJSON: bufferToBase64url(answer.clientDataJSON) };
    if (answer.attestationObject) {
        // Registration answer
        response.attestationObject = bufferToBase64url(answer.attestationObject);
        response.transports = [];
        if (answer.getTransports) {
            response.transports = answer.getTransports();
        }
    } else {
        // Sign-in answer
        response.authenticatorData = bufferToBase64url(answer.authenticatorData);
        response.signature = bufferToBase64url(answer.signature);
        response.userHandle = null;
        if (answer.userHandle) {
            response.userHandle = bufferToBase64url(answer.userHandle);
        }
    }
    return JSON.stringify({
        id: credential.id,
        rawId: bufferToBase64url(credential.rawId),
        type: credential.type,
        authenticatorAttachment: credential.authenticatorAttachment,
        response: response,
        clientExtensionResults: credential.getClientExtensionResults()
    });
}

// Asks the device to create a new passkey for the options the server sent.
// hints tells the browser what to offer first: 'client-device' = this device, 'hybrid' = a phone through the browser's QR code.
async function createPasskey(options, hints) {
    checkPasskeySupport();
    options.challenge = base64urlToBuffer(options.challenge);
    options.user.id = base64urlToBuffer(options.user.id);
    if (options.excludeCredentials) {
        for (var i = 0; i < options.excludeCredentials.length; i++) {
            options.excludeCredentials[i].id = base64urlToBuffer(options.excludeCredentials[i].id);
        }
    }
    if (hints) {
        options.hints = hints;
    }

    try {
        var credential = await navigator.credentials.create({ publicKey: options });
        return credentialToJson(credential);
    } catch (error) {
        throw friendlyPasskeyError(error);
    }
}

// Asks the device to sign the server's challenge with an existing passkey.
async function getPasskey(options, hints) {
    checkPasskeySupport();
    options.challenge = base64urlToBuffer(options.challenge);
    if (options.allowCredentials) {
        for (var i = 0; i < options.allowCredentials.length; i++) {
            options.allowCredentials[i].id = base64urlToBuffer(options.allowCredentials[i].id);
        }
    }
    if (hints) {
        options.hints = hints;
    }

    try {
        var credential = await navigator.credentials.get({ publicKey: options });
        return credentialToJson(credential);
    } catch (error) {
        throw friendlyPasskeyError(error);
    }
}

// Registration of this device: password check + options -> device -> server saves the passkey.
async function registerPasskey(password, hints) {
    checkPasskeySupport();
    var options = await postJson('/Biometrics/PasskeyRegisterOptions', { password: password });
    var credentialJson = await createPasskey(options, hints);
    return await postJson('/Biometrics/PasskeyRegister', { credentialJson: credentialJson, label: '' });
}

// Sign-in: options -> device -> server verifies and signs in. Returns { succeeded, redirect }.
async function signInWithPasskey(returnUrl, hints) {
    checkPasskeySupport();
    var options = await postJson('/Biometrics/PasskeyLoginOptions', {});
    var credentialJson = await getPasskey(options, hints);
    return await postJson('/Biometrics/PasskeyLogin', { credentialJson: credentialJson, returnUrl: returnUrl });
}

// A phone or tablet: there "this device" is the phone itself, so no QR code is needed.
function isMobileDevice() {
    if (navigator.userAgentData && navigator.userAgentData.mobile === true) {
        return true;
    }
    var userAgent = navigator.userAgent || '';
    if (/Android|iPhone|iPad|iPod|Mobile/i.test(userAgent)) {
        return true;
    }
    // iPads that report themselves as a Mac
    return /Macintosh/.test(userAgent) && navigator.maxTouchPoints > 1;
}

// true / false when the browser can tell whether this device has its own fingerprint / face unlock / PIN; null when it cannot.
async function hasDeviceAuthenticator() {
    try {
        if (!window.PublicKeyCredential || !PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable) {
            return false;
        }
        return Boolean(await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable());
    } catch (e) {
        return null;
    }
}

// true / false when the browser can tell whether it offers "use a phone" (hybrid); null when it cannot.
async function browserOffersPhonePasskeys() {
    try {
        if (!window.PublicKeyCredential || !PublicKeyCredential.getClientCapabilities) {
            return null;
        }
        var capabilities = await PublicKeyCredential.getClientCapabilities();
        if (typeof capabilities.hybridTransport === 'boolean') {
            return capabilities.hybridTransport;
        }
        return null;
    } catch (e) {
        return null;
    }
}

// =====================================================================================================
// Part 3: Face ID
// face-api.js (in wwwroot/lib/face-api) finds the face in camera frames and turns it into a "descriptor":
// 128 numbers. Only these numbers are sent; the camera image never leaves the device.
// There is NO liveness check (owner decision for the academic project): a photo of the person can pass.
// =====================================================================================================

var FACE_LIBRARY_FOLDER = '/lib/face-api';
var CAPTURE_MS = 7000;           // hold still for 7 seconds...
var SAMPLE_MS = 700;             // ...and take one sample every 0.7 seconds
var NO_FACE_TIMEOUT_MS = 40000;  // time to find the face and finish one continuous capture
var MIN_FACE_WIDTH = 110;        // pixels; smaller faces give unreliable descriptors

var faceModelsPromise = null;    // the models are loaded once per page
var activeCameraStream = null;   // the camera that is open now (null = closed)
var faceCaptureCancelled = false; // set by the "Cancel and close the camera" button

function waitMilliseconds(ms) {
    return new Promise(function (resolve) {
        setTimeout(resolve, ms);
    });
}

function waitForNextFrame() {
    return new Promise(function (resolve) {
        requestAnimationFrame(resolve);
    });
}

function loadScript(src) {
    return new Promise(function (resolve, reject) {
        var script = document.createElement('script');
        script.src = src;
        script.onload = resolve;
        script.onerror = function () {
            reject(new Error('Could not load the face recognition library.'));
        };
        document.head.appendChild(script);
    });
}

// Loads face-api.js and its three models (face detector, face landmarks, face descriptor).
// Called early (preload) so the first click is fast; later calls reuse the same loading.
function loadFaceModels() {
    if (faceModelsPromise === null) {
        faceModelsPromise = loadFaceModelsNow();
        faceModelsPromise.catch(function () {
            faceModelsPromise = null; // a failed load can be tried again
        });
    }
    return faceModelsPromise;
}

async function loadFaceModelsNow() {
    if (!window.faceapi) {
        await loadScript(FACE_LIBRARY_FOLDER + '/face-api.js');
    }
    var nets = window.faceapi.nets;
    var modelFolder = FACE_LIBRARY_FOLDER + '/model';
    if (!nets.tinyFaceDetector.isLoaded) {
        await nets.tinyFaceDetector.loadFromUri(modelFolder);
    }
    if (!nets.faceLandmark68Net.isLoaded) {
        await nets.faceLandmark68Net.loadFromUri(modelFolder);
    }
    if (!nets.faceRecognitionNet.isLoaded) {
        await nets.faceRecognitionNet.loadFromUri(modelFolder);
    }

    // One practice run on an empty picture prepares the graphics card, so the first real capture is not slow.
    try {
        var blank = document.createElement('canvas');
        blank.width = 160;
        blank.height = 160;
        await window.faceapi.detectSingleFace(blank, new window.faceapi.TinyFaceDetectorOptions({ inputSize: 160 }));
        await window.faceapi.detectFaceLandmarks(blank);
        await window.faceapi.computeFaceDescriptor(blank);
    } catch (e) {
        console.info('Face model warm-up skipped:', e);
    }
}

function preloadFaceModels() {
    loadFaceModels().catch(function (error) {
        console.error('Face models could not be preloaded:', error);
    });
}

// Opens the front camera in the <video> element. Throws a clear message when it is denied or missing.
async function startCamera(video) {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        throw new Error('This browser cannot use the camera here. Open the site over https or on localhost.');
    }

    var stream;
    try {
        stream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } },
            audio: false
        });
    } catch (e) {
        var problem;
        if (e.name === 'NotAllowedError' || e.name === 'PermissionDeniedError') {
            problem = new Error('Camera access was denied. Allow this site to use the camera, then try again.');
            problem.code = 'cameraDenied';
        } else if (e.name === 'NotFoundError' || e.name === 'DevicesNotFoundError') {
            problem = new Error('No camera was found. Connect a camera, then try again.');
            problem.code = 'cameraMissing';
        } else {
            problem = new Error('The camera is not available. Check that another app is not using it, then try again.');
            problem.code = 'cameraUnavailable';
        }
        throw problem;
    }

    activeCameraStream = stream;
    video.srcObject = stream;
    try {
        await video.play();
    } catch (e) {
        stopCamera(video);
        throw new Error('The camera preview could not start. Try again.');
    }

    // Some cameras need a moment after play() before they really deliver pictures (up to 3 seconds).
    for (var i = 0; i < 50; i++) {
        if (video.videoWidth && video.readyState >= 2) {
            break;
        }
        await waitMilliseconds(60);
    }
}

// Turns the camera off (the camera light goes out).
function stopCamera(video) {
    if (activeCameraStream) {
        var tracks = activeCameraStream.getTracks();
        for (var i = 0; i < tracks.length; i++) {
            tracks[i].stop();
        }
        activeCameraStream = null;
    }
    if (video) {
        video.srcObject = null;
    }
}

function cancelledError() {
    var error = new Error('Face verification was cancelled.');
    error.code = 'cancelled';
    return error;
}

// Watches the camera until one face has been seen continuously for 7 seconds, and returns
// { descriptors: [ { t: milliseconds, v: [128 numbers] }, ... ] }.
// onFrame (optional, used by the face demo) is called for every analysed picture with true / false (face found).
async function captureFaceSamples(video, onFrame) {
    var faceapi = window.faceapi;
    // A fast detector first; after 1.5 seconds without a face a more sensitive one (dim light, face further away).
    var fastDetector = new faceapi.TinyFaceDetectorOptions({ inputSize: 416, scoreThreshold: 0.5 });
    var sensitiveDetector = new faceapi.TinyFaceDetectorOptions({ inputSize: 512, scoreThreshold: 0.3 });
    var triedCpu = false;

    // Each picture is copied to a normal 2D canvas first: on some graphics drivers a <video> sent
    // straight to the face library arrives black and no face is ever found.
    var canvas = document.createElement('canvas');
    var context = canvas.getContext('2d', { willReadFrequently: true });

    var samples = [];
    var startTime = performance.now();
    var lastFaceTime = startTime;   // when a usable face was last seen
    var lastPictureTime = -1;       // video.currentTime of the last analysed picture
    var captureStartTime = null;    // when the current 7-second hold began (null = not holding)
    var lastSampleTime = -Infinity;
    showFaceHint('Hold still and look straight at the camera for 7 seconds.');

    while (performance.now() - startTime < NO_FACE_TIMEOUT_MS) {
        if (faceCaptureCancelled) {
            throw cancelledError();
        }

        // Analyse only new pictures, so every sample comes from a different camera frame.
        if (video.currentTime === lastPictureTime) {
            await waitForNextFrame();
            continue;
        }
        lastPictureTime = video.currentTime;

        var timeWithoutFace = performance.now() - lastFaceTime;
        if (timeWithoutFace > 5000 && !triedCpu) {
            // Last resort: some graphics drivers give wrong results, so compute on the processor instead.
            triedCpu = true;
            try {
                if (faceapi.tf.getBackend() !== 'cpu') {
                    await faceapi.tf.setBackend('cpu');
                    await faceapi.tf.ready();
                }
            } catch (e) {
                console.error('Could not switch to the CPU backend:', e);
            }
        }

        // Copy the current picture to the canvas (at most 640 pixels wide).
        var width = Math.min(video.videoWidth || 640, 640);
        var height = Math.round(width * (video.videoHeight || 480) / (video.videoWidth || 640));
        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }
        context.drawImage(video, 0, 0, width, height);

        var detector = fastDetector;
        if (timeWithoutFace > 1500) {
            detector = sensitiveDetector;
        }
        var faces = await faceapi.detectAllFaces(canvas, detector).withFaceLandmarks().withFaceDescriptors();

        var face = null;
        if (faces.length === 1) {
            face = faces[0];
        }
        if (onFrame) {
            onFrame(face !== null);
        }

        if (face !== null && face.detection.box.width >= MIN_FACE_WIDTH) {
            // One usable face: continue (or start) the 7-second hold.
            lastFaceTime = performance.now();
            if (captureStartTime === null) {
                captureStartTime = lastFaceTime;
            }
            var heldFor = lastFaceTime - captureStartTime;

            if (lastFaceTime - lastSampleTime >= SAMPLE_MS || heldFor >= CAPTURE_MS) {
                samples.push({ t: Math.round(heldFor), v: Array.from(face.descriptor) });
                lastSampleTime = lastFaceTime;
            }

            var secondsLeft = Math.max(0, Math.ceil((CAPTURE_MS - heldFor) / 1000));
            showFaceHint('Hold still: ' + secondsLeft + ' seconds remaining.');

            if (heldFor >= CAPTURE_MS && samples.length >= 3) {
                return { descriptors: samples };
            }
        } else {
            // No face, several faces, or a face too far away: the 7-second hold starts again.
            captureStartTime = null;
            samples = [];
            lastSampleTime = -Infinity;
            if (faces.length > 1) {
                showFaceHint('Only one person should be in view. Restarting the 7-second capture.');
            } else if (face !== null) {
                showFaceHint('Move a little closer to the camera.');
            } else {
                showFaceHint('Look straight at the camera with light on your face. The 7-second capture will restart.');
            }
        }
    }

    var noFace = new Error('No face detected. Make sure your face is well lit and fully in view, then try again.');
    noFace.code = 'noFace';
    throw noFace;
}

// One complete face check, used by enrollment, Face ID sign-in and the demo:
//   1. challengeUrl gives a one-time token (for enrollment the password is checked first),
//   2. the camera opens and the face is captured for 7 seconds,
//   3. the samples and the token go to submitUrl, which answers with the result.
// extraFields are added to the submitted JSON (for example { replace: true } or { returnUrl: '...' }).
async function runFaceCheck(challengeUrl, challengeBody, submitUrl, extraFields) {
    var video = document.getElementById('faceVideo');
    faceCaptureCancelled = false;
    showFaceCancelButton(true);
    tellIllustration('authstudio:face', { state: 'requesting' });
    showBiometricStatus('Starting the camera...');

    // The token first: a wrong enrollment password fails before the camera opens.
    var challenge = await postJson(challengeUrl, challengeBody);

    await startCamera(video);
    try {
        await loadFaceModels(); // usually already loaded in the background
    } catch (error) {
        stopCamera(video);
        throw error;
    }
    if (faceCaptureCancelled) {
        stopCamera(video);
        throw cancelledError();
    }

    try {
        showCameraPanel(true);
        tellIllustration('authstudio:face', { state: 'camera' });

        showBiometricStatus('Looking for your face...');
        tellIllustration('authstudio:face', { state: 'capturing' });
        var capture = await captureFaceSamples(video, null);

        showBiometricStatus('Checking...');
        tellIllustration('authstudio:face', { state: 'processing' });
        var body = { token: challenge.token, descriptors: capture.descriptors };
        for (var name in extraFields) {
            body[name] = extraFields[name];
        }
        var result = await postJson(submitUrl, body);

        if (result && result.succeeded) {
            tellIllustration('authstudio:face', { state: 'confirmed' });
        }
        return result;
    } finally {
        stopCamera(video);
    }
}

// =====================================================================================================
// Part 4: page helpers (shared by the pages that use this file)
// =====================================================================================================

// The animated illustration beside the form (wwwroot/js/auth-studio/*.js) listens to these events on #authStudio.
// It only draws what really happened; it never decides anything. Pages without the illustration ignore this.
function tellIllustration(eventName, details) {
    var studio = document.getElementById('authStudio');
    if (studio) {
        studio.dispatchEvent(new CustomEvent(eventName, { detail: details }));
    }
}

function prefersReducedMotion() {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

// After a successful sign-in: the server chose "redirect". The Login page illustration may play a short
// closing animation first; it then opens exactly that address itself.
function goToAfterSignIn(redirect) {
    var studio = document.getElementById('authStudio');
    if (studio) {
        var signedIn = new CustomEvent('authstudio:signed-in', { cancelable: true, detail: { redirect: redirect } });
        var illustrationWillNavigate = !studio.dispatchEvent(signedIn);
        if (illustrationWillNavigate) {
            return;
        }
    }

    // Let the "face confirmed" animation finish before leaving the page.
    var delay = 0;
    if (studio && studio.dataset.faceState === 'confirmed' && !prefersReducedMotion()) {
        delay = 1450;
    }
    setTimeout(function () {
        window.location.href = redirect;
    }, delay);
}

// The status line (#bio-status). kind: 'info' (default), 'success' or 'danger'.
function showBiometricStatus(text, kind) {
    var box = document.getElementById('bio-status');
    if (!box) {
        return;
    }
    box.classList.remove('bio-status--info', 'bio-status--success', 'bio-status--danger');
    box.classList.add('bio-status', 'bio-status--' + (kind || 'info'));
    box.textContent = text;
}

// The hint under the camera (#faceHint).
function showFaceHint(text) {
    var hint = document.getElementById('faceHint');
    if (hint) {
        hint.textContent = text;
    }
}

// The camera picture (#facePanel). Hiding it also hides the Cancel button.
function showCameraPanel(visible) {
    var panel = document.getElementById('facePanel');
    if (panel) {
        if (visible) {
            panel.classList.remove('bio-hidden');
        } else {
            panel.classList.add('bio-hidden');
        }
    }
    if (!visible) {
        showFaceCancelButton(false);
    }
}

// "Cancel and close the camera" (#faceActions): shown while a face check is running.
function showFaceCancelButton(visible) {
    var cancelButtons = document.getElementById('faceActions');
    if (cancelButtons) {
        cancelButtons.hidden = !visible;
    }
}

// Disables every button inside #bio-root while a face or fingerprint action runs.
// Buttons marked data-bio-keep (Cancel, Show password, ...) stay usable.
function setBiometricButtonsDisabled(disabled) {
    var root = document.getElementById('bio-root');
    if (!root) {
        return;
    }
    var buttons = root.querySelectorAll('button');
    for (var i = 0; i < buttons.length; i++) {
        if (!buttons[i].hasAttribute('data-bio-keep')) {
            buttons[i].disabled = disabled || buttons[i].hasAttribute('data-bio-disabled');
        }
    }
}

// Shows what went wrong. method: 'face' or 'passkey' (tells the illustration which step failed).
function showBiometricError(error, method) {
    if (error && error.code === 'cancelled') {
        showBiometricStatus('');
        tellIllustration('authstudio:face', { state: 'cancelled', method: method });
        return;
    }
    showBiometricStatus(error.message, 'danger');
    var state = 'failed';
    if (error.restart) {
        state = 'restart';
    }
    tellIllustration('authstudio:face', { state: state, method: method, code: error.code || null, message: error.message });
}

// The value of the "Current password" field on the enrollment pages ('' when there is none).
function enrollmentPassword() {
    var input = document.getElementById('bioPassword');
    if (input) {
        return input.value;
    }
    return '';
}

// =====================================================================================================
// Part 5: the buttons of the enrollment pages
// =====================================================================================================

async function enrollFace() {
    var password = enrollmentPassword();
    if (!password) {
        showBiometricStatus('Enter your current password to continue.', 'danger');
        return;
    }
    var replaceBox = document.getElementById('faceReplace');
    var replace = true;
    if (replaceBox) {
        replace = replaceBox.checked;
    }

    // Without the illustration the camera picture is shown at once.
    if (!document.getElementById('authStudio')) {
        showCameraPanel(true);
    }
    setBiometricButtonsDisabled(true);
    try {
        await runFaceCheck('/Biometrics/FaceEnrollChallenge', { password: password }, '/Biometrics/FaceEnroll', { replace: replace });
        showBiometricStatus('Face registered successfully.', 'success');

        // During registration the page moves on to its confirmation; the settings page just reloads.
        var root = document.getElementById('bio-root');
        var nextUrl = root.getAttribute('data-enrolled-url');
        if (nextUrl) {
            var delay = 1750;
            if (prefersReducedMotion()) {
                delay = 0;
            }
            setTimeout(function () { window.location.href = nextUrl; }, delay);
        } else {
            window.location.reload();
        }
    } catch (error) {
        showBiometricError(error, 'face');
    } finally {
        showCameraPanel(false);
        setBiometricButtonsDisabled(false);
    }
}

// Pending Approval page of an approved supplier: password + face, then the supplier workspace.
async function signInWithFaceFromSetupPage() {
    var password = enrollmentPassword();
    if (!password) {
        showBiometricStatus('Enter your current password to continue.', 'danger');
        return;
    }
    setBiometricButtonsDisabled(true);
    try {
        var result = await runFaceCheck('/Biometrics/FaceSetupChallenge', { password: password }, '/Biometrics/FaceSetupLogin', { returnUrl: '/Supplier' });
        showBiometricStatus('Signed in. Redirecting...', 'success');
        goToAfterSignIn(result.redirect);
    } catch (error) {
        showBiometricError(error, 'face');
    } finally {
        showCameraPanel(false);
        setBiometricButtonsDisabled(false);
    }
}

async function removeFace() {
    if (!window.confirm('Remove your stored face samples?')) {
        return;
    }
    setBiometricButtonsDisabled(true);
    try {
        await postJson('/Biometrics/FaceRemove', {});
        showBiometricStatus('Face samples removed.', 'success');
        window.location.reload();
    } catch (error) {
        showBiometricError(error, 'face');
    } finally {
        setBiometricButtonsDisabled(false);
    }
}

// Registers this device itself (a phone, or a computer's fingerprint reader / Windows Hello).
// A phone for this computer uses the QR code in phone-passkey.js instead.
async function registerThisDevice() {
    var password = enrollmentPassword();
    if (!password) {
        showBiometricStatus('Enter your current password to continue.', 'danger');
        return;
    }
    showBiometricStatus('Confirm with your fingerprint, face or screen lock when your device asks.');
    tellIllustration('authstudio:face', { state: 'requesting', method: 'passkey' });
    setBiometricButtonsDisabled(true);
    try {
        await registerPasskey(password, ['client-device']);
        showBiometricStatus('This device is registered for fingerprint / phone sign-in.', 'success');
        tellIllustration('authstudio:face', { state: 'confirmed', method: 'passkey' });

        // Let the illustration finish its "confirmed" animation before the page shows the new state.
        var delay = 0;
        if (document.getElementById('authStudio') && !prefersReducedMotion()) {
            delay = 1700;
        }
        setTimeout(function () { window.location.reload(); }, delay);
    } catch (error) {
        showBiometricError(error, 'passkey');
    } finally {
        setBiometricButtonsDisabled(false);
    }
}

async function removePasskey(button) {
    if (!window.confirm('Remove this passkey from your account?')) {
        return;
    }
    setBiometricButtonsDisabled(true);
    try {
        await postJson('/Biometrics/PasskeyRemove?id=' + encodeURIComponent(button.getAttribute('data-remove-passkey')), {});
        showBiometricStatus('Passkey removed.', 'success');
        window.location.reload();
    } catch (error) {
        showBiometricError(error, 'passkey');
    } finally {
        setBiometricButtonsDisabled(false);
    }
}

// Connects the buttons that exist on this page.
function setUpBiometricButtons() {
    var faceEnrollButton = document.getElementById('btnFaceEnroll');
    if (faceEnrollButton) {
        faceEnrollButton.addEventListener('click', enrollFace);
        preloadFaceModels();
    }

    var faceSetupLoginButton = document.getElementById('btnFaceSetupLogin');
    if (faceSetupLoginButton) {
        faceSetupLoginButton.addEventListener('click', signInWithFaceFromSetupPage);
    }

    var faceRemoveButton = document.getElementById('btnFaceRemove');
    if (faceRemoveButton) {
        faceRemoveButton.addEventListener('click', removeFace);
    }

    var faceCancelButton = document.getElementById('btnFaceCancel');
    if (faceCancelButton) {
        faceCancelButton.addEventListener('click', function () {
            faceCaptureCancelled = true; // captureFaceSamples() stops at the next picture
        });
    }

    var registerDeviceButton = document.getElementById('btnPasskeyRegister');
    if (registerDeviceButton) {
        registerDeviceButton.addEventListener('click', registerThisDevice);
    }

    var removeButtons = document.querySelectorAll('[data-remove-passkey]');
    for (var i = 0; i < removeButtons.length; i++) {
        removeButtons[i].addEventListener('click', function (event) {
            removePasskey(event.currentTarget);
        });
    }

    // Leaving the page always turns the camera off.
    window.addEventListener('pagehide', function () {
        stopCamera(document.getElementById('faceVideo'));
    });
}

document.addEventListener('DOMContentLoaded', setUpBiometricButtons);
