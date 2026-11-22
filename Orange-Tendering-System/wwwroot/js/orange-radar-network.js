// ORANGE RADAR - living market network (Canvas 2D engine, no dependency). Used by orange-radar.js; draws server data only.
// Three 2D views of the same suppliers with stable ids ("s-<supplierId>"):
//   category    - every registered supplier inside its registered category (an organic hull = classification, not a relationship)
//   competition - suppliers linked by distinct closed bidding cycles both bid in (server: data.network.competitionLinks)
//   tender      - closed bidding cycles on a time arc, suppliers below, offers flowing to the rounds they were submitted in
// A bounded force simulation pulls every node toward its view anchor and adds a slow organic drift, so the market feels alive
// without ever wandering off. Hover, selection and dragging calm the nodes involved; "Pause motion" and reduced-motion stop drift.
(() => {
  'use strict';
  window.OrangeRadarNetwork = function createNetwork(ctx) {
    const { root, $, $$, esc, fmt } = ctx;
    const VIEW = {
      category: { title: 'The supplier market', caption: 'Every registered supplier inside its registered category. A category is a classification, not a relationship.' },
      competition: { title: 'Who competes with whom', caption: 'Lines join suppliers that bid in the same closed bidding cycles (each cycle counted once). Clusters are connected groups: ordinary market context, not suspicion.' },
      tender: { title: 'Offers flowing into closed bidding cycles', caption: 'Closed bidding cycles along the time arc, suppliers below. Each curve is a submitted offer; the bright arrow is the official award. Sealed rounds never appear.' }
    };
    const REL = {
      competition: { label: 'Shared Bidding Cycles', views: ['competition'] },
      offer: { label: 'Submitted offer', views: ['tender'] },
      award: { label: 'Official award', views: ['tender'] }
    };
    const st = {
      view: 'category', data: null, nodes: new Map(), edges: [], groups: [], groupsByView: {}, selected: [], focusGroup: null, edge: null, ego: null, crumbs: [],
      rel: new Set(Object.keys(REL)), cats: null, opts: { inactive: true, isolated: true, competitionMin: 2 },
      replayAt: null, catColor: new Map(), maxRounds: 1, hover: null, hoverGroup: null, drag: null, path: null, pulses: [], ready: false,
      motion: true, cam: { x: 0, y: 0, k: 1 }, camT: { x: 0, y: 0, k: 1 }, camSpeed: 0.1, settle: 0, w: 800, h: 600, dpr: 1, visible: true, groupRects: [], tenderAxis: null
    };
    const host = $('[data-cy]');
    const canvas = document.createElement('canvas');
    canvas.className = 'rd-net-gl';
    canvas.setAttribute('aria-hidden', 'true');
    host.append(canvas);
    const g = canvas.getContext('2d');
    const stage = $('[data-net-stage]');
    const inspector = $('[data-inspector]');
    const reducedMotion = () => ctx.reduced.matches;
    try { st.motion = localStorage.getItem('orange-radar:motion') !== 'off'; const v = localStorage.getItem('orange-radar:view'); if (VIEW[v]) st.view = v; } catch { /* storage blocked */ }

    // ---------- motion coordinator ----------
    const motion = { epoch: 0, timers: new Set() };
    function cancel() { motion.epoch++; motion.timers.forEach(clearTimeout); motion.timers.clear(); return motion.epoch; }
    function later(fn, ms, epoch = motion.epoch) { const t = setTimeout(() => { motion.timers.delete(t); if (epoch === motion.epoch) fn(); }, ms); motion.timers.add(t); }

    // ---------- palette ----------
    const css = n => getComputedStyle(root).getPropertyValue(n).trim();
    const isDark = () => document.documentElement.dataset.theme === 'dark';
    let P = null;
    function readPalette() {
      P = { cats: [1, 2, 3, 4, 5, 6, 7, 8].map(i => css(`--c${i}`)), neutral: css('--rd-neutral'), edge: css('--rd-edge'), review: css('--rd-review'), violet: css('--rd-violet'), award: css('--rd-award'),
        orange: css('--rd-orange-strong'), ink: css('--rd-ink'), ink2: css('--rd-ink-2'), muted: css('--rd-muted'), surface: css('--rd-surface'), canvas: css('--rd-canvas'), line: css('--rd-line'), dark: isDark() };
    }
    const catColor = cat => (st.catColor.has(cat) ? P.cats[st.catColor.get(cat) % 8] : P.neutral);
    const rgb = hex => { const h = hex.replace('#', ''); const f = h.length === 3 ? h.split('').map(c => c + c).join('') : h.slice(0, 6); const n = parseInt(f, 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; };
    const rgba = (hex, a) => `rgba(${rgb(hex).join(',')},${a})`;
    const mix = (hex, other, t) => { const a = rgb(hex), b = rgb(other); return `rgb(${a.map((v, i) => Math.round(v + (b[i] - v) * t)).join(',')})`; };
    const ICON = {
      supplier: new Path2D('M5 21V8l7-4 7 4v13M9 21v-5h6v5M9 11h.01M15 11h.01'),
      round: new Path2D('M7 3h7l4 4v14H7zM14 3v4h4M10 12h5M10 16h5')
    };

    // ---------- model ----------
    const short = s => String(s || '').replace(/^RADAR DEMO (- )?/, '');
    const clip = (s, n) => (s.length > n ? `${s.slice(0, n - 1)}…` : s);
    const catKey = c => (c && c.trim() ? c.trim() : 'Uncategorized');
    const supplier = id => st.data.suppliers.find(s => s.supplierId === id);
    const hash = s => { let h = 0; for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0; return h; };
    const node = id => st.nodes.get(id);
    const shownNodes = () => [...st.nodes.values()].filter(n => n.shown && !n.future);
    const shownEdges = () => st.edges.filter(e => e.shown && !e.future && e.s.shown && e.t.shown && !e.s.future && !e.t.future);

    function build(data) {
      const prev = st.nodes; st.nodes = new Map(); st.edges = [];
      const add = n => { const o = prev.get(n.id); const h = Math.abs(hash(n.id)); st.nodes.set(n.id, Object.assign({ x: o?.x ?? 0, y: o?.y ?? 0, vx: 0, vy: 0, ax: o?.ax ?? 0, ay: o?.ay ?? 0, alpha: o?.alpha ?? 0, hoverT: 0, selT: 0,
        phase: (h & 1023) / 163, f1: 0.6 + ((h >> 10) & 255) / 400, f2: 0.6 + ((h >> 18) & 255) / 400, shown: false, future: false }, n)); };
      const gnode = new Map(data.graph.nodes.map(n => [n.id, n]));
      data.suppliers.forEach(s => {
        const id = `s-${s.supplierId}`;
        add({ id, type: 'supplier', entityId: s.supplierId, label: short(s.name), cat: catKey(s.category), status: s.status, approved: s.status === 'Approved', rounds: s.participations, wins: s.wins,
          r: 16 + 15 * Math.sqrt(Math.min(1, s.participations / st.maxRounds)), avail: gnode.get(id)?.availableAt ? Date.parse(gnode.get(id).availableAt) : 0 });
      });
      data.network.rounds.forEach(r => add({ id: `r-${r.roundId}`, type: 'round', entityId: r.roundId, label: `${short(r.tender)} · ${r.roundNumber === 1 ? 'Initial Bidding' : 'Re-tendering — Cycle ' + r.roundNumber}`, r: 15, at: Date.parse(r.availableAt), avail: Date.parse(r.availableAt) }));
      const edge = e => { const s = node(e.source), t = node(e.target); if (s && t) st.edges.push(Object.assign({ s, t, shown: false, future: false, bend: (hash(e.id) & 1) ? 1 : -1 }, e)); };
      const when = new Map(data.network.rounds.map(r => [r.roundId, Date.parse(r.availableAt)]));
      for (const l of data.network.competitionLinks)
        edge({ id: l.id, source: `s-${l.supplierA}`, target: `s-${l.supplierB}`, type: 'competition', kind: 'context', weight: l.roundIds.length, roundIds: l.roundIds, first: l.firstAt, last: l.lastAt, when });
      data.network.rounds.forEach(r => r.supplierIds.forEach(sid => {
        const award = r.awardedSupplierId === sid;
        edge({ id: `e-${award ? 'award' : 'offer'}-s-${sid}-r-${r.roundId}`, source: `s-${sid}`, target: `r-${r.roundId}`, type: award ? 'award' : 'offer', kind: 'fact', weight: 1, roundId: r.roundId, avail: Date.parse(r.availableAt) });
      }));
    }
    const competitionAvail = e => e.when.get(e.roundIds[Math.min(st.opts.competitionMin, e.roundIds.length) - 1]) || 0;

    // ---------- which entities and links belong to a view ----------
    function visibleSuppliers() {
      return st.data.suppliers.filter(s => (st.opts.inactive || s.status === 'Approved') && (!st.cats || st.cats.has(catKey(s.category))) && (st.opts.isolated || hasLink(s.supplierId)));
    }
    function hasLink(id) { return st.edges.some(e => (e.s.entityId === id || e.t.entityId === id) && e.type === 'competition'); }
    function edgeInView(e, view) {
      if (!st.rel.has(e.type)) return false;
      if (view === 'category') return false;
      if (view === 'competition') return e.type === 'competition' && e.roundIds.length >= st.opts.competitionMin;
      if (view === 'tender') return e.type === 'offer' || e.type === 'award';
      return false;
    }

    // ---------- organic layouts (anchors; physics does the rest) ----------
    const SP = 128;
    function phyllo(ids, cx, cy) {
      // Sunflower packing: the busiest supplier at the heart of its group, stable order by id.
      const list = ids.map(node).filter(Boolean).sort((a, b) => (b.rounds || 0) - (a.rounds || 0) || a.id.localeCompare(b.id, 'en', { numeric: true }));
      list.forEach((n, i) => { const r = SP * 0.64 * Math.sqrt(i + (list.length > 1 ? 0.4 : 0)); const a = i * 2.39996 - Math.PI / 2; n.ax = cx + Math.cos(a) * r * 1.12; n.ay = cy + Math.sin(a) * r * 0.92; });
      return list.length ? SP * 0.64 * Math.sqrt(list.length - 1 + 0.4) * 1.1 + 64 : 64;
    }
    function constellation(groups, stretch) {
      // Largest group at the centre, the others on one or two rings around it: no hub node, only space.
      if (!groups.length) return;
      // A group needs room for its title pill too, which is often wider than a one-supplier group.
      groups.forEach(gr => (gr.R = Math.max(gr.size, ((gr.label.length + 6) * 9.2 + 30) / 2)));
      const [first, ...rest] = groups;
      first.cx = 0; first.cy = 0;
      let inner = first.R;
      const rings = rest.length > 7 ? [rest.slice(0, Math.ceil(rest.length / 2)), rest.slice(Math.ceil(rest.length / 2))] : [rest];
      rings.forEach((ring, ri) => {
        if (!ring.length) return;
        const maxR = Math.max(...ring.map(x => x.R));
        const need = ring.reduce((s, x) => s + 2 * x.R + 60, 0);
        const radius = Math.max(inner + maxR + 50, need / (2 * Math.PI));
        let a = -Math.PI / 2 + ri * 0.35 + (ring.length === 1 ? Math.PI / 2 : 0);
        ring.forEach(x => { const span = ((2 * x.R + 60) / need) * Math.PI * 2; a += span / 2; x.cx = Math.cos(a) * radius * stretch; x.cy = Math.sin(a) * radius; a += span / 2; });
        inner = radius + maxR;
      });
      // Relax: gravity pulls groups together (harder vertically, to match the wide stage) while circles keep them apart,
      // so a handful of groups forms one compact composition instead of islands in empty space.
      const ay = Math.max(1.4, stretch * 2.2);
      for (let it = 0; it < 260; it++) {
        groups.forEach(x => { x.cx -= x.cx * 0.05; x.cy -= x.cy * 0.05 * ay; });
        for (let i = 0; i < groups.length; i++) for (let j = i + 1; j < groups.length; j++) {
          const a = groups[i], b = groups[j]; let dx = b.cx - a.cx, dy = b.cy - a.cy; let d = Math.hypot(dx, dy);
          if (d < 0.01) { dx = Math.cos(j); dy = Math.sin(j); d = 1; }
          const min = a.R + b.R + 44;
          if (d < min) { const push = (min - d) / 2 / d; a.cx -= dx * push; a.cy -= dy * push; b.cx += dx * push; b.cy += dy * push; }
        }
      }
    }
    function layout(view) {
      const sups = visibleSuppliers(); const supIds = new Set(sups.map(s => `s-${s.supplierId}`));
      const stretch = Math.max(1, Math.min(1.8, st.w / Math.max(1, st.h) / 1.2));
      st.nodes.forEach(n => { n.shown = false; n.group = null; n.quiet = false; });
      sups.forEach(s => (node(`s-${s.supplierId}`).shown = true));
      st.edges.forEach(e => (e.shown = edgeInView(e, view) && (!e.s.id.startsWith('s-') || supIds.has(e.s.id)) && (!e.t.id.startsWith('s-') || supIds.has(e.t.id))));
      let groups = [];
      if (view === 'tender') {
        // Time arc: closed bidding cycles in date order across the top; suppliers below, ordered by category.
        const rounds = st.data.network.rounds.map(r => node(`r-${r.roundId}`)).filter(Boolean);
        rounds.forEach(n => (n.shown = true));
        const W = Math.max(1000, Math.min(1900, st.w * 1.1));
        rounds.forEach((n, i) => { const t = rounds.length > 1 ? i / (rounds.length - 1) : 0.5; n.ax = (t - 0.5) * W; n.ay = -230 - Math.sin(t * Math.PI) * 80; });
        const ordered = sups.slice().sort((a, b) => catKey(a.category).localeCompare(catKey(b.category)) || b.participations - a.participations || a.name.localeCompare(b.name));
        const perRow = Math.max(6, Math.floor(W / 130));
        ordered.forEach((s, i) => { const r = Math.floor(i / perRow); const inRow = Math.min(perRow, ordered.length - r * perRow); const c = i % perRow; const n = node(`s-${s.supplierId}`);
          n.ax = (c - (inRow - 1) / 2) * (W / Math.max(perRow, 2)); n.ay = 190 + r * 150 + (c % 2) * 34; n.group = null; });
        st.groups = []; st.tenderAxis = { rounds };
        return;
      }
      st.tenderAxis = null;
      if (view === 'category') {
        const map = new Map(); sups.forEach(s => { const k = catKey(s.category); if (!map.has(k)) map.set(k, []); map.get(k).push(`s-${s.supplierId}`); });
        groups = [...map].sort((a, b) => b[1].length - a[1].length || a[0].localeCompare(b[0])).map(([k, ids]) => ({ id: `g-cat-${k}`, key: k, label: k, kind: 'category', members: ids, color: catColor(k) }));
      } else if (view === 'competition') {
        const comps = competitionGroups().map(c => c.map(id => `s-${id}`).filter(id => supIds.has(id))).filter(c => c.length);
        const multi = comps.filter(c => c.length > 1); const single = comps.filter(c => c.length === 1).flat();
        groups = multi.map((c, i) => ({ id: `g-comp-${i + 1}`, key: `comp-${i + 1}`, label: `Competition cluster ${i + 1}`, kind: 'competition', members: c, color: P.cats[(i + 2) % 8] }));
        if (single.length) groups.push({ id: 'g-comp-none', key: 'comp-none', label: 'No recurring shared bidding cycles', kind: 'orbit', members: single, color: P.neutral });
      }
      groups.forEach(gr => { gr.size = gr.kind === 'orbit' ? 0 : phyllo(gr.members, 0, 0) + (gr.attrs?.length ? 34 : 0); });
      const core = groups.filter(x => x.kind !== 'orbit'); const orbit = groups.find(x => x.kind === 'orbit');
      constellation(core, stretch);
      core.forEach(gr => {
        phyllo(gr.members, gr.cx, gr.cy);
        gr.members.forEach(id => (node(id).group = gr.id));
        (gr.attrs || []).forEach((id, i) => { const n = node(id); n.ax = gr.cx + (i - (gr.attrs.length - 1) / 2) * 56; n.ay = gr.cy + 6; n.group = gr.id; });
      });
      if (orbit) {
        // Suppliers outside every cluster orbit the composition along a lower arc: present, readable, clearly apart.
        const extent = core.length ? Math.max(...core.map(x => x.cy + x.R)) : 0;
        const list = orbit.members.map(node).sort((a, b) => a.label.localeCompare(b.label));
        const width = Math.max(600, Math.min(st.w * 1.2, list.length * 150));
        list.forEach((n, i) => { const t = list.length > 1 ? i / (list.length - 1) : 0.5; n.ax = (t - 0.5) * width; n.ay = extent + 150 + Math.sin(t * Math.PI) * 40 + (i % 2) * 30; n.group = orbit.id; });
        orbit.cx = 0; orbit.cy = extent + 150; orbit.R = 0;
      }
      st.groups = groups;
    }
    function competitionGroups() {
      if (st.opts.competitionMin === st.data.network.competitionMinimum) return st.data.network.competitionGroups;
      const adj = new Map(st.data.suppliers.map(s => [s.supplierId, new Set()]));
      st.edges.filter(e => e.type === 'competition' && e.roundIds.length >= st.opts.competitionMin).forEach(e => { adj.get(e.s.entityId)?.add(e.t.entityId); adj.get(e.t.entityId)?.add(e.s.entityId); });
      const seen = new Set(); const out = [];
      [...adj.keys()].sort((a, b) => a - b).forEach(s => { if (seen.has(s)) return; const c = []; const q = [s]; seen.add(s); while (q.length) { const u = q.pop(); c.push(u); adj.get(u).forEach(v => { if (!seen.has(v)) { seen.add(v); q.push(v); } }); } out.push(c.sort((a, b) => a - b)); });
      return out.sort((a, b) => b.length - a.length || a[0] - b[0]);
    }
    function egoLayout(id) {
      const c = node(id); if (!c) return;
      const links = st.edges.filter(e => (e.s === c || e.t === c) && e.type === 'competition' && st.view === 'competition' && e.roundIds.length >= st.opts.competitionMin);
      const offers = st.edges.filter(e => e.s === c && (e.type === 'offer' || e.type === 'award'));
      const peers = [...new Set(links.map(e => (e.s === c ? e.t : e.s)))].sort((a, b) => a.id.localeCompare(b.id, 'en', { numeric: true }));
      const rounds = offers.map(e => e.t).sort((a, b) => a.at - b.at);
      st.nodes.forEach(n => { n.shown = false; n.quiet = false; n.group = null; });
      st.edges.forEach(e => (e.shown = links.includes(e) || offers.includes(e)));
      c.shown = true; c.ax = 0; c.ay = 0;
      const ring = (list, R, a0) => list.forEach((n, i) => { const a = a0 + (i / Math.max(1, list.length)) * Math.PI * 2; n.shown = true; n.ax = Math.cos(a) * R * 1.3; n.ay = Math.sin(a) * R; });
      ring(peers, Math.max(190, peers.length * 34), -Math.PI / 2);
      ring(rounds, Math.max(330, rounds.length * 42), -Math.PI / 2 + Math.PI / Math.max(2, rounds.length));
      st.groups = []; st.tenderAxis = null;
    }

    // ---------- simulation ----------
    function step(now) {
      const nodes = shownNodes(); const live = st.motion && !reducedMotion();
      const kA = st.settle > 0 ? 0.05 : 0.016; st.settle = Math.max(0, st.settle - 1);
      const t = now * 0.00045; let energy = 0;
      for (const n of nodes) {
        if (st.drag?.node === n) continue;
        const calm = n === st.hover || (n.type === 'supplier' && st.selected.includes(n.entityId));
        let fx = (n.ax - n.x) * kA, fy = (n.ay - n.y) * kA;
        if (live && !calm) { fx += Math.sin(t * n.f1 + n.phase) * 0.06 + Math.sin(t * 2.3 * n.f2 + n.phase * 2) * 0.03; fy += Math.cos(t * n.f2 + n.phase * 1.3) * 0.06 + Math.cos(t * 1.9 * n.f1 + n.phase) * 0.03; }
        n.vx = (n.vx + fx) * (calm ? 0.6 : 0.86); n.vy = (n.vy + fy) * (calm ? 0.6 : 0.86);
      }
      // Collision keeps names readable; springs make dragging elastic for connected nodes.
      for (let i = 0; i < nodes.length; i++) for (let j = i + 1; j < nodes.length; j++) {
        const a = nodes[i], b = nodes[j]; const dx = b.x - a.x, dy = b.y - a.y; const d = Math.hypot(dx, dy) || 0.01;
        const min = (a.type === 'supplier' ? 58 : 34) + (b.type === 'supplier' ? 58 : 34);
        if (d < min) { const push = ((min - d) / d) * 0.12; const px = dx * push, py = dy * push; if (st.drag?.node !== a) { a.vx -= px; a.vy -= py; } if (st.drag?.node !== b) { b.vx += px; b.vy += py; } }
      }
      if (st.drag) for (const e of shownEdges()) {
        if (e.s !== st.drag.node && e.t !== st.drag.node) continue;
        const o = e.s === st.drag.node ? e.t : e.s; const rest = Math.hypot(e.s.ax - e.t.ax, e.s.ay - e.t.ay);
        const dx = st.drag.node.x - o.x, dy = st.drag.node.y - o.y; const d = Math.hypot(dx, dy) || 0.01; const f = (d - rest) * 0.012;
        o.vx += (dx / d) * f; o.vy += (dy / d) * f;
      }
      for (const n of nodes) { if (st.drag?.node === n) continue; n.x += n.vx; n.y += n.vy; energy += Math.abs(n.vx) + Math.abs(n.vy); }
      return energy;
    }

    // ---------- camera ----------
    const toScreen = (x, y) => ({ x: (x - st.cam.x) * st.cam.k + st.w / 2, y: (y - st.cam.y) * st.cam.k + st.h / 2 });
    const toWorld = (x, y) => ({ x: (x - st.w / 2) / st.cam.k + st.cam.x, y: (y - st.h / 2) / st.cam.k + st.cam.y });
    function bounds(list, pad = 70) {
      if (!list.length) return null;
      let x1 = Infinity, y1 = Infinity, x2 = -Infinity, y2 = -Infinity;
      list.forEach(n => { x1 = Math.min(x1, n.ax - pad); x2 = Math.max(x2, n.ax + pad); y1 = Math.min(y1, n.ay - pad); y2 = Math.max(y2, n.ay + pad + 30); });
      return { x1, y1, x2, y2 };
    }
    function frame(b, { maxK = 1.35, instant = false, speed = 0.085 } = {}) {
      if (!b) return;
      const padX = 80, padTop = 64, padBottom = 24;
      const k = Math.max(0.25, Math.min(maxK, Math.min((st.w - padX * 2) / (b.x2 - b.x1), (st.h - padTop - padBottom) / (b.y2 - b.y1))));
      st.camT = { k, x: (b.x1 + b.x2) / 2, y: (b.y1 + b.y2) / 2 - (padTop - padBottom) / 2 / k };
      st.camSpeed = speed;
      if (instant || reducedMotion()) st.cam = { ...st.camT };
    }
    function frameView(opts) {
      const pts = shownNodes().map(n => ({ ax: n.ax, ay: n.ay }));
      st.groups.filter(x => x.kind !== 'orbit' && x.R).forEach(x => pts.push({ ax: x.cx - x.R, ay: x.cy - x.R - 30 }, { ax: x.cx + x.R, ay: x.cy + x.R }));
      frame(bounds(pts, 50), opts);
    }

    // ---------- rendering ----------
    function hull(points) {
      const p = points.slice().sort((a, b) => a.x - b.x || a.y - b.y); if (p.length < 3) return p;
      const cross = (o, a, b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
      const lo = []; for (const q of p) { while (lo.length >= 2 && cross(lo[lo.length - 2], lo[lo.length - 1], q) <= 0) lo.pop(); lo.push(q); }
      const up = []; for (const q of p.slice().reverse()) { while (up.length >= 2 && cross(up[up.length - 2], up[up.length - 1], q) <= 0) up.pop(); up.push(q); }
      return lo.slice(0, -1).concat(up.slice(0, -1));
    }
    function groupShape(gr, now, live) {
      const members = gr.members.concat(gr.attrs || []).map(node).filter(n => n && n.shown && !n.future);
      if (!members.length) return null;
      const pts = [];
      members.forEach(n => { const pad = (n.type === 'supplier' ? n.r : 14) + 42 + (live ? Math.sin(now * 0.0008 + n.phase) * 4 : 0);
        for (let i = 0; i < 14; i++) { const a = (i / 14) * Math.PI * 2; pts.push({ x: n.x + Math.cos(a) * pad * 1.08, y: n.y + Math.sin(a) * pad + 10 }); } });
      const h = hull(pts); const cx = members.reduce((s, n) => s + n.x, 0) / members.length, cy = members.reduce((s, n) => s + n.y, 0) / members.length;
      return { h, cx, cy, top: Math.min(...h.map(q => q.y)) };
    }
    function smoothPath(c, pts) {
      if (pts.length < 3) return;
      const mid = (a, b) => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });
      let m = mid(pts[pts.length - 1], pts[0]); c.moveTo(m.x, m.y);
      for (let i = 0; i < pts.length; i++) { const p = pts[i], n = pts[(i + 1) % pts.length]; m = mid(p, n); c.quadraticCurveTo(p.x, p.y, m.x, m.y); }
      c.closePath();
    }
    function curve(e, centers) {
      const a = e.s, b = e.t;
      if (e.type === 'offer' || e.type === 'award') { const dy = (b.y - a.y) * 0.55; return { c1: { x: a.x, y: a.y + dy }, c2: { x: b.x, y: b.y - dy } }; }
      const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2; const dx = b.x - a.x, dy = b.y - a.y; const len = Math.hypot(dx, dy) || 1;
      if (a.group && b.group && a.group !== b.group && centers.has(a.group) && centers.has(b.group)) {
        // Bundling: links between the same two groups share one corridor between the group centres.
        const ga = centers.get(a.group), gb = centers.get(b.group); const cx = (ga.cx + gb.cx) / 2, cy = (ga.cy + gb.cy) / 2;
        return { q: { x: mx + (cx - mx) * 0.8, y: my + (cy - my) * 0.8 } };
      }
      const bend = Math.min(70, len * 0.24) * e.bend;
      return { q: { x: mx - (dy / len) * bend, y: my + (dx / len) * bend } };
    }
    function strokeCurve(c, e, geom) {
      c.beginPath(); c.moveTo(e.s.x, e.s.y);
      if (geom.q) c.quadraticCurveTo(geom.q.x, geom.q.y, e.t.x, e.t.y); else c.bezierCurveTo(geom.c1.x, geom.c1.y, geom.c2.x, geom.c2.y, e.t.x, e.t.y);
      c.stroke();
    }
    function pointOn(e, geom, t) {
      const a = e.s, b = e.t, u = 1 - t;
      if (geom.q) return { x: u * u * a.x + 2 * u * t * geom.q.x + t * t * b.x, y: u * u * a.y + 2 * u * t * geom.q.y + t * t * b.y };
      return { x: u * u * u * a.x + 3 * u * u * t * geom.c1.x + 3 * u * t * t * geom.c2.x + t * t * t * b.x, y: u * u * u * a.y + 3 * u * u * t * geom.c1.y + 3 * u * t * t * geom.c2.y + t * t * t * b.y };
    }
    function related() {
      // Highlight set: path, inspected edge, focused group or selection; hover previews without dimming the market.
      const nodesOn = new Set(); const edgesOn = new Set();
      if (st.path) { st.path.nodes.forEach(n => n && nodesOn.add(n)); st.path.edges.forEach(e => edgesOn.add(e)); return { nodesOn, edgesOn, any: true }; }
      if (st.edge) { const e = st.edges.find(x => x.id === st.edge); if (e) { nodesOn.add(e.s); nodesOn.add(e.t); edgesOn.add(e); return { nodesOn, edgesOn, any: true }; } }
      const focus = st.focusGroup && st.groups.find(x => x.id === st.focusGroup);
      if (focus) focus.members.concat(focus.attrs || []).forEach(id => nodesOn.add(node(id)));
      st.selected.forEach(id => { const n = node(`s-${id}`); if (n) nodesOn.add(n); });
      const seeds = new Set(nodesOn);
      if (seeds.size) { shownEdges().forEach(e => { if (seeds.has(e.s) || seeds.has(e.t)) { edgesOn.add(e); if (!focus) { nodesOn.add(e.s); nodesOn.add(e.t); } } }); return { nodesOn, edgesOn, any: true }; }
      if (st.hover) { nodesOn.add(st.hover); shownEdges().forEach(e => { if (e.s === st.hover || e.t === st.hover) { edgesOn.add(e); nodesOn.add(e.s); nodesOn.add(e.t); } }); return { nodesOn, edgesOn, any: false, hover: true }; }
      return { nodesOn, edgesOn, any: false };
    }
    function roundRect(c, x, y, w, h, r) { c.moveTo(x + r, y); c.arcTo(x + w, y, x + w, y + h, r); c.arcTo(x + w, y + h, x, y + h, r); c.arcTo(x, y + h, x, y, r); c.arcTo(x, y, x + w, y, r); }
    // Nodes never shrink below a readable on-screen size when the camera pulls back.
    const vis = () => Math.min(1.6, Math.max(1, 0.85 / st.cam.k));
    let fontCache = '';
    const font = () => (fontCache ||= getComputedStyle(root).fontFamily || 'system-ui, sans-serif');

    function draw(c, w, h, now, { exporting = null } = {}) {
      c.clearRect(0, 0, w, h);
      if (exporting) { c.fillStyle = exporting; c.fillRect(0, 0, w, h); }
      const k = st.cam.k; const vs = vis(); const rel = related(); const dim = rel.any; const soft = rel.hover;
      const live = st.motion && !reducedMotion() && !exporting;
      c.save(); c.translate(w / 2, h / 2); c.scale(k, k); c.translate(-st.cam.x, -st.cam.y);
      // organic group hulls (they morph with their members)
      const centers = new Map();
      for (const gr of st.groups) {
        gr.shape = null;
        if (gr.kind === 'orbit') continue;
        const shape = groupShape(gr, now, live); if (!shape) continue;
        gr.shape = shape; centers.set(gr.id, { cx: shape.cx, cy: shape.cy });
        const on = !dim || [...rel.nodesOn].some(n => n.group === gr.id) || st.focusGroup === gr.id;
        const hot = st.hoverGroup === gr.id || st.focusGroup === gr.id;
        const R = Math.max(...shape.h.map(q => Math.hypot(q.x - shape.cx, q.y - shape.cy)));
        const grad = c.createRadialGradient(shape.cx, shape.cy - R * 0.2, R * 0.05, shape.cx, shape.cy, R * 1.05);
        grad.addColorStop(0, rgba(gr.color, (P.dark ? 0.24 : 0.16) * (on ? 1 : 0.3) * (hot ? 1.35 : 1)));
        grad.addColorStop(1, rgba(gr.color, (P.dark ? 0.05 : 0.04) * (on ? 1 : 0.4)));
        c.beginPath(); smoothPath(c, shape.h); c.fillStyle = grad; c.fill();
        c.lineWidth = (hot ? 2.4 : 1.5) / k; c.strokeStyle = rgba(gr.color, on ? (hot ? 0.9 : 0.55) : 0.16); c.stroke();
      }
      // tender time arc
      if (st.tenderAxis && !st.ego) {
        const rs = st.tenderAxis.rounds.filter(n => n.shown && !n.future);
        if (rs.length > 1) { c.beginPath(); c.moveTo(rs[0].x, rs[0].y); for (let i = 1; i < rs.length; i++) { const p = rs[i - 1], q = rs[i]; c.quadraticCurveTo(p.x, p.y, (p.x + q.x) / 2, (p.y + q.y) / 2); } c.lineTo(rs[rs.length - 1].x, rs[rs.length - 1].y);
          const grad = c.createLinearGradient(rs[0].x, 0, rs[rs.length - 1].x, 0); grad.addColorStop(0, rgba(P.edge, 0.15)); grad.addColorStop(1, rgba(P.orange, 0.6));
          c.strokeStyle = grad; c.lineWidth = 3 / k; c.stroke(); }
      }
      // links
      for (const e of shownEdges()) {
        const geom = curve(e, centers); e.geom = geom;
        const on = rel.edgesOn.has(e); const pathOn = !!(st.path && st.path.lit.has(e));
        const a = Math.min(e.s.alpha, e.t.alpha) * (dim ? (on ? 1 : 0.06) : soft ? (on ? 1 : 0.35) : 1);
        let col = P.edge, width = 1.6, alpha = 0.45, dash = null;
        if (e.type === 'competition') { width = 1.3 + Math.min(5, e.weight) * 0.85; alpha = 0.4; }
        else if (e.type === 'offer') { width = 1.4; alpha = 0.3; }
        else if (e.type === 'award') { col = P.award; width = 2.6; alpha = 0.85; }
        else { width = 1.5 + Math.min(3, e.weight * 0.3); alpha = 0.32; }
        if (on && (dim || soft)) { alpha = Math.max(alpha, 0.95); width += 1.2; }
        if (pathOn || st.edge === e.id) { col = P.orange; width = 5; alpha = 1; dash = null; }
        c.globalAlpha = a * alpha; c.strokeStyle = col; c.lineWidth = width / k; c.lineCap = 'round';
        if (dash) { c.setLineDash(dash.map(d => d / k)); c.lineDashOffset = live ? -(now * 0.02) / k : 0; }
        strokeCurve(c, e, geom); c.setLineDash([]);
        if (e.type === 'award' && a > 0.2) { const p = pointOn(e, geom, 0.985), q = pointOn(e, geom, 0.93); const ang = Math.atan2(p.y - q.y, p.x - q.x); const tx = e.t.x - Math.cos(ang) * 18, ty = e.t.y - Math.sin(ang) * 18;
          c.fillStyle = col; c.beginPath(); c.moveTo(tx, ty); c.lineTo(tx - Math.cos(ang) * 13 - Math.sin(ang) * 7, ty - Math.sin(ang) * 13 + Math.cos(ang) * 7); c.lineTo(tx - Math.cos(ang) * 13 + Math.sin(ang) * 7, ty - Math.sin(ang) * 13 - Math.cos(ang) * 7); c.fill(); }
        // travelling sparks give emphasised links direction and life
        if (live && a > 0.3 && (pathOn || (on && dim))) {
          const n = pathOn ? 2 : 1;
          for (let s = 0; s < n; s++) { const tt = ((now * (pathOn ? 0.0005 : 0.00022) + (Math.abs(hash(e.id)) % 997) / 997 + s / n) % 1); const p = pointOn(e, geom, tt);
            c.globalAlpha = a * (pathOn ? 1 : 0.85); c.fillStyle = pathOn ? P.orange : col; c.beginPath(); c.arc(p.x, p.y, (pathOn ? 4.5 : 3) / k, 0, Math.PI * 2); c.fill(); }
        }
      }
      c.globalAlpha = 1;
      // nodes
      const order = shownNodes().sort((x, y) => (rel.nodesOn.has(x) ? 1 : 0) - (rel.nodesOn.has(y) ? 1 : 0) || x.r - y.r);
      for (const n of order) {
        const on = rel.nodesOn.has(n); const sel = n.type === 'supplier' && st.selected.includes(n.entityId);
        n.hoverT += ((st.hover === n ? 1 : 0) - n.hoverT) * 0.2; n.selT += ((sel ? 1 : 0) - n.selT) * 0.15;
        const a = n.alpha * (dim ? (on ? 1 : 0.18) : soft && !on ? 0.55 : n.quiet ? 0.5 : 1);
        const r = n.r * vs * (1 + n.hoverT * 0.16);
        c.globalAlpha = a;
        if (n.type === 'supplier') {
          const col = catColor(n.cat);
          if (n.selT > 0.02) { const pulse = live ? (Math.sin(now * 0.004) + 1) / 2 : 0.5; c.beginPath(); c.arc(n.x, n.y, r + 10 + pulse * 7, 0, Math.PI * 2); c.fillStyle = rgba(P.orange, 0.14 * n.selT); c.fill(); }
          if (n.rounds > 0) {
            const grad = c.createRadialGradient(n.x - r * 0.35, n.y - r * 0.45, r * 0.08, n.x, n.y, r);
            grad.addColorStop(0, mix(col, '#ffffff', 0.5)); grad.addColorStop(0.55, col); grad.addColorStop(1, mix(col, '#000000', 0.28));
            c.shadowColor = rgba(col, P.dark ? 0.6 : 0.35); c.shadowBlur = (on || st.hover === n ? 26 : 12) * k; c.shadowOffsetY = 3 * k;
            c.beginPath(); c.arc(n.x, n.y, r, 0, Math.PI * 2); c.fillStyle = grad; c.fill();
            c.shadowColor = 'transparent'; c.shadowBlur = 0; c.shadowOffsetY = 0;
            c.lineWidth = 2.2 / k; c.strokeStyle = n.approved ? rgba('#ffffff', P.dark ? 0.55 : 0.95) : P.surface; if (!n.approved) c.setLineDash([4 / k, 3 / k]); c.stroke(); c.setLineDash([]);
          } else {
            c.beginPath(); c.arc(n.x, n.y, r, 0, Math.PI * 2); c.fillStyle = rgba(col, P.dark ? 0.18 : 0.12); c.fill();
            c.lineWidth = 2 / k; c.strokeStyle = col; c.setLineDash(n.approved ? [] : [4 / k, 3 / k]); c.stroke(); c.setLineDash([]);
          }
          c.save(); c.translate(n.x - r * 0.52, n.y - r * 0.55); c.scale(r / 23, r / 23); c.lineWidth = 2; c.lineCap = 'round'; c.lineJoin = 'round';
          c.strokeStyle = n.rounds > 0 ? 'rgba(255,255,255,.96)' : col; c.stroke(ICON.supplier); c.restore();
          if (n.selT > 0.02) { c.globalAlpha = a * n.selT; c.beginPath(); c.arc(n.x, n.y, r + 5.5, 0, Math.PI * 2); c.lineWidth = 3 / k; c.strokeStyle = P.orange; c.stroke(); }
          if (n.wins > 0) { const bx = n.x + r * 0.74, by = n.y - r * 0.74; c.globalAlpha = a; c.beginPath(); c.arc(bx, by, 8.5 * vs, 0, Math.PI * 2); c.fillStyle = P.award; c.fill(); c.lineWidth = 2 / k; c.strokeStyle = P.surface; c.stroke();
            c.fillStyle = '#fff'; c.font = `700 ${10 * vs}px ${font()}`; c.textAlign = 'center'; c.textBaseline = 'middle'; c.fillText(String(n.wins), bx, by + 0.5); }
        } else {
          const col = n.type === 'round' ? P.ink2 : P.violet;
          c.beginPath();
          if (n.type === 'round') roundRect(c, n.x - r, n.y - r, r * 2, r * 2, 8);
          else { c.moveTo(n.x, n.y - r); c.lineTo(n.x + r, n.y); c.lineTo(n.x, n.y + r); c.lineTo(n.x - r, n.y); }
          c.closePath(); c.fillStyle = P.surface; c.shadowColor = 'rgba(0,0,0,.28)'; c.shadowBlur = 10 * k; c.fill(); c.shadowBlur = 0; c.shadowColor = 'transparent';
          c.lineWidth = (on && dim ? 2.6 : 1.6) / k; c.strokeStyle = n.type === 'round' ? (on && dim ? P.orange : rgba(P.ink2, 0.55)) : col; c.stroke();
          c.save(); c.translate(n.x - r * 0.62, n.y - r * 0.62); c.scale(r / 19.5, r / 19.5); c.lineWidth = 1.8; c.strokeStyle = col; c.stroke(ICON[n.type]); c.restore();
        }
      }
      c.globalAlpha = 1;
      // pulses: analyst actions, focus and replay arrivals
      st.pulses = st.pulses.filter(p => now - p.t0 < 1400);
      if (!exporting) for (const p of st.pulses) { const q = (now - p.t0) / 1400; c.globalAlpha = (1 - q) * 0.85; c.beginPath(); c.arc(p.n.x, p.n.y, p.n.r + 6 + q * 80, 0, Math.PI * 2); c.lineWidth = (3 - q * 2) / k; c.strokeStyle = p.color; c.stroke(); }
      c.globalAlpha = 1;
      c.restore();
      labels(c, w, h, rel, dim);
    }

    function labels(c, w, h, rel, dim) {
      // Names stay at a readable screen size whatever the zoom; zoom only changes how many fit. Greedy collision placement.
      const k = st.cam.k; const placed = []; st.groupRects = [];
      const over = r => placed.some(p => r.x < p.x + p.w && r.x + r.w > p.x && r.y < p.y + p.h && r.y + r.h > p.y);
      const pill = (text, x, y, size, weight, color, bg, border, alpha, opts = {}) => {
        c.font = `${weight} ${size}px ${font()}`; const tw = c.measureText(text).width; const pw = tw + (opts.padX ?? 9) * 2, ph = size + (opts.padY ?? 5) * 2;
        const r = { x: x - pw / 2, y, w: pw, h: ph };
        if (!opts.force && over(r)) return null;
        placed.push(r); c.globalAlpha = alpha;
        if (bg) { c.beginPath(); roundRect(c, r.x, r.y, r.w, r.h, Math.min(ph / 2, 11)); c.fillStyle = bg; c.fill(); if (border) { c.lineWidth = 1.2; c.strokeStyle = border; c.stroke(); } }
        c.fillStyle = color; c.textAlign = 'center'; c.textBaseline = 'middle'; c.fillText(text, x, y + ph / 2 + 0.5);
        c.globalAlpha = 1; return r;
      };
      const bg = rgba(P.surface, P.dark ? 0.8 : 0.88);
      for (const gr of st.groups) {
        if (gr.kind === 'orbit') {
          const ms = gr.members.map(node).filter(n => n?.shown && !n.future); if (!ms.length) continue;
          const sp = toScreen((Math.min(...ms.map(n => n.x)) + Math.max(...ms.map(n => n.x))) / 2, Math.min(...ms.map(n => n.y)) - 58);
          pill(`${gr.label.toUpperCase()} · ${ms.length}`, sp.x, sp.y, 12, 700, P.muted, null, null, 0.9, { force: true });
          continue;
        }
        if (!gr.shape) continue;
        const sp = toScreen(gr.shape.cx, gr.shape.top); const focus = st.focusGroup === gr.id;
        const on = !dim || [...rel.nodesOn].some(n => n.group === gr.id) || focus;
        const count = gr.members.filter(id => node(id)?.shown && !node(id).future).length;
        const text = `${gr.label}  ·  ${count}`;
        const color = P.dark ? mix(gr.color, '#ffffff', 0.4) : mix(gr.color, '#000000', 0.3);
        // Title pill above the hull; if another title is already there, try the sides, then below the hull.
        const bottom = toScreen(gr.shape.cx, Math.max(...gr.shape.h.map(q => q.y))).y;
        c.font = `700 15.5px ${font()}`; const shift = c.measureText(text).width / 2 + 20;
        const spots = [[sp.x, sp.y - 18], [sp.x - shift, sp.y - 18], [sp.x + shift, sp.y - 18], [sp.x, sp.y - 54], [sp.x, bottom + 6]];
        const args = [text, 0, 0, 15.5, 700, color, focus ? rgba(gr.color, P.dark ? 0.3 : 0.18) : rgba(P.surface, P.dark ? 0.9 : 0.95), rgba(gr.color, on ? 0.7 : 0.2), on ? 1 : 0.45];
        let r = null;
        for (const [x, y] of spots) { r = pill(args[0], x, y, ...args.slice(3), { padX: 14, padY: 7 }); if (r) break; }
        if (!r) r = pill(args[0], spots[0][0], spots[0][1], ...args.slice(3), { force: true, padX: 14, padY: 7 });
        if (r) st.groupRects.push({ ...r, id: gr.id });
      }
      const nodes = shownNodes().filter(n => n.alpha > 0.3);
      // Node discs are obstacles too, so a name never covers a neighbouring supplier.
      const discs = nodes.map(n => { const s = toScreen(n.x, n.y); const r = n.r * k * vis() * 0.92; return { x: s.x - r, y: s.y - r, w: r * 2, h: r * 2 }; });
      placed.push(...discs);
      const imp = n => (st.hover === n ? 3000 : 0) + (rel.nodesOn.has(n) ? 1000 : 0) + (n.type === 'supplier' ? 100 + (n.rounds || 0) : n.type === 'round' ? 50 : 0);
      nodes.sort((a, b) => imp(b) - imp(a));
      for (const n of nodes) {
        const important = rel.nodesOn.has(n) || st.hover === n;
        if (k < 0.45 && !important && n.type !== 'supplier') continue;
        if (dim && !important && n.type !== 'supplier') continue;
        const s = toScreen(n.x, n.y); const r = n.r * k * vis() * (1 + n.hoverT * 0.16);
        if (s.x < -90 || s.x > w + 90 || s.y < -50 || s.y > h + 50) continue;
        const size = n.type === 'supplier' ? (k < 0.55 ? 13.5 : 15) : 13.5;
        const weight = n.type === 'supplier' ? (important ? 750 : 650) : 600;
        const col = important ? P.ink : n.quiet ? P.muted : P.ink2;
        const text = clip(n.label, n.type === 'round' ? 28 : 30);
        const a = dim && !important ? 0.4 : n.quiet ? 0.65 : 1;
        c.font = `${weight} ${size}px ${font()}`; const half = c.measureText(text).width / 2 + 9; const ph = size + 10;
        const below = [s.x, s.y + r + 6], above = [s.x, s.y - r - ph - 4], right = [s.x + r + 8 + half, s.y - ph / 2], left = [s.x - r - 8 - half, s.y - ph / 2];
        const cands = n.type === 'round' ? [above, below, right, left] : [below, above, right, left];
        let done = null;
        for (const [x, y] of cands) { done = pill(text, x, y, size, weight, col, bg, null, a); if (done) break; }
        if (!done && (important || n.type === 'supplier' && k >= 0.55)) pill(text, cands[0][0], cands[0][1], size, weight, col, bg, null, a, { force: true });
      }
    }

    // ---------- loop ----------
    let raf = 0, lastDraw = 0;
    function tick(now) {
      raf = 0;
      if (!st.ready || !st.visible || document.hidden) return;
      let fading = false;
      st.nodes.forEach(n => { const ta = n.shown && !n.future ? 1 : 0; if (Math.abs(n.alpha - ta) > 0.01) { n.alpha += (ta - n.alpha) * (reducedMotion() ? 1 : 0.12); fading = true; } else n.alpha = ta; });
      const energy = step(now);
      const c = st.cam, t = st.camT; const sp = reducedMotion() ? 1 : st.camSpeed;
      const moving = Math.abs(c.x - t.x) + Math.abs(c.y - t.y) > 0.05 || Math.abs(c.k - t.k) > 0.0005;
      if (moving) { c.x += (t.x - c.x) * sp; c.y += (t.y - c.y) * sp; c.k += (t.k - c.k) * sp; }
      const live = st.motion && !reducedMotion();
      const busy = live || moving || fading || energy > 0.05 || st.pulses.length || st.drag || st.hoverAnim;
      if (busy || now - lastDraw > 500) { g.setTransform(st.dpr, 0, 0, st.dpr, 0, 0); draw(g, st.w, st.h, now); lastDraw = now; }
      st.hoverAnim = [...st.nodes.values()].some(n => Math.abs(n.hoverT - (st.hover === n ? 1 : 0)) > 0.01 || Math.abs(n.selT - (n.type === 'supplier' && st.selected.includes(n.entityId) ? 1 : 0)) > 0.01);
      if (busy || st.hoverAnim) raf = requestAnimationFrame(tick);
    }
    function wake() { if (!raf && st.ready) raf = requestAnimationFrame(tick); }
    new IntersectionObserver(es => { st.visible = es[0].isIntersecting; wake(); }, { threshold: 0.01 }).observe(canvas);
    document.addEventListener('visibilitychange', wake);
    function resize() {
      const r = host.getBoundingClientRect(); st.dpr = Math.min(2, window.devicePixelRatio || 1);
      st.w = Math.max(10, r.width); st.h = Math.max(10, r.height);
      canvas.width = Math.round(st.w * st.dpr); canvas.height = Math.round(st.h * st.dpr); canvas.style.width = `${st.w}px`; canvas.style.height = `${st.h}px`;
      if (st.ready) { g.setTransform(st.dpr, 0, 0, st.dpr, 0, 0); draw(g, st.w, st.h, performance.now()); wake(); }
    }
    let resizeTimer = 0;
    new ResizeObserver(() => { clearTimeout(resizeTimer); resizeTimer = setTimeout(resize, 30); }).observe(host);
    window.addEventListener('orange:sidebar', () => setTimeout(() => { if (!st.ready) return; resize(); if (!st.crumbs.length && !st.focusGroup && !st.path) { layout(st.view); st.settle = 40; frameView({ speed: 0.08 }); wake(); } }, 330));

    // ---------- init & views ----------
    function init(data) {
      st.data = data; readPalette(); fontCache = '';
      const cats = [...new Set(data.suppliers.map(s => catKey(s.category)))].sort((a, b) => a.localeCompare(b));
      st.catColor = new Map(cats.filter(c => c !== 'Uncategorized').map((c, i) => [c, i]));
      st.maxRounds = Math.max(1, ...data.suppliers.map(s => s.participations));
      if (!st.opts.competitionMinTouched) st.opts.competitionMin = data.network.competitionMinimum;
      $('[data-net-opt="competition-min"]').value = String(st.opts.competitionMin);
      if (st.cats) st.cats = new Set([...st.cats].filter(c => cats.includes(c)));
      const first = !st.ready;
      build(data);
      renderCatFilter(cats);
      const empty = $('[data-net-empty]'); empty.hidden = data.suppliers.length > 0;
      if (!data.suppliers.length) empty.innerHTML = '<div><strong>No registered suppliers yet</strong>The network appears as soon as suppliers are registered.</div>';
      st.selected = st.selected.filter(id => data.suppliers.some(s => s.supplierId === id));
      resize(); st.ready = true;
      setView(st.view, { animate: !first, reveal: first });
      updateMotionButton();
    }

    function setView(view, { animate = true, reveal = false, fromCrumb = false } = {}) {
      if (!VIEW[view] || !st.data) return;
      cancel();
      if (!fromCrumb) { st.crumbs = []; st.ego = null; st.focusGroup = null; st.path = null; }
      st.view = view;
      $$('[data-net-view]').forEach(b => b.setAttribute('aria-checked', b.dataset.netView === view));
      $('[data-net-title]').textContent = VIEW[view].title;
      $('[data-net-caption]').textContent = VIEW[view].caption;
      layout(view); st.groupsByView[view] = st.groups;
      applyReplay();
      if (reveal && !reducedMotion()) {
        // Opening: the market condenses from a soft cloud into its groups.
        st.nodes.forEach(n => { const a = Math.random() * Math.PI * 2, d = 300 + Math.random() * 420; n.x = n.ax * 0.1 + Math.cos(a) * d; n.y = n.ay * 0.1 + Math.sin(a) * d * 0.7; n.vx = n.vy = 0; n.alpha = 0; });
        st.settle = 80; frameView({ instant: true }); st.cam.k = st.camT.k * 0.8;
      } else if (!animate || reducedMotion()) { st.nodes.forEach(n => { if (n.shown) { n.x = n.ax; n.y = n.ay; n.alpha = 1; } }); frameView({ instant: true }); }
      else {
        st.nodes.forEach(n => { if (n.shown && n.alpha < 0.05) { n.x = n.ax + (Math.random() - 0.5) * 60; n.y = n.ay + (Math.random() - 0.5) * 60; } });
        st.settle = 70; frameView({ speed: 0.06 });
      }
      renderGroups(); renderLegend(); renderCounts(); renderCrumbs(); renderRelChips(); renderSelectionChips();
      const e = st.edge && st.edges.find(x => x.id === st.edge);
      if (e?.shown) inspectEdge(e); else if (st.selected.length) openSelectionInspector(); else closeInspector(false);
      if (!$('[data-net-table]').hidden) renderTable();
      ctx.onViewChange?.(view);
      wake();
    }

    // ---------- selection & focus ----------
    const viewRect = () => { const a = toWorld(0, 0), b = toWorld(st.w, st.h); return { x1: a.x, y1: a.y, x2: b.x, y2: b.y }; };
    const visibleIn = b => { const v = viewRect(); return b.x1 >= v.x1 && b.x2 <= v.x2 && b.y1 >= v.y1 && b.y2 <= v.y2; };
    function syncSelection(ids, { center = false, inspect = true } = {}) {
      st.selected = ids.slice(0, 5); st.edge = null; st.path = null;
      if (!st.ready) return;
      renderSelectionChips();
      const ns = st.selected.map(id => node(`s-${id}`)).filter(n => n?.shown);
      if (center && ns.length) { const b = bounds(ns.map(n => ({ ax: n.x, ay: n.y })), 170); if (!visibleIn(b)) frame(b, { maxK: Math.max(st.cam.k, 1), speed: 0.06 }); }
      ns.forEach(n => st.pulses.push({ n, t0: performance.now(), color: P.orange }));
      if (inspect) { if (st.selected.length) openSelectionInspector(); else if (!st.focusGroup) closeInspector(false); }
      wake();
    }
    function pushCrumb() { st.crumbs.push({ focusGroup: st.focusGroup, ego: st.ego, edge: st.edge, cam: { ...st.camT } }); if (st.crumbs.length > 8) st.crumbs.shift(); }
    function focusGroup(id, { push = true } = {}) {
      const gr = st.groups.find(x => x.id === id); if (!gr || gr.kind === 'orbit') return;
      cancel(); if (push) pushCrumb();
      st.focusGroup = id; st.edge = null; st.path = null;
      const ms = gr.members.concat(gr.attrs || []).map(node).filter(n => n?.shown);
      frame(bounds(ms.map(n => ({ ax: n.x, ay: n.y })), 130), { maxK: 1.7, speed: 0.055 });
      ms.forEach((n, i) => later(() => { st.pulses.push({ n, t0: performance.now(), color: gr.color }); wake(); }, 120 + i * 45));
      renderCrumbs(); renderGroups(); inspectGroup(gr); wake();
    }
    function back() {
      const prev = st.crumbs.pop();
      if (!prev) { reset(); return; }
      cancel();
      const wasEgo = st.ego;
      st.focusGroup = prev.focusGroup; st.edge = prev.edge; st.ego = prev.ego; st.path = null;
      if (wasEgo && !prev.ego) { layout(st.view); applyReplay(); st.settle = 60; }
      st.camT = { ...prev.cam }; st.camSpeed = 0.06;
      if (st.focusGroup) inspectGroup(st.groups.find(x => x.id === st.focusGroup)); else if (st.selected.length) openSelectionInspector(); else closeInspector(false);
      renderCrumbs(); renderGroups(); renderCounts(); wake();
    }
    function reset() { st.crumbs = []; st.focusGroup = null; st.edge = null; st.ego = null; st.path = null; setView(st.view, { animate: true }); }
    function egoFocus(id) {
      const c = node(`s-${id}`); if (!c) return;
      cancel(); pushCrumb();
      st.ego = id; st.focusGroup = null; st.edge = null; st.path = null;
      if (!st.selected.includes(id)) { st.selected = [id]; ctx.onSelect([id], { source: 'network-silent' }); }
      egoLayout(`s-${id}`); applyReplay(); st.settle = 70;
      frame(bounds(shownNodes().map(n => ({ ax: n.ax, ay: n.ay })), 110), { maxK: 1.35, speed: 0.055 });
      st.pulses.push({ n: c, t0: performance.now(), color: P.orange });
      renderCrumbs(); renderCounts(); renderGroups(); inspectSupplier(id); wake();
    }

    // ---------- edges & paths ----------
    function openEdge(id) {
      const e = st.edges.find(x => x.id === id); if (!e) return;
      cancel(); st.edge = id; st.path = null;
      const b = bounds([e.s, e.t].map(n => ({ ax: n.x, ay: n.y })), 160); if (!visibleIn(b)) frame(b, { maxK: Math.max(st.cam.k, 1), speed: 0.06 });
      inspectEdge(e); wake();
    }
    function tracePath(fromId, toId) {
      const a = node(`s-${fromId}`), b = node(`s-${toId}`);
      if (!a || !b || !a.shown || !b.shown) { inspectPath(fromId, toId, null, 'One of the suppliers is hidden by the current filters.'); return false; }
      const adj = new Map();
      shownEdges().forEach(e => [[e.s, e.t], [e.t, e.s]].forEach(([x, y]) => { if (!adj.has(x)) adj.set(x, []); adj.get(x).push([y, e]); }));
      const prev = new Map([[a, null]]); const q = [a];
      while (q.length && !prev.has(b)) { const u = q.shift(); (adj.get(u) || []).forEach(([v, e]) => { if (!prev.has(v)) { prev.set(v, [u, e]); q.push(v); } }); }
      if (!prev.has(b)) { inspectPath(fromId, toId, null); return false; }
      const nodes = [b], es = []; let cur = b; while (prev.get(cur)) { const [u, e] = prev.get(cur); es.unshift(e); nodes.unshift(u); cur = u; }
      const epoch = cancel();
      st.edge = null; st.selected = [fromId, toId];
      st.path = { nodes, edges: es, lit: new Set() };
      frame(bounds(nodes.map(n => ({ ax: n.x, ay: n.y })), 160), { maxK: 1.35, speed: 0.055 });
      const stepMs = reducedMotion() ? 0 : Math.min(340, 1300 / Math.max(1, es.length));
      es.forEach((e, i) => later(() => { st.path?.lit.add(e); st.pulses.push({ n: e.t === nodes[i] ? e.s : e.t, t0: performance.now(), color: P.orange }); wake(); }, (reducedMotion() ? 0 : 500) + i * stepMs, epoch));
      ctx.onSelect([fromId, toId], { source: 'network-silent' });
      renderSelectionChips(); inspectPath(fromId, toId, { nodes, edges: es }); wake();
      return true;
    }
    function lightUp(es, epoch) {
      const ns = [...new Set(es.flatMap(e => [e.s, e.t]))];
      st.path = { nodes: ns, edges: es, lit: new Set() };
      frame(bounds(ns.map(n => ({ ax: n.x, ay: n.y })), 170), { maxK: 1.35, speed: 0.05 });
      es.forEach((e, i) => later(() => { st.path?.lit.add(e); wake(); }, reducedMotion() ? 0 : 450 + i * 200, epoch));
    }
    // ---------- replay ----------
    function applyReplay() {
      const t = st.replayAt;
      st.nodes.forEach(n => (n.future = t != null && (n.avail || 0) > t));
      st.edges.forEach(e => (e.future = t != null && ((e.type === 'competition' ? competitionAvail(e) : e.avail) || 0) > t));
    }
    function setReplayAt(t) {
      const before = new Set(shownNodes());
      st.replayAt = t; applyReplay();
      if (t != null) shownNodes().filter(n => !before.has(n)).forEach(n => st.pulses.push({ n, t0: performance.now(), color: P.orange }));
      $('[data-net-replaying]').hidden = t == null;
      if (t != null) $('[data-net-replaying]').textContent = `Replay · ${fmt.date(t)}`;
      renderCounts(); wake();
    }

    // ---------- pointer interaction ----------
    function hit(px, py) {
      const w = toWorld(px, py); const k = st.cam.k;
      let best = null, bd = Infinity;
      for (const n of shownNodes()) { if (n.alpha < 0.3) continue; const d = Math.hypot(n.x - w.x, n.y - w.y); if (d < n.r * vis() + 8 / k && d < bd) { best = n; bd = d; } }
      if (best) return { node: best };
      const gl = st.groupRects.find(r => px >= r.x && px <= r.x + r.w && py >= r.y && py <= r.y + r.h);
      if (gl) return { group: gl.id };
      let be = null; bd = 9 / k;
      for (const e of shownEdges()) { if (!e.geom) continue; for (let i = 0; i <= 18; i++) { const p = pointOn(e, e.geom, i / 18); const d = Math.hypot(p.x - w.x, p.y - w.y); if (d < bd) { bd = d; be = e; } } }
      if (be) return { edge: be };
      for (const gr of st.groups) if (gr.shape && inside(w, gr.shape.h)) return { group: gr.id, area: true };
      return {};
    }
    function inside(p, poly) { let c = false; for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) { const a = poly[i], b = poly[j]; if ((a.y > p.y) !== (b.y > p.y) && p.x < ((b.x - a.x) * (p.y - a.y)) / (b.y - a.y) + a.x) c = !c; } return c; }
    let down = null, lastClick = { t: 0, id: null };
    const pos = e => { const r = canvas.getBoundingClientRect(); return { px: e.clientX - r.left, py: e.clientY - r.top }; };
    canvas.addEventListener('pointerdown', e => {
      canvas.setPointerCapture(e.pointerId); const { px, py } = pos(e);
      down = { px, py, h: hit(px, py), moved: false, cam: { ...st.cam }, shift: e.shiftKey || e.ctrlKey || e.metaKey };
      if (motion.timers.size) cancel();
    });
    canvas.addEventListener('pointermove', e => {
      const { px, py } = pos(e);
      if (down) {
        if (!down.moved && Math.hypot(px - down.px, py - down.py) > 4) { down.moved = true; if (down.h.node) { st.drag = { node: down.h.node }; tip(false); } }
        if (down.moved) {
          if (st.drag) { const w = toWorld(px, py); Object.assign(st.drag.node, { x: w.x, y: w.y, vx: 0, vy: 0 }); canvas.style.cursor = 'grabbing'; }
          else { st.cam.x = down.cam.x - (px - down.px) / st.cam.k; st.cam.y = down.cam.y - (py - down.py) / st.cam.k; st.camT = { ...st.cam }; canvas.style.cursor = 'grabbing'; }
          wake();
        }
        return;
      }
      const h = hit(px, py);
      if ((h.node || null) !== st.hover || (h.group || null) !== st.hoverGroup) { st.hover = h.node || null; st.hoverGroup = h.group || null; wake(); }
      canvas.style.cursor = h.node || h.edge || (h.group && !h.area) ? 'pointer' : h.group ? 'zoom-in' : 'grab';
      if (h.node || h.edge || (h.group && !h.area)) tip(true, h, px, py); else tip(false);
    });
    canvas.addEventListener('pointerleave', () => { if (!down) { st.hover = null; st.hoverGroup = null; tip(false); wake(); } });
    canvas.addEventListener('pointerup', () => {
      const d = down; down = null; canvas.style.cursor = '';
      if (!d) return;
      if (st.drag) { const n = st.drag.node; n.ax = n.x; n.ay = n.y; st.drag = null; wake(); return; }
      if (d.moved) return;
      const h = d.h;
      if (h.node) {
        const n = h.node; const now = performance.now();
        if (lastClick.id === n.id && now - lastClick.t < 330 && n.type === 'supplier') { egoFocus(n.entityId); lastClick = { t: 0, id: null }; return; }
        lastClick = { t: now, id: n.id };
        if (n.type === 'supplier') { st.edge = null; st.path = null; ctx.onSelect([n.entityId], { additive: d.shift, source: 'network' }); }
        else if (n.type === 'round') inspectRound(n.entityId);
      } else if (h.edge) openEdge(h.edge.id);
      else if (h.group) focusGroup(h.group);
      else if (st.edge || st.focusGroup || st.selected.length || st.path) closeInspector(true);
    });
    canvas.addEventListener('wheel', e => {
      e.preventDefault(); const { px, py } = pos(e);
      const before = toWorld(px, py); st.cam.k = Math.max(0.25, Math.min(3, st.cam.k * Math.exp(-e.deltaY * 0.0014)));
      const after = toWorld(px, py); st.cam.x += before.x - after.x; st.cam.y += before.y - after.y; st.camT = { ...st.cam }; tip(false); wake();
    }, { passive: false });
    host.addEventListener('keydown', e => {
      if (e.key === 'Escape') { if (st.crumbs.length) back(); else closeInspector(true); }
      else if (e.key === '+' || e.key === '=') tool('zoom-in');
      else if (e.key === '-') tool('zoom-out');
      else if (e.key === '0') tool('fit');
      else if (e.key === ' ') { e.preventDefault(); tool('motion'); }
    });

    // ---------- tooltip ----------
    let tipEl = null;
    function tip(show, h, px, py) {
      if (!tipEl) { tipEl = document.createElement('div'); tipEl.className = 'rd-tip'; tipEl.setAttribute('role', 'tooltip'); $('[data-net-canvas]').append(tipEl); }
      if (!show) { tipEl.classList.remove('is-on'); return; }
      let html;
      if (h.edge) { const e = h.edge; html = `<b>${esc(REL[e.type]?.label || e.type)}</b><span>${esc(e.s.label)} – ${esc(e.t.label)}</span><span>${e.type === 'competition' ? `${e.weight} shared closed bidding cycles` : 'Recorded offer or award'}</span>`; }
      else if (h.node) { const n = h.node; html = n.type === 'supplier' ? `<b>${esc(n.label)}</b><span>${esc(n.cat)} · ${esc(n.status)}</span><span>${n.rounds} closed round${n.rounds === 1 ? '' : 's'} with an offer${n.wins ? ` · ${n.wins} award${n.wins > 1 ? 's' : ''}` : ''}</span><em>Click · double-click to focus · drag</em>` : `<b>${esc(n.label)}</b><span>${n.type === 'round' ? `Closed ${fmt.date(n.at)} · click for bidders` : esc(n.type)}</span>`; }
      else { const gr = st.groups.find(x => x.id === h.group); html = `<b>${esc(gr?.label || '')}</b><span>${gr?.members.length || 0} suppliers · click to explore</span>`; }
      tipEl.innerHTML = html;
      tipEl.style.left = `${Math.min(st.w - 280, Math.max(8, px + 18))}px`; tipEl.style.top = `${Math.min(st.h - 120, Math.max(8, py + 18))}px`;
      tipEl.classList.add('is-on');
    }

    // ---------- inspector ----------
    function openInspector(kicker, title, html) {
      $('[data-inspector-kicker]').textContent = kicker; $('[data-inspector-title]').textContent = title; $('[data-inspector-body]').innerHTML = html;
      if (inspector.hidden) { inspector.hidden = false; stage.classList.add('has-inspector'); requestAnimationFrame(() => stage.classList.add('is-open')); }
    }
    function closeInspector(clear = true) {
      if (clear) { st.edge = null; st.focusGroup = null; st.path = null; if (st.selected.length) { st.selected = []; ctx.onSelect([], { source: 'network-silent' }); } renderSelectionChips(); renderGroups(); renderCrumbs(); wake(); }
      if (inspector.hidden) return;
      stage.classList.remove('is-open');
      setTimeout(() => { if (!stage.classList.contains('is-open')) { inspector.hidden = true; stage.classList.remove('has-inspector'); } }, reducedMotion() ? 0 : 300);
    }
    const statusBadge = s => (s.status === 'Approved' ? '<span class="rd-badge is-ok">Approved</span>' : `<span class="rd-badge is-muted">${esc(s.status)}</span>`);
    const kv = rows => `<dl class="rd-kv">${rows.filter(Boolean).map(([k, v, title]) => `<dt${title ? ` title="${esc(title)}"` : ''}>${esc(k)}</dt><dd>${v}</dd>`).join('')}</dl>`;
    function relationLines(n) {
      const es = shownEdges().filter(e => e.s === n || e.t === n);
      if (!es.length) return '';
      return `<ul class="rd-rel-list">${es.map(e => { const o = e.s === n ? e.t : e.s;
        const meta = e.type === 'competition' ? `${e.weight} shared closed bidding cycles` : e.type === 'offer' ? 'submitted offer' : 'official award';
        return `<li><button type="button" data-net-edge="${esc(e.id)}"><i class="rd-edge-key is-${esc(e.kind)}"></i><span><b>${esc(o.label)}</b><small>${esc(REL[e.type]?.label || e.type)} · ${esc(meta)}</small></span></button></li>`; }).join('')}</ul>`;
    }
    function inspectGroup(gr) {
      if (!gr) return;
      const members = gr.members.map(id => supplier(node(id).entityId)).filter(Boolean).sort((a, b) => b.participations - a.participations || a.name.localeCompare(b.name));
      const active = members.filter(m => m.participations > 0); const ids = new Set(gr.members);
      const es = shownEdges(); const inner = es.filter(e => ids.has(e.s.id) && ids.has(e.t.id)); const out = es.filter(e => ids.has(e.s.id) !== ids.has(e.t.id) && e.pair);
      const tally = list => Object.entries(list.reduce((m, e) => { const t = REL[e.type]?.label || e.type; m[t] = (m[t] || 0) + 1; return m; }, {}));
      const awards = {}; members.forEach(m => m.awardedValue.forEach(v => { awards[v.currency] = awards[v.currency] || { value: 0, count: 0 }; awards[v.currency].value += v.value; awards[v.currency].count += v.count; }));
      const cross = new Map(); if (st.view === 'category') out.forEach(e => { const o = ids.has(e.s.id) ? e.t : e.s; const key = `${o.cat}|${REL[e.type]?.label || e.type}`; cross.set(key, (cross.get(key) || 0) + 1); });
      const note = { category: 'A registered business category: membership is recorded at registration, not a relationship between these suppliers.', competition: 'Connected through shared closed bidding cycles. Transitive: two members may never have met directly.' }[gr.kind] || '';
      openInspector(gr.kind === 'category' ? 'Registered category' : 'Competition cluster', gr.label, `<p class="rd-note">${esc(note)}</p>
        <div class="rd-stat-row"><div><strong>${members.length}</strong><span>suppliers</span></div><div><strong>${active.length}</strong><span>bid in a closed round</span></div><div><strong>${members.reduce((s, m) => s + m.participations, 0)}</strong><span>rounds with an offer</span></div></div>
        ${Object.keys(awards).length ? kv(Object.entries(awards).map(([c, v]) => [`Official awards (${c})`, `${fmt.money(c, v.value)} · ${v.count}`])) : '<p class="rd-muted">No official award recorded for these suppliers in this period.</p>'}
        <h4 class="rd-section-title">Links inside (${inner.length})</h4>${inner.length ? `<ul class="rd-tally">${tally(inner).map(([t, n]) => `<li><span>${esc(t)}</span><b>${n}</b></li>`).join('')}</ul>` : '<p class="rd-muted">No supported link between members in this view.</p>'}
        ${st.view === 'category' ? `<h4 class="rd-section-title">Links to other categories</h4>${cross.size ? `<ul class="rd-tally">${[...cross].map(([key, n]) => { const [c, t] = key.split('|'); return `<li><span><i class="rd-dot" style="background:${catColor(c)}"></i>${esc(c)} · ${esc(t)}</span><b>${n}</b></li>`; }).join('')}</ul>` : '<p class="rd-muted">No real link crosses this category boundary.</p>'}` : ''}
        <h4 class="rd-section-title">Members</h4><ul class="rd-member-list">${members.map(m => `<li><button type="button" data-net-supplier="${m.supplierId}"><i class="rd-dot" style="background:${catColor(catKey(m.category))}"></i><span><b>${esc(short(m.name))}</b><small>${m.participations} closed round${m.participations === 1 ? '' : 's'}${m.wins ? ` · ${m.wins} award${m.wins > 1 ? 's' : ''}` : ''}</small></span>${m.status !== 'Approved' ? `<em>${esc(m.status)}</em>` : ''}</button></li>`).join('')}</ul>
        <div class="rd-actions">${active.length >= 2 ? `<button type="button" class="rd-btn" data-net-compare="${active.slice(0, 5).map(m => m.supplierId).join(',')}">Compare the most active</button>` : ''}<button type="button" class="rd-btn rd-btn-ghost" data-ask="${esc(gr.kind === 'category' ? `Tell me about the ${gr.label} supplier category.` : `Explain ${gr.label} in the ${st.view} view.`)}">Ask the analyst</button><button type="button" class="rd-btn rd-btn-ghost" data-net-tool="back">Back</button></div>`);
    }
    function inspectSupplier(id) {
      const s = supplier(id); if (!s) return;
      const others = st.selected.filter(x => x !== id); const d = st.data.definitions;
      openInspector('Supplier', short(s.name), `<div class="rd-who"><i class="rd-dot" style="background:${catColor(catKey(s.category))}"></i>${esc(catKey(s.category))} ${statusBadge(s)}</div>
        ${s.participations === 0 ? '<p class="rd-note">No offer in a closed round of this period. The supplier stays on the map as part of the registered market.</p>' : ''}
        <div class="rd-stat-row"><div><strong>${s.participations}</strong><span>rounds with an offer</span></div><div><strong>${s.participationRate == null ? '—' : fmt.pct(s.participationRate)}</strong><span>response rate</span></div><div><strong>${s.winRate == null ? '—' : fmt.pct(s.winRate)}</strong><span>${s.winRate == null ? 'no awarded tender bid on' : `win rate (${s.wins}/${s.awardedTendersBid})`}</span></div></div>
        ${kv([['Invited (closed bidding cycles)', s.invitations, d.participationRate], ['Awarded value', s.awardedValue.map(v => fmt.money(v.currency, v.value)).join('<br>') || '<span class="rd-muted">none recorded</span>'],
          ['Avg. price vs median', s.pricedRounds ? `${fmt.signed(s.relativePrice)} <small class="rd-muted">(${s.pricedRounds} rounds)</small>` : '<span class="rd-muted">no comparable round</span>', d.relativePrice],
          ['Activity', s.firstActivity ? `${fmt.date(s.firstActivity)} – ${fmt.date(s.lastActivity)}` : '<span class="rd-muted">none</span>']])}
        <h4 class="rd-section-title">Links in this view</h4>${relationLines(node(`s-${id}`)) || '<p class="rd-muted">No link in this view.</p>'}
        <div class="rd-actions">${st.ego === id ? '<button type="button" class="rd-btn" data-net-tool="back">Back to market</button>' : `<button type="button" class="rd-btn" data-net-ego="${id}">Focus & show its rounds</button>`}
          ${others.length ? `<button type="button" class="rd-btn" data-net-path="${others[0]},${id}">Trace path to ${esc(clip(short(supplier(others[0])?.name || ''), 18))}</button>` : ''}
          <button type="button" class="rd-btn rd-btn-ghost" data-net-add="${id}">${st.selected.length > 1 && st.selected.includes(id) ? 'Remove from comparison' : 'Add to comparison'}</button>
          <button type="button" class="rd-btn rd-btn-ghost" data-ask="Tell me about ${esc(short(s.name))}.">Ask the analyst</button><button type="button" class="rd-btn rd-btn-ghost" data-scenario="${id}">What if unavailable?</button></div>`);
    }
    function inspectCompare(ids) {
      const rows = ids.map(supplier).filter(Boolean);
      const links = st.data.network.competitionLinks.filter(l => ids.includes(l.supplierA) && ids.includes(l.supplierB));
      openInspector('Comparison', `${rows.length} suppliers`, `<table class="rd-table rd-compare"><thead><tr><th></th>${rows.map(r => `<th><i class="rd-dot" style="background:${catColor(catKey(r.category))}"></i>${esc(clip(short(r.name), 16))}</th>`).join('')}</tr></thead><tbody>
        ${[['Rounds with offer', r => r.participations], ['Invited', r => r.invitations], ['Response rate', r => fmt.pct(r.participationRate)], ['Wins / awarded bid', r => `${r.wins}/${r.awardedTendersBid}`], ['Avg. vs median', r => fmt.signed(r.relativePrice)], ['Compliance', r => (r.complianceChecked ? `${r.compliant}/${r.complianceChecked}` : '—')]].map(([k, f]) => `<tr><td>${k}</td>${rows.map(r => `<td class="num">${f(r)}</td>`).join('')}</tr>`).join('')}</tbody></table>
        <h4 class="rd-section-title">Shared closed bidding cycles</h4>${links.length ? `<ul class="rd-tally">${links.map(l => `<li><span>${esc(clip(short(supplier(l.supplierA)?.name || ''), 18))} · ${esc(clip(short(supplier(l.supplierB)?.name || ''), 18))}</span><b>${l.roundIds.length}</b></li>`).join('')}</ul>` : '<p class="rd-muted">They never bid in the same closed round.</p>'}
        <div class="rd-actions"><button type="button" class="rd-btn" data-net-path="${ids[0]},${ids[1]}">Trace relationship path</button><button type="button" class="rd-btn rd-btn-ghost" data-ask="Compare these suppliers.">Ask the analyst</button><button type="button" class="rd-btn rd-btn-ghost" data-scene-jump="market">See them in the charts</button></div>`);
    }
    function openSelectionInspector() { if (st.selected.length === 1) inspectSupplier(st.selected[0]); else if (st.selected.length > 1) inspectCompare(st.selected); }
    function inspectEdge(e) {
      const rounds = new Map(st.data.network.rounds.map(r => [r.roundId, r]));
      const names = `${esc(e.s.label)} <span aria-hidden="true">↔</span> ${esc(e.t.label)}`;
      const roundList = ids => `<ul class="rd-records">${ids.map(id => { const r = rounds.get(id); return r ? `<li><button type="button" data-net-round="${r.roundId}"><code>Round #${r.roundId}</code> ${esc(short(r.tender))} · ${r.roundNumber === 1 ? 'Initial Bidding' : 'Re-tendering — Cycle ' + r.roundNumber}<small>${fmt.date(r.availableAt)} · ${esc(r.department)}</small></button></li>` : `<li><code>Round #${id}</code></li>`; }).join('')}</ul>`;
      let html, kicker; const title = REL[e.type]?.label || e.type;
      if (e.type === 'competition') {
        kicker = 'Context relationship';
        html = `<p class="rd-pair">${names}</p><span class="rd-badge is-context">Context · not a signal</span><p>Both suppliers submitted an offer in <b>${e.weight}</b> distinct closed round${e.weight > 1 ? 's' : ''}. Each round counts once.</p>
          ${kv([['First shared round', fmt.date(e.first)], ['Latest shared round', fmt.date(e.last)], ['Shown from', `${st.opts.competitionMin} shared round${st.opts.competitionMin > 1 ? 's' : ''} (filter)`]])}
          <h4 class="rd-section-title">Supporting records (${e.roundIds.length})</h4>${roundList(e.roundIds)}<div class="rd-note"><b>Alternative explanation.</b> Suppliers in the same field are routinely invited to the same tenders; shared participation is ordinary competition.</div>`;
      } else if (e.type === 'offer' || e.type === 'award') {
        const r = rounds.get(e.roundId); const p = st.data.positions.find(x => x.supplierId === e.s.entityId && x.roundId === e.roundId);
        kicker = 'Recorded fact';
        html = `<p class="rd-pair">${esc(e.s.label)} → ${esc(e.t.label)}</p><span class="rd-badge">${e.type === 'award' ? 'Official award' : 'Submitted offer'}</span>
          ${kv([['Round', r ? `${esc(short(r.tender))} · ${r.roundNumber === 1 ? 'Initial Bidding' : 'Re-tendering — Cycle ' + r.roundNumber}` : `#${e.roundId}`], ['Visible since', r ? fmt.date(r.availableAt) : '—'], ['Department', r ? esc(r.department) : '—'], p ? ['Offer total', `${fmt.money(p.currency, p.total)} <small class="rd-muted">(${fmt.signed(p.relative)} vs median of ${p.comparable})</small>`] : null])}
          <p class="rd-muted">${e.type === 'award' ? 'The official award recorded on the tender. A recommendation is never shown as an award.' : 'Latest submitted revision in a closed round; totals include extra costs.'}</p>`;
      }
      openInspector(kicker, title, html);
    }
    function inspectRound(roundId) {
      const r = st.data.network.rounds.find(x => x.roundId === roundId); if (!r) return;
      const c = st.data.competition.rounds.find(x => x.roundId === roundId);
      const es = st.edges.filter(e => e.roundId === roundId && e.shown);
      st.selected = []; st.edge = null; st.path = { nodes: [node(`r-${roundId}`), ...r.supplierIds.map(id => node(`s-${id}`))].filter(Boolean), edges: es, lit: new Set(es) };
      openInspector('Closed round', `${short(r.tender)} · ${r.roundNumber === 1 ? 'Initial Bidding' : 'Re-tendering — Cycle ' + r.roundNumber}`, `${kv([['Closed / visible', fmt.date(r.availableAt)], ['Department', esc(r.department)], c ? ['Invited · submitted · compliant', `${c.invited} · ${c.submitted} · ${c.compliant}`] : null,
        c ? ['Below minimum offers', c.lowCompetition ? `<span class="rd-badge is-signal">yes (min ${st.data.competition.minimumBids})</span>` : 'no'] : null, ['Official award', r.awardedSupplierId ? esc(short(supplier(r.awardedSupplierId)?.name || '')) : '<span class="rd-muted">none recorded</span>']])}
        <h4 class="rd-section-title">Suppliers with an offer (${r.supplierIds.length})</h4><ul class="rd-member-list">${r.supplierIds.map(supplier).filter(Boolean).map(m => `<li><button type="button" data-net-supplier="${m.supplierId}"><i class="rd-dot" style="background:${catColor(catKey(m.category))}"></i><span><b>${esc(short(m.name))}</b><small>${esc(catKey(m.category))}</small></span>${r.awardedSupplierId === m.supplierId ? '<em>awarded</em>' : ''}</button></li>`).join('')}</ul>`);
      wake();
    }
    function inspectPath(a, b, path, reason) {
      const na = short(supplier(a)?.name || ''), nb = short(supplier(b)?.name || '');
      if (!path) { openInspector('Relationship path', `${na} → ${nb}`, `<p class="rd-note">${esc(reason || `No path connects them through the links shown in the ${st.view} view.`)}</p><p class="rd-muted">The tender view links suppliers through the rounds they bid in; the competition view through shared closed bidding cycles.</p><div class="rd-actions">${['category', 'competition', 'tender'].filter(v => v !== st.view).map(v => `<button type="button" class="rd-btn rd-btn-ghost" data-net-path-view="${v}" data-net-path="${a},${b}">Try ${v} view</button>`).join('')}</div>`); return; }
      const hops = path.edges.map((e, i) => { const from = path.nodes[i], to = path.nodes[i + 1]; return `<li><button type="button" data-net-edge="${esc(e.id)}"><b>${esc(from.label)}</b> → <b>${esc(to.label)}</b><small>${esc(REL[e.type]?.label || e.type)}${e.type === 'competition' ? ` · ${e.weight} rounds` : ''}</small></button></li>`; });
      openInspector('Relationship path', `${na} → ${nb}`, `<p>${hops.length} step${hops.length > 1 ? 's' : ''} in the ${esc(st.view)} view.</p><ol class="rd-hops">${hops.join('')}</ol><div class="rd-note">An indirect path shows how records connect; it is not evidence of a direct relationship.</div>`);
    }

    // ---------- chrome ----------
    function renderGroups() {
      const el = $('[data-net-groups]'); const orbit = st.groups.find(x => x.kind === 'orbit');
      el.innerHTML = (st.ego ? [] : st.groups.filter(x => x.kind !== 'orbit')).map(gr => `<button type="button" data-net-group="${esc(gr.id)}" aria-pressed="${gr.id === st.focusGroup}"><i class="rd-dot" style="background:${gr.color}"></i>${esc(gr.label)}<b>${gr.members.length}</b></button>`).join('')
        + (orbit && !st.ego ? `<span class="rd-groups-note"><i class="rd-dot is-hollow"></i>${esc(orbit.label)} · ${orbit.members.length}</span>` : '');
    }
    function renderLegend() {
      const v = st.view; const items = ['<span><i class="rd-lg-node"></i>Supplier · size = rounds bid</span>', '<span><i class="rd-lg-node is-hollow"></i>No closed-round offer</span>', '<span><i class="rd-lg-badge">2</i>Official awards</span>'];
      if (v === 'tender') items.push('<span><i class="rd-lg-round"></i>Closed round</span>', '<span><i class="rd-lg-edge"></i>Offer</span>', '<span><i class="rd-lg-edge" style="--c:var(--rd-award);--w:3px"></i>Official award</span>');
      else if (v === 'competition') items.push('<span><i class="rd-lg-edge" style="--w:4px"></i>Shared closed bidding cycles</span>');
      $('[data-net-legend]').innerHTML = items.join('');
    }
    function renderCounts() {
      if (!st.data) return;
      const total = st.data.suppliers.length; const ns = shownNodes(); const shown = ns.filter(n => n.type === 'supplier').length; const links = shownEdges().length;
      const hidden = total - visibleSuppliers().length;
      $('[data-net-counts]').innerHTML = `<span><b>${shown}</b><small>of ${total} suppliers</small></span>${st.view === 'tender' ? `<span><b>${ns.filter(n => n.type === 'round').length}</b><small>closed bidding cycles</small></span>` : `<span><b>${st.groups.filter(x => x.kind !== 'orbit').length}</b><small>${st.view === 'category' ? 'categories' : 'clusters'}</small></span>`}<span><b>${links}</b><small>${st.view === 'tender' ? 'offers' : 'links'}</small></span>${hidden ? `<span class="rd-muted">${hidden} filtered</span>` : ''}`;
      const filters = (st.cats ? 1 : 0) + (st.opts.inactive ? 0 : 1) + (st.opts.isolated ? 0 : 1) + (Object.keys(REL).filter(t => REL[t].views.includes(st.view)).some(t => !st.rel.has(t)) ? 1 : 0);
      const badge = $('[data-net-filter-badge]'); badge.hidden = !filters; badge.textContent = filters;
    }
    function renderCrumbs() {
      const el = $('[data-net-crumbs]'); const parts = [];
      if (st.focusGroup) parts.push(esc(st.groups.find(x => x.id === st.focusGroup)?.label || ''));
      if (st.ego) parts.push(`${esc(short(supplier(st.ego)?.name || ''))} · focus`);
      el.hidden = !parts.length;
      el.innerHTML = parts.length ? `<button type="button" class="rd-crumb-back" data-net-tool="back" aria-label="Back"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M15 6l-6 6 6 6"/></svg></button><button type="button" data-net-tool="reset">${esc({ category: 'Market', competition: 'Competition', tender: 'Tenders' }[st.view])}</button><i>/</i><span>${parts.join(' / ')}</span>` : '';
    }
    function renderSelectionChips() {
      const el = $('[data-net-selection]');
      el.innerHTML = st.selected.length < 2 ? '' : `${st.selected.map(id => `<span><i class="rd-dot" style="background:${catColor(catKey(supplier(id)?.category))}"></i>${esc(clip(short(supplier(id)?.name || ''), 20))}<button type="button" data-net-remove="${id}" aria-label="Remove">×</button></span>`).join('')}<button type="button" class="rd-link" data-net-tool="clear">Clear</button>`;
    }
    function renderRelChips() {
      const present = new Set(st.edges.map(e => e.type));
      $('[data-net-rel]').innerHTML = Object.entries(REL).filter(([, r]) => r.views.includes(st.view)).map(([t, r]) => `<button type="button" class="rd-chip${present.has(t) ? '' : ' is-none'}" data-net-rel-type="${t}" aria-pressed="${st.rel.has(t)}"><i class="rd-chip-key is-${t}"></i>${esc(r.label)}</button>`).join('');
    }
    function renderCatFilter(cats) {
      $('[data-net-cats]').innerHTML = cats.map(c => `<label><input type="checkbox" data-net-cat="${esc(c)}" ${!st.cats || st.cats.has(c) ? 'checked' : ''} /><i class="rd-dot" style="background:${catColor(c)}"></i>${esc(c)}</label>`).join('');
      $('[data-net-suggest]').innerHTML = st.data.suppliers.map(s => `<option value="${esc(short(s.name))}"></option>`).join('') + cats.map(c => `<option value="${esc(c)}">category</option>`).join('');
    }
    function renderTable() {
      const box = $('[data-net-table]'); if (box.hidden) return;
      const ns = shownNodes().filter(n => n.type === 'supplier').sort((a, b) => a.cat.localeCompare(b.cat) || a.label.localeCompare(b.label)); const es = shownEdges();
      box.innerHTML = `<div class="rd-table-grid"><div><h3>Suppliers in this view (${ns.length})</h3><table class="rd-table"><thead><tr><th>Supplier</th><th>Category</th><th>Status</th><th class="num">Rounds</th><th class="num">Links</th></tr></thead><tbody>
        ${ns.map(n => `<tr><td><button type="button" class="rd-link" data-net-supplier="${n.entityId}">${esc(n.label)}</button></td><td>${esc(n.cat)}</td><td>${esc(n.status)}</td><td class="num">${n.rounds}</td><td class="num">${es.filter(e => e.s === n || e.t === n).length}</td></tr>`).join('')}</tbody></table></div>
        <div><h3>Links in this view (${es.length})</h3><table class="rd-table"><thead><tr><th>Type</th><th>From</th><th>To</th><th>Class</th></tr></thead><tbody>
        ${es.map(e => `<tr><td><button type="button" class="rd-link" data-net-edge="${esc(e.id)}">${esc(REL[e.type]?.label || e.type)}</button></td><td>${esc(e.s.label)}</td><td>${esc(e.t.label)}</td><td>${e.kind === 'context' ? '<span class="rd-badge is-context">context</span>' : 'fact'}</td></tr>`).join('') || '<tr><td colspan="4" class="rd-muted">No link in this view.</td></tr>'}</tbody></table></div></div>`;
    }
    function updateMotionButton() {
      const b = $('[data-net-tool="motion"]'); if (!b) return;
      b.setAttribute('aria-pressed', String(!st.motion)); b.title = st.motion ? 'Pause motion (Space)' : 'Resume motion (Space)';
      const label = b.querySelector('[data-motion-label]'); if (label) label.textContent = st.motion ? 'Live' : 'Paused';
      b.classList.toggle('is-paused', !st.motion);
    }

    // ---------- events ----------
    root.addEventListener('click', e => {
      const t = e.target.closest('button, [data-net-supplier]'); if (!t) return;
      const d = t.dataset;
      if (d.netView) { setView(d.netView); try { localStorage.setItem('orange-radar:view', d.netView); } catch { /* storage blocked */ } }
      else if (d.netGroup) focusGroup(d.netGroup);
      else if (d.netSupplier) { const id = +d.netSupplier; if (!node(`s-${id}`)?.shown) setView(st.view); ctx.onSelect([id], { source: 'network', center: true }); }
      else if (d.netEdge) openEdge(d.netEdge);
      else if (d.netRound) inspectRound(+d.netRound);
      else if (d.netEgo) egoFocus(+d.netEgo);
      else if (d.netAdd || d.netRemove) ctx.onSelect([+(d.netAdd || d.netRemove)], { additive: true, source: 'network' });
      else if (d.netCompare) ctx.onSelect(d.netCompare.split(',').map(Number), { source: 'network' });
      else if (d.netPath) { const [a, b] = d.netPath.split(',').map(Number); if (d.netPathView) setView(d.netPathView, { animate: false }); tracePath(a, b); }
      else if (d.netRelType) { const key = d.netRelType; if (st.rel.has(key)) st.rel.delete(key); else st.rel.add(key); setView(st.view); }
      else if (t.hasAttribute('data-inspector-close')) closeInspector(true);
      else if (d.netTool) tool(d.netTool, t);
    });
    root.addEventListener('change', e => {
      const t = e.target;
      if (t.dataset.netCat != null) { const all = $$('[data-net-cat]'); const on = all.filter(x => x.checked).map(x => x.dataset.netCat); st.cats = on.length === all.length ? null : new Set(on); setView(st.view); }
      else if (t.dataset.netOpt) { const key = t.dataset.netOpt; if (key === 'competition-min') { st.opts.competitionMin = +t.value; st.opts.competitionMinTouched = true; } else st.opts[key] = t.checked; setView(st.view); }
    });
    const search = $('[data-net-search]');
    search.addEventListener('change', () => runSearch(search.value));
    search.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); runSearch(search.value); } });
    function runSearch(q) {
      q = q.trim().toLowerCase(); if (q.length < 2 || !st.data) return;
      const cat = st.data.suppliers.find(s => catKey(s.category).toLowerCase() === q);
      if (cat) { focusCategory(catKey(cat.category)); return; }
      const found = st.data.suppliers.find(s => short(s.name).toLowerCase() === q) || st.data.suppliers.find(s => s.name.toLowerCase().includes(q));
      if (found) ctx.onSelect([found.supplierId], { source: 'network', center: true }); else ctx.toast?.(`No supplier or category matches “${q}”.`);
    }
    inspector.addEventListener('keydown', e => { if (e.key === 'Escape') { closeInspector(true); host.focus(); } });
    function tool(name, button) {
      if (name === 'filters') { const p = $('[data-net-filter-panel]'); p.hidden = !p.hidden; button?.setAttribute('aria-expanded', !p.hidden); return; }
      if (name === 'clear-filters') { st.cats = null; Object.assign(st.opts, { inactive: true, isolated: true }); st.rel = new Set(Object.keys(REL)); $$('[data-net-opt]').forEach(i => { if (i.type === 'checkbox') i.checked = true; }); $$('[data-net-cat]').forEach(i => (i.checked = true)); setView(st.view); return; }
      if (name === 'replay') { const r = $('[data-replay]'); r.hidden = !r.hidden; $$('[data-net-tool="replay"]').forEach(b => b.setAttribute('aria-expanded', !r.hidden)); ctx.onReplayToggle?.(!r.hidden); return; }
      if (name === 'table') { const box = $('[data-net-table]'); box.hidden = !box.hidden; button?.setAttribute('aria-pressed', !box.hidden); renderTable(); return; }
      if (name === 'path') { if (st.selected.length >= 2) tracePath(st.selected[0], st.selected[1]); else ctx.toast?.('Select two suppliers first (Shift+click or “Add to comparison”).'); return; }
      if (name === 'fullscreen') { if (document.fullscreenElement) document.exitFullscreen(); else stage.requestFullscreen?.(); return; }
      if (name === 'motion') { st.motion = !st.motion; try { localStorage.setItem('orange-radar:motion', st.motion ? 'on' : 'off'); } catch { /* storage blocked */ } updateMotionButton(); wake(); return; }
      if (name === 'zoom-in' || name === 'zoom-out') { st.camT = { ...st.camT, k: Math.max(0.25, Math.min(3, st.camT.k * (name === 'zoom-in' ? 1.3 : 0.77))) }; st.camSpeed = 0.15; wake(); }
      else if (name === 'fit') { frameView({ speed: 0.08 }); wake(); }
      else if (name === 'reset') reset();
      else if (name === 'back') back();
      else if (name === 'clear') ctx.onSelect([], { source: 'network' });
      else if (name === 'png') { const a = document.createElement('a'); a.href = png({ scale: 2 }); a.download = `orange-radar-network-${st.view}.png`; a.click(); }
    }
    function png({ scale = 1.5, bg } = {}) {
      if (!st.ready) return null;
      const b = bounds(shownNodes().map(n => ({ ax: n.x, ay: n.y })), 130); if (!b) return null;
      const w = Math.max(1000, Math.min(1800, b.x2 - b.x1)); const h = Math.max(620, w * ((b.y2 - b.y1) / (b.x2 - b.x1)));
      const off = document.createElement('canvas'); off.width = Math.round(w * scale); off.height = Math.round(h * scale);
      const c = off.getContext('2d'); c.scale(scale, scale);
      const saved = { cam: st.cam, w: st.w, h: st.h };
      Object.assign(st, { w, h, cam: { x: (b.x1 + b.x2) / 2, y: (b.y1 + b.y2) / 2, k: Math.min(w / (b.x2 - b.x1), h / (b.y2 - b.y1)) } });
      draw(c, w, h, performance.now(), { exporting: bg || css('--rd-canvas') || '#ffffff' });
      Object.assign(st, saved);
      return off.toDataURL('image/png');
    }
    function focusCategory(name) {
      const needView = st.view !== 'category';
      if (needView) setView('category', { animate: true });
      const gr = st.groups.find(x => x.kind === 'category' && x.key.toLowerCase() === String(name).toLowerCase());
      if (gr) { if (needView) later(() => focusGroup(gr.id), reducedMotion() ? 0 : 650); else focusGroup(gr.id); }
      return !!gr;
    }
    ctx.reduced.addEventListener?.('change', wake);
    window.addEventListener('orange:themechange', () => requestAnimationFrame(() => { readPalette(); st.groups.forEach(gr => { if (gr.kind === 'category') gr.color = catColor(gr.key); }); renderGroups(); wake(); }));

    return {
      init, setView, focusGroup, syncSelection, egoFocus, openEdge, tracePath, setReplayAt, png, cancel, back, focusCategory,
      restyle() { readPalette(); renderGroups(); wake(); },
      showElements(ids) { const es = st.edges.filter(e => ids.includes(e.id) && e.shown); if (es.length) { lightUp(es, cancel()); wake(); } },
      // Visible feedback when the analyst moves the network: a banner and a pulse on every entity it touched.
      aiCue(label, supplierIds = []) {
        const el = $('[data-net-ai]');
        if (el) { el.innerHTML = `<i aria-hidden="true"></i><span>Analyst</span>${esc(label)}`; el.hidden = false; el.classList.remove('is-on'); void el.offsetWidth; el.classList.add('is-on'); clearTimeout(el._t); el._t = setTimeout(() => { el.classList.remove('is-on'); setTimeout(() => (el.hidden = true), 400); }, 3800); }
        supplierIds.map(id => node(`s-${id}`)).filter(n => n?.shown).forEach((n, i) => setTimeout(() => { st.pulses.push({ n, t0: performance.now(), color: P.orange }); wake(); }, 250 + i * 110));
      },
      fit: () => tool('fit'),
      get view() { return st.view; }, get edgeFilters() { return [...st.rel]; }, get ready() { return st.ready; }, viewTitle: v => VIEW[v]?.title
    };
  };
})();
