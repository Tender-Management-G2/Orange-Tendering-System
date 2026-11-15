(() => {
    'use strict';
    const scene = document.querySelector('.ld-scene');
    if (!scene) return;
    const gsap = window.gsap;
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const quick = reduce || !gsap;
    const q = selector => scene.querySelector(selector);
    const all = selector => [...scene.querySelectorAll(selector)];
    if (!quick && window.matchMedia('(pointer: fine)').matches) {
        const artwork = all('.ld-art');
        const shiftX = gsap.quickTo(artwork, 'x', { duration: .9, ease: 'power2.out' });
        const shiftY = gsap.quickTo(artwork, 'y', { duration: .9, ease: 'power2.out' });
        scene.addEventListener('pointermove', event => {
            const bounds = scene.getBoundingClientRect();
            shiftX(((event.clientX - bounds.left) / bounds.width - .5) * -18);
            shiftY(((event.clientY - bounds.top) / bounds.height - .5) * -12);
        });
        scene.addEventListener('pointerleave', () => { shiftX(0); shiftY(0); });
    }

    if (scene.dataset.scene === 'registration') {
        const root = document.getElementById('livingRegistration');
        const entrance = {
            card: q('.ld-story-card'), envelope: q('.ld-story-envelope'),
            company: q('.ld-paper-label--company'), category: q('.ld-paper-label--category'),
            documents: q('.ld-document-markers'), closed: q('.ld-art--closed'), open: q('.ld-art--open')
        };
        const reveal = (element, on, fromY = 24, rotation = 0) => {
            if (quick) {
                element.style.opacity = on ? '1' : '0';
                element.style.transform = on ? 'translateY(0) rotate(' + rotation + 'deg)' :
                    'translateY(' + fromY + 'px) rotate(' + rotation + 'deg)';
            } else gsap.to(element, { opacity: on ? 1 : 0, y: on ? 0 : fromY,
                rotate: rotation, duration: on ? .62 : .32, ease: 'power3.out', overwrite: true });
        };
        function sync() {
            const stage = Number(root.dataset.stage || 0);
            const opened = stage > 0;
            if (quick) {
                entrance.closed.style.opacity = opened ? '0' : '1';
                entrance.open.style.opacity = opened ? '1' : '0';
            } else {
                gsap.to(entrance.closed, { opacity: opened ? 0 : 1, duration: .75, ease: 'power2.inOut', overwrite: true });
                gsap.to(entrance.open, { opacity: opened ? 1 : 0, duration: .75, ease: 'power2.inOut', overwrite: true });
                gsap.fromTo(q('.ld-light-sweep'), { xPercent: -100 }, { xPercent: 100, duration: .9, ease: 'power1.inOut', overwrite: true });
            }
            reveal(entrance.card, root.classList.contains('has-name'), 85, -11);
            reveal(entrance.envelope, root.classList.contains('has-email'), 45, 12);
            reveal(entrance.company, opened && root.classList.contains('has-company'), 22, 4);
            reveal(entrance.category, opened && !!document.getElementById('Input_Category')?.value, 22, -4);
            reveal(entrance.documents, stage >= 2 && root.classList.contains('has-documents'), -22);
        }
        root.addEventListener('dossier:stage', sync);
        root.addEventListener('dossier:field', sync);
        sync();
    }

    if (['verify', 'forgot', 'reset', 'confirm'].includes(scene.dataset.scene)) {
        if (!quick) {
            gsap.fromTo(q('.ld-verification-letter'),
                { y: 80, rotation: -16, scale: .88, opacity: 0 },
                { y: 0, rotation: -7, scale: 1, opacity: 1, duration: 1.1, ease: 'power3.out' });
            gsap.fromTo(all('.ld-code-marks i'), { y: 18, opacity: 0 },
                { y: 0, opacity: 1, duration: .45, stagger: .09, delay: .52, ease: 'power2.out' });
        }
        const input = document.getElementById('Input_Code');
        input?.addEventListener('input', () => {
            const count = Math.min(6, input.value.replace(/\D/g, '').length);
            scene.classList.toggle('has-code', count > 0);
            all('.ld-code-marks i').forEach((mark, index) => {
                if (quick) mark.style.background = index < count ? '#f7bd84' : '#fff5e8';
                else gsap.to(mark, { backgroundColor: index < count ? '#f7bd84' : '#fff5e8',
                    y: index < count ? -4 : 0, duration: .25, overwrite: true });
            });
        });
    }

    if (scene.dataset.scene !== 'face' && scene.dataset.scene !== 'login') return;
    const fragments = all('.ld-face-fragment');
    const origins = [
        { y: -75, rotationX: -64 }, { x: -85, rotationY: -55 },
        { x: 85, rotationY: 55 }, { x: -45, rotationY: -45 },
        { x: 45, rotationY: 45 }, { y: 65, rotationX: 55 }
    ];
    const camera = q('.ld-camera');
    const portrait = q('.ld-portrait');
    const card = q('.ld-id-card');
    const orbit = q('.ld-camera-orbit');
    let current = 'idle';
    let timeline;
    function stop() { timeline?.kill(); }
    function formFace() {
        stop();
        if (quick) {
            fragments.forEach(fragment => { fragment.style.opacity = '1'; fragment.style.transform = 'none'; });
            return;
        }
        timeline = gsap.timeline();
        fragments.forEach((fragment, index) => {
            timeline.fromTo(fragment, { ...origins[index], opacity: index === 0 ? 1 : .25 },
                { x: 0, y: 0, rotationX: 0, rotationY: 0, opacity: 1,
                    duration: .62, ease: 'power3.out' }, index * .1);
        });
        timeline.fromTo(portrait, { y: 50, rotation: -5, scale: .85, opacity: .7 },
            { y: 0, rotation: 2, scale: 1, opacity: 1, duration: .95, ease: 'power3.out' }, 0);
    }
    function openFace() {
        stop();
        if (quick) {
            fragments.forEach(fragment => { fragment.style.opacity = '0'; });
            camera.style.opacity = '1'; camera.style.clipPath = 'inset(0 0% 0 0%)';
            return;
        }
        timeline = gsap.timeline();
        timeline.to(camera, { opacity: 1, clipPath: 'inset(0 0% 0 0%)', duration: .65, ease: 'power2.out' }, 0);
        fragments.forEach((fragment, index) => {
            const direction = origins[index];
            timeline.to(fragment, { x: direction.x || 0, y: direction.y || 0,
                rotationX: direction.rotationX || 0, rotationY: direction.rotationY || 0,
                opacity: .08, duration: .75, ease: 'power3.inOut' }, .05 + index * .055);
        });
        timeline.to(orbit, { rotation: 24, opacity: .95, duration: .8, ease: 'power2.inOut' }, 0);
    }
    function insertCard() {
        stop();
        if (quick) {
            portrait.style.opacity = '0'; card.style.opacity = '1';
            card.style.transform = 'translate(34%, 230%) rotate(8deg) scale(.72)';
            return;
        }
        const target = q('.ld-pocket-target').getBoundingClientRect();
        const from = card.getBoundingClientRect();
        const dx = target.left + target.width / 2 - (from.left + from.width / 2);
        const dy = target.top + target.height / 2 - (from.top + from.height / 2);
        timeline = gsap.timeline();
        timeline.to(camera, { opacity: 0, duration: .25 }, 0);
        timeline.to(portrait, { scale: .75, y: 26, opacity: 0, duration: .55, ease: 'power2.inOut' }, 0);
        timeline.fromTo(card, { opacity: 0, scale: .75, x: 0, y: 0, rotation: 4 },
            { opacity: 1, scale: 1, duration: .42, ease: 'power2.out' }, .17);
        timeline.to(card, { x: dx, y: dy, scale: .72, rotation: 8, duration: .82,
            ease: 'power3.inOut' }, .6);
        timeline.to(card, { y: dy + 24, duration: .38, ease: 'power2.in' }, 1.34);
        timeline.to(orbit, { opacity: 0, duration: .4 }, .65);
    }
    function change(state) {
        if (current === state) return;
        current = state;
        scene.dataset.faceState = state;
        if (state === 'requesting') {
            if (quick) { card.style.opacity = '0'; portrait.style.opacity = '1'; }
            else gsap.set([card, portrait], { clearProps: 'all' });
            formFace();
        } else if (state === 'camera' || state === 'capturing') {
            if (state === 'camera') openFace();
            if (state === 'capturing' && !quick) gsap.to(orbit, { rotation: '+=30', repeat: -1, yoyo: true,
                duration: 1.8, ease: 'sine.inOut', overwrite: true });
        } else if (state === 'processing') {
            if (!quick) gsap.to(orbit, { opacity: .35, duration: .4, overwrite: true });
        } else if (state === 'confirmed') {
            insertCard();
        } else if (state === 'failed') {
            if (!quick) gsap.to(camera, { opacity: 0, duration: .25 });
            formFace();
        }
    }
    scene.addEventListener('dossier:face-state', event => change(event.detail.state));
    if (document.getElementById('bio-root')?.dataset.faceComplete === 'true') {
        change('confirmed');
    } else formFace();
})();
