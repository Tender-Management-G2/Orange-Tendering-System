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
        var hint = root.querySelector('[data-wz-lockhint]'), nextText = root.querySelector('[data-wz-next-text]');
        var invitations = root.querySelector('[data-wz-invitations]');
        var summary = invitations && invitations.querySelector('[data-wz-summary]');
        var picker = invitations && invitations.querySelector('[data-wz-picker]');
        function togglePicker(open) {
            if (!picker || !summary) return;
            summary.hidden = open;
            picker.hidden = !open;
            root.classList.toggle('wz-picking', open);
            window.dispatchEvent(new Event('resize'));
        }
        function open(n) { var b = root.querySelector('[data-wz-go="' + n + '"]'); return !!b && !b.disabled; }
        if (!open(current)) current = server;

        function show(n, animate) {
            if (n !== current && picker && !picker.hidden) togglePicker(false);
            current = n;
            var panel = panels.find(function (p) { return Number(p.dataset.wzPanel) === n; });
            panels.forEach(function (p) { var on = Number(p.dataset.wzPanel) === n; p.classList.toggle('is-current', on); if (on && animate) { p.classList.remove('is-enter'); void p.offsetWidth; p.classList.add('is-enter'); } });
            root.querySelectorAll('[data-wz-go]').forEach(function (b) { var on = Number(b.dataset.wzGo) === n; b.classList.toggle('is-view', on); if (on) b.setAttribute('aria-current', 'step'); else b.removeAttribute('aria-current'); });
            if (stepNo) stepNo.textContent = stepNo.textContent.replace(/\d+/, String(n));
            dots.forEach(function (d, i) { d.classList.toggle('is-on', i < n); });
            if (back) back.disabled = n <= 1;
            if (next) next.disabled = n >= total || !open(n + 1);
            if (nextText && next) nextText.textContent = panel && panel.dataset.wzNextLabel || next.dataset.wzLabel;
            // A locked "Continue" always says why (the same reason is on the locked stage pill).
            var nextPill = root.querySelector('[data-wz-go="' + (n + 1) + '"]');
            var reason = next && next.disabled && nextPill && nextPill.dataset.wzReason ? '🔒 ' + nextPill.dataset.wzReason : '';
            if (hint) hint.textContent = reason;
            if (next) next.title = reason;
            try { sessionStorage.setItem(key(root), JSON.stringify({ server: server, view: n })); } catch (e) { /* ignore */ }
        }
        root.addEventListener('click', function (e) {
            var go = e.target.closest('[data-wz-go]'); if (go && !go.disabled) { show(Number(go.dataset.wzGo), true); return; }
            var jump = e.target.closest('[data-wz-jump]');
            if (jump && open(Number(jump.dataset.wzJump))) { show(Number(jump.dataset.wzJump), true); return; }
            if (e.target.closest('[data-wz-picker-open]')) { togglePicker(true); return; }
            if (e.target.closest('[data-wz-picker-close]')) { togglePicker(false); return; }
            if (e.target.closest('[data-wz-next]') && open(current + 1)) show(current + 1, true);
            else if (e.target.closest('[data-wz-back]') && current > 1) show(current - 1, true);
        });

        var deadline = root.querySelector('input[data-wz-deadline]');
        var utc = root.querySelector('input[data-wz-deadline-utc]');
        var openBidding = root.querySelector('[data-wz-open-bidding]');
        var badHint = root.querySelector('[data-wz-deadline-error]');
        function sync() {
            var value = deadline.value;
            // The input is Jordan time. Submit a UTC instant regardless of the browser's local time zone.
            var instant = value ? new Date(value + (value.length === 16 ? ':00' : '') + '+03:00') : null;
            var earliest = Date.now() + Number(deadline.dataset.wzLeadHours) * 3600000;
            var valid = !!instant && instant.getTime() > earliest && deadline.validity.valid;
            utc.value = valid ? instant.toISOString() : '';
            openBidding.disabled = !valid;
            badHint.hidden = !value || valid;
        }
        if (deadline) {
            ['input', 'change'].forEach(function (n) { deadline.addEventListener(n, sync); });
            root.addEventListener('change', function (e) {
                var pick = e.target.closest('[data-wz-preset]'); if (!pick || !pick.value) return;
                // N days from now, at the same time of day as now in Jordan time (rounded up to 5 minutes); the field stays editable.
                var d = new Date(Date.now() + Number(pick.value) * 86400000); d.setMinutes(Math.ceil(d.getMinutes() / 5) * 5, 0, 0);
                var parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Amman', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(d), p = {};
                parts.forEach(function (x) { p[x.type] = x.value; });
                var value = p.year + '-' + p.month + '-' + p.day + 'T' + p.hour + ':' + p.minute;
                if (deadline.max && value > deadline.max) value = deadline.max;
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
    init();
    new MutationObserver(function () { init(); }).observe(document.body, { childList: true, subtree: true });
}());
