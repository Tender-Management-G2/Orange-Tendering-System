// My Offers (Supplier): filters + offer details popup. A won offer gets a short Orange/gold confetti burst
// (skipped when the user prefers reduced motion). Data comes from the server's ownership-checked Details action.
(function () {
    'use strict';
    var q = document.getElementById('so-q'), st = document.getElementById('so-status'), rs = document.getElementById('so-result');
    var rows = Array.prototype.slice.call(document.querySelectorAll('#so-body tr')), empty = document.getElementById('so-empty'), count = document.getElementById('so-count');
    var total = rows.length;
    function apply() {
        var text = q.value.trim().toLowerCase(), shown = 0;
        rows.forEach(function (r) {
            var ok = (!text || r.dataset.text.indexOf(text) > -1) && (!st.value || r.dataset.status === st.value) && (!rs.value || r.dataset.result === rs.value);
            r.hidden = !ok; if (ok) shown++;
        });
        if (total) { empty.hidden = shown > 0; document.getElementById('so-empty-text').textContent = 'No offers match these filters.'; }
        count.textContent = shown + (shown === 1 ? ' offer' : ' offers') + (shown !== total ? ' of ' + total : '');
    }
    if (q) {
        [q, st, rs].forEach(function (el) { el.addEventListener('input', apply); el.addEventListener('change', apply); });
        document.getElementById('so-reset').addEventListener('click', function () { q.value = ''; st.value = ''; rs.value = ''; apply(); q.focus(); });
    }

    var dialog = document.getElementById('so-dialog');
    if (!dialog) return;
    var body = dialog.querySelector('[data-so-body]'), request, opener;
    var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    function confetti(host) {
        if (reduce) return;
        var cv = document.createElement('canvas'); cv.className = 'so-confetti'; cv.setAttribute('aria-hidden', 'true');
        host.appendChild(cv);
        var ctx = cv.getContext('2d'), dpr = Math.min(2, window.devicePixelRatio || 1), W, H;
        function size() { W = host.clientWidth; H = host.clientHeight; cv.width = W * dpr; cv.height = H * dpr; ctx.setTransform(dpr, 0, 0, dpr, 0, 0); }
        size();
        var colors = ['#ff7900', '#ffb347', '#ffd166', '#f5c542', '#ffffff', '#ff9a3c'], parts = [];
        for (var i = 0; i < 160; i++) {
            var a = -Math.PI / 2 + (Math.random() - .5) * 1.9, v = 7 + Math.random() * 9;
            parts.push({ x: W / 2 + (Math.random() - .5) * 60, y: H * .55, vx: Math.cos(a) * v, vy: Math.sin(a) * v, r: 4 + Math.random() * 5, c: colors[i % colors.length], s: Math.random() * 6, w: .1 + Math.random() * .25, t: Math.random() < .3 });
        }
        var start = performance.now();
        (function frame(now) {
            var age = (now - start) / 1000;
            if (age > 3.6 || !dialog.open) { cv.remove(); return; }
            ctx.clearRect(0, 0, W, H); ctx.globalAlpha = Math.max(0, 1 - Math.max(0, age - 2.4) / 1.2);
            parts.forEach(function (p) {
                p.vy += .28; p.vx *= .985; p.vy *= .985; p.x += p.vx; p.y += p.vy; p.s += p.w;
                ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.s); ctx.fillStyle = p.c;
                if (p.t) { ctx.beginPath(); ctx.arc(0, 0, p.r * .55, 0, 7); ctx.fill(); } else ctx.fillRect(-p.r / 2, -p.r / 4, p.r, p.r / 2 * (1 + Math.abs(Math.sin(p.s))));
                ctx.restore();
            });
            requestAnimationFrame(frame);
        })(start);
    }

    document.querySelectorAll('[data-offer-details]').forEach(function (button) {
        button.addEventListener('click', async function () {
            opener = button;
            if (request) request.abort();
            request = new AbortController();
            body.innerHTML = '<div class="wf-loading"><i></i>Loading offer details…</div>';
            dialog.showModal();
            try {
                var res = await fetch(button.dataset.url, { credentials: 'same-origin', signal: request.signal, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                if (!res.ok || res.redirected) throw new Error('Unavailable');
                var html = await res.text();
                if (!dialog.open || html.indexOf('so-pop') < 0) throw new Error('Unavailable');
                body.innerHTML = html; body.scrollTop = 0;
                var pop = body.querySelector('.so-pop');
                if (pop && pop.dataset.soState === 'won') confetti(dialog);
                var title = body.querySelector('#so-pop-title'); if (title) { title.tabIndex = -1; title.focus({ preventScroll: true }); }
            } catch (e) {
                if (e.name !== 'AbortError') body.innerHTML = '<div class="wf-loading wf-loading-error">These offer details could not be loaded. Close and try again.</div>';
            }
        });
    });
    dialog.addEventListener('click', function (e) {
        if (e.target.closest('[data-so-close]')) { dialog.close(); return; }
        if (e.target === dialog) {
            var b = dialog.getBoundingClientRect();
            if (e.clientX < b.left || e.clientX > b.right || e.clientY < b.top || e.clientY > b.bottom) dialog.close();
        }
    });
    dialog.addEventListener('close', function () { if (request) request.abort(); if (opener) opener.focus(); });
})();
