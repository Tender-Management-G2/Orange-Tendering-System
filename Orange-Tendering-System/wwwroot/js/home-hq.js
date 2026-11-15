/* Orange HQ hero: the approved reference image, cut into layers by Tools/HeroLayers/build_layers.py.
   Exploded at rest (pixel-identical to the reference), assembled by scroll in the same locked perspective.
   Layout (cover scaling) works without GSAP; the scroll assembly uses GSAP 3.12.5 + ScrollTrigger. */
(function () {
  "use strict";
  var stage = document.getElementById("hq-stage"), world = document.getElementById("hq-world");
  if (!stage || !world) return;
  var root = document.documentElement, section = document.getElementById("home");
  var REF_W = 1672, REF_H = 941;
  var reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  var gsap = window.gsap, ScrollTrigger = window.ScrollTrigger;
  var clamp = function (v, a, b) { return Math.min(b, Math.max(a, v)); };

  /* ---------- layers (geometry comes from manifest.json, written into the markup by the view) ---------- */
  var GAPS = [6, 10, 10, 18, 16]; // keep in sync with Tools/HeroLayers/build_layers.py (also in manifest.json)
  var STACK = { l5: 1, l4: 2, card: 2, l3: 3, l2: 4, roof: 5 };
  var SIDE = { leftbox: [1, 44, 26, .96], rightcube: [3, -313, 122, .72], ulpanels: [4, 141, 55, .8] };
  var DEPTH = { roof: 1, l2: .85, l3: .7, card: .9, l4: .55, l5: .35, leftbox: .8, rightcube: .9, ulpanels: .95 };
  var layers = {};
  Array.prototype.forEach.call(world.querySelectorAll(".hq-layer"), function (el, i) { layers[el.dataset.layer] = { el: el, phase: i * 1.7 }; });

  /* ---------- state (driven by the scroll timeline) ---------- */
  var S = { g: [0, 0, 0, 0, 0], side: { leftbox: 0, rightcube: 0, ulpanels: 0 }, glow: 0, pulse: 0, zoom: 0, fade: 0, wire: [0, 0, 0, 0, 0, 0, 0] };
  var mouse = { x: 0, y: 0, tx: 0, ty: 0 };
  var view = { s: 1, ox: 0, oy: 0, wide: true };

  function stackDy(k) { var d = 0; for (var j = 0; j < k; j++) d += GAPS[j] * S.g[j]; return d; }
  function assembled() { return (S.g[0] + S.g[1] + S.g[2] + S.g[3] + S.g[4]) / 5; }

  /* ---------- orange connectors (redrawn so they stay attached to the floors) ---------- */
  var NS = "http://www.w3.org/2000/svg", svg = document.getElementById("hq-wires");
  // each wire: [stage, layer it is attached to, function(dy) -> path]
  var WIRES = [
    [1, "l4", function (d) { return "M669 396H704Q716 396 716 408V" + (458 + d) + "Q716 " + (470 + d) + " 728 " + (470 + d) + "H792"; }],
    [2, null, function () { return "M680 457H714"; }],
    [3, "l4", function (d) { return "M669 518H708Q716 518 719 " + (512 + d * .5) + "Q724 " + (507 + d) + " 736 " + (507 + d) + "H762"; }],
    [4, null, function () { return "M669 582H704Q716 582 716 570V522"; }],
    [5, "l3", function (d) { return "M1312 243H1240Q1222 243 1220 260Q1217 " + (300 + d) + " 1196 " + (302 + d) + "H1170"; }],
    [3, "l4", function (d) { return "M1300 402H1262Q1251 403 1250 415Q1248 " + (436 + d) + " 1234 " + (438 + d) + "H1172"; }],
    [5, "l5", function (d) { return "M1378 566H1282Q1262 566 1250 580Q1238 " + (598 + d) + " 1214 " + (600 + d) + "H1195"; }]
  ];
  var wires = WIRES.map(function (w) {
    var g = document.createElementNS(NS, "g");
    var glow = document.createElementNS(NS, "path"), core = document.createElementNS(NS, "path"), pulse = document.createElementNS(NS, "path");
    glow.setAttribute("class", "hq-w-glow"); core.setAttribute("class", "hq-w-core"); pulse.setAttribute("class", "hq-w-pulse");
    pulse.setAttribute("pathLength", "1");
    g.append(glow, core, pulse); svg.appendChild(g);
    return { stage: w[0], layer: w[1], d: w[2], glow: glow, core: core, pulse: pulse };
  });

  /* ---------- glows that follow parts ---------- */
  var glowRoof = world.querySelector(".hq-glow-roof"), glowBase = world.querySelector(".hq-glow-base"), glowAward = world.querySelector(".hq-glow-award"), ring = world.querySelector(".hq-ring");

  /* ---------- layout: cover the stage, keep the composition of the reference ---------- */
  function layout() {
    var W = stage.clientWidth, H = stage.clientHeight;
    var wide = W >= 1100 && W / H >= 1.42;
    var s, ox, oy;
    if (wide) {
      s = Math.min(Math.max(W / REF_W, H / REF_H), H / REF_H * 1.14);
      var ww = REF_W * s;
      ox = ww > W ? clamp((W - ww) * .35, W - ww, 0) : (W - ww) / 2;
      oy = (H - REF_H * s) * .3;
    } else {
      s = Math.min(W < 700 ? W / 860 : W / 760, H * .6 / REF_H);
      ox = W / 2 - 968 * s;
      oy = H - REF_H * s * .97;
    }
    view.s = s; view.ox = ox; view.oy = oy; view.wide = wide;
    stage.classList.toggle("hq-wide", wide); stage.classList.toggle("hq-narrow", !wide);
    stage.style.setProperty("--s", s.toFixed(5)); stage.style.setProperty("--ox", ox.toFixed(1) + "px"); stage.style.setProperty("--oy", oy.toFixed(1) + "px");
  }

  /* ---------- render ---------- */
  var t0 = performance.now();
  function render(now) {
    var t = reduce ? 0 : (now - t0) / 1000;
    var a = assembled();
    mouse.x += (mouse.tx - mouse.x) * .06; mouse.y += (mouse.ty - mouse.y) * .06;
    var idle = reduce ? 0 : 1 - a;   // exploded parts drift gently; it fades as the building assembles
    var bob = Math.sin(t * .9) * 2.2 * idle; // the whole stack breathes together, so no gap ever opens
    // world transform (+ final push toward the entrance)
    var z = 1 + S.zoom * 1.15, fx = 988, fy = 800;
    var s = view.s * z, ox = view.ox + fx * view.s * (1 - z), oy = view.oy + fy * view.s * (1 - z);
    world.style.transform = "translate(" + ox.toFixed(2) + "px," + oy.toFixed(2) + "px) scale(" + s.toFixed(5) + ")";
    Object.keys(layers).forEach(function (n) {
      var L = layers[n], px = mouse.x * (DEPTH[n] || 0) * 7 * idle, py = mouse.y * (DEPTH[n] || 0) * 4 * idle, tr;
      if (STACK[n]) {
        tr = "translate3d(" + px.toFixed(2) + "px," + (stackDy(STACK[n]) + bob + py).toFixed(2) + "px,0)";
      } else if (SIDE[n]) {
        var k = S.side[n], sd = SIDE[n], sc = 1 + (sd[3] - 1) * k, drift = Math.sin(t * .7 + L.phase) * 3 * (1 - k) * (reduce ? 0 : 1);
        tr = "translate3d(" + (sd[1] * k + px).toFixed(2) + "px," + (sd[2] * k + drift + py).toFixed(2) + "px,0) scale(" + sc.toFixed(4) + ")";
      } else { tr = "none"; }
      L.el.style.transform = tr;
    });
    wires.forEach(function (w, i) {
      var d = w.layer ? stackDy(STACK[w.layer]) + bob : 0, path = w.d(d);
      w.glow.setAttribute("d", path); w.core.setAttribute("d", path); w.pulse.setAttribute("d", path);
      var on = S.wire[i];
      w.glow.style.opacity = (.55 + .45 * on + .3 * S.glow).toFixed(3);
      w.pulse.style.opacity = on > 0 && on < 1 ? "1" : "0";
      w.pulse.style.strokeDashoffset = (1 - on * 1.12).toFixed(3);
    });
    var roofDy = stackDy(5) + bob;
    glowRoof.style.transform = "translate3d(0," + roofDy.toFixed(2) + "px,0)";
    glowRoof.style.opacity = glowBase.style.opacity = (S.glow * .9).toFixed(3);
    glowAward.style.opacity = (S.glow).toFixed(3);
    ring.style.opacity = S.pulse > 0 && S.pulse < 1 ? ((1 - S.pulse) * .85).toFixed(3) : "0";
    ring.style.transform = "scale(" + (1 + S.pulse * 1.6).toFixed(3) + ")";
  }
  var raf = 0, visible = true;
  function loop(now) { raf = 0; if (!visible) return; render(now); if (!reduce) raf = requestAnimationFrame(loop); }
  new IntersectionObserver(function (es) { visible = es[0].isIntersecting; if (visible && !raf) raf = requestAnimationFrame(loop); }).observe(stage);
  addEventListener("resize", function () { layout(); if (reduce) render(performance.now()); });
  if (matchMedia("(hover:hover)").matches && !reduce) {
    stage.addEventListener("pointermove", function (e) { mouse.tx = e.clientX / innerWidth - .5; mouse.ty = e.clientY / innerHeight - .5; });
    stage.addEventListener("pointerleave", function () { mouse.tx = mouse.ty = 0; });
  }
  layout(); render(performance.now());
  root.classList.add("hq-live");

  if (!gsap || !ScrollTrigger || reduce) return; // reduced motion / no GSAP: the exact reference frame, no pinning

  /* ---------- scroll timeline: 0..80 = assembly 0..100 %, 80..100 = push toward the entrance ---------- */
  gsap.registerPlugin(ScrollTrigger);
  var STAGES = [
    [0, "00", "Scroll to assemble the headquarters"],
    [15, "01", "Purchase Requests — the foundation locks into place"],
    [30, "02", "Tender Management — the lower floors align"],
    [45, "03", "Suppliers & Bidding — the middle floors slide into place"],
    [60, "04", "Evaluation & AI — the upper floors assemble"],
    [75, "05", "Reports & Approvals — the roof closes the structure"],
    [90, "06", "Final Award — one complete headquarters"]
  ];
  var A = function (p) { return p * .8; };
  var label = document.getElementById("hq-label"), bar = document.getElementById("hq-progress"), copy = document.getElementById("hq-copy"), fade = document.getElementById("hq-fade");
  var lastIdx = -1, st = null;

  function build() {
    var tl = gsap.timeline({ defaults: { ease: "none" } });
    var g = S.g;
    // stage k: window [15k, 15k+15]; the floor settles with a soft landing
    [0, 1, 2, 3, 4].forEach(function (k) {
      var start = 15 + k * 15;
      tl.fromTo(S.g, { [k]: 0 }, { [k]: 1, duration: A(11), ease: "power3.inOut", immediateRender: false }, A(start + 1));
    });
    Object.keys(SIDE).forEach(function (n) {
      var start = 15 + (SIDE[n][0] - 1) * 15;
      var o = {}; o[n] = 1;
      tl.to(S.side, Object.assign({ duration: A(12), ease: "power2.inOut" }, o), A(start));
    });
    WIRES.forEach(function (w, i) {
      var o = {}; o[i] = 1;
      tl.fromTo(S.wire, (function () { var f = {}; f[i] = 0; return f; })(), Object.assign({ duration: A(8), immediateRender: false }, o), A(15 + (w[0] - 1) * 15 + 6));
    });
    tl.to(copy, { autoAlpha: 0, y: -24, duration: 4, ease: "power2.in" }, 82);
    tl.to(S, { glow: 1, duration: A(5), ease: "power2.out" }, A(90));
    tl.fromTo(S, { pulse: 0 }, { pulse: 1, duration: A(8), ease: "power1.out", immediateRender: false }, A(91));
    tl.to(".hq-stagebar", { autoAlpha: 0, duration: 3 }, 82);
    tl.to(S, { zoom: 1, duration: 16, ease: "power2.in" }, 84);
    tl.to(S, { fade: 1, duration: 4 }, 96);
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
      trigger: section, start: "top top", end: function () { return "+=" + Math.round(innerHeight * (c.wide ? 6 : c.mid ? 3.2 : 2.4)); },
      pin: true, scrub: c.wide ? .9 : .6, anticipatePin: 1, animation: tl, invalidateOnRefresh: true
    });
    // "See It in Action": play the whole assembly by scrolling through it, then land in the platform section
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

  // read-only state for automated visual QA
  window.__hq = { get assembly() { return assembled(); }, get trigger() { return st ? { start: st.start, end: st.end } : null; }, get view() { return view; } };
})();
