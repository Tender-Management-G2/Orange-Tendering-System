// DEVELOPMENT-ONLY demo pages. Needs biometric.js first (postJson, startCamera, captureFaceSamples, ...).
//   /face-demo         (Views/FaceDemo/Index.cshtml, Controllers/FaceDemoController.cs)
//   /fingerprint-demo  (Views/FingerprintDemo/Index.cshtml, Controllers/FingerprintDemoController.cs)
// They use the same capture, the same passkey ceremony and the same server checks as the real sign-in,
// show the numbers, and never sign anybody in.

// =====================================================================================================
// Face demo
// =====================================================================================================

var demoFramesWithFace = 0;
var demoFramesWithoutFace = 0;

function showDemoValue(id, text) {
    var element = document.getElementById(id);
    if (element) {
        element.textContent = text;
    }
}

function showDemoResult(text, kind) {
    var box = document.getElementById('demoResult');
    box.className = 'bio-status bio-status--' + (kind || 'info');
    box.textContent = text;
}

function clearDemoDebug() {
    demoFramesWithFace = 0;
    demoFramesWithoutFace = 0;
    showDemoValue('dbgDetection', '—');
    showDemoValue('dbgMatch', '—');
    showDemoValue('dbgDistance', '—');
    showDemoValue('dbgSamples', '—');
    showDemoValue('dbgTime', '—');
}

// Called by captureFaceSamples() for every analysed camera picture.
function countDemoFrame(faceFound) {
    if (faceFound) {
        demoFramesWithFace++;
    } else {
        demoFramesWithoutFace++;
    }
    var text = 'No face in frame';
    if (faceFound) {
        text = 'Face detected';
    }
    showDemoValue('dbgDetection', text + ' (' + demoFramesWithFace + ' frames with a face, ' + demoFramesWithoutFace + ' without)');
}

function setDemoButtonsDisabled(disabled) {
    var buttons = document.querySelectorAll('#face-demo-root button');
    for (var i = 0; i < buttons.length; i++) {
        buttons[i].disabled = disabled;
    }
}

// The same steps as runFaceCheck() in biometric.js, with every step shown in the debug table.
async function runDemoFaceCheck(submitUrl) {
    var video = document.getElementById('faceVideo');
    var startTime = performance.now();
    setDemoButtonsDisabled(true);
    clearDemoDebug();
    video.parentElement.classList.remove('bio-hidden');
    showDemoResult('Look at the camera.');

    try {
        showDemoValue('dbgStatus', 'Starting the camera...');
        var challenge = await postJson('/face-demo/challenge', {});
        await startCamera(video);
        showDemoValue('dbgCamera', 'On');
        try {
            await loadFaceModels();
            showDemoValue('dbgStatus', 'Looking for your face...');
            var capture = await captureFaceSamples(video, countDemoFrame);
            showDemoValue('dbgStatus', 'Checking...');
            return await postJson(submitUrl, { token: challenge.token, descriptors: capture.descriptors });
        } finally {
            stopCamera(video);
            showDemoValue('dbgCamera', 'Off');
        }
    } catch (error) {
        if (error && error.code === 'noFace') {
            showDemoValue('dbgDetection', 'No face detected');
            showDemoResult('No Face Detected — ' + error.message, 'danger');
        } else {
            if (error && /camera/i.test(error.message)) {
                showDemoValue('dbgCamera', 'Not available');
            }
            showDemoResult(error.message, 'danger');
        }
        return null;
    } finally {
        var seconds = ((performance.now() - startTime) / 1000).toFixed(1);
        showDemoValue('dbgTime', seconds + ' s (camera start + capture + check)');
        showDemoValue('dbgStatus', 'Idle');
        setDemoButtonsDisabled(false);
    }
}

async function registerDemoFace() {
    var answer = await runDemoFaceCheck('/face-demo/register');
    if (answer === null) {
        return;
    }
    if (!answer.registered) {
        showDemoResult('Capture rejected — ' + answer.problem, 'danger');
        return;
    }
    showDemoValue('dbgSamples', answer.samples + ' samples stored in memory for this browser');
    showDemoValue('demoTestFace', 'Registered (' + answer.samples + ' samples)');
    showDemoResult('Test Face Registered Successfully', 'success');
}

async function verifyDemoFace() {
    var answer = await runDemoFaceCheck('/face-demo/verify');
    if (answer === null) {
        return;
    }
    if (answer.stage === 'noTestFace') {
        showDemoResult('Register a test face first.', 'danger');
        return;
    }
    if (answer.stage === 'rejected') {
        showDemoResult('Capture rejected — ' + answer.problem, 'danger');
        return;
    }
    showDemoValue('dbgThreshold', String(answer.threshold));
    showDemoValue('dbgDistance', 'average ' + answer.averageDistance + ' (per sample: ' + answer.bestDistances.join(', ') + ')');
    showDemoValue('dbgMatch', answer.matchingSamples + ' of ' + answer.bestDistances.length + ' samples within threshold (' +
        answer.requiredSamples + ' required, average must be ≤ threshold)');
    if (answer.matched) {
        showDemoResult('Face Matched — Success', 'success');
    } else {
        showDemoResult('Face Not Matched — Failed', 'danger');
    }
}

