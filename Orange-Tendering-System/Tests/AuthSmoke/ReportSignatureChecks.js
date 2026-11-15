// Run with node. Exercises the standalone signing form without writing database approvals.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
function fixture(decision, action = '/EvaluationCommittee/Reports/Sign/1') {
    const handlers = {}, input = { value: '' }, error = { hidden: true };
    const canvas = {
        width: 640, height: 200,
        getContext: () => ({ beginPath() {}, moveTo() {}, lineTo() {}, stroke() {}, clearRect() {} }),
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 320, height: 100 }),
        setPointerCapture() {}, scrollIntoView() {}, toDataURL: () => 'data:image/png;base64,test-png',
        addEventListener: (name, fn) => { handlers[name] = fn; }
    };
    const form = { action, querySelector: () => decision ? { value: decision } : null, addEventListener: (_, fn) => { handlers.submit = fn; } };
    const pad = { dataset: {},
        closest: () => form,
        querySelector: selector => selector === 'canvas' ? canvas : selector === 'input' ? input : selector === '[data-signature-error]' ? error : { addEventListener: (_, fn) => { handlers.clear = fn; } }
    };
    let observer;
    const runtime = { window: {}, document: { body: {}, querySelectorAll: () => [pad] }, MutationObserver: class { constructor(callback) { observer = callback; } observe() {} } };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../wwwroot/js/report-signature.js'), 'utf8'), runtime);
    function submit() { let blocked = false; handlers.submit({ preventDefault() { blocked = true; }, stopImmediatePropagation() {} }); return blocked; }
    function draw() { handlers.pointerdown({ clientX: 30, clientY: 40, pointerId: 1, preventDefault() {} }); handlers.pointerup(); }
    return { handlers, input, error, submit, draw, reinitialize: () => observer() };
}
const committee = fixture();
assert.equal(committee.submit(), true, 'Blank committee signature must be blocked');
assert.equal(committee.error.hidden, false);
committee.draw();
committee.reinitialize(); // Opening/replacing modal content must not reset an existing signature.
assert.equal(committee.submit(), false);
assert.equal(committee.input.value, 'data:image/png;base64,test-png', 'Drawn PNG must be sent');
committee.handlers.clear();
assert.equal(committee.input.value, '');
assert.equal(committee.submit(), true, 'Cleared signature must be blocked');
assert.equal(fixture('Approve', '/SourcingManager/Reports/Decision/1').submit(), true);
assert.equal(fixture('Reject', '/SourcingManager/Reports/Decision/1').submit(), false, 'Manager rejection does not require a signature');
assert.equal(fixture('Reject', '/TopCommittee/Reports/Vote/1').submit(), true, 'Top rejection still signs its vote');
console.log('PASS: blank, drawn PNG, clear, Manager approval/rejection and Top vote signature checks');
