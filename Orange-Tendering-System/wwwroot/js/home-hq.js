/* Orange HQ hero: the approved image of the headquarters (Tools/HeroLayers/reference.webp), cut into floors by
   Tools/HeroLayers/build_layers.py. The building is centred. It starts with its floors lifted apart and seen from a
   wide, low, orbiting camera; scrolling lowers every floor into place, grows the orange lines live, and moves the
   camera to the exact approved frontal view of the completed building. Fully reversible.
   Layout works without GSAP; the scroll sequence uses GSAP 3.12.5 + ScrollTrigger. */
(function () {
  "use strict";
  var stage = document.getElementById("hq-stage"), world = document.getElementById("hq-world");
  if (!stage || !world) return;
  var root = document.documentElement, section = document.getElementById("home");
  var REF_W = 1448, REF_H = 1086, FX = 700, FY = 560; // reference size, building centre
  var reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  var gsap = window.gsap, ScrollTrigger = window.ScrollTrigger;
  var clamp = function (v, a, b) { return Math.min(b, Math.max(a, v)); };

  /* ---------- floors (keep GAPS in sync with build_layers.py / manifest.json) ---------- */
  var GAPS = [42, 48, 52, 58, 74];
  var STACK = { f5: 1, f4: 2, card: 2, f3: 3, f2: 4, roof: 5 };
  var layers = {};
  Array.prototype.forEach.call(world.querySelectorAll(".hq-layer"), function (el, i) { layers[el.dataset.layer] = { el: el, phase: i * 1.3 }; });

  /* ---------- state ---------- */
  // g[k]: lock progress of stage k (0 = lifted, 1 = in place). The page loads exploded unless reduced motion.
  var S = { g: reduce ? [1, 1, 1, 1, 1] : [0, 0, 0, 0, 0], glow: 0, pulse: 0, fade: 0 };
  // camera: dolly z, orbit ry / tilt rx (deg), pan tx / rise ty (reference px). Starts wide, low and orbiting.
  var C = reduce ? { z: 1, ry: 0, rx: 0, tx: 0, ty: 0 } : { z: .8, ry: 7, rx: 4, tx: -10, ty: 120 };
  var mouse = { x: 0, y: 0, tx: 0, ty: 0 };
  var view = { s: 1, ox: 0, oy: 0, wide: true };
  var st = null;

  // a floor is lifted by the gaps of its own stage and every stage below that has not locked yet
  function lift(k) { var d = 0; for (var j = 0; j < k; j++) d += GAPS[j] * (1 - S.g[j]); return -d; }
  function assembled() { return (S.g[0] + S.g[1] + S.g[2] + S.g[3] + S.g[4]) / 5; }

  /* ---------- orange lines: each grows with the lock progress of the floor it plugs into ---------- */
  var NS = "http://www.w3.org/2000/svg", svg = document.getElementById("hq-wires");
  function left(y0, y1) { return function (d) { var e = y1 + d; return "M252 " + y0 + "H300Q310 " + y0 + " 310 " + (y0 + 10) + "V" + (e - 12).toFixed(1) + "Q310 " + e.toFixed(1) + " 322 " + e.toFixed(1) + "H357"; }; }
  function right(x0, y0, xb, y1) { return function (d) { var e = y1 + d; return "M" + x0 + " " + y0 + "H" + (xb + 10) + "Q" + xb + " " + y0 + " " + xb + " " + (y0 + 10) + "V" + (e - 10).toFixed(1) + "Q" + xb + " " + e.toFixed(1) + " " + (xb - 12) + " " + e.toFixed(1) + "H1078"; }; }
  var WIRES = [
    [1, "f5", left(535, 612), [357, 612]],               // Supplier Management -> terrace floor
    [1, "f5", right(1222, 535, 1140, 598), [1078, 598]], // Reports -> terrace floor
    [2, "f4", left(452, 530), [357, 530]],               // Tender Management -> floor 4
    [3, "f3", right(1175, 322, 1136, 440), [1078, 440]], // Suppliers -> floor 3
    [4, "f2", left(367, 440), [357, 440]],               // Sourcing -> floor 2
    [5, "roof", right(1158, 185, 1100, 235), [1080, 235]] // Workflow & Approval -> roof
  ];
  var wires = WIRES.map(function (w) {
    var glow = document.createElementNS(NS, "path"), core = document.createElementNS(NS, "path"), dot = document.createElementNS(NS, "circle");
    glow.setAttribute("class", "hq-w-glow"); core.setAttribute("class", "hq-w-core"); dot.setAttribute("class", "hq-w-dot"); dot.setAttribute("r", "5.5");
    [glow, core].forEach(function (p) { p.setAttribute("pathLength", "1"); p.style.strokeDasharray = "1 1"; });
    svg.append(glow, core, dot);
    return { stage: w[0], layer: w[1], d: w[2], end: w[3], glow: glow, core: core, dot: dot };
  });
  var glowRoof = world.querySelector(".hq-glow-roof"), glowBase = world.querySelector(".hq-glow-base"), ring = world.querySelector(".hq-ring");

  /* ---------- layout: building centred ---------- */
  function layout() {
    var W = stage.clientWidth, H = stage.clientHeight, nav = parseFloat(getComputedStyle(root).getPropertyValue("--nav")) || 84;
    var wide = W >= 1100 && W / H >= 1.2, s, ox, oy;
    if (wide) {
      s = (H - nav) / REF_H;
      ox = (W - REF_W * s) / 2;
      oy = nav;
    } else {
      s = Math.min(W / 980, H * .64 / REF_H);
      ox = W / 2 - FX * s;
      oy = H - REF_H * s;
    }
    view.s = s; view.ox = ox; view.oy = oy; view.wide = wide;
    stage.classList.toggle("hq-wide", wide); stage.classList.toggle("hq-narrow", !wide);
    stage.style.setProperty("--s", s.toFixed(5));
  }

  /* ---------- render ---------- */
  var t0 = performance.now();
  function render(now) {
    var t = reduce ? 0 : (now - t0) / 1000, a = assembled(), idle = reduce ? 0 : 1 - a;
    mouse.x += (mouse.tx - mouse.x) * .06; mouse.y += (mouse.ty - mouse.y) * .06;
    var bob = Math.sin(t * .9) * 2.5 * idle;
    world.style.transform = "translate(" + view.ox.toFixed(2) + "px," + view.oy.toFixed(2) + "px) scale(" + view.s.toFixed(5) + ") translate(" + FX + "px," + FY + "px) rotateY(" + (C.ry + mouse.x * 1.2).toFixed(3) + "deg) rotateX(" + (C.rx - mouse.y * .8).toFixed(3) + "deg) scale(" + C.z.toFixed(4) + ") translate(" + (-FX + C.tx).toFixed(2) + "px," + (-FY + C.ty).toFixed(2) + "px)";
    Object.keys(layers).forEach(function (n) {
      var k = STACK[n], L = layers[n];
      var sway = Math.sin(t * .6 + L.phase) * 1.6 * idle * (1 - S.g[k - 1]);
      L.el.style.transform = "translate3d(" + sway.toFixed(2) + "px," + (lift(k) + bob * (k / 5)).toFixed(2) + "px,0)";
    });
    wires.forEach(function (w) {
      var grow = S.g[w.stage - 1], d = lift(STACK[w.layer]) + bob * (STACK[w.layer] / 5), path = w.d(d), off = (1 - grow).toFixed(4);
      w.glow.setAttribute("d", path); w.core.setAttribute("d", path);
      w.glow.style.strokeDashoffset = off; w.core.style.strokeDashoffset = off;
      w.glow.style.opacity = grow > 0 ? (.65 + .35 * S.glow).toFixed(3) : "0";
      w.core.style.opacity = grow > 0 ? "1" : "0";
      w.dot.setAttribute("cx", w.end[0]); w.dot.setAttribute("cy", (w.end[1] + d).toFixed(1));
      w.dot.style.opacity = clamp((grow - .9) * 10, 0, 1).toFixed(3);
    });
    glowRoof.style.transform = "translate3d(0," + lift(5).toFixed(2) + "px,0)";
    glowRoof.style.opacity = glowBase.style.opacity = (S.glow * .85).toFixed(3);
    ring.style.opacity = S.pulse > 0 && S.pulse < 1 ? ((1 - S.pulse) * .8).toFixed(3) : "0";
    ring.style.transform = "scale(" + (1 + S.pulse * 1.5).toFixed(3) + ")";
  }
  var raf = 0, visible = true;
  function loop(now) { raf = 0; if (!visible) return; render(now); if (!reduce) raf = requestAnimationFrame(loop); }
  new IntersectionObserver(function (es) { visible = es[0].isIntersecting; if (visible && !raf) raf = requestAnimationFrame(loop); }).observe(stage);
  addEventListener("resize", function () { layout(); render(performance.now()); });
  if (matchMedia("(hover:hover)").matches && !reduce) {
    stage.addEventListener("pointermove", function (e) { var k = 1 - assembled(); mouse.tx = (e.clientX / innerWidth - .5) * k; mouse.ty = (e.clientY / innerHeight - .5) * k; });
    stage.addEventListener("pointerleave", function () { mouse.tx = mouse.ty = 0; });
  }
  layout(); render(performance.now());
  root.classList.add("hq-live");
  // read-only state for automated visual QA
  window.__hq = { get assembly() { return assembled(); }, get trigger() { return st ? { start: st.start, end: st.end } : null; }, get view() { return view; }, get camera() { return { z: C.z, ry: C.ry, rx: C.rx, ty: C.ty }; } };
  if (!gsap || !ScrollTrigger || reduce) return; // reduced motion / no GSAP: the completed building, as approved

  /* ---------- scroll timeline: 0..80 = assembly 0..100 %, 80..100 = hero hold, then the sections ---------- */
  gsap.registerPlugin(ScrollTrigger);
  var STAGES = [
    [0, "00", "Scroll to assemble the headquarters"],
    [15, "01", "Purchase Requests — the terrace floor locks onto the podium"],
    [30, "02", "Tender Management — the next floor lands"],
    [45, "03", "Suppliers & Bidding — the middle floor connects"],
    [60, "04", "Evaluation & AI — the upper floor settles"],
    [75, "05", "Reports & Approvals — the roof closes the structure"],
    [90, "06", "Final Award — one complete headquarters"]
  ];
  var A = function (p) { return p * .8; };
  var label = document.getElementById("hq-label"), bar = document.getElementById("hq-progress"), copy = document.getElementById("hq-copy"), fade = document.getElementById("hq-fade");
  var lastIdx = -1;

  function build() {
    var tl = gsap.timeline({ defaults: { ease: "none" } });
    [0, 1, 2, 3, 4].forEach(function (k) {
      var o0 = {}, o1 = {}; o0[k] = 0; o1[k] = 1;
      tl.fromTo(S.g, o0, Object.assign({ duration: A(11), ease: "power3.inOut", immediateRender: false }, o1), A(16 + k * 15));
    });
    // camera: wide low orbit -> sweeps the other way while rising -> the exact approved frontal hero view
    tl.fromTo(C, { z: .8, ry: 7, rx: 4, tx: -10, ty: 120 }, { z: .9, ry: -5, rx: 2.5, tx: 18, ty: 64, duration: A(45), ease: "sine.inOut", immediateRender: false }, 0);
    tl.to(C, { z: 1, ry: 0, rx: 0, tx: 0, ty: 0, duration: A(55), ease: "sine.inOut" }, A(45));
    tl.to(C, { z: 1.05, duration: 15, ease: "sine.inOut" }, 82);
    tl.to([copy, "#hq-shade"], { autoAlpha: 0, duration: A(8), ease: "power2.in" }, A(4));
    tl.to(S, { glow: 1, duration: A(5), ease: "power2.out" }, A(90));
    tl.fromTo(S, { pulse: 0 }, { pulse: 1, duration: A(8), ease: "power1.out", immediateRender: false }, A(91));
    tl.to(".hq-stagebar", { autoAlpha: 0, duration: 3 }, 84);
    tl.to(S, { fade: 1, duration: 2.5 }, 97.5);
    tl.to({}, { duration: .01 }, 100);
    tl.eventCallback("onUpdate", function () {
      var pct = clamp(tl.time() / .8, 0, 100), idx = 0;
      STAGES.forEach(function (s, i) { if (pct >= s[0]) idx = i; });
      if (idx !== lastIdx) { lastIdx = idx; label.innerHTML = "<b>" + STAGES[idx][1] + "</b><span>" + STAGES[idx][2] + "</span>"; }
      bar.style.transform = "scaleX(" + (pct / 100).toFixed(4) + ")";
      fade.style.opacity = S.fade.toFixed(3);
      stage.classList.toggle("is-assembled", pct >= 99.5);
    });
    return tl;
  }

  var mm = gsap.matchMedia();
  mm.add({ wide: "(min-width:1100px)", mid: "(min-width:700px) and (max-width:1099px)", small: "(max-width:699px)" }, function (ctx) {
    var c = ctx.conditions, tl = build();
    st = ScrollTrigger.create({
      trigger: section, start: "top top", end: function () { return "+=" + Math.round(innerHeight * (c.wide ? 6 : c.mid ? 3.4 : 2.6)); },
      pin: true, scrub: c.wide ? .9 : .6, anticipatePin: 1, animation: tl, invalidateOnRefresh: true
    });
    // "See It in Action": play the whole sequence by scrolling through it, then land in the platform section
    var see = Array.prototype.slice.call(document.querySelectorAll("[data-see-action]"));
    function onSee(e) {
      e.preventDefault();
      var y0 = scrollY, target = st.end + 2, dur = clamp((target - y0) * 1.1, 4000, 12000), start = performance.now(), stop = false;
      function cancel() { stop = true; removeEventListener("wheel", cancel); removeEventListener("touchstart", cancel); removeEventListener("keydown", cancel); }
      addEventListener("wheel", cancel, { passive: true }); addEventListener("touchstart", cancel, { passive: true }); addEventListener("keydown", cancel);
      (function step(now) { var k = Math.min(1, (now - start) / dur), e2 = k < .5 ? 2 * k * k : 1 - Math.pow(-2 * k + 2, 2) / 2; scrollTo(0, y0 + (target - y0) * e2); if (k < 1 && !stop) requestAnimationFrame(step); else cancel(); })(start);
    }
    see.forEach(function (a) { a.addEventListener("click", onSee); });
    return function () { see.forEach(function (a) { a.removeEventListener("click", onSee); }); st && st.kill(); st = null; tl.kill(); };
  });
})();
