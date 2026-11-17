// Global event spotlight (Views/Shared/_EventSpotlight.cshtml). Real, own, unacknowledged events from GET /Spotlight/Pending.
// - Verified tender win: one-time celebration (dimmed backdrop, Orange/gold confetti, trophy reveal, congratulations card).
//   It opens by itself only on a dashboard; on any other page a small gold card offers it, so work is never interrupted.
// - Approvals, rejections, messages, returned items and required actions: calm, non-blocking cards (no celebration for negatives).
// Dismissal is stored server-side (POST /Spotlight/Acknowledge/{id}), so it is not shown again on the next login.
// Reduced motion: no confetti or movement. Sound: off by default, opt-in per browser.
(function () {
    'use strict';
    var host = document.getElementById('event-spotlight');
    if (!host || !window.fetch) return;
    var ar = host.dataset.lang === 'ar' || document.documentElement.lang === 'ar' || document.documentElement.dir === 'rtl';
    var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var T = ar ? {
        won: 'فزت بالعطاء!', congrats: 'مبروك!', selected: 'تم اختيار شركتك بعد استكمال جميع الموافقات الإلزامية.', ref: 'رقم العطاء',
        view: 'عرض تفاصيل الترسية', replay: 'إعادة العرض', close: 'إغلاق', soundOn: 'تشغيل الصوت', soundOff: 'كتم الصوت',
        celebrate: 'احتفل', winCard: 'فزت بعطاء جديد', open: 'فتح', dismiss: 'إخفاء', history: 'سجل الإشعارات', region: 'تنبيهات الأحداث',
        kinds: { approval: 'تمت الموافقة', rejection: 'تحديث على القرار', message: 'رسالة جديدة', returned: 'أُعيد للمراجعة', action: 'إجراء مطلوب' }
    } : {
        won: 'YOU WON THE TENDER!', congrats: 'Congratulations!', selected: 'Your company was selected after all mandatory approvals.', ref: 'Tender reference',
        view: 'View Award Details', replay: 'Replay', close: 'Close', soundOn: 'Turn sound on', soundOff: 'Mute sound',
        celebrate: 'Celebrate', winCard: 'You won a tender', open: 'Open', dismiss: 'Dismiss', history: 'Notification history', region: 'Event alerts',
        kinds: { approval: 'Approved', rejection: 'Decision update', message: 'New message', returned: 'Returned for review', action: 'Action required' }
    };
    var dir = ar ? 'rtl' : 'ltr';
    function el(tag, cls, html) { var e = document.createElement(tag); if (cls) e.className = cls; if (html != null) e.innerHTML = html; return e; }
    function text(tag, cls, s) { var e = el(tag, cls); e.textContent = s; return e; }
    var acked = {};
    function ack(id) {
        if (acked[id]) return Promise.resolve(); acked[id] = true;
        return fetch(host.dataset.ackUrl + id, { method: 'POST', credentials: 'same-origin', keepalive: true, headers: { RequestVerificationToken: host.dataset.csrf } }).catch(function () { acked[id] = false; });
    }
    function go(link, id) { ack(id).then(function () { window.location.href = link; }); }

    /* ---------- sound (opt-in, synthesized, no files) ---------- */
    var soundKey = 'orange-spotlight-sound';
    function soundOn() { try { return localStorage.getItem(soundKey) === 'on'; } catch (e) { return false; } }
    function setSound(v) { try { localStorage.setItem(soundKey, v ? 'on' : 'off'); } catch (e) { } }
    function chime() {
        if (!soundOn()) return;
        try {
            var C = window.AudioContext || window.webkitAudioContext; if (!C) return; var ctx = new C(), t0 = ctx.currentTime;
            [523.25, 659.25, 783.99, 1046.5].forEach(function (f, i) {
                var o = ctx.createOscillator(), g = ctx.createGain(); o.type = 'sine'; o.frequency.value = f;
                g.gain.setValueAtTime(0, t0 + i * .12); g.gain.linearRampToValueAtTime(.12, t0 + i * .12 + .02); g.gain.exponentialRampToValueAtTime(.0001, t0 + i * .12 + .9);
                o.connect(g).connect(ctx.destination); o.start(t0 + i * .12); o.stop(t0 + i * .12 + 1);
            });
            setTimeout(function () { ctx.close(); }, 1800);
        } catch (e) { }
    }

    /* ---------- confetti (canvas, ~3.5 s, stops when hidden) ---------- */
    function confetti(layer) {
        if (reduce) return;
        var cv = el('canvas', 'es-confetti'); cv.setAttribute('aria-hidden', 'true'); layer.appendChild(cv);
        var ctx = cv.getContext('2d'), dpr = Math.min(2, window.devicePixelRatio || 1), W = window.innerWidth, H = window.innerHeight;
        cv.width = W * dpr; cv.height = H * dpr; ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        var colors = ['#ff7900', '#ffb347', '#ffd166', '#f5c542', '#fff4d6', '#ff9a3c'], parts = [], n = W < 600 ? 110 : 220;
        for (var i = 0; i < n; i++) {
            var side = i % 2 ? 1 : -1, burst = i < n * .5;
            var a = burst ? -Math.PI / 2 + (Math.random() - .5) * 1.6 : -Math.PI / 2 - side * (.5 + Math.random() * .5), v = (burst ? 9 : 13) + Math.random() * 8;
            parts.push({ x: burst ? W / 2 : (side < 0 ? 0 : W), y: burst ? H * .42 : H * .9, vx: Math.cos(a) * v, vy: Math.sin(a) * v, r: 5 + Math.random() * 6, c: colors[i % colors.length], s: Math.random() * 6, w: .08 + Math.random() * .22, round: Math.random() < .25 });
        }
        var start = performance.now();
        (function frame(now) {
            var age = (now - start) / 1000;
            if (age > 4 || !cv.isConnected || document.hidden) { cv.remove(); return; }
            ctx.clearRect(0, 0, W, H); ctx.globalAlpha = Math.max(0, 1 - Math.max(0, age - 2.8) / 1.2);
            for (var k = 0; k < parts.length; k++) {
                var p = parts[k]; p.vy += .26; p.vx *= .988; p.vy *= .988; p.x += p.vx; p.y += p.vy; p.s += p.w;
                ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.s); ctx.fillStyle = p.c;
                if (p.round) { ctx.beginPath(); ctx.arc(0, 0, p.r * .5, 0, 6.3); ctx.fill(); } else ctx.fillRect(-p.r / 2, -p.r / 4, p.r, p.r * .5 * (.4 + Math.abs(Math.cos(p.s))));
                ctx.restore();
            }
            requestAnimationFrame(frame);
        })(start);
    }

    var TROPHY = '<svg viewBox="0 0 120 120" aria-hidden="true"><defs><linearGradient id="es-gold" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fff1b8"/><stop offset=".45" stop-color="#ffc94d"/><stop offset="1" stop-color="#ff8a00"/></linearGradient></defs>' +
        '<path d="M34 18h52v30c0 16-12 28-26 28S34 64 34 48z" fill="url(#es-gold)"/><path d="M34 26H18v10c0 14 10 20 18 20M86 26h16v10c0 14-10 20-18 20" fill="none" stroke="url(#es-gold)" stroke-width="7" stroke-linecap="round"/>' +
        '<rect x="54" y="76" width="12" height="16" rx="3" fill="#ffb02e"/><rect x="38" y="92" width="44" height="12" rx="5" fill="url(#es-gold)"/><path class="es-shine" d="M48 24c-3 10-3 22 2 32" stroke="#fff" stroke-width="5" stroke-linecap="round" fill="none" opacity=".7"/></svg>';

    /* ---------- win celebration ---------- */
    var winDialog = null;
    function celebrate(ev) {
        if (winDialog) { winDialog.remove(); }
        var d = el('dialog', 'es-win'); d.setAttribute('dir', dir); d.setAttribute('aria-labelledby', 'es-win-title'); d.setAttribute('aria-describedby', 'es-win-desc');
        if (reduce) d.classList.add('es-still');
        var stage = el('div', 'es-stage');
        var rays = el('div', 'es-rays'); rays.setAttribute('aria-hidden', 'true');
        var trophy = el('div', 'es-trophy', TROPHY);
        var h = text('h2', 'es-win-title', T.won); h.id = 'es-win-title'; h.tabIndex = -1;
        var card = el('section', 'es-card');
        card.appendChild(text('p', 'es-card-kicker', T.congrats));
        card.appendChild(text('h3', 'es-card-tender', ev.tenderTitle || ev.title));
        if (ev.tenderReference) { var r = el('p', 'es-card-ref'); r.appendChild(text('span', '', T.ref)); r.appendChild(text('strong', '', ev.tenderReference)); card.appendChild(r); }
        var desc = text('p', 'es-card-msg', ev.message || T.selected); desc.id = 'es-win-desc'; card.appendChild(desc);
        var actions = el('div', 'es-card-actions');
        var view = text('a', 'es-btn es-btn-gold', T.view); view.href = ev.link; view.addEventListener('click', function (e) { e.preventDefault(); close(false); go(ev.link, ev.id); });
        var replay = text('button', 'es-btn', T.replay); replay.type = 'button'; replay.addEventListener('click', function () { play(true); });
        var closeBtn = text('button', 'es-btn es-btn-ghost', T.close); closeBtn.type = 'button'; closeBtn.addEventListener('click', function () { close(true); });
        actions.append(view, replay, closeBtn); card.appendChild(actions);
        var x = el('button', 'es-x', '&times;'); x.type = 'button'; x.setAttribute('aria-label', T.close); x.addEventListener('click', function () { close(true); });
        var snd = el('button', 'es-sound'); snd.type = 'button';
        function syncSound() { var on = soundOn(); snd.setAttribute('aria-pressed', on ? 'true' : 'false'); snd.setAttribute('aria-label', on ? T.soundOff : T.soundOn); snd.title = snd.getAttribute('aria-label');
            snd.innerHTML = on ? '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 9h4l5-4v14l-5-4H4zM16 9a4 4 0 0 1 0 6M19 6a8 8 0 0 1 0 12"/></svg>' : '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 9h4l5-4v14l-5-4H4zM17 9l5 6M22 9l-5 6"/></svg>'; }
        snd.addEventListener('click', function () { setSound(!soundOn()); syncSound(); chime(); }); syncSound();
        stage.append(rays, trophy, h, card); d.append(x, snd, stage); document.body.appendChild(d); winDialog = d;
        d.addEventListener('cancel', function (e) { e.preventDefault(); close(true); });   // Escape
        function play(again) {
            d.classList.remove('es-play'); void d.offsetWidth; d.classList.add('es-play');
            d.querySelectorAll('.es-confetti').forEach(function (c) { c.remove(); });
            confetti(d); chime();
            if (!again) setTimeout(function () { if (d.open) h.focus({ preventScroll: true }); }, 50);
        }
        function close(record) { if (!d.open) return; d.classList.add('es-leaving'); setTimeout(function () { d.close(); d.remove(); winDialog = null; showCards(); }, reduce ? 0 : 260); if (record) ack(ev.id); var wc = document.querySelector('.es-wincard'); if (wc && record) wc.remove(); }
        d.showModal(); play(false);
    }

    /* ---------- calm cards for every other event ---------- */
    var queue = [], region = null;
    var ICON = {
        approval: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/><path class="es-draw" d="M7 12.5l3.2 3.2L17 9"/></svg>',
        rejection: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/><path d="M12 7v6M12 16.5v.5"/></svg>',
        message: '<svg viewBox="0 0 24 24"><path d="M4 5h16v11H9l-5 4z"/><path d="M8 9h8M8 12h5"/></svg>',
        returned: '<svg viewBox="0 0 24 24"><path d="M9 14L4 9l5-5"/><path d="M4 9h10a6 6 0 0 1 0 12h-3"/></svg>',
        action: '<svg viewBox="0 0 24 24"><path d="M5 16h14l-2-3V9a5 5 0 0 0-10 0v4zM10 20h4"/></svg>',
        win: '<svg viewBox="0 0 24 24"><path d="M7 3h10v7a5 5 0 0 1-10 0zM7 5H3v4c0 4 4 4 4 4M17 5h4v4c0 4-4 4-4 4M12 15v6M7 22h10"/></svg>'
    };
    function ensureRegion() {
        if (region) return region;
        region = el('section', 'es-region'); region.setAttribute('dir', dir); region.setAttribute('aria-label', T.region); region.setAttribute('aria-live', 'polite');
        document.body.appendChild(region); return region;
    }
    function card(ev, onOpen) {
        var c = el('article', 'es-ev es-ev-' + ev.kind); c.tabIndex = -1;
        var icon = el('div', 'es-ev-icon', ICON[ev.kind] || ICON.action); icon.setAttribute('aria-hidden', 'true');
        var body = el('div', 'es-ev-body');
        body.appendChild(text('p', 'es-ev-kind', ev.kind === 'win' ? T.winCard : (T.kinds[ev.kind] || '')));
        body.appendChild(text('h3', 'es-ev-title', ev.kind === 'win' ? (ev.tenderTitle || ev.title) : ev.title));
        if (ev.kind !== 'win') body.appendChild(text('p', 'es-ev-msg', ev.message));
        var row = el('div', 'es-ev-actions');
        if (onOpen) { var o = text('button', 'es-ev-open', T.celebrate); o.type = 'button'; o.addEventListener('click', onOpen); row.appendChild(o); }
        else if (ev.link) { var a = text('a', 'es-ev-open', T.open); a.href = ev.link; a.addEventListener('click', function (e) { e.preventDefault(); go(ev.link, ev.id); }); row.appendChild(a); }
        var hist = text('a', 'es-ev-hist', T.history); hist.href = host.dataset.historyUrl; row.appendChild(hist);
        body.appendChild(row);
        var x = el('button', 'es-ev-x', '&times;'); x.type = 'button'; x.setAttribute('aria-label', T.dismiss + ': ' + ev.title);
        function dismiss() { if (ev.kind !== 'win') ack(ev.id); c.classList.add('es-out'); setTimeout(function () { c.remove(); }, reduce ? 0 : 240); }
        x.addEventListener('click', dismiss);
        c.addEventListener('keydown', function (e) { if (e.key === 'Escape') { e.stopPropagation(); dismiss(); } });
        c.append(icon, body, x);
        return c;
    }
    function showCards() {
        if (winDialog || !queue.length) return;
        var r = ensureRegion();
        queue.splice(0).forEach(function (ev, i) { var c = card(ev); c.style.setProperty('--i', i); r.appendChild(c); });
    }

    function isDashboard() {
        var p = window.location.pathname.replace(/\/+$/, '').split('/').filter(Boolean);
        return p.length <= 1 || /^dashboard$/i.test(p[1]) && (p.length === 2 || /^index$/i.test(p[2] || ''));
    }

    var started = false;
    function start() {
        if (started) return; started = true;
        fetch(host.dataset.pendingUrl, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
            .then(function (r) { return r.ok && (r.headers.get('content-type') || '').indexOf('json') > -1 ? r.json() : []; })
            .then(function (events) {
                if (!Array.isArray(events) || !events.length) return;
                var win = events.filter(function (e) { return e.kind === 'win' && e.link; })[0];
                // Ordinary events live only in the navbar bell (owner decision): no corner cards are shown.
                // Only a verified tender win is celebrated, once, on the dashboard.
                queue = [];
                if (win && isDashboard()) celebrate(win);
            }).catch(function () { });
    }
    // Wait until the page is idle so the first paint and critical scripts are never delayed.
    if (document.readyState === 'complete') setTimeout(start, 400); else window.addEventListener('load', function () { setTimeout(start, 400); });
    window.OrangeSpotlight = { refresh: function () { started = false; start(); } };
})();
