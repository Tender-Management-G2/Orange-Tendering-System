(function () {
    'use strict';
    // Wizard shell of the tender workflow popup. Navigation (Back / Continue / stage pills) only moves between stages;
    // every real workflow action is an ordinary form inside a stage and posts to the same endpoints as before.
    function key(root) { return 'wz-stage-' + root.dataset.tender; }
    function setup(root) {
        if (root.dataset.wzReady) return;
        root.dataset.wzReady = 'true';
        var panels = Array.prototype.slice.call(root.querySelectorAll('[data-wz-panel]'));
        var total = Number(root.dataset.wzTotal || 1);
        if (!panels.length) { return; }
        var server = Number(root.dataset.wzCurrent), current = server;
        try { var saved = JSON.parse(sessionStorage.getItem(key(root)) || 'null'); if (saved && saved.server === server) current = saved.view; } catch (e) { /* storage may be unavailable */ }
        var back = root.querySelector('[data-wz-back]'), next = root.querySelector('[data-wz-next]'), stepNo = root.querySelector('[data-wz-stepno]'), dots = root.querySelectorAll('.wz-dots i');
        var hint = root.querySelector('[data-wz-lockhint]'), body = root.querySelector('.wz-body');
        function open(n) { var b = root.querySelector('[data-wz-go="' + n + '"]'); return !!b && !b.disabled; }
        if (!open(current)) current = server;

        function show(n, animate) {
            current = n;
            panels.forEach(function (p) { var on = Number(p.dataset.wzPanel) === n; p.classList.toggle('is-current', on); if (on && animate) { p.classList.remove('is-enter'); void p.offsetWidth; p.classList.add('is-enter'); } });
            root.querySelectorAll('[data-wz-go]').forEach(function (b) { var on = Number(b.dataset.wzGo) === n; b.classList.toggle('is-view', on); if (on) b.setAttribute('aria-current', 'step'); else b.removeAttribute('aria-current'); });
            if (stepNo) stepNo.textContent = stepNo.textContent.replace(/\d+/, String(n));
            dots.forEach(function (d, i) { d.classList.toggle('is-on', i < n); });
            if (back) back.disabled = n <= 1;
            if (next) next.disabled = n >= total || !open(n + 1);
            // A locked "Continue" always says why (the same reason is on the locked stage pill).
            var nextPill = root.querySelector('[data-wz-go="' + (n + 1) + '"]');
            var reason = next && next.disabled && nextPill && nextPill.dataset.wzReason ? '🔒 ' + nextPill.dataset.wzReason : '';
            if (hint) hint.textContent = reason;
            if (next) next.title = reason;
            try { sessionStorage.setItem(key(root), JSON.stringify({ server: server, view: n })); } catch (e) { /* ignore */ }
        }
        root.addEventListener('click', function (e) {
            var go = e.target.closest('[data-wz-go]'); if (go && !go.disabled) { show(Number(go.dataset.wzGo), true); return; }
            if (e.target.closest('[data-wz-next]') && open(current + 1)) show(current + 1, true);
            else if (e.target.closest('[data-wz-back]') && current > 1) show(current - 1, true);
        });

        // The closing date is chosen inside the Publish stage and feeds its hidden UTC field. Publish stays disabled until the date is valid
        // (the stage itself only opens once every earlier stage is complete).
        var deadline = root.querySelector('input[data-wz-deadline]'), utc = root.querySelector('input[data-wz-deadline-utc]'), echo = root.querySelector('[data-wz-deadline-echo]');
        var publish = root.querySelector('[data-wz-publish]'), publishHint = root.querySelector('[data-wz-publish-hint]'), badHint = root.querySelector('[data-wz-deadline-error]');
        function sync() {
            if (!deadline) return;
            var has = !!deadline.value, instant = has ? new Date(deadline.value + (deadline.value.length === 16 ? ':00' : '') + '+03:00') : null; // the system works in Jordan time (UTC+3)
            var valid = !!instant && !isNaN(instant) && instant.getTime() >= Date.now() + 3600 * 1000;
            if (echo) echo.textContent = has ? new Date(deadline.value).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : '\u2014';
            if (utc) utc.value = valid ? instant.toISOString() : '';
            if (publish) publish.disabled = !valid;
            if (publishHint) publishHint.hidden = has;
            if (badHint) badHint.hidden = !has || valid;
        }
        if (deadline) {
            ['input', 'change'].forEach(function (n) { deadline.addEventListener(n, sync); });
            // Never let Enter in the date field (or any other path) post the Publish form without a valid closing date.
            var publishForm = publish && publish.form;
            if (publishForm) publishForm.addEventListener('submit', function (e) { sync(); if (publish.disabled || !utc.value) { e.preventDefault(); e.stopImmediatePropagation(); } }, true);
            root.addEventListener('change', function (e) {
                var pick = e.target.closest('[data-wz-preset]'); if (!pick || !pick.value) return;
                // N days from now, at the same time of day as now in Jordan time (rounded up to 5 minutes); the field stays editable.
                var d = new Date(Date.now() + Number(pick.value) * 86400000); d.setMinutes(Math.ceil(d.getMinutes() / 5) * 5, 0, 0);
                var parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Amman', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(d), p = {};
                parts.forEach(function (x) { p[x.type] = x.value; });
                var value = p.year + '-' + p.month + '-' + p.day + 'T' + p.hour + ':' + p.minute;
                if (deadline.max && value > deadline.max) value = deadline.max; // only while the department's needed-by date is still ahead
                deadline.value = value;
                sync();
            });
            sync();
        }
        show(current, false);
    }
    function init() {
        document.querySelectorAll('.wz[data-wz]').forEach(setup);
    }
    // Manual supplier selection lives inside the Publish stage: it replaces the summary until the Officer goes back.
    document.addEventListener('click', function (e) {
        var openBtn = e.target.closest('[data-wz-picker-open]'), closeBtn = e.target.closest('[data-wz-picker-close]');
        if (!openBtn && !closeBtn) return;
        var main = (openBtn || closeBtn).closest('[data-wz-publish-main]'); if (!main) return;
        var summary = main.querySelector('[data-wz-summary]'), picker = main.querySelector('[data-wz-picker]');
        summary.hidden = !!openBtn; picker.hidden = !openBtn;
        var root = main.closest('.wz'); if (root) root.classList.toggle('wz-picking', !!openBtn);
        window.dispatchEvent(new Event('resize'));
    });
    init();
    new MutationObserver(function () { init(); }).observe(document.body, { childList: true, subtree: true });
}());
