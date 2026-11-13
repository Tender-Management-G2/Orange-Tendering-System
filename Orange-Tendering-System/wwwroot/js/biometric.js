// Biometric sign-in and enrolment (fingerprint through the device, and face recognition).
// Used by the Login page and by Views/Biometrics (Fingerprint, Face). Server side: Controllers/BiometricsController.cs.
(function () {
    'use strict';

    // Face recognition runs in the browser; the camera image never leaves the device, only 128-number
    // descriptors are sent. face-api.js 1.7.13 and its models are served locally from wwwroot/lib/face-api.
    var FACE_BASE = '/lib/face-api';

    // ---------- Small helpers ----------

    function enc(buffer) {
        var bytes = new Uint8Array(buffer);
        var text = '';
        for (var i = 0; i < bytes.length; i++) { text += String.fromCharCode(bytes[i]); }
        return btoa(text).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    }

    function dec(text) {
        var s = text.replace(/-/g, '+').replace(/_/g, '/');
        while (s.length % 4) { s += '='; }
        var raw = atob(s);
        var bytes = new Uint8Array(raw.length);
        for (var i = 0; i < raw.length; i++) { bytes[i] = raw.charCodeAt(i); }
        return bytes.buffer;
    }

    function sleep(ms) { return new Promise(function (resolve) { setTimeout(resolve, ms); }); }
    function $(id) { return document.getElementById(id); }

    function antiForgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    async function post(url, body) {
        var response = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': antiForgeryToken() },
            body: JSON.stringify(body || {})
        });
        var data = null;
        try { data = await response.json(); } catch (e) { /* no body */ }
        if (!response.ok) {
            throw new Error((data && data.error) || 'The request failed (' + response.status + ').');
        }
        return data;
    }

    // The browser reports very different problems (cancelled, timed out, no Windows Hello set up,
    // unsupported options) with the same few error names, so the technical detail is kept in the
    // message and in the console to make the real cause visible.
    function friendly(error) {
        console.error('Biometric error:', error);
        var detail = error && error.name
            ? ' [' + error.name + (error.message ? ': ' + error.message : '') + ']'
            : '';
        if (error && (error.name === 'NotAllowedError' || error.name === 'AbortError')) {
            return new Error('The request was cancelled or timed out. If you did not cancel it, make sure Windows Hello ' +
                '(fingerprint, face or PIN) is set up on this device.' + detail);
        }
        if (error && error.name === 'InvalidStateError') {
            return new Error('This device is already registered.' + detail);
        }
        if (error && error.name === 'NotSupportedError') {
            return new Error('This device or browser does not support this kind of sign-in.' + detail);
        }
        return error instanceof Error ? error : new Error(String(error));
    }

    // ---------- Fingerprint / device passkey (WebAuthn) ----------

    function ensureWebAuthn() {
        if (!window.PublicKeyCredential || !navigator.credentials) {
            throw new Error('This browser does not support fingerprint sign-in.');
        }
    }

    async function passkeyLogin(returnUrl) {
        ensureWebAuthn();
        var start = await post('/Biometrics/PasskeyLoginOptions');
        var options = start.options;
        options.challenge = dec(options.challenge);

        var credential;
        try {
            credential = await navigator.credentials.get({ publicKey: options });
        } catch (e) {
            throw friendly(e);
        }

        var r = credential.response;
        return post('/Biometrics/PasskeyLogin', {
            token: start.token,
            id: enc(credential.rawId),
            clientDataJson: enc(r.clientDataJSON),
            authenticatorData: enc(r.authenticatorData),
            signature: enc(r.signature),
            userHandle: r.userHandle ? enc(r.userHandle) : null,
            returnUrl: returnUrl
        });
    }

    async function passkeyRegister(label, password) {
        ensureWebAuthn();
        var start = await post('/Biometrics/PasskeyRegisterOptions', { password: password });
        var options = start.options;
        options.challenge = dec(options.challenge);
        options.user.id = dec(options.user.id);
        (options.excludeCredentials || []).forEach(function (c) { c.id = dec(c.id); });

        var credential;
        try {
            credential = await navigator.credentials.create({ publicKey: options });
        } catch (e) {
            throw friendly(e);
        }

        return post('/Biometrics/PasskeyRegister', {
            token: start.token,
            id: enc(credential.rawId),
            clientDataJson: enc(credential.response.clientDataJSON),
            attestationObject: enc(credential.response.attestationObject),
            label: label
        });
    }

    // ---------- Face recognition (instant capture) ----------
    // Detect a face, take a few descriptors from consecutive camera frames, send them. There are no liveness
    // steps (owner decision for the academic demo), so a photo of the person held up to the camera can pass.

    var SAMPLES = 3;               // descriptors per attempt (server accepts 3-8: FaceService.CheckSamples)
    var NO_FACE_TIMEOUT_MS = 10000; // give up after 10 s without a clear face
    var MIN_FACE_WIDTH = 80;       // pixels in the analysed frame; smaller faces give unreliable descriptors

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

    // face-api.js and its three models are loaded once per page and shared by every attempt. A dummy inference
    // then compiles the WebGL programs, so the first real capture is not slowed down by that one-time cost.
    var faceApiReady = null;
    function loadFaceApi() {
        if (!faceApiReady) {
            faceApiReady = (async function () {
                if (!window.faceapi) { await loadScript(FACE_BASE + '/face-api.js'); }
                var nets = window.faceapi.nets;
                var model = FACE_BASE + '/model';
                await Promise.all([
                    nets.tinyFaceDetector.isLoaded || nets.tinyFaceDetector.loadFromUri(model),
                    nets.faceLandmark68Net.isLoaded || nets.faceLandmark68Net.loadFromUri(model),
                    nets.faceRecognitionNet.isLoaded || nets.faceRecognitionNet.loadFromUri(model)
                ]);
                try {
                    var blank = document.createElement('canvas');
                    blank.width = blank.height = 160;
                    await window.faceapi.detectSingleFace(blank, new window.faceapi.TinyFaceDetectorOptions({ inputSize: 160 }));
                    await window.faceapi.detectFaceLandmarks(blank);
                    await window.faceapi.computeFaceDescriptor(blank);
                } catch (e) { console.info('Face model warm-up skipped:', e); }
            })();
            faceApiReady.catch(function () { faceApiReady = null; }); // a failed load can be retried
        }
        return faceApiReady;
    }

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
            throw new Error('The camera is not available. Allow camera access and try again.');
        }
        video.srcObject = stream;
        await video.play();

        // Wait until the camera is really delivering frames (some cameras need a moment after play()).
        for (var i = 0; i < 50 && (!video.videoWidth || video.readyState < 2); i++) { await sleep(60); }
        return stream;
    }

    function stopCamera(stream, video) {
        if (stream) { stream.getTracks().forEach(function (track) { track.stop(); }); }
        if (video) { video.srcObject = null; }
    }

    function nextFrame() { return new Promise(function (resolve) { requestAnimationFrame(function () { resolve(); }); }); }

    // onDetect (optional, used by /face-demo): called for every analysed frame with true/false (face found).
    async function capture(video, setHint, onDetect) {
        var faceapi = window.faceapi;
        // Fast detector first; after 1.5 s without a face a more sensitive one (dim light, face further away).
        var fast = new faceapi.TinyFaceDetectorOptions({ inputSize: 320, scoreThreshold: 0.5 });
        var sensitive = new faceapi.TinyFaceDetectorOptions({ inputSize: 416, scoreThreshold: 0.3 });
        var cpuTried = false;

        // Each frame is read from a plain 2D canvas instead of straight from the <video>: on some graphics drivers
        // a video uploaded to WebGL arrives black and the detector never finds a face.
        var canvas = document.createElement('canvas');
        var context = canvas.getContext('2d', { willReadFrequently: true });
        function grab() {
            var w = Math.min(video.videoWidth || 640, 640);
            var h = Math.round(w * (video.videoHeight || 480) / (video.videoWidth || 640));
            if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
            context.drawImage(video, 0, 0, w, h);
            return canvas;
        }

        var descriptors = [];
        var started = performance.now();
        var lastSeen = started;
        var lastFrameTime = -1;
        setHint('Look at the camera.');

        while (performance.now() - started < NO_FACE_TIMEOUT_MS) {
            // Only analyse a new camera frame, so every sample comes from a different picture.
            if (video.currentTime === lastFrameTime) { await nextFrame(); continue; }
            lastFrameTime = video.currentTime;

            var missing = performance.now() - lastSeen;
            if (missing > 5000 && !cpuTried) {
                // Last resort: some graphics drivers give wrong results in WebGL, so compute on the CPU.
                cpuTried = true;
                try { if (faceapi.tf.getBackend() !== 'cpu') { await faceapi.tf.setBackend('cpu'); await faceapi.tf.ready(); } }
                catch (e) { console.error('Could not switch to the CPU backend:', e); }
            }

            var found = await faceapi.detectSingleFace(grab(), missing > 1500 ? sensitive : fast)
                .withFaceLandmarks().withFaceDescriptor();
            if (onDetect) { onDetect(!!found); }

            if (found && found.detection.box.width >= MIN_FACE_WIDTH) {
                lastSeen = performance.now();
                descriptors.push({ t: Math.round(lastSeen - started), v: Array.from(found.descriptor) });
                setHint('Face detected (' + descriptors.length + '/' + SAMPLES + ')');
                if (descriptors.length >= SAMPLES) { return { descriptors: descriptors }; }
            } else {
                setHint(found ? 'Move a little closer to the camera.' : 'No face found yet. Look straight at the camera, with light on your face.');
            }
        }

        var failure = new Error('No face detected. Make sure your face is well lit and fully in view, then try again.');
        failure.code = 'noFace';
        throw failure;
    }

    async function runFace(options, challengeUrl, submitUrl, extra, challengeBody) {
        options.setStatus('Starting the camera...');
        // The token first: a wrong enrollment password fails before the camera opens.
        var challenge = await post(challengeUrl, challengeBody);
        var cameraStarting = startCamera(options.video);
        try {
            await loadFaceApi(); // usually already loaded in the background when the page opened
        } catch (e) {
            cameraStarting.then(function (s) { stopCamera(s, options.video); }, function () { });
            throw e;
        }
        var stream = await cameraStarting;
        if (options.onCamera) { options.onCamera(true); }
        try {
            options.setStatus('Looking for your face...');
            var evidence = await capture(options.video, options.setHint, options.onDetect);
            options.setStatus('Checking...');
            return await post(submitUrl, Object.assign({ token: challenge.token }, evidence, extra));
        } finally {
            stopCamera(stream, options.video);
            if (options.onCamera) { options.onCamera(false); }
        }
    }

    function faceLogin(options, email, returnUrl) {
        return runFace(options, '/Biometrics/FaceChallenge', '/Biometrics/FaceLogin', { email: email, returnUrl: returnUrl });
    }

    function faceEnroll(options, replace, password) {
        return runFace(options, '/Biometrics/FaceEnrollChallenge', '/Biometrics/FaceEnroll', { replace: !!replace }, { password: password });
    }

    // ---------- Page wiring ----------

    function init() {
        var root = $('bio-root');
        if (!root) { return; }

        var statusBox = $('bio-status');
        var panel = $('facePanel');
        var video = $('faceVideo');
        var hint = $('faceHint');
        var returnUrl = root.getAttribute('data-return-url') || '';

        function setStatus(message, kind) {
            if (!statusBox) { return; }
            statusBox.className = 'bio-status bio-status--' + (kind || 'info');
            statusBox.textContent = message;
        }

        function setHint(text) { if (hint) { hint.textContent = text; } }

        function busy(buttons, isBusy) {
            buttons.forEach(function (b) { if (b) { b.disabled = isBusy || b.hasAttribute('data-bio-disabled'); } });
        }

        var faceOptions = { video: video, setStatus: setStatus, setHint: setHint };
        var allButtons = Array.prototype.slice.call(root.querySelectorAll('button'));

        async function guarded(action, successMessage) {
            busy(allButtons, true);
            try {
                var result = await action();
                if (result && result.redirect) {
                    setStatus('Signed in. Redirecting...', 'success');
                    window.location.href = result.redirect;
                    return;
                }
                setStatus(successMessage, 'success');
                return result;
            } catch (e) {
                setStatus(e.message, 'danger');
            } finally {
                if (panel) { panel.classList.add('bio-hidden'); }
                busy(allButtons, false);
            }
        }

        var passkeyLoginButton = $('btnPasskeyLogin');
        if (passkeyLoginButton) {
            passkeyLoginButton.addEventListener('click', function () {
                setStatus('Use your fingerprint, face or PIN when your device asks.');
                guarded(function () { return passkeyLogin(returnUrl); }, 'Signed in.');
            });
        }

        var faceLoginButton = $('btnFaceLogin');
        if (faceLoginButton) {
            faceLoginButton.addEventListener('click', function () {
                var emailBox = $('faceEmail') || $('Input_Email');
                var email = ((emailBox && emailBox.value) || '').trim();
                if (!email) { setStatus('Enter your supplier email first.', 'danger'); return; }
                if (panel) { panel.classList.remove('bio-hidden'); }
                guarded(function () { return faceLogin(faceOptions, email, returnUrl); }, 'Signed in.');
            });
        }

        var passkeyRegisterButton = $('btnPasskeyRegister');
        if (passkeyRegisterButton) {
            passkeyRegisterButton.addEventListener('click', function () {
                var label = ($('passkeyLabel').value || '').trim() || 'My device';
                var password = ($('bioPassword') || {}).value || '';
                if (!password) { setStatus('Enter your current password to continue.', 'danger'); return; }
                setStatus('Use your fingerprint, face or PIN when your device asks.');
                guarded(function () { return passkeyRegister(label, password); }, 'Device registered.').then(function (ok) {
                    if (ok) { window.location.reload(); }
                });
            });
        }

        var faceEnrollButton = $('btnFaceEnroll');
        if (faceEnrollButton) {
            faceEnrollButton.addEventListener('click', function () {
                var password = ($('bioPassword') || {}).value || '';
                if (!password) { setStatus('Enter your current password to continue.', 'danger'); return; }
                var replaceBox = $('faceReplace');
                if (panel) { panel.classList.remove('bio-hidden'); }
                guarded(function () { return faceEnroll(faceOptions, replaceBox ? replaceBox.checked : true, password); }, 'Face registered successfully.').then(function (ok) {
                    // The registration step moves on to its confirmation; the settings page just reloads.
                    var next = root.getAttribute('data-enrolled-url');
                    if (ok) { if (next) { window.location.href = next; } else { window.location.reload(); } }
                });
            });
        }

        var faceRemoveButton = $('btnFaceRemove');
        if (faceRemoveButton) {
            faceRemoveButton.addEventListener('click', function () {
                if (!window.confirm('Remove your stored face samples?')) { return; }
                guarded(function () { return post('/Biometrics/FaceRemove'); }, 'Face samples removed.').then(function (ok) {
                    if (ok) { window.location.reload(); }
                });
            });
        }
    }

    // ---------- Development-only face demo (/face-demo, Controllers/FaceDemoController.cs) ----------
    // Same capture and server-side matching as sign-in; it only shows the numbers and signs nobody in.

    function initFaceDemo() {
        var root = $('face-demo-root');
        if (!root) { return; }

        var video = $('faceVideo');
        var hint = $('faceHint');
        var result = $('demoResult');
        var buttons = Array.prototype.slice.call(root.querySelectorAll('button'));
        var detectedFrames = 0;
        var missedFrames = 0;
        var started = 0;

        function show(id, text) { var el = $(id); if (el) { el.textContent = text; } }
        function setResult(text, kind) {
            result.className = 'bio-status bio-status--' + (kind || 'info');
            result.textContent = text;
        }
        function elapsed() { return ((performance.now() - started) / 1000).toFixed(1) + ' s'; }

        var options = {
            video: video,
            setStatus: function (text) { show('dbgStatus', text); },
            setHint: function (text) { if (hint) { hint.textContent = text; } },
            onCamera: function (on) { show('dbgCamera', on ? 'On' : 'Off'); },
            onDetect: function (found) {
                if (found) { detectedFrames++; } else { missedFrames++; }
                show('dbgDetection', (found ? 'Face detected' : 'No face in frame') + ' (' + detectedFrames + ' frames with a face, ' + missedFrames + ' without)');
            }
        };

        function clearDebug() {
            detectedFrames = 0;
            missedFrames = 0;
            ['dbgDetection', 'dbgMatch', 'dbgDistance', 'dbgSamples', 'dbgTime'].forEach(function (id) { show(id, '—'); });
        }

        async function run(submitUrl, onResult) {
            buttons.forEach(function (b) { b.disabled = true; });
            clearDebug();
            video.parentElement.classList.remove('bio-hidden');
            setResult('Look at the camera.');
            started = performance.now();
            try {
                onResult(await runFace(options, '/face-demo/challenge', submitUrl, {}));
            } catch (e) {
                if (e && e.code === 'noFace') {
                    show('dbgDetection', 'No face detected');
                    setResult('No Face Detected — ' + e.message, 'danger');
                } else {
                    if (e && /camera/i.test(e.message)) { show('dbgCamera', 'Not available'); }
                    setResult((e && e.message) || String(e), 'danger');
                }
            } finally {
                show('dbgTime', elapsed() + ' (camera start + capture + check)');
                buttons.forEach(function (b) { b.disabled = false; });
                options.setStatus('Idle');
            }
        }

        $('btnDemoRegister').addEventListener('click', function () {
            run('/face-demo/register', function (r) {
                if (!r.registered) { setResult('Capture rejected — ' + r.problem, 'danger'); return; }
                show('dbgSamples', r.samples + ' samples stored in memory for this browser');
                show('demoTestFace', 'Registered (' + r.samples + ' samples)');
                setResult('Test Face Registered Successfully', 'success');
            });
        });

        $('btnDemoVerify').addEventListener('click', function () {
            run('/face-demo/verify', function (r) {
                if (r.stage === 'noTestFace') { setResult('Register a test face first.', 'danger'); return; }
                if (r.stage === 'rejected') { setResult('Capture rejected — ' + r.problem, 'danger'); return; }
                show('dbgThreshold', String(r.threshold));
                show('dbgDistance', 'average ' + r.averageDistance + ' (per sample: ' + r.bestDistances.join(', ') + ')');
                show('dbgMatch', r.matchingSamples + ' of ' + r.bestDistances.length + ' samples within threshold (' + r.requiredSamples + ' required, average must be ≤ threshold)');
                setResult(r.matched ? 'Face Matched — Success' : 'Face Not Matched — Failed', r.matched ? 'success' : 'danger');
            });
        });

        $('btnDemoReset').addEventListener('click', async function () {
            try {
                await post('/face-demo/reset');
                clearDebug();
                show('demoTestFace', 'None registered');
                setResult('Test face cleared. Register a new one.');
            } catch (e) {
                setResult(e.message, 'danger');
            }
        });
    }

    // Load the face models in the background as soon as a page can use them, so the first click is fast.
    // On the Login page (also used by employees) only once the supplier starts using the face sign-in.
    function preloadFace() { loadFaceApi().catch(function (e) { console.error('Face models could not be preloaded:', e); }); }

    function start() {
        init();
        initFaceDemo();
        if ($('btnFaceEnroll') || $('face-demo-root')) { preloadFace(); }
        var supplierEmail = $('faceEmail');
        if (supplierEmail) { supplierEmail.addEventListener('focus', preloadFace, { once: true }); }
        var faceLoginButton = $('btnFaceLogin');
        if (faceLoginButton) { faceLoginButton.addEventListener('pointerenter', preloadFace, { once: true }); }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
