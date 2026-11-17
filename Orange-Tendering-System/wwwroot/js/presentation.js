// /presentation: keyboard, click, swipe and hash navigation; speaker notes (N); full screen (F).
(function () {
    'use strict';
    var slides = Array.prototype.slice.call(document.querySelectorAll('.pz-slide'));
    if (!slides.length) return;
    var prev = document.querySelector('[data-pz-prev]'), next = document.querySelector('[data-pz-next]');
    var dots = Array.prototype.slice.call(document.querySelectorAll('[data-pz-go]'));
    var current = document.querySelector('[data-pz-current]'), bar = document.querySelector('[data-pz-bar]');
    var notesBtn = document.querySelector('[data-pz-notes]'), notesPanel = document.querySelector('[data-pz-notes-panel]'), notesText = document.querySelector('[data-pz-notes-text]');
    var index = 0;

    function show(i, focus) {
        index = Math.max(0, Math.min(slides.length - 1, i));
        slides.forEach(function (s, k) {
            s.classList.toggle('is-active', k === index);
            s.classList.toggle('is-before', k < index);
            s.setAttribute('aria-hidden', k === index ? 'false' : 'true');
            s.inert = k !== index;
        });
        dots.forEach(function (d, k) { d.setAttribute('aria-current', k === index ? 'true' : 'false'); });
        current.textContent = index + 1;
        bar.style.width = ((index + 1) / slides.length * 100) + '%';
        prev.disabled = index === 0; next.disabled = index === slides.length - 1;
        notesText.textContent = slides[index].dataset.notes || '';
        if (history.replaceState) history.replaceState(null, '', '#' + (index + 1));
        if (focus) { var h = slides[index].querySelector('.pz-title'); if (h) { h.tabIndex = -1; h.focus({ preventScroll: true }); } }
        slides[index].scrollTop = 0;
    }

    prev.addEventListener('click', function () { show(index - 1); });
    next.addEventListener('click', function () { show(index + 1); });
    dots.forEach(function (d) { d.addEventListener('click', function () { show(+d.dataset.pzGo, true); }); });

    function toggleNotes() { var on = notesPanel.hidden; notesPanel.hidden = !on; notesBtn.setAttribute('aria-pressed', on ? 'true' : 'false'); }
    function toggleFull() { if (!document.fullscreenElement) { if (document.documentElement.requestFullscreen) document.documentElement.requestFullscreen().catch(function () { }); } else if (document.exitFullscreen) document.exitFullscreen(); }
    notesBtn.addEventListener('click', toggleNotes);
    document.querySelector('[data-pz-full]').addEventListener('click', toggleFull);

    document.addEventListener('keydown', function (e) {
        var t = e.target && e.target.closest ? e.target : document.body;
        if (t.closest('input,textarea,select') || e.altKey || e.ctrlKey || e.metaKey) return;
        var k = e.key;
        if (k === 'ArrowRight' || k === 'PageDown' || (k === ' ' && !t.closest('button'))) { e.preventDefault(); show(index + 1); }
        else if (k === 'ArrowLeft' || k === 'PageUp') { e.preventDefault(); show(index - 1); }
        else if (k === 'Home') { e.preventDefault(); show(0); }
        else if (k === 'End') { e.preventDefault(); show(slides.length - 1); }
        else if (k === 'n' || k === 'N') toggleNotes();
        else if (k === 'f' || k === 'F') toggleFull();
    });

    var x0 = null, y0 = null;
    document.addEventListener('touchstart', function (e) { x0 = e.touches[0].clientX; y0 = e.touches[0].clientY; }, { passive: true });
    document.addEventListener('touchend', function (e) {
        if (x0 === null) return; var dx = e.changedTouches[0].clientX - x0, dy = e.changedTouches[0].clientY - y0; x0 = null;
        if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5) show(index + (dx < 0 ? 1 : -1));
    }, { passive: true });

    function fromHash() { var n = parseInt((location.hash || '').slice(1), 10); return isNaN(n) ? 0 : n - 1; }
    window.addEventListener('hashchange', function () { if (fromHash() !== index) show(fromHash()); });
    show(fromHash());
})();
