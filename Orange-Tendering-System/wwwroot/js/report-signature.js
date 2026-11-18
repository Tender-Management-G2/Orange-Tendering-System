(function () {
    'use strict';
    if (window.OrangeReportSignatures) { window.OrangeReportSignatures.init(); return; }
    function init() { document.querySelectorAll('[data-report-signature]').forEach(function (pad) {
        if (pad.dataset.signatureReady) return;
        pad.dataset.signatureReady = 'true';
        var canvas = pad.querySelector('canvas'), context = canvas.getContext('2d');
        var form = pad.closest('form'), drawing = false, drawn = false, last;
        context.lineWidth = 3; context.lineCap = 'round'; context.strokeStyle = '#111';
        function point(event) {
            var rect = canvas.getBoundingClientRect();
            return { x: (event.clientX - rect.left) * canvas.width / rect.width, y: (event.clientY - rect.top) * canvas.height / rect.height };
        }
        canvas.addEventListener('pointerdown', function (event) {
            drawing = true; last = point(event); canvas.setPointerCapture(event.pointerId);
            context.beginPath(); context.moveTo(last.x, last.y); context.lineTo(last.x + .1, last.y + .1); context.stroke();
            drawn = true; pad.querySelector('[data-signature-error]').hidden = true;
            event.preventDefault();
        });
        canvas.addEventListener('pointermove', function (event) {
            if (!drawing) return;
            var next = point(event); context.beginPath(); context.moveTo(last.x, last.y); context.lineTo(next.x, next.y); context.stroke(); last = next;
        });
        ['pointerup', 'pointercancel', 'lostpointercapture'].forEach(function (name) { canvas.addEventListener(name, function () { drawing = false; }); });
        pad.querySelector('[data-signature-clear]').addEventListener('click', function () {
            context.clearRect(0, 0, canvas.width, canvas.height); drawn = false; pad.querySelector('input').value = '';
        });
        form.addEventListener('submit', function (event) {
            var decision = form.querySelector('select[name="decision"]');
            var required = !decision || decision.value === 'Approve' || form.action.indexOf('/TopCommittee/') >= 0;
            if (required && !drawn) {
                event.preventDefault(); event.stopImmediatePropagation(); pad.querySelector('[data-signature-error]').hidden = false; canvas.scrollIntoView({ block: 'center' }); return;
            }
            pad.querySelector('input').value = drawn ? canvas.toDataURL('image/png') : '';
        }, true); // Populate the PNG before modal handlers construct FormData.
    }); }
    window.OrangeReportSignatures = { init: init };
    init();
    new MutationObserver(init).observe(document.body, { childList: true, subtree: true });
}());
