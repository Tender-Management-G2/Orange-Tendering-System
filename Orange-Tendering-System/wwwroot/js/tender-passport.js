// Tender Passport behaviour (Views/Shared/Components/TenderPassport/Default.cshtml).
// The tender popup is re-rendered with innerHTML, so everything is wired by delegation on the document.
// One popover per passport: hovering, focusing or tapping a sticker shows that sticker, the package shows the summary.
(function () {
    'use strict';
    var HIDE_DELAY = 180;
    var finePointer = window.matchMedia && window.matchMedia('(hover: hover) and (pointer: fine)').matches;

    function root(el) { return el && el.closest ? el.closest('[data-tp]') : null; }
    function trigger(el) { return el && el.closest ? el.closest('[data-tp-tip]') : null; }
    function pop(tp) { return tp.querySelector('.tp-pop'); }

    function show(tp, trig, pinned) {
        var p = pop(tp), body = p.querySelector('.tp-pop-body'), id = trig.getAttribute('data-tp-tip');
        clearTimeout(tp._hide);
        var same = tp._trig === trig;
        if (!same) {
            var tpl = tp.querySelector('template[data-tp-tpl="' + id + '"]');
            if (!tpl) return;
            body.textContent = '';
            body.appendChild(tpl.content.cloneNode(true));
            localTimes(body);
            if (tp._trig) { tp._trig.classList.remove('is-active'); tp._trig.setAttribute('aria-expanded', 'false'); }
            tp._trig = trig;
        }
        tp._pinned = !!pinned || tp._pinned && same;
        trig.classList.add('is-active');
        trig.setAttribute('aria-expanded', 'true');
        tp.querySelector('.tp-box').classList.toggle('is-active', true);
        p.setAttribute('aria-label', trig.classList.contains('tp-hit') ? 'Tender Passport summary' : (trig.getAttribute('aria-label') || 'Sticker details'));
        // Point the arrow at the hovered sticker (or the package centre).
        var tr = trig.getBoundingClientRect();
        p.hidden = false;
        var pr = p.getBoundingClientRect();
        var x = tr.left + tr.width / 2 - pr.left;
        p.style.setProperty('--tp-arrow', Math.max(18, Math.min(pr.width - 18, x)) + 'px');
        requestAnimationFrame(function () { p.classList.add('is-in'); });
    }
    function hide(tp, now) {
        clearTimeout(tp._hide);
        var go = function () {
            var p = pop(tp);
            p.classList.remove('is-in');
            tp._pinned = false;
            if (tp._trig) { tp._trig.classList.remove('is-active'); tp._trig.setAttribute('aria-expanded', 'false'); }
            tp._trig = null;
            tp.querySelector('.tp-box').classList.remove('is-active');
            setTimeout(function () { if (!p.classList.contains('is-in')) p.hidden = true; }, 200);
        };
        if (now) go(); else tp._hide = setTimeout(function () { if (!tp._pinned) go(); }, HIDE_DELAY);
    }
    function hideAll(except) {
        document.querySelectorAll('[data-tp]').forEach(function (tp) { if (tp !== except && tp._trig) hide(tp, true); });
    }

    // Times in the popovers are already server-formatted in Jordan time; add an exact tooltip.
    function localTimes(scope) {
        scope.querySelectorAll('time[datetime]').forEach(function (t) {
            var d = new Date(t.getAttribute('datetime'));
            if (!isNaN(d) && !t.title) t.title = d.toLocaleString('en-GB', { timeZone: 'Asia/Amman', dateStyle: 'full', timeStyle: 'medium' }) + ' (Jordan time)';
        });
    }

    // ---------- Pointer ----------
    document.addEventListener('pointerover', function (e) {
        if (e.pointerType && e.pointerType !== 'mouse') return;
        var tp = root(e.target); if (!tp) return;
        var trig = trigger(e.target);
        if (trig && !tp._pinned) { hideAll(tp); show(tp, trig, false); }
        else clearTimeout(tp._hide);
    });
    document.addEventListener('pointerout', function (e) {
        if (e.pointerType && e.pointerType !== 'mouse') return;
        var tp = root(e.target); if (!tp || tp._pinned) return;
        if (!root(e.relatedTarget) || root(e.relatedTarget) !== tp) hide(tp, false);
    });
    document.addEventListener('click', function (e) {
        var tp = root(e.target);
        if (e.target.closest && e.target.closest('[data-tp-trail]')) { e.preventDefault(); if (tp) openTrail(tp); return; }
        if (e.target.closest && e.target.closest('[data-tp-trail-close]')) { var d = e.target.closest('dialog'); if (d) d.close(); return; }
        if (e.target.matches && e.target.matches('dialog.tp-trail')) { e.target.close(); return; } // backdrop click
        if (!tp) { hideAll(null); return; }
        var trig = trigger(e.target);
        if (!trig) return;
        // A second tap/click on the open item closes it; otherwise pin it open.
        if (tp._trig === trig && tp._pinned) hide(tp, true); else { hideAll(tp); show(tp, trig, true); }
    });

    // ---------- Keyboard ----------
    document.addEventListener('focusin', function (e) {
        var tp = root(e.target), trig = trigger(e.target);
        if (tp && trig && !tp._pinned) { hideAll(tp); show(tp, trig, false); }
    });
    document.addEventListener('focusout', function (e) {
        var tp = root(e.target); if (!tp) return;
        if (!root(e.relatedTarget) && !tp._pinned) hide(tp, false);
    });
    document.addEventListener('keydown', function (e) {
        var tp = root(e.target); if (!tp) return;
        if (e.key === 'Escape' && tp._trig && !e.target.closest('dialog.tp-trail')) { var t = tp._trig; hide(tp, true); t.focus(); e.stopPropagation(); e.preventDefault(); return; }
        var trig = trigger(e.target);
        if (trig && /^Arrow(Left|Right|Up|Down)$/.test(e.key)) {
            var items = Array.prototype.slice.call(tp.querySelectorAll('.tp-hit, .tp-box .tp-st'));
            var i = items.indexOf(trig.closest('.tp-st') || trig), step = e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 1;
            if (i > -1) { e.preventDefault(); items[(i + step + items.length) % items.length].focus(); }
        }
    }, true);
    // Escape inside the popup dialog would close the whole tender dialog; close only the open popover first.
    document.addEventListener('cancel', function (e) {
        var open = Array.prototype.find.call(document.querySelectorAll('[data-tp]'), function (tp) { return !!tp._trig; });
        if (open && e.target.classList && e.target.classList.contains('wf-dialog')) { e.preventDefault(); hide(open, true); }
    }, true);

    // ---------- Full Activity Trail ----------
    function openTrail(tp) {
        var dlg = tp.querySelector('dialog.tp-trail'), url = tp.getAttribute('data-trail');
        if (!dlg || !url) return;
        hide(tp, true);
        if (!dlg.open) dlg.showModal();
        if (dlg._loaded) return;
        var body = dlg.querySelector('[data-tp-trail-body]');
        fetch(url, { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.text(); })
            .then(function (html) { body.innerHTML = html; dlg._loaded = true; localTimes(body); wireFilters(body); })
            .catch(function () { body.innerHTML = '<p class="tp-trail-empty"><b>The activity trail could not be loaded.</b>You may no longer have access to this tender, or the server could not be reached.</p>'; });
    }
    function wireFilters(scope) {
        var bar = scope.querySelector('[data-tp-filters]'); if (!bar) return;
        var list = scope.querySelector('[data-tp-journey]'), none = scope.querySelector('[data-tp-none]'), count = scope.querySelector('[data-tp-count]');
        var rows = Array.prototype.slice.call(list.children), total = rows.length;
        var f = function (n) { return bar.querySelector('[data-f="' + n + '"]'); };
        var apply = function () {
            var kind = f('kind').value, actor = f('actor').value, from = f('from').value, to = f('to').value, shown = 0;
            rows.forEach(function (li) {
                var ok = (!kind || li.dataset.kind === kind) && (!actor || li.dataset.actor === actor) && (!from || li.dataset.date >= from) && (!to || li.dataset.date <= to);
                li.hidden = !ok; if (ok) shown++;
            });
            none.hidden = shown > 0;
            count.textContent = (shown === total ? total : shown + ' of ' + total) + ' recorded event(s) · times in Jordan time';
        };
        ['kind', 'actor', 'from', 'to'].forEach(function (n) { f(n).addEventListener('input', apply); f(n).addEventListener('change', apply); });
        f('order').addEventListener('click', function () {
            var desc = list.classList.toggle('is-desc');
            f('order').setAttribute('aria-pressed', desc ? 'true' : 'false');
            f('order').textContent = desc ? 'Oldest first' : 'Newest first';
        });
        f('reset').addEventListener('click', function () { f('kind').value = ''; f('actor').value = ''; f('from').value = ''; f('to').value = ''; apply(); });
    }

    // ---------- Sticker placement for an action the viewer just completed (once per action) ----------
    function placeFresh() {
        document.querySelectorAll('[data-tp] .tp-st[data-fresh]').forEach(function (st) {
            var key = 'tp-fresh-' + st.getAttribute('data-fresh'), seen = false;
            try { seen = sessionStorage.getItem(key) === '1'; sessionStorage.setItem(key, '1'); } catch (_) { seen = true; }
            if (!seen) { st.classList.add('is-placing'); setTimeout(function () { st.classList.remove('is-placing'); }, 900); }
            st.removeAttribute('data-fresh');
        });
    }
    new MutationObserver(function () { if (document.querySelector('[data-tp] .tp-st[data-fresh]')) placeFresh(); })
        .observe(document.documentElement, { childList: true, subtree: true });
    if (document.readyState !== 'loading') placeFresh(); else document.addEventListener('DOMContentLoaded', placeFresh);
    if (!finePointer) document.documentElement.classList.add('tp-touch');
})();
