// Exercise the actual capture loop with a deterministic clock/camera/model.
// These tests check timing and resets, not recognition accuracy or physical liveness.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync('wwwroot/js/biometric.js', 'utf8')
    .replace('    function start() {', '    window.testCapture = capture;\n    function start() {');

async function captureScenario(frame) {
    let time = 0, calls = 0;
    const video = { videoWidth: 640, videoHeight: 480, get currentTime() { return time / 1000; } };
    const context = {
        window: { faceapi: {
            TinyFaceDetectorOptions: function () {},
            detectAllFaces: () => ({ withFaceLandmarks: () => ({ withFaceDescriptors: async () => {
                time += 200; calls++;
                const number = frame(calls);
                return Array.from({ length: number }, () => ({ detection: { box: { width: 150 } }, descriptor: new Float32Array(128).fill(calls / 1000) }));
            } }) }),
            tf: { getBackend: () => 'cpu' }
        } },
        performance: { now: () => time }, console,
        document: { readyState: 'loading', addEventListener() {}, createElement: () => ({ getContext: () => ({ drawImage() {} }) }) },
        requestAnimationFrame: callback => { time += 200; callback(); },
        setTimeout
    };
    vm.runInNewContext(source, context);
    const hints = [];
    const result = await context.window.testCapture(video, hint => hints.push(hint));
    return { result, hints, time };
}

(async () => {
    const steady = await captureScenario(() => 1);
    assert(steady.time >= 5000);
    assert(steady.result.descriptors.at(-1).t - steady.result.descriptors[0].t >= 5000);
    assert(steady.result.descriptors.every(s => s.v.length === 128));
    assert(steady.result.descriptors.length >= 3 && steady.result.descriptors.length <= 8);
    console.log('PASS five-second capture returns spaced 128-value descriptors');
    for (const invalidCount of [0, 2]) {
        const reset = await captureScenario(call => call === 15 ? invalidCount : 1);
        assert(reset.time >= 8000);
        assert(reset.result.descriptors[0].v[0] >= 0.015);
        console.log('PASS capture restarts after ' + (invalidCount === 0 ? 'face disappears' : 'multiple faces'));
    }
    await assert.rejects(captureScenario(() => 0), error => error.code === 'noFace');
    console.log('PASS absent face times out without returning evidence');
})().catch(error => { console.error(error); process.exitCode = 1; });
