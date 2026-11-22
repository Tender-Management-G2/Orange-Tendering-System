// Orange Motion: the application's single action-feedback system (docs/ORANGE_MOTION.md).
// One SVG orange with citrus segments, a leaf and a highlight. It processes while the server works, assembles,
// merges into the whole fruit and shows what happened. Feedback is only ever shown for a result the server reported.
//
//   OrangeMotion.show(feedback)                    play a finished server result (popup, toast or inline)
//   OrangeMotion.begin({ mode, title, ... })       start processing; then handle.finish(feedback) / handle.fail(message)
//   OrangeMotion.submit(form, { mode })            send a form with fetch, play the server's feedback, then follow its redirect
//   <form data-om="popup|toast">                   the same, automatically
//   feedback = { title, description, variant, mode, approvals, required, complete }
//   variants: save submit send upload approve return error committee award      modes: popup toast inline
(() => {
    // Global timing (ms) and presentation settings. Adjust here; nothing else hard-codes durations.
    const config = {
        enter: 150, assemble: 240, transform: 340, exit: 180,
        holdPopup: 950, holdToast: 2400, holdError: 0,
        toastDelay: 140,          // a fast operation shows no processing toast at all
        requestTimeout: 120000,
        defaults: {
            save: 'toast', submit: 'popup', send: 'popup', upload: 'inline', approve: 'popup',
            return: 'popup', error: 'popup', committee: 'popup', award: 'popup'
        }
    };

    const NS = 'http://www.w3.org/2000/svg';
    const C = { x: 100, y: 112 }, R = 58;
    const EASE_OUT = 'cubic-bezier(.22,1,.36,1)', EASE_IN_OUT = 'cubic-bezier(.65,0,.35,1)';
    const reduced = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const wait = ms => new Promise(resolve => window.setTimeout(resolve, ms));
    const VARIANTS = ['save', 'submit', 'send', 'upload', 'approve', 'return', 'error', 'committee', 'award'];

    function svgEl(name, attrs, parent) {
        const node = document.createElementNS(NS, name);
        for (const [key, value] of Object.entries(attrs || {})) node.setAttribute(key, value);
        if (parent) parent.appendChild(node);
        return node;
    }

    function segmentPath(count) {
        const r0 = 8, r1 = 45, outerHalf = (180 / count - 3.5) * Math.PI / 180, innerHalf = (180 / count) * .57 * Math.PI / 180;
        const p = (r, a) => `${(C.x + r * Math.sin(a)).toFixed(2)} ${(C.y - r * Math.cos(a)).toFixed(2)}`;
        return `M${p(r0, -innerHalf)} L${p(r1, -outerHalf)} A${r1} ${r1} 0 0 1 ${p(r1, outerHalf)} L${p(r0, innerHalf)} A${r0} ${r0} 0 0 0 ${p(r0, -innerHalf)}Z`;
    }

    const SYMBOLS = {
        check: [`M${C.x - 25} ${C.y + 1} L${C.x - 7} ${C.y + 19} L${C.x + 27} ${C.y - 17}`],
        cross: [`M${C.x - 19} ${C.y - 19} L${C.x + 19} ${C.y + 19}`, `M${C.x + 19} ${C.y - 19} L${C.x - 19} ${C.y + 19}`],
        plane: ['M72 114 L128 90 L110 136 L98 118 Z', 'M98 118 L128 90'],
        upload: ['M100 134 V91', 'M83 107 L100 90 L117 107'],
        return: ['M122 128 C122 106 110 98 92 98 H80', 'M91 86 L79 98 L91 110'],
        trophy: ['M84 90 H116 V102 C116 116 109 123 100 123 C91 123 84 116 84 102 Z', 'M84 96 C73 96 73 111 85 111', 'M116 96 C127 96 127 111 115 111', 'M100 123 V133', 'M89 136 H111']
    };

    // One orange: the same object processes, assembles, merges and shows the result.
    class Fruit {
        constructor(host, segments) {
            this.count = segments;
            this.loops = [];
            const svg = this.svg = svgEl('svg', { viewBox: '0 0 200 200', class: 'om-svg', 'aria-hidden': 'true', focusable: 'false' });
            const ripples = svgEl('g', {}, svg);
            this.ripples = [0, 1, 2].map(() => svgEl('circle', { cx: C.x, cy: C.y, r: R, class: 'om-ripple' }, ripples));
            const fruit = this.fruit = svgEl('g', { class: 'om-fruit' }, svg);
            const leaf = this.leaf = svgEl('g', { class: 'om-leaf' }, fruit);
            svgEl('path', { class: 'om-stem', d: `M${C.x} ${C.y - R + 3} C${C.x} ${C.y - R - 4} ${C.x + 1} ${C.y - R - 8} ${C.x + 3} ${C.y - R - 11}` }, leaf);
            svgEl('path', { class: 'om-leaf-blade', d: `M${C.x + 2} ${C.y - R - 9} C${C.x + 10} ${C.y - R - 24} ${C.x + 26} ${C.y - R - 25} ${C.x + 35} ${C.y - R - 19} C${C.x + 27} ${C.y - R - 7} ${C.x + 12} ${C.y - R - 3} ${C.x + 2} ${C.y - R - 9}Z` }, leaf);
            svgEl('path', { class: 'om-leaf-vein', d: `M${C.x + 6} ${C.y - R - 10} C${C.x + 15} ${C.y - R - 15} ${C.x + 23} ${C.y - R - 17} ${C.x + 31} ${C.y - R - 18}` }, leaf);
            svgEl('circle', { cx: C.x, cy: C.y, r: R, class: 'om-rind' }, fruit);
            this.pith = svgEl('circle', { cx: C.x, cy: C.y, r: R - 6, class: 'om-pith' }, fruit);
            this.segs = svgEl('g', { class: 'om-segs' }, fruit);
            const d = segmentPath(segments);
            this.segments = Array.from({ length: segments }, (_, i) => {
                const slot = svgEl('g', { transform: `rotate(${i * 360 / segments} ${C.x} ${C.y})` }, this.segs);
                const move = svgEl('g', { class: 'om-seg-move' }, slot);
                return { move, shape: svgEl('path', { d, class: 'om-seg' }, move) };
            });
            this.core = svgEl('circle', { cx: C.x, cy: C.y, r: 4.5, class: 'om-core' }, fruit);
            this.highlight = svgEl('path', { class: 'om-highlight', d: `M${C.x - 41} ${C.y - 12} A${R - 14} ${R - 14} 0 0 1 ${C.x - 14} ${C.y - 41}`, pathLength: 1 }, fruit);
            this.symbols = svgEl('g', { class: 'om-symbols' }, fruit);
            host.appendChild(svg);
        }
        process() {
            if (reduced()) { this.loops.push(this.segs.animate([{ opacity: 1 }, { opacity: .6 }, { opacity: 1 }], { duration: 1400, iterations: Infinity })); return; }
            this.loops.push(this.segs.animate([{ transform: 'rotate(0deg)' }, { transform: 'rotate(360deg)' }], { duration: 6000, iterations: Infinity }));
            this.segments.forEach(({ move }, i) => this.loops.push(move.animate([
                { transform: 'translateY(0) rotate(0deg)', opacity: 1 },
                { transform: 'translateY(-5px) rotate(4deg)', opacity: .5, offset: .45 },
                { transform: 'translateY(0) rotate(0deg)', opacity: 1 }
            ], { duration: 1050, delay: i * 1050 / this.count, iterations: Infinity, easing: EASE_IN_OUT })));
        }
        settle() {
            const spin = this.loops[0];
            const angle = !this.loops.length || reduced() ? 0 : ((spin.currentTime || 0) % 6000) / 6000 * 360;
            this.loops.forEach(a => { try { a.commitStyles(); } catch { } a.cancel(); });
            this.loops = [];
            return angle;
        }
        // Segments drift outward, turn and click back into one complete cross-section.
        assemble(angle) {
            if (reduced()) return Promise.resolve();
            const step = 360 / this.count, target = Math.ceil((angle + 1) / step) * step + step;
            const t = config.assemble;
            const turn = this.segs.animate([{ transform: `rotate(${angle}deg)` }, { transform: `rotate(${target}deg)` }], { duration: t, easing: EASE_OUT, fill: 'forwards' });
            const moves = this.segments.map(({ move }, i) => {
                const from = getComputedStyle(move);
                return move.animate([
                    { transform: from.transform === 'none' ? 'translateY(0) rotate(0deg) scale(1)' : from.transform, opacity: from.opacity },
                    { transform: 'translateY(-9px) rotate(12deg) scale(.93)', opacity: .92, offset: .45 },
                    { transform: 'translateY(0) rotate(0deg) scale(1)', opacity: 1 }
                ], { duration: t, delay: i * 12, easing: EASE_IN_OUT, fill: 'forwards' });
            });
            return Promise.all([turn.finished, ...moves.map(m => m.finished)]);
        }
        // Segments collapse inward and fuse with the rind into the solid fruit.
        merge(duration) {
            const t = reduced() ? 120 : duration;
            const shapes = this.segments.map(({ shape }) => shape.animate([
                { transform: 'scale(1)', fill: getComputedStyle(shape).fill, stroke: getComputedStyle(shape).stroke, opacity: 1 },
                { transform: 'scale(.92)', fill: '#FF8A1E', stroke: '#FF8A1E', opacity: 1, offset: .3 },
                { transform: 'scale(1.17)', fill: '#FF7900', stroke: '#FF7900', strokeWidth: '9', opacity: 1, offset: .8 },
                { transform: 'scale(1.17)', fill: '#FF7900', stroke: '#FF7900', strokeWidth: '9', opacity: 0 }
            ], { duration: t, easing: EASE_IN_OUT, fill: 'forwards' }));
            const pith = this.pith.animate([{ opacity: 1, transform: 'scale(1)' }, { opacity: 0, transform: 'scale(.55)' }], { duration: t * .75, easing: EASE_IN_OUT, fill: 'forwards' });
            const core = this.core.animate([{ opacity: 1 }, { opacity: 0 }], { duration: t * .4, fill: 'forwards' });
            if (!reduced()) this.fruit.animate([{ transform: 'scale(1)' }, { transform: 'scale(.965)', offset: .4 }, { transform: 'scale(1)' }], { duration: t, easing: EASE_IN_OUT });
            this.highlight.animate([{ strokeDashoffset: 1, opacity: 0 }, { strokeDashoffset: 0, opacity: .9 }], { duration: reduced() ? 1 : t + 120, delay: reduced() ? 0 : t * .5, easing: EASE_OUT, fill: 'forwards' });
            return Promise.all([...shapes.map(s => s.finished), pith.finished, core.finished]);
        }
        draw(name, duration, delay = 0) {
            const paths = SYMBOLS[name].map(d => svgEl('path', { d, class: 'om-mark', pathLength: 1 }, this.symbols));
            const each = paths.length > 1 ? duration / (1 + (paths.length - 1) * .45) : duration;
            return Promise.all(paths.map((p, i) => p.animate([{ strokeDashoffset: 1, opacity: 1 }, { strokeDashoffset: 0, opacity: 1 }],
                { duration: reduced() ? 1 : each, delay: reduced() ? 0 : delay + i * each * .45, easing: EASE_OUT, fill: 'forwards' }).finished)).then(() => paths);
        }
        // A transitional symbol (paper plane, upload arrow) leaves before the checkmark arrives.
        async lift(name, dx, dy) {
            const paths = await this.draw(name, config.transform * .45);
            if (reduced()) { paths.forEach(p => p.remove()); return; }
            await Promise.all(paths.map(p => p.animate([{ transform: 'translate(0,0) scale(1)', opacity: 1 }, { transform: `translate(${dx}px,${dy}px) scale(.7)`, opacity: 0 }],
                { duration: config.transform * .45, easing: 'cubic-bezier(.5,0,.75,0)', fill: 'forwards' }).finished));
            paths.forEach(p => p.remove());
        }
        spring() {
            if (reduced()) return Promise.resolve();
            this.leaf.animate([{ transform: 'rotate(0)' }, { transform: 'rotate(-9deg)' }, { transform: 'rotate(4deg)' }, { transform: 'rotate(0)' }], { duration: 520, easing: 'ease-out' });
            return this.fruit.animate([{ transform: 'scale(1)' }, { transform: 'scale(1.08)', offset: .35 }, { transform: 'scale(.98)', offset: .7 }, { transform: 'scale(1)' }], { duration: 380, easing: 'ease-out' }).finished;
        }
        ripple(rings) {
            if (reduced()) return;
            this.ripples.slice(0, rings).forEach((node, i) => node.animate([
                { transform: 'scale(1)', opacity: .55, strokeWidth: '2.4' }, { transform: `scale(${rings > 2 ? 1.85 : 1.6})`, opacity: 0, strokeWidth: '.6' }
            ], { duration: 900, delay: i * 200, easing: EASE_OUT }));
        }
        shake() {
            if (reduced()) return;
            this.fruit.animate([{ transform: 'translateX(0)' }, { transform: 'translateX(-7px)' }, { transform: 'translateX(6px)' }, { transform: 'translateX(-4px)' }, { transform: 'translateX(2px)' }, { transform: 'translateX(0)' }], { duration: 420, easing: 'ease-in-out' });
        }
        // Committee: one segment per member; lit segments are the approvals recorded for this round.
        light(approvals) {
            this.segments.forEach(({ shape }, i) => shape.classList.toggle('is-pending', i >= approvals));
            if (reduced() || approvals < 1) return Promise.resolve();
            const newest = this.segments[Math.min(approvals, this.count) - 1].move;
            return newest.animate([{ transform: 'translateY(0) scale(1)' }, { transform: 'translateY(-6px) scale(1.06)' }, { transform: 'translateY(0) scale(1)' }], { duration: 360, easing: EASE_OUT }).finished;
        }
        stop() { this.loops.forEach(a => a.cancel()); this.loops = []; this.svg.getAnimations({ subtree: true }).forEach(a => a.cancel()); }
    }

    function createLoadingFruit(host) {
        const fruit = new Fruit(host, 4);
        if (!reduced()) fruit.process();
        return {
            assemble: () => fruit.assemble(fruit.settle()),
            stop: () => fruit.stop()
        };
    }

    function normalize(feedback, fallbackMode) {
        const f = Object.assign({}, feedback || {});
        f.variant = VARIANTS.includes(String(f.variant).toLowerCase()) ? String(f.variant).toLowerCase() : 'save';
        f.mode = String(f.mode || fallbackMode || config.defaults[f.variant] || 'toast').toLowerCase();
        if (f.variant === 'committee') {
            f.required = Number(f.required) || 4;
            f.approvals = Math.max(0, Math.min(f.required, Number(f.approvals) || 0));
            f.complete = f.complete === true && f.approvals >= f.required;
        }
        return f;
    }

    // Plays the result on a fruit that may still be processing. Returns when the symbol is in place.
    async function playResult(fruit, f, setCopy) {
        const angle = fruit.settle();
        await fruit.assemble(angle);
        const t = config.transform;
        if (f.variant === 'error') {
            await fruit.merge(t * .6);
            setCopy(f);
            await fruit.draw('cross', t * .55);
            fruit.shake();
            return;
        }
        if (f.variant === 'committee' && !f.complete) {
            setCopy(f);
            await fruit.light(f.approvals);
            return;
        }
        if (f.variant === 'committee') await fruit.light(f.required);
        const merging = fruit.merge(t * .6);
        setCopy(f);
        if (f.variant === 'send') { await merging; await fruit.lift('plane', 26, -26); }
        else if (f.variant === 'upload') { await merging; await fruit.lift('upload', 0, -24); }
        else if (f.variant === 'return') { await wait(reduced() ? 0 : t * .35); await fruit.draw('return', t * .65); await merging; return; }
        else await wait(reduced() ? 0 : t * .35);
        await fruit.draw(f.variant === 'award' ? 'trophy' : 'check', f.variant === 'award' ? t * .8 : t * .65);
        await merging;
        await fruit.spring();
        fruit.ripple(f.variant === 'award' ? 3 : f.variant === 'committee' ? 2 : 1);
    }

    // ---------- Popup (important operations) ----------
    let activePopup = null;
    function createPopup(start) {
        if (activePopup) activePopup.dismiss(true);
        const dialog = document.createElement('dialog');
        dialog.className = 'om-dialog';
        dialog.setAttribute('aria-labelledby', 'om-title');
        dialog.setAttribute('aria-describedby', 'om-description');
        dialog.innerHTML = `<div class="om-card"><div class="om-stage"></div>
            <div class="om-copy" role="status" aria-live="polite" aria-atomic="true"><h2 id="om-title"></h2><p id="om-description"></p></div>
            <button type="button" class="om-close" hidden>Close</button>
            <div class="om-countdown" aria-hidden="true"><span></span></div></div>`;
        const card = dialog.querySelector('.om-card');
        const fruit = new Fruit(card.querySelector('.om-stage'), start.variant === 'committee' ? 4 : 6);
        const title = card.querySelector('h2'), text = card.querySelector('p'), close = card.querySelector('.om-close');
        title.textContent = start.title || '';
        text.textContent = start.description || '';
        document.body.appendChild(dialog);
        let skip = null, gone = null, closable = false;
        const handle = {
            card, fruit,
            setCopy(f) {
                if (!f.title && f.description == null) return;
                const copy = card.querySelector('.om-copy');
                copy.animate([{ opacity: 1 }, { opacity: 0 }], { duration: reduced() ? 1 : 90, fill: 'forwards' }).finished.then(() => {
                    if (f.title) title.textContent = f.title;
                    if (f.description != null) text.textContent = f.description;
                    copy.animate([{ opacity: 0, transform: 'translateY(3px)' }, { opacity: 1, transform: 'none' }], { duration: reduced() ? 1 : 160, easing: EASE_OUT, fill: 'forwards' });
                });
            },
            async hold(ms) {
                card.dataset.phase = 'complete';
                const bar = card.querySelector('.om-countdown span').animate([{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }], { duration: ms, easing: 'linear', fill: 'forwards' });
                await new Promise(resolve => { skip = resolve; bar.finished.then(resolve, resolve); });
                skip = null;
            },
            awaitClose() {
                closable = true;
                close.hidden = false;
                close.focus();
                return new Promise(resolve => dialog.addEventListener('close', resolve, { once: true }));
            },
            dismiss(fast) {
                if (gone) return gone;
                if (activePopup === handle) activePopup = null;
                gone = (async () => {
                    const t = fast || reduced() ? 80 : config.exit;
                    await Promise.all([
                        card.animate([{ opacity: 1, transform: 'scale(1)' }, { opacity: 0, transform: 'scale(.92)' }], { duration: t, easing: 'cubic-bezier(.4,0,1,1)', fill: 'forwards' }).finished,
                        dialog.animate([{ opacity: 1 }, { opacity: 0 }], { duration: t, easing: 'ease-in', fill: 'forwards' }).finished
                    ]).catch(() => { });
                    fruit.stop();
                    if (dialog.open) dialog.close();
                    dialog.remove();
                })();
                return gone;
            }
        };
        dialog.addEventListener('cancel', event => { event.preventDefault(); if (skip) skip(); else if (closable) handle.dismiss(); });
        close.addEventListener('click', () => handle.dismiss());
        dialog.showModal();
        dialog.animate([{ opacity: 0 }, { opacity: 1 }], { duration: reduced() ? 1 : config.enter, easing: 'ease-out' });
        card.animate([{ opacity: 0, transform: 'translateY(6px) scale(.96)' }, { opacity: 1, transform: 'none' }], { duration: reduced() ? 1 : config.enter, easing: EASE_OUT });
        activePopup = handle;
        return handle;
    }

    async function finishPopup(popup, f) {
        await playResult(popup.fruit, f, x => popup.setCopy(x));
        if (f.variant === 'error' && !config.holdError) { await popup.awaitClose(); return; }
        await popup.hold(f.variant === 'error' ? config.holdError : f.hold || config.holdPopup);
        await popup.dismiss();
    }

    // ---------- Toast (frequent, lightweight operations) ----------
    let activeToast = null;
    function createToast(start) {
        if (activeToast) activeToast.dismiss(true);
        const toast = document.createElement('div');
        toast.className = 'om-toast';
        toast.setAttribute('role', 'status');
        toast.setAttribute('aria-live', 'polite');
        if ('popover' in toast) toast.setAttribute('popover', 'manual');
        toast.innerHTML = '<div class="om-toast-fruit"></div><div class="om-toast-copy"><strong></strong><span></span></div><button type="button" class="om-toast-close" aria-label="Dismiss">&times;</button><i class="om-toast-bar" aria-hidden="true"></i>';
        const fruit = new Fruit(toast.querySelector('.om-toast-fruit'), start.variant === 'committee' ? 4 : 6);
        const title = toast.querySelector('strong'), text = toast.querySelector('span');
        title.textContent = start.title || '';
        text.textContent = start.description || '';
        document.body.appendChild(toast);
        if (toast.showPopover) toast.showPopover();
        toast.animate([{ opacity: 0, transform: 'translateY(10px) scale(.97)' }, { opacity: 1, transform: 'none' }], { duration: reduced() ? 1 : config.enter, easing: EASE_OUT });
        let skip = null, gone = null;
        const handle = {
            fruit,
            setCopy(f) { if (f.title) title.textContent = f.title; if (f.description != null) text.textContent = f.description; toast.classList.toggle('is-error', f.variant === 'error'); },
            async hold(ms) {
                const bar = toast.querySelector('.om-toast-bar').animate([{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }], { duration: ms, easing: 'linear', fill: 'forwards' });
                toast.addEventListener('pointerenter', () => bar.pause());
                toast.addEventListener('pointerleave', () => bar.play());
                await new Promise(resolve => { skip = resolve; bar.finished.then(resolve, resolve); });
            },
            dismiss(fast) {
                if (gone) return gone;
                if (activeToast === handle) activeToast = null;
                gone = toast.animate([{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateY(8px) scale(.97)' }], { duration: fast || reduced() ? 80 : config.exit, easing: 'ease-in', fill: 'forwards' })
                    .finished.catch(() => { }).then(() => { fruit.stop(); if (toast.hidePopover && toast.matches(':popover-open')) toast.hidePopover(); toast.remove(); });
                if (skip) skip();
                return gone;
            }
        };
        toast.querySelector('.om-toast-close').addEventListener('click', () => handle.dismiss());
        activeToast = handle;
        return handle;
    }

    async function finishToast(toast, f) {
        await playResult(toast.fruit, f, x => toast.setCopy(x));
        await toast.hold(f.variant === 'error' ? Math.max(config.holdToast * 2, 5000) : f.hold || config.holdToast);
        await toast.dismiss();
    }

    // ---------- Inline (uploads, background and long-running work) ----------
    function createInline(target, start) {
        const host = typeof target === 'string' ? document.querySelector(target) : target;
        if (!host) return null;
        host.querySelectorAll('.om-inline').forEach(n => n.remove());
        const row = document.createElement('div');
        row.className = 'om-inline';
        row.setAttribute('role', 'status');
        row.setAttribute('aria-live', 'polite');
        row.innerHTML = '<span class="om-inline-fruit"></span><span class="om-inline-copy"><strong></strong><span></span></span>';
        const fruit = new Fruit(row.querySelector('.om-inline-fruit'), start.variant === 'committee' ? 4 : 6);
        const title = row.querySelector('strong'), text = row.querySelector('.om-inline-copy > span');
        title.textContent = start.title || '';
        text.textContent = start.description || '';
        host.appendChild(row);
        return {
            fruit,
            setCopy(f) { if (f.title) title.textContent = f.title; if (f.description != null) text.textContent = f.description; row.classList.toggle('is-error', f.variant === 'error'); },
            dismiss() { fruit.stop(); row.remove(); return Promise.resolve(); }
        };
    }

    function surface(mode, start, target) {
        if (mode === 'inline' && target) { const inline = createInline(target, start); if (inline) return { kind: 'inline', ui: inline }; }
        return mode === 'popup' ? { kind: 'popup', ui: createPopup(start) } : { kind: 'toast', ui: createToast(start) };
    }

    async function play(s, f) {
        if (s.kind === 'popup') return finishPopup(s.ui, f);
        if (s.kind === 'toast') return finishToast(s.ui, f);
        return playResult(s.ui.fruit, f, x => s.ui.setCopy(x));
    }

    // Show a result the server already reported (server TempData signal, JSON response).
    function show(feedback, options = {}) {
        const f = normalize(feedback, options.mode);
        const s = surface(f.mode, { variant: f.variant, title: f.title, description: f.description }, options.target);
        return play(s, f);
    }

    // Start processing for a request that is now running. Nothing is shown for a fast response.
    function begin(options = {}) {
        const mode = (options.mode || 'toast').toLowerCase();
        const start = { variant: options.variant, title: options.title || 'Working…', description: options.description || '' };
        let s = null, timer = null, settled = false;
        const open = () => { if (settled || s) return; s = surface(mode, start, options.target); s.ui.fruit.process(); };
        if (mode === 'popup' || mode === 'inline') open(); else timer = window.setTimeout(open, config.toastDelay);
        return {
            async finish(feedback) {
                settled = true; window.clearTimeout(timer);
                const f = normalize(feedback, mode);
                // An important result is always shown in its own popup, even when the request started as a toast.
                if (s && s.kind !== f.mode && !(s.kind === 'popup' && f.mode === 'toast') && !(s.kind === 'inline' && f.mode !== 'popup')) { await s.ui.dismiss(true); s = null; }
                if (!s) return show(f, { target: options.target });
                return play(s, f);
            },
            fail(message, title) {
                return this.finish({ variant: 'error', mode: mode === 'inline' ? 'inline' : 'popup', title: title || 'Not completed', description: message || 'The operation could not be completed. Nothing was changed.' });
            },
            cancel() { settled = true; window.clearTimeout(timer); if (s) s.ui.dismiss(true); s = null; }
        };
    }

    // Reads the server's feedback from a JSON body or from a rendered page (#om-feedback).
    function feedbackFrom(data) {
        if (!data) return null;
        if (typeof data === 'object') return data.feedback || null;
        const node = new DOMParser().parseFromString(data, 'text/html').getElementById('om-feedback');
        try { return node ? JSON.parse(node.textContent) : null; } catch { return null; }
    }

    const sending = new WeakSet();
    const NEXT_KEY = 'orange-motion:next';
    // Sends a form with fetch. The server answers a redirect with { redirect, feedback } (OrangeMotionResultFilter).
    async function submit(form, options = {}) {
        if (sending.has(form)) return;
        sending.add(form);
        const submitter = options.submitter;
        const buttons = [...form.querySelectorAll('button[type="submit"], button:not([type]), input[type="submit"]')];
        buttons.forEach(b => { b.dataset.omDisabled = b.disabled ? '1' : ''; b.disabled = true; });
        const release = () => { sending.delete(form); buttons.forEach(b => { b.disabled = b.dataset.omDisabled === '1'; delete b.dataset.omDisabled; }); };
        const data = new FormData(form);
        if (submitter && submitter.name) data.set(submitter.name, submitter.value);
        // The clicked button may refine the form's settings (a draft is a toast, a submission a popup).
        const pick = name => (submitter && submitter.dataset[name]) || form.dataset[name] || '';
        const mode = options.mode || pick('om') || 'toast';
        const handle = begin({ mode, title: options.title || pick('omWorking') || 'Saving…', description: options.description || pick('omWorkingDescription'), target: pick('omTarget') });
        let response, ctrl = new AbortController(), timer = window.setTimeout(() => ctrl.abort(), config.requestTimeout);
        try {
            response = await fetch(form.getAttribute('action') || location.href, { method: 'POST', body: data, credentials: 'same-origin', signal: ctrl.signal, headers: { 'X-Orange-Motion': '1', Accept: 'application/json, text/html;q=0.9' } });
        } catch {
            window.clearTimeout(timer);
            await handle.fail('The connection was interrupted or timed out. Check the page before trying again.', 'Not sent');
            release();
            return;
        }
        window.clearTimeout(timer);
        const type = response.headers.get('content-type') || '';
        const path = new URL(response.url).pathname;
        if (response.redirected && /\/Account\/(Login|AccessDenied)/i.test(path)) {
            await handle.fail(/Login/i.test(path) ? 'Your session ended, so nothing was saved. Sign in again, then repeat the action.' : 'Your account is not allowed to perform this action.', 'Not completed');
            release();
            return;
        }
        if (response.ok && type.includes('application/json')) {
            const body = await response.json().catch(() => ({}));
            if (body.redirect) {
                // A popup finishes here, over the page that sent it. A toast never makes the user wait: it travels to the next page.
                if (body.feedback && normalize(body.feedback, mode).mode === 'popup') await handle.finish(body.feedback);
                else { handle.cancel(); if (body.feedback) { try { sessionStorage.setItem(NEXT_KEY, JSON.stringify(body.feedback)); } catch { } } }
                window.location.assign(body.redirect);
                return;
            }
            if (body.feedback) { await handle.finish(body.feedback); release(); return; }
        }
        if (response.ok && type.includes('text/html')) {
            // The server re-rendered the page (normally validation messages). Show it as it is; nothing is hidden behind feedback.
            const html = await response.text();
            handle.cancel();
            document.open(); document.write(html); document.close();
            return;
        }
        let message = '';
        try {
            const body = type.includes('json') ? await response.json() : await response.text();
            message = typeof body === 'string' ? body : (body.error || body.detail || (body.errors && Object.values(body.errors).flat().join(' ')) || body.title || '');
        } catch { }
        if (!message || message.length > 300 || /<html/i.test(message)) message = `The server did not accept the request (status ${response.status}). Nothing was changed.`;
        await handle.fail(message, 'Not completed');
        release();
    }

    document.addEventListener('submit', event => {
        const form = event.target;
        if (event.defaultPrevented || !(form instanceof HTMLFormElement) || !form.hasAttribute('data-om') || form.dataset.om === 'off') return;
        if ((form.getAttribute('method') || 'get').toLowerCase() !== 'post' || !window.fetch) return;
        event.preventDefault();
        submit(form, { submitter: event.submitter });
    });

    // A one-time server signal rendered by Views/Shared/_OrangeMotion.cshtml (TempData, consumed on read: never replayed on refresh).
    // A toast handed over by submit() is read once and removed, so it is not replayed either.
    function autoplay() {
        let handed = null;
        try { handed = sessionStorage.getItem(NEXT_KEY); sessionStorage.removeItem(NEXT_KEY); } catch { }
        const node = document.getElementById('om-feedback');
        const json = node && !node.dataset.played ? node.textContent : handed;
        if (node) node.dataset.played = '1';
        if (!json) return;
        try { show(JSON.parse(json)); } catch { }
    }
    window.addEventListener('pageshow', event => { if (event.persisted) document.querySelectorAll('form').forEach(f => sending.delete(f)); });

    window.OrangeMotion = { config, show, begin, submit, feedbackFrom, createLoadingFruit, variants: VARIANTS.slice() };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', autoplay); else autoplay();
})();
