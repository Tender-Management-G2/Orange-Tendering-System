// Runs the real capture loop of wwwroot/js/biometric.js (captureFaceSamples) with a fake clock, camera and face model.
// It checks timing and restarts, not recognition accuracy or liveness.
// Run from the web project folder: node Tests/AuthSmoke/FaceCaptureChecks.js
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync('wwwroot/js/biometric.js', 'utf8');

// frame(callNumber) says how many faces the fake model "sees" in that picture.
async function captureScenario(frame) {
    let time = 0;
    let calls = 0;
    const hints = [];
    const video = { videoWidth: 640, videoHeight: 480, get currentTime() { return time / 1000; } };
    const faceapi = {
        TinyFaceDetectorOptions: function () { },
        detectAllFaces: () => ({
            withFaceLandmarks: () => ({
                withFaceDescriptors: async () => {
                    time += 200; // every analysed picture takes 200 ms
                    calls++;
                    const count = frame(calls);
                    const faces = [];
                    for (let i = 0; i < count; i++) {
                        faces.push({ detection: { box: { width: 150 } }, descriptor: new Float32Array(128).fill(calls / 1000) });
                    }
                    return faces;
                }
            })
        }),
        tf: { getBackend: () => 'cpu' }
    };
    const context = {
        window: { faceapi: faceapi },
        performance: { now: () => time },
        console,
        document: {
            addEventListener() { },
            createElement: () => ({ getContext: () => ({ drawImage() { } }) }),
            getElementById: id => (id === 'faceHint' ? { set textContent(text) { hints.push(text); } } : null)
        },
        requestAnimationFrame: callback => { time += 200; callback(); },
        setTimeout
    };
    vm.runInNewContext(source, context);
    const result = await context.captureFaceSamples(video, null);
    return { result, hints, time };
}

(async () => {
    const steady = await captureScenario(() => 1);
    const samples = steady.result.descriptors;
    assert(steady.time >= 7000);
    assert(samples[samples.length - 1].t - samples[0].t >= 5000); // the server requires at least 5 seconds
    assert(samples.every(s => s.v.length === 128));
    assert(samples.length >= 3 && samples.length <= 14);           // the server accepts 3 to 14 samples
    console.log('PASS seven-second capture returns 3 to 14 spaced 128-value descriptors');

    for (const facesAtCall15 of [0, 2]) {
        const reset = await captureScenario(call => (call === 15 ? facesAtCall15 : 1));
        assert(reset.time >= 7000 + 15 * 200);
        assert(reset.result.descriptors[0].v[0] >= 0.016); // only samples taken after the restart
        console.log('PASS capture restarts after ' + (facesAtCall15 === 0 ? 'the face disappears' : 'a second face appears'));
    }

    await assert.rejects(captureScenario(() => 0), error => error.code === 'noFace');
    console.log('PASS absent face times out without returning samples');
})().catch(error => { console.error(error); process.exitCode = 1; });