async function resetDemoFace() {
    try {
        await postJson('/face-demo/reset', {});
        clearDemoDebug();
        showDemoValue('demoTestFace', 'None registered');
        showDemoResult('Test face cleared. Register a new one.');
    } catch (error) {
        showDemoResult(error.message, 'danger');
    }
}

// =====================================================================================================
// Fingerprint / passkey demo
// =====================================================================================================

function setPasskeyDemoButtonsDisabled(disabled) {
    var buttons = document.querySelectorAll('#passkey-demo-root button');
    for (var i = 0; i < buttons.length; i++) {
        buttons[i].disabled = disabled;
    }
}

function showPasskeyDemoResult(text, kind) {
    var box = document.getElementById('passkeyDemoResult');
    box.className = 'bio-status';
    if (kind) {
        box.className = 'bio-status bio-status--' + kind;
    }
    box.textContent = text;
}

function showPasskeyDemoTime(startTime) {
    var seconds = ((performance.now() - startTime) / 1000).toFixed(1);
    document.getElementById('passkeyDemoTime').textContent = seconds + ' seconds';
}

async function registerDemoPasskey() {
    var startTime = performance.now();
    setPasskeyDemoButtonsDisabled(true);
    showPasskeyDemoResult('Follow the browser and phone instructions…');
    try {
        var options = await postJson('/fingerprint-demo/register-options', {});
        var credentialJson = await createPasskey(options, null);
        var answer = await postJson('/fingerprint-demo/register', { credentialJson: credentialJson, label: 'Demo phone' });
        showPasskeyDemoResult('Fingerprint / Passkey Registered Successfully', 'success');
        document.getElementById('passkeyDemoCount').textContent = answer.count + ' registered';
    } catch (error) {
        showPasskeyDemoResult(error.message, 'danger');
    } finally {
        showPasskeyDemoTime(startTime);
        setPasskeyDemoButtonsDisabled(false);
    }
}

async function verifyDemoPasskey() {
    var startTime = performance.now();
    var resultCell = document.getElementById('passkeyDemoAuth');
    setPasskeyDemoButtonsDisabled(true);
    showPasskeyDemoResult('Follow the browser and phone instructions…');
    try {
        var options = await postJson('/fingerprint-demo/login-options', {});
        var credentialJson = await getPasskey(options, null);
        await postJson('/fingerprint-demo/verify', { credentialJson: credentialJson });
        showPasskeyDemoResult('Authentication Successful', 'success');
        resultCell.textContent = 'Authentication Successful';
    } catch (error) {
        showPasskeyDemoResult(error.message, 'danger');
        if (error.code === 'cancelled') {
            resultCell.textContent = 'Authentication Cancelled';
        } else if (error.message === 'No Passkey Registered') {
            resultCell.textContent = error.message;
        } else {
            resultCell.textContent = 'Authentication Failed';
        }
    } finally {
        showPasskeyDemoTime(startTime);
        setPasskeyDemoButtonsDisabled(false);
    }
}

async function resetDemoPasskeys() {
    var startTime = performance.now();
    setPasskeyDemoButtonsDisabled(true);
    showPasskeyDemoResult('Follow the browser and phone instructions…');
    try {
        await postJson('/fingerprint-demo/reset', {});
        document.getElementById('passkeyDemoAuth').textContent = 'Not attempted';
        document.getElementById('passkeyDemoCount').textContent = '0 registered';
        showPasskeyDemoResult('Demo reset. Delete the saved passkey on your phone separately if needed.', 'success');
    } catch (error) {
        showPasskeyDemoResult(error.message, 'danger');
    } finally {
        showPasskeyDemoTime(startTime);
        setPasskeyDemoButtonsDisabled(false);
    }
}

// =====================================================================================================

function setUpDemoPages() {
    if (document.getElementById('face-demo-root')) {
        document.getElementById('btnDemoRegister').addEventListener('click', registerDemoFace);
        document.getElementById('btnDemoVerify').addEventListener('click', verifyDemoFace);
        document.getElementById('btnDemoReset').addEventListener('click', resetDemoFace);
        preloadFaceModels();
    }

    if (document.getElementById('passkey-demo-root')) {
        var support = 'Unavailable — use HTTPS and a compatible browser';
        if (window.isSecureContext && window.PublicKeyCredential) {
            support = 'Supported (HTTPS)';
        }
        document.getElementById('passkeyDemoSupport').textContent = support;
        document.getElementById('btnPasskeyDemoRegister').addEventListener('click', registerDemoPasskey);
        document.getElementById('btnPasskeyDemoVerify').addEventListener('click', verifyDemoPasskey);
        document.getElementById('btnPasskeyDemoReset').addEventListener('click', resetDemoPasskeys);
    }
}

document.addEventListener('DOMContentLoaded', setUpDemoPages);
