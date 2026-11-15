/* Orange cinematic homepage. GSAP 3.12.5 + ScrollTrigger. Everything illustrative; no network calls. */
(function () {
  "use strict";
  var root = document.documentElement;
  var reduceMQ = window.matchMedia("(prefers-reduced-motion: reduce)");
  var hasGsap = !!(window.gsap && window.ScrollTrigger);
  if (!hasGsap) { root.classList.remove("hc-anim"); }
  var $ = function (s, c) { return (c || document).querySelector(s); };
  var $$ = function (s, c) { return Array.prototype.slice.call((c || document).querySelectorAll(s)); };
  var clamp = function (v, a, b) { return Math.min(b, Math.max(a, v)); };
  var fmt = function (n) { return (Math.round(n * 100) / 100).toFixed(2).replace(/\.00$/, "").replace(/(\.\d)0$/, "$1"); };

  /* ---------- navigation ---------- */
  (function nav() {
    var burger = $(".hc-burger"), links = $("#hc-links");
    if (!burger || !links) return;
    function set(open) {
      links.classList.toggle("is-open", open);
      burger.setAttribute("aria-expanded", String(open));
      burger.setAttribute("aria-label", open ? "Close navigation" : "Open navigation");
    }
    burger.addEventListener("click", function () { set(burger.getAttribute("aria-expanded") !== "true"); });
    links.addEventListener("click", function (e) { if (e.target.closest("a")) set(false); });
    document.addEventListener("keydown", function (e) { if (e.key === "Escape") { set(false); } });
    window.addEventListener("resize", function () { if (window.innerWidth > 1180) set(false); });
    // highlight the in-page section currently on screen
    var spy = $$('a[href^="#"]', links).map(function (a) { return { a: a, el: document.getElementById(a.getAttribute("href").slice(1)) }; }).filter(function (x) { return x.el; });
    function mark() {
      var line = window.innerHeight * .35, cur = null;
      spy.forEach(function (x) { var r = x.el.getBoundingClientRect(); if (r.top <= line && r.bottom > line) cur = x; });
      spy.forEach(function (x) { var on = x === cur; x.a.classList.toggle("is-active", on); if (on) x.a.setAttribute("aria-current", "location"); else x.a.removeAttribute("aria-current"); });
    }
    window.addEventListener("scroll", mark, { passive: true }); window.addEventListener("resize", mark); mark();
    // day / night switch (the other public pages use experience.js for the same control)
    var themeBtn = $("#theme-toggle"), themeStatus = $("#theme-status"), metaTheme = $('meta[name="theme-color"]');
    function applyTheme(theme, announce) {
      root.dataset.theme = theme; root.style.colorScheme = theme;
      if (metaTheme) metaTheme.content = theme === "light" ? "#f6f4f0" : "#090909";
      if (themeBtn) { themeBtn.setAttribute("aria-label", "Switch to " + (theme === "dark" ? "light" : "dark") + " mode"); themeBtn.setAttribute("aria-pressed", String(theme === "light")); themeBtn.title = theme === "dark" ? "Light mode" : "Dark mode"; }
      if (announce && themeStatus) themeStatus.textContent = (theme === "light" ? "Light" : "Dark") + " mode enabled";
    }
    applyTheme(root.dataset.theme === "light" ? "light" : "dark");
    if (themeBtn) themeBtn.addEventListener("click", function () {
      var next = root.dataset.theme === "light" ? "dark" : "light";
      try { localStorage.setItem("orange-ui-theme", next); } catch (_) {}
      applyTheme(next, true);
    });
    var bar = $("#hc-nav");
    function solid() { bar.classList.toggle("is-solid", window.scrollY > 40 || links.classList.contains("is-open")); }
    window.addEventListener("scroll", solid, { passive: true }); burger.addEventListener("click", solid); solid();
  })();

  /* ---------- "Explore the Platform" video player ---------- */
  (function tour() {
    var dlg = document.getElementById("hc-tour"), btn = $("[data-tour-open]");
    if (!dlg || !btn) return;
    var video = dlg.querySelector("video");
    btn.addEventListener("click", function () {
      dlg.showModal();
      var pl = video.play(); if (pl && pl.catch) pl.catch(function () { /* autoplay blocked: the controls stay available */ });
    });
    function close() { if (dlg.open) dlg.close(); }
    dlg.addEventListener("click", function (e) { if (e.target === dlg || e.target.closest("[data-tour-close]")) close(); });
    dlg.addEventListener("close", function () { video.pause(); btn.focus({ preventScroll: true }); });
  })();

  /* ---------- roving tablist helper ---------- */
  function tablist(tabs, onSelect) {
    function select(tab, focus) {
      tabs.forEach(function (t) {
        var on = t === tab;
        t.setAttribute("aria-selected", String(on));
        t.tabIndex = on ? 0 : -1;
      });
      if (focus) tab.focus();
      onSelect(tab);
    }
    tabs.forEach(function (t, i) {
      t.addEventListener("click", function () { select(t, false); });
      t.addEventListener("keydown", function (e) {
        var k = e.key, n = tabs.length, j = -1;
        if (k === "ArrowDown" || k === "ArrowRight") j = (i + 1) % n;
        else if (k === "ArrowUp" || k === "ArrowLeft") j = (i - 1 + n) % n;
        else if (k === "Home") j = 0;
        else if (k === "End") j = n - 1;
        if (j > -1) { e.preventDefault(); select(tabs[j], true); }
      });
    });
    return select;
  }

  /* ---------- 03 roles network ---------- */
  (function roles() {
    var wrap = $("#hc-roles"); if (!wrap) return;
    var LINKS = { admin: ["dept", "officer", "supplier", "committee", "manager", "top"], dept: ["officer"], officer: ["dept", "supplier", "committee"], supplier: ["officer"], committee: ["officer", "manager"], manager: ["committee", "top"], top: ["manager"] };
    var tabs = $$(".hc-role-pick [role=tab]", wrap), nodes = $$(".hc-node", wrap), lines = $$(".hc-net-lines path", wrap), panels = $$(".hc-panel", wrap);
    function apply(id) {
      var linked = LINKS[id];
      panels.forEach(function (p) { p.hidden = p.id !== "rp-" + id; });
      nodes.forEach(function (n) {
        var r = n.dataset.role;
        n.classList.toggle("is-on", r === id);
        n.classList.toggle("is-link", r !== id && linked.indexOf(r) > -1);
        n.classList.toggle("is-dim", r !== id && linked.indexOf(r) === -1);
      });
      lines.forEach(function (l) {
        var a = l.dataset.a, b = l.dataset.b, adm = l.classList.contains("adm");
        var touches = a === id || b === id;
        if (adm) { l.classList.toggle("on", id === "admin"); }
        else { l.classList.toggle("on", touches); l.classList.toggle("off", !touches); }
      });
    }
    var select = tablist(tabs, function (t) { apply(t.dataset.role); });
    nodes.forEach(function (n) { n.addEventListener("click", function () { var t = tabs.filter(function (x) { return x.dataset.role === n.dataset.role; })[0]; select(t, false); }); });
    select(tabs.filter(function (t) { return t.dataset.role === "officer"; })[0], false);
  })();

  /* ---------- 04 governed communication ---------- */
  (function comm() {
    var wrap = $("#hc-comm"); if (!wrap) return;
    var chain = $(".hc-chain", wrap), chans = $$(".hc-chan", wrap), cls = $$(".hc-cl", wrap), cns = $$(".hc-cn", wrap);
    var title = $("#hc-chan-title"), purpose = $("#hc-chan-purpose"), note = $("#hc-state-note");
    var stateBtns = $$("#hc-states button");
    var sel = null;
    var NOTES = [
      "Pending: a clarification request was sent and is waiting for a response.",
      "Answered: the receiving user responded and the sender is notified.",
      "Resolved: the matter is closed and kept in the related transaction history."
    ];
    function show(ch) {
      if (!ch) { chain.classList.remove("is-active"); cls.concat(cns).forEach(function (e) { e.classList.remove("on"); }); return; }
      chain.classList.add("is-active");
      cls.forEach(function (l) { var on = l.dataset.c === ch.dataset.c; l.classList.toggle("on", on); if (on) l.style.setProperty("--len", Math.max(20, l.offsetWidth - 9) + "px"); });
      cns.forEach(function (n) { n.classList.toggle("on", n.dataset.n === ch.dataset.a || n.dataset.n === ch.dataset.b); });
      title.textContent = ch.children[0].textContent + " ↔ " + ch.children[2].textContent;
      purpose.textContent = ch.dataset.purpose;
    }
    chans.forEach(function (c) {
      c.addEventListener("mouseenter", function () { show(c); });
      c.addEventListener("focus", function () { show(c); });
      c.addEventListener("mouseleave", function () { show(sel); });
      c.addEventListener("blur", function () { show(sel); });
      c.addEventListener("click", function () { sel = c; chans.forEach(function (x) { x.classList.toggle("is-on", x === c); }); show(c); });
    });
    stateBtns.forEach(function (b) {
      b.addEventListener("click", function () {
        stateBtns.forEach(function (x) { x.setAttribute("aria-pressed", String(x === b)); });
        note.textContent = NOTES[+b.dataset.s];
      });
    });
    sel = chans[0]; sel.classList.add("is-on"); show(sel);
  })();

  /* ---------- 05 weighted evaluation demo ---------- */
  (function evaluation() {
    var wrap = $("#hc-eval"); if (!wrap) return;
    var MEMBERS = ["Sourcing Manager", "Department Representative", "Financial Representative", "Technical Representative"];
    var PROPS = [40, 50, 60, 55];
    var SUPS = [
      { n: "Supplier A", s: [[82, 70], [78, 74], [85, 72], [80, 76]] },
      { n: "Supplier B", s: [[68, 90], [70, 88], [64, 92], [72, 86]] },
      { n: "Supplier C", s: [[75, 80], [72, 78], [78, 82], [74, 79]] }
    ];
    var cur = 0, avgF = 50;
    var propsEl = $("#hc-props"), scoresEl = $("#hc-scores"), rankEl = $("#hc-rank"), tabsEl = $("#hc-sup-tabs");
    function slider(id, label, val, cb, unit) {
      var d = document.createElement("div"); d.className = "hc-prop";
      d.innerHTML = '<label for="' + id + '"><span>' + label + '</span><output for="' + id + '"></output></label><input type="range" id="' + id + '" min="0" max="100" step="1">';
      var inp = d.querySelector("input"), out = d.querySelector("output");
      inp.value = val;
      function sync() { inp.style.setProperty("--p", inp.value + "%"); out.textContent = unit(+inp.value); }
      inp.addEventListener("input", function () { sync(); cb(+inp.value); });
      sync();
      return d;
    }
    MEMBERS.forEach(function (m, i) {
      propsEl.appendChild(slider("hc-prop-" + i, m, PROPS[i], function (v) { PROPS[i] = v; update(); }, function (v) { return v + "% financial / " + (100 - v) + "% technical"; }));
    });
    SUPS.forEach(function (s, i) {
      var b = document.createElement("button"); b.type = "button"; b.setAttribute("role", "tab"); b.id = "hc-st-" + i; b.textContent = s.n;
      tabsEl.appendChild(b);
    });
    var tabs = $$("button", tabsEl);
    function buildScores() {
      scoresEl.innerHTML = "";
      scoresEl.setAttribute("role", "tabpanel"); scoresEl.setAttribute("aria-labelledby", "hc-st-" + cur);
      MEMBERS.forEach(function (m, i) {
        var row = document.createElement("div"); row.className = "hc-mrow";
        row.innerHTML = '<h4><span>' + m + '</span><b data-ms="' + i + '"></b></h4>';
        row.appendChild(slider("hc-f-" + cur + "-" + i, "Financial score", SUPS[cur].s[i][0], function (v) { SUPS[cur].s[i][0] = v; update(); }, function (v) { return String(v); }));
        row.appendChild(slider("hc-t-" + cur + "-" + i, "Technical score", SUPS[cur].s[i][1], function (v) { SUPS[cur].s[i][1] = v; update(); }, function (v) { return String(v); }));
        scoresEl.appendChild(row);
      });
    }
    var select = tablist(tabs, function (t) { cur = tabs.indexOf(t); buildScores(); update(); });
    // result rows
    SUPS.forEach(function (s, i) {
      var r = document.createElement("div"); r.className = "hc-rank-row"; r.dataset.i = i;
      r.innerHTML = '<div><span><b class="rk"></b>' + s.n + '</span><span class="sc"></span></div><div class="hc-track"><i></i></div>';
      rankEl.appendChild(r);
    });
    rankEl.style.display = "flex"; rankEl.style.flexDirection = "column";
    var rows = $$(".hc-rank-row", rankEl);
    function member(f, t, fw, tw) { return (f * fw + t * tw) / 100; } // MemberScore
    function update() {
      avgF = PROPS.reduce(function (a, b) { return a + b; }, 0) / 4;
      var fw = avgF, tw = 100 - avgF;
      $("#hc-avg-txt").textContent = fmt(fw) + "% / " + fmt(tw) + "%";
      $("#hc-appr-txt").textContent = fmt(fw) + "% / " + fmt(tw) + "%";
      $("#hc-wbar-f").style.width = fw + "%"; $("#hc-wbar-t").style.width = tw + "%";
      var finals = SUPS.map(function (s) {
        var ms = s.s.map(function (p) { return member(p[0], p[1], fw, tw); });
        return { ms: ms, fin: ms.reduce(function (a, b) { return a + b; }, 0) / 4 }; // FinalSupplierScore
      });
      $$("[data-ms]", scoresEl).forEach(function (b) { b.textContent = "Member score " + fmt(finals[cur].ms[+b.dataset.ms]); });
      var before = rows.map(function (r) { return r.getBoundingClientRect().top; });
      var order = finals.map(function (f, i) { return i; }).sort(function (a, b) { return finals[b].fin - finals[a].fin; });
      rows.forEach(function (r, i) {
        var rank = 1 + finals.filter(function (f) { return f.fin > finals[i].fin; }).length;
        var tied = finals.filter(function (f) { return f.fin === finals[i].fin; }).length > 1;
        r.style.order = order.indexOf(i);
        r.classList.toggle("top", rank === 1);
        r.querySelector(".rk").textContent = rank;
        r.querySelector(".sc").textContent = fmt(finals[i].fin) + (tied ? " (tied)" : "");
        r.querySelector(".hc-track i").style.setProperty("--s", (finals[i].fin / 100).toFixed(4));
      });
      if (hasGsap && !reduceMQ.matches) {
        rows.forEach(function (r, i) { var d = before[i] - r.getBoundingClientRect().top; if (Math.abs(d) > 1) window.gsap.fromTo(r, { y: d }, { y: 0, duration: .5, ease: "power2.out", overwrite: true }); });
      }
    }
    buildScores();
    select(tabs[0], false);
  })();

  /* ---------- 09 workspace previews ---------- */
  (function workspaces() {
    var wrap = $("#hc-ws"); if (!wrap) return;
    var tabs = $$("[role=tab]", wrap), panels = $$(".hc-ws-panel", wrap);
    var select = tablist(tabs, function (t) { panels.forEach(function (p) { p.hidden = p.id !== "wp-" + t.dataset.ws; }); });
    select(tabs[0], false);
  })();

  if (!hasGsap) return; // everything below is progressive enhancement

  var gsap = window.gsap, ScrollTrigger = window.ScrollTrigger;
  gsap.registerPlugin(ScrollTrigger);
  ScrollTrigger.config({ ignoreMobileResize: true });
  var mm = gsap.matchMedia();

  /* ---------- reveals + vertical rail (all sizes) ---------- */
  mm.add("(prefers-reduced-motion: no-preference)", function () {
    ScrollTrigger.batch(".hc-reveal", {
      start: "top 90%", once: true,
      onEnter: function (els) { gsap.to(els, { opacity: 1, y: 0, duration: .8, stagger: .08, ease: "power3.out", overwrite: true }); }
    });
    var steps = $("#hc-steps");
    if (steps && window.innerWidth < 700) {
      var lis = $$("li[data-stage]", steps);
      gsap.set(lis, { opacity: .35 });
      lis.forEach(function (li) { ScrollTrigger.create({ trigger: li, start: "top 80%", end: "bottom 30%", toggleActions: "play reverse play reverse", onToggle: function (s) { gsap.to(li, { opacity: s.isActive ? 1 : .55, duration: .35 }); } }); });
      steps.style.setProperty("--prog", 0);
      ScrollTrigger.create({ trigger: steps, start: "top 70%", end: "bottom 60%", scrub: true, onUpdate: function (s) { steps.style.setProperty("--prog", s.progress.toFixed(3)); } });
    }
    return function () { var s = $("#hc-steps"); if (s) { s.style.removeProperty("--prog"); } };
  });

  /* ---------- 06 AI + compliance walkthrough ---------- */
  mm.add("(prefers-reduced-motion: no-preference)", function () {
    var split = $("#hc-split"), doc = $("#hc-doc"); if (!split || !doc) return;
    var panes = $$(".hc-pane", split), ems = $$(".hc-checks em", split), bars = $$(".hc-mini i", split), msg = $("#hc-ai-msg");
    function offset(pane) {
      var s = split.getBoundingClientRect(), p = pane.getBoundingClientRect(), d = doc.getBoundingClientRect();
      var ds = { x: (d.left + d.width / 2) - s.left - gsap.getProperty(doc, "x"), y: (d.top + d.height / 2) - s.top - gsap.getProperty(doc, "y") };
      return { x: (p.left + p.width / 2) - s.left - ds.x, y: (p.top + Math.min(p.height / 2, 150)) - s.top - ds.y };
    }
    gsap.set(doc, { opacity: 0 }); gsap.set(ems, { opacity: 0 }); gsap.set(bars, { scaleX: 0 }); gsap.set(msg, { opacity: .25 });
    var tl = gsap.timeline({ paused: true, defaults: { ease: "power2.inOut" } });
    tl.to(doc, { opacity: 1, duration: .3 })
      .to(doc, { x: function () { return offset(panes[0]).x; }, y: function () { return offset(panes[0]).y; }, rotation: -4, duration: .9 })
      .to(ems, { opacity: 1, duration: .35, stagger: .22, ease: "none" }, "-=.2")
      .to(doc, { x: function () { return offset(panes[1]).x; }, y: function () { return offset(panes[1]).y; }, rotation: 4, duration: 1 }, "+=.25")
      .to(bars, { scaleX: 1, duration: .6, stagger: .12, ease: "power3.out" }, "-=.3")
      .to(doc, { opacity: 0, scale: .8, duration: .4 }, "+=.4")
      .to(msg, { opacity: 1, duration: .6 }, "-=.2");
    var st = ScrollTrigger.create({
      trigger: split, start: "top 70%", end: "bottom top",
      onEnter: function () { tl.play(); }, onEnterBack: function () { tl.play(); },
      onLeaveBack: function () { tl.pause(0); gsap.set([doc, msg], { clearProps: "x,y" }); }
    });
    return function () { st.kill(); tl.kill(); };
  });

  /* ---------- 07 report assembly ---------- */
  mm.add("(prefers-reduced-motion: no-preference)", function () {
    var rep = $("#hc-rep"); if (!rep) return;
    var frags = $$(".hc-frag, .hc-sigs", rep), lis = $$("#hc-tl li", rep), pdf = $(".hc-pdf", rep);
    var dirs = [[-160, -60, -10], [180, -90, 8], [-190, 70, 12], [170, 110, -9], [0, 160, 6]];
    frags.forEach(function (f, i) { gsap.set(f, { opacity: 0, x: dirs[i][0], y: dirs[i][1], rotation: dirs[i][2] }); });
    gsap.set(pdf, { opacity: .55, scale: .94 });
    var tl = gsap.timeline({ defaults: { ease: "power3.out" } });
    tl.to(pdf, { opacity: 1, scale: 1, duration: .6, ease: "none" }, 0);
    frags.forEach(function (f, i) { tl.to(f, { opacity: 1, x: 0, y: 0, rotation: 0, duration: .8 }, .3 + i * .45); });
    tl.to({}, { duration: 1.2 });
    var N = lis.length;
    var st = ScrollTrigger.create({
      trigger: rep, start: "top 70%", end: "bottom 55%", scrub: .6, animation: tl,
      onUpdate: function (self) {
        var k = Math.round(clamp((self.progress - .08) / .85, 0, 1) * N);
        lis.forEach(function (li, i) { li.classList.toggle("is-on", i < k); });
      }
    });
    return function () { st.kill(); tl.kill(); lis.forEach(function (li) { li.classList.remove("is-on"); }); };
  });

  /* ---------- hash targets + late layout ---------- */
  function afterLayout() {
    ScrollTrigger.refresh();
    var h = location.hash, t = null;
    if (h && h.length > 1) { try { t = document.querySelector(h); } catch (e) { t = null; } if (t) t.scrollIntoView(); }
  }
  var done = false;
  function once() { if (done) return; done = true; afterLayout(); }
  if (document.readyState === "complete") once(); else window.addEventListener("load", once);
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(function () { if (done) ScrollTrigger.refresh(); });
})();
