// ORANGE INTELLIGENCE - shared AI intelligence workspace (Views/Intelligence/Index.cshtml), built on the Orange Radar engine.
// Draws server-calculated data only: every metric, tender and edge comes from /Intelligence/Workspace, already limited
// to the signed-in user's authorized scope. Four exclusive tabs (overview, suppliers, tenders, studio) replace each
// other; one page state (filters, selection, tender, replay) is shared by the 3D universe
// (orange-intelligence-universe.js), the 2D network (orange-radar-network.js), the charts, the report studio
// and the AI analyst (orange-radar-voice.js), which may only call the whitelisted apply() commands.
(() => {
  'use strict';
  const root = document.querySelector('[data-radar]');
  if (!root) return;
  const $ = (s, el = root) => el.querySelector(s);
  const $$ = (s, el = root) => Array.from(el.querySelectorAll(s));
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  const dur = ms => (reduced.matches ? 0 : ms);
  const csrf = root.dataset.csrf;
  const urls = Object.fromEntries(Object.entries(root.dataset).filter(([k]) => k.startsWith('url')).map(([k, v]) => [k.slice(3).toLowerCase(), v]));
  const escapeHtml = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const esc = s => escapeHtml(String(s ?? '').replace(/RADAR DEMO (- )?/g, ''));
  const fmt = {
    int: n => (n == null ? '—' : Number(n).toLocaleString('en-US')),
    num: (n, d = 2) => (n == null ? '—' : Number(n).toLocaleString('en-US', { maximumFractionDigits: d })),
    pct: (n, d = 0) => (n == null ? '—' : `${(n * 100).toFixed(d)}%`),
    signed: (n, d = 1) => (n == null ? '—' : `${n >= 0 ? '+' : ''}${(n * 100).toFixed(d)}%`),
    money: (c, v) => `${c} ${Number(v).toLocaleString('en-US', { maximumFractionDigits: 0 })}`,
    date: d => (d ? new Date(d).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }) : '—')
  };
  const TABS = ['overview', 'suppliers', 'tenders', 'studio'];
  // Old scene names (AI commands, briefings, deep links) map onto the tabs.
  const SCENE_TAB = { overview: 'overview', network: 'suppliers', market: 'suppliers', suppliers: 'suppliers', tenders: 'tenders', price: 'suppliers', analyst: 'studio', reports: 'studio', studio: 'studio' };
  const MAX_SELECTED = 5;

  const state = {
    data: null, selected: [], tender: null, chartPoint: null, simulation: null,
    replayAt: null, playing: false, suppliers: new Map(), supplierSlot: new Map(), catColor: new Map(), scene: 'suppliers', tab: 'suppliers',
    filters: { department: '', category: '', activity: 'all' }, tenderSel: null
  };
  const charts = {};
  const timers = new Set();
  const later = (fn, ms) => { const t = setTimeout(() => { timers.delete(t); fn(); }, ms); timers.add(t); return t; };
  function cancelAnimations() { timers.forEach(clearTimeout); timers.clear(); network.cancel(); }

  // ---------- helpers ----------
  const css = name => getComputedStyle(root).getPropertyValue(name).trim();
  const palette = () => ({ s: [1, 2, 3, 4, 5, 6, 7, 8].map(i => css(`--s${i}`)), c: [1, 2, 3, 4, 5, 6, 7, 8].map(i => css(`--c${i}`)), ink: css('--rd-ink'), ink2: css('--rd-ink-2'), muted: css('--rd-muted'),
    line: css('--rd-line'), soft: css('--rd-line-soft'), surface: css('--rd-surface'), surface2: css('--rd-surface-2'), orange: css('--rd-orange'), signal: css('--rd-review'), award: css('--rd-award'), neutral: css('--rd-neutral'), violet: css('--rd-violet') });
  const status = (text, error = false) => { const el = $('[data-status]'); el.textContent = text || ''; el.classList.toggle('is-error', !!error); };
  const period = () => ({ from: $('#rd-from').value || '', to: $('#rd-to').value || '' });
  const name = id => state.suppliers.get(id)?.name?.replace(/^RADAR DEMO /, '') ?? `Supplier #${id}`;
  const fullName = id => state.suppliers.get(id)?.name ?? `Supplier #${id}`;
  const supplierColor = id => { const slot = state.supplierSlot.get(id); return slot == null ? palette().neutral : palette().s[slot]; };
  const catKey = c => (c && c.trim() ? c.trim() : 'Uncategorized');
  const catColor = c => (state.catColor.has(c) ? palette().c[state.catColor.get(c) % 8] : palette().neutral);
  let toastTimer = 0;
  function toast(html, ms = 4200) {
    const el = $('[data-toast]'); el.innerHTML = html; el.hidden = false;
    requestAnimationFrame(() => el.classList.add('is-on'));
    clearTimeout(toastTimer); toastTimer = setTimeout(() => { el.classList.remove('is-on'); setTimeout(() => (el.hidden = true), 250); }, ms);
  }

  async function api(url, options = {}) {
    const headers = { RequestVerificationToken: csrf, ...(options.body && !(options.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}), ...(options.headers || {}) };
    const response = await fetch(url, { credentials: 'same-origin', ...options, headers });
    if (!response.ok) {
      let message = `Request failed (${response.status})`;
      try { message = (await response.clone().json()).error || message; } catch { /* non-JSON error page */ }
      throw new Error(message);
    }
    return response;
  }
  const post = (url, body) => api(url, { method: 'POST', body: JSON.stringify(body) });

  // ---------- network ----------
  const network = window.OrangeRadarNetwork({
    root, $, $$, esc, fmt, dur, reduced, toast: t => toast(esc(t)),
    onSelect: (ids, o) => select(ids, o),
    onViewChange: () => { renderAiContext(); window.dispatchEvent(new CustomEvent('radar:selection', { detail: context() })); },
    onReplayToggle: open => { if (open) renderTimeline(); }
  });

  // ---------- loading ----------
  let loadController = null;
  async function load(refresh = false) {
    loadController?.abort(); loadController = new AbortController();
    status('Running the analysis on recorded procurement data…');
    $$('.rd-kpi').forEach(k => k.classList.add('is-loading'));
    try {
      const q = new URLSearchParams({ ...period(), refresh: refresh ? 'true' : 'false' });
      ingest(await (await api(`${urls.workspace}?${q}`, { signal: loadController.signal })).json());
      status(!state.data.tenders.length && state.data.scope !== 'Whole market' ? 'No authorized tenders in this scope. Data appears when a tender of your department or committee falls inside the period.'
        : state.data.summary.analyzedRounds === 0 ? 'No closed bidding cycle falls inside this period - every supplier in scope is still shown. Widen the period for patterns.' : '');
    } catch (e) { if (e.name !== 'AbortError') status(`The analysis could not load: ${e.message}`, true); }
  }

  function ingest(data) {
    state.data = data;
    state.suppliers = new Map(data.suppliers.map(s => [s.supplierId, s]));
    state.selected = state.selected.filter(id => state.suppliers.has(id));
    const cats = [...new Set(data.suppliers.map(s => catKey(s.category)))].filter(c => c !== 'Uncategorized').sort((a, b) => a.localeCompare(b));
    state.catColor = new Map(cats.map((c, i) => [c, i]));
    const ranked = data.suppliers.filter(s => s.participations > 0).sort((a, b) => b.participations - a.participations || a.supplierId - b.supplierId);
    state.supplierSlot = new Map(ranked.slice(0, 8).map((s, i) => [s.supplierId, i]));
    const demo = data.suppliers.length > 0 && data.suppliers.filter(s => /^RADAR DEMO /.test(s.name)).length >= data.suppliers.length / 2;
    $('[data-demo-marker]').hidden = !demo;
    $('[data-scope-label]').textContent = data.scope || 'Authorized scope';
    if (!data.tenders.some(t => t.tenderId === state.tenderSel)) state.tenderSel = (data.tenders.find(t => t.rounds.some(r => r.analysed && r.offers)) || data.tenders[0])?.tenderId ?? null;
    renderFilterOptions(); renderKpis(); renderSnapshot(); renderOverview(); renderTenders(); renderMarket(); network.init(data); renderTimeline(); renderScenarioPick(); renderStudio(); renderSelection('load'); renderFind(); renderChips();
    window.dispatchEvent(new CustomEvent('radar:data', { detail: data }));
    if (!state.suppliers.has(comparison.a)) comparison.a = null;
    if (!state.suppliers.has(comparison.b)) comparison.b = null;
    loadComparison();
  }

  // ---------- KPI tiles (each KPI has one owning tab) ----------
  const ICON = {
    users: '<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20c.6-4 3.2-6 6.5-6s5.9 2 6.5 6M16 4.8a3.3 3.3 0 0 1 0 6.4M18.5 14.4c1.8.8 2.8 2.7 3 5.6"/>',
    check: '<circle cx="12" cy="12" r="9"/><path d="m8 12 3 3 5-6"/>', layers: '<path d="M3 7l9-4 9 4-9 4zM3 12l9 4 9-4M3 17l9 4 9-4"/>', chart: '<path d="M4 20h16M7 17v-5M12 17V8M17 17v-9"/>',
    doc: '<path d="M7 3h7l4 4v14H7zM14 3v4h4M10 12h5M10 16h5"/>', send: '<path d="M22 2 11 13M22 2l-7 20-4-9-9-4z"/>', clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    award: '<path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0z"/>', signal: '<path d="M12 3 2 20h20zM12 10v4M12 17h.01"/>',
    mail: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/>', scan: '<circle cx="11" cy="11" r="6.5"/><path d="m16 16 5 5"/>',
    spark: '<path d="M12 3l1.8 4.7 4.7 1.8-4.7 1.8L12 16l-1.8-4.7-4.7-1.8 4.7-1.8z"/>'
  };
  function kpiTiles(el, tiles) {
    if (!el) return;
    el.innerHTML = tiles.map(t => `<div class="oi-kpi is-${t.tone || 'orange'}" title="${esc(t.def || '')}"><span class="oi-kpi-icon" aria-hidden="true"><svg viewBox="0 0 24 24">${ICON[t.icon] || ''}</svg></span>
      <div class="oi-kpi-text"><span class="oi-kpi-label">${esc(t.label)}</span><strong class="${typeof t.value === 'number' ? '' : 'is-text'}" data-count="${typeof t.value === 'number' ? t.value : ''}">${typeof t.value === 'number' ? fmt.int(t.value) : esc(t.value ?? '—')}${t.unit ? `<em>${esc(t.unit)}</em>` : ''}</strong><small>${t.sub || ''}</small></div>
      ${t.bar != null ? `<i class="oi-kpi-bar" aria-hidden="true"><u style="--w:${Math.max(0, Math.min(1, t.bar)) * 100}%"></u></i>` : ''}</div>`).join('');
    el.querySelectorAll('[data-count]').forEach(c => {
      const target = parseFloat(c.dataset.count); if (Number.isNaN(target) || reduced.matches || target === 0) return;
      const unit = c.querySelector('em')?.outerHTML || ''; const start = performance.now();
      const step = now => { const t = Math.min(1, (now - start) / 650); c.innerHTML = fmt.int(Math.round(target * (1 - Math.pow(1 - t, 3)))) + unit; if (t < 1) requestAnimationFrame(step); };
      requestAnimationFrame(step);
    });
  }
  const median = a => { const v = a.filter(x => x != null && !Number.isNaN(x)).sort((x, y) => x - y); return v.length ? (v.length % 2 ? v[(v.length - 1) / 2] : (v[v.length / 2 - 1] + v[v.length / 2]) / 2) : null; };
  const clean = t => String(t ?? '').replace(/^RADAR DEMO (- )?/, '');
  function renderKpis() {
    const d = state.data, defs = d.definitions;
    const list = filteredSuppliers();
    const active = list.filter(x => x.participations > 0);
    const cats = new Map(); list.forEach(x => cats.set(catKey(x.category), (cats.get(catKey(x.category)) || 0) + 1));
    const top = [...cats].sort((a, b) => b[1] - a[1])[0];
    const inv = list.reduce((n, x) => n + x.invitations, 0), part = list.reduce((n, x) => n + x.participations, 0);
    const approved = list.filter(x => /approved/i.test(x.status)).length;
    kpiTiles($('[data-sup-kpis]'), [
      { label: 'Registered suppliers', value: list.length, sub: `${fmt.int(approved)} approved / eligible`, def: defs.registeredSuppliers, icon: 'users', tone: 'orange', bar: list.length ? approved / list.length : 0 },
      { label: 'Participating suppliers', value: active.length, sub: `${fmt.pct(list.length ? active.length / list.length : null)} submitted an offer in a closed bidding cycle`, def: defs.participatingSuppliers, icon: 'check', tone: 'teal', bar: list.length ? active.length / list.length : 0 },
      { label: 'Categories', value: cats.size, sub: top ? `largest: ${esc(top[0])} (${top[1]})` : 'none registered', def: 'Distinct registered business categories of the suppliers shown.', icon: 'layers', tone: 'violet', bar: top && list.length ? top[1] / list.length : 0 },
      { label: 'Response rate', value: inv ? `${Math.round(part / inv * 100)}%` : '—', sub: inv ? `${fmt.int(part)} offers ÷ ${fmt.int(inv)} closed bidding cycle invitations` : 'no closed bidding cycle invitation', def: 'Offers submitted in closed bidding cycles divided by invitations to those rounds.', icon: 'chart', tone: 'blue', bar: inv ? part / inv : 0 }
    ]);
  }

  // ---------- supplier filters (shared by KPIs, snapshot, universe, table) ----------
  function filteredSupplierIds() {
    const f = state.filters; if (!state.data || (!f.department && !f.category && f.activity === 'all')) return null;
    let ids = state.data.suppliers;
    if (f.category) ids = ids.filter(x => catKey(x.category) === f.category);
    if (f.activity === 'active') ids = ids.filter(x => x.participations > 0);
    else if (f.activity === 'silent') ids = ids.filter(x => x.participations === 0);
    else if (f.activity === 'approved') ids = ids.filter(x => /approved/i.test(x.status));
    if (f.department) { const bid = new Set(state.data.network.rounds.filter(r => r.department === f.department).flatMap(r => r.supplierIds)); ids = ids.filter(x => bid.has(x.supplierId)); }
    return new Set(ids.map(x => x.supplierId));
  }
  const filteredSuppliers = () => { const ids = filteredSupplierIds(); return ids ? state.data.suppliers.filter(x => ids.has(x.supplierId)) : state.data.suppliers; };
  function renderFilterOptions() {
    const d = state.data;
    const deps = [...new Set(d.network.rounds.map(r => r.department))].sort();
    const cats = [...new Set(d.suppliers.map(x => catKey(x.category)))].sort((a, b) => a.localeCompare(b));
    if (state.filters.department && !deps.includes(state.filters.department)) state.filters.department = '';
    if (state.filters.category && !cats.includes(state.filters.category)) state.filters.category = '';
    $('[data-sup-filter="department"]').innerHTML = `<option value="">All departments</option>${deps.map(x => `<option value="${esc(x)}">${esc(clean(x))}</option>`).join('')}`;
    $('[data-sup-filter="category"]').innerHTML = `<option value="">All categories</option>${cats.map(x => `<option value="${esc(x)}">${esc(x)}</option>`).join('')}`;
    $('[data-sup-filter="department"]').value = state.filters.department; $('[data-sup-filter="category"]').value = state.filters.category; $('[data-sup-filter="activity"]').value = state.filters.activity;
  }
  function setFilter(key, value) {
    state.filters[key] = value; renderFilterOptions(); renderKpis(); renderSnapshot(); landscape(); positions(); renderInsights(); renderChips();
    window.dispatchEvent(new CustomEvent('radar:filters', { detail: { ...state.filters } }));
  }
  function clearFilter(key) {
    if (key === 'period' || key === 'all') { if ($('#rd-from').value || $('#rd-to').value) { $('#rd-from').value = ''; $('#rd-to').value = ''; load(); } }
    if (key === 'all') { state.filters = { department: '', category: '', activity: 'all' }; setFilter('activity', 'all'); }
    else if (key !== 'period') setFilter(key, key === 'activity' ? 'all' : '');
    renderChips();
  }
  function renderChips() {
    const f = state.filters, p = period(); const chips = [];
    if (p.from || p.to) chips.push(['period', `${p.from || '…'} → ${p.to || '…'}`]);
    if (f.department) chips.push(['department', clean(f.department)]);
    if (f.category) chips.push(['category', f.category]);
    if (f.activity !== 'all') chips.push(['activity', { active: 'Submitted offers', silent: 'No offer yet', approved: 'Approved only' }[f.activity]]);
    $('[data-filter-chips]').innerHTML = chips.map(([k, v]) => `<span class="oi-chip">${esc(v)}<button type="button" data-clear-filter="${k}" aria-label="Remove filter ${esc(v)}">×</button></span>`).join('') + (chips.length > 1 ? '<button type="button" class="rd-link" data-clear-filter="all">Clear filters</button>' : '');
  }

  // ---------- market snapshot (Suppliers) ----------
  function renderSnapshot() {
    const p = palette(); const list = filteredSuppliers();
    const cats = new Map(); list.forEach(x => cats.set(catKey(x.category), (cats.get(catKey(x.category)) || 0) + 1));
    const sorted = [...cats].sort((a, b) => b[1] - a[1]);
    const shown = sorted.slice(0, 4); const others = sorted.slice(4).reduce((n, x) => n + x[1], 0);
    const rows = [...shown.map(([k, v]) => ({ name: k, value: v, color: catColor(k) })), ...(others ? [{ name: `Others (${sorted.length - 4})`, value: others, color: p.neutral, others: true }] : [])];
    const c = chart('snapCats');
    if (c) {
      c.setOption({ ...base(), tooltip: { ...base().tooltip, formatter: d => `${esc(d.name)}<br>${d.value} supplier${d.value === 1 ? '' : 's'} · ${fmt.pct(d.value / Math.max(1, list.length))}` },
        title: { text: fmt.int(list.length), subtext: 'Suppliers', left: 'center', top: '31%', textStyle: { color: p.ink, fontSize: 24, fontWeight: 800 }, subtextStyle: { color: p.muted, fontSize: 13 }, itemGap: 2 },
        series: [{ type: 'pie', radius: ['72%', '94%'], center: ['50%', '50%'], padAngle: rows.length > 1 ? 3 : 0, itemStyle: { borderRadius: 6 }, label: { show: false }, emphasis: { scale: true, scaleSize: 4 },
          data: rows.map(r => ({ name: r.name, value: r.value, itemStyle: { color: r.color, opacity: state.filters.category && state.filters.category !== r.name ? 0.35 : 1 } })) }] }, true);
      c.off('click'); c.on('click', d => !/^Others/.test(d.name) && setFilter('category', state.filters.category === d.name ? '' : d.name));
    }
    $('[data-snap-legend]').innerHTML = rows.map(r => `<li><button type="button" ${r.others ? 'disabled' : `data-snap-cat="${esc(r.name)}"`} class="${state.filters.category === r.name ? 'is-on' : ''}"><i style="background:${r.color}"></i><span>${esc(r.name)}</span><b>${r.value}</b><small>${fmt.pct(r.value / Math.max(1, list.length))}</small></button></li>`).join('') || '<li class="rd-muted">No suppliers</li>';
    // Distinct suppliers that submitted an offer in a closed bidding cycle, per month the round closed.
    const ids = filteredSupplierIds(); const months = new Map();
    state.data.network.rounds.forEach(r => { const k = String(r.availableAt).slice(0, 7); if (!months.has(k)) months.set(k, new Set()); r.supplierIds.forEach(id => { if (!ids || ids.has(id)) months.get(k).add(id); }); });
    const keys = [...months.keys()].sort();
    const t = chart('snapTrend'); if (!t) return;
    if (keys.length < 2) { empty(t, keys.length ? `All closed bidding cycles fall in ${keys[0]}: ${months.get(keys[0]).size} suppliers submitted offers.` : 'No closed bidding cycle in this period.'); return; }
    const all = []; for (let d = new Date(keys[0] + '-01T00:00:00Z'); d <= new Date(keys[keys.length - 1] + '-01T00:00:00Z'); d.setUTCMonth(d.getUTCMonth() + 1)) all.push(d.toISOString().slice(0, 7));
    t.setOption({ ...base(), grid: { left: 6, right: 16, top: 14, bottom: 4, containLabel: true },
      tooltip: { ...base().tooltip, trigger: 'axis', formatter: ps => `${esc(ps[0].axisValue)}<br><b>${ps[0].value}</b> supplier${ps[0].value === 1 ? '' : 's'} submitted offers` },
      xAxis: { ...axis(), type: 'category', data: all, boundaryGap: false, axisLabel: { color: p.muted, fontSize: 13, hideOverlap: true, formatter: v => new Date(v + '-01T00:00:00Z').toLocaleDateString('en-GB', { month: 'short', year: '2-digit' }) } },
      yAxis: { ...axis(), minInterval: 1, splitNumber: 2 },
      series: [{ type: 'line', smooth: 0.35, symbol: 'circle', symbolSize: 8, data: all.map(k => months.get(k)?.size || 0), lineStyle: { width: 3, color: p.c[1], shadowBlur: 12, shadowColor: alpha(p.c[1], 0.45) }, itemStyle: { color: p.c[1], borderColor: p.surface, borderWidth: 2 }, areaStyle: { color: vgrad(alpha(p.c[1], 0.3), alpha(p.c[1], 0)) } }] }, true);
  }

  // ---------- overview ----------
  function renderOverview() {
    const d = state.data; const s = d.summary; const tenders = d.tenders;
    const done = tenders.filter(t => t.daysToNotification != null);
    const med = median(done.map(t => t.daysToNotification));
    const p = period();
    $('[data-ov-lead]').textContent = `${d.scope} · ${p.from || p.to ? `${p.from || '…'} → ${p.to || '…'}` : 'all recorded history'}`;
    kpiTiles($('[data-ov-kpis]'), [
      { label: 'Tenders in scope', value: tenders.length, sub: `${done.length} completed · ${tenders.filter(t => t.state === 'In progress' || t.state === 'Awarded').length} in progress`, icon: 'doc', tone: 'orange', def: 'Tenders with a round in the selected period, within your authorized scope.' },
      { label: 'Suppliers', value: d.suppliers.length, sub: `${fmt.int(s.participatingSuppliers)} submitted offers in closed bidding cycles`, icon: 'users', tone: 'teal', def: d.definitions.registeredSuppliers },
      { label: 'Submitted offers', value: s.submittedBids, sub: s.sealedBids ? `${s.sealedBids} sealed offers excluded` : 'no sealed offer pending', icon: 'send', tone: 'violet', def: d.definitions.submittedBids },
      { label: 'Median lifecycle', value: med == null ? 'Not yet' : fmt.num(med, 1), unit: med == null ? '' : ' days', sub: med == null ? 'no tender has a sent winner notification' : `creation → winner notification sent · ${done.length} tender${done.length === 1 ? '' : 's'}`, icon: 'clock', tone: 'blue', def: 'Median of (winner notification sent - tender created), completed tenders only.' }
    ]);
    const stages = ['created', 'invited', 'closed', 'awarded', 'notified'];
    const labels = { created: 'Created', invited: 'Suppliers invited', closed: 'Bidding closed', awarded: 'Officially awarded', notified: 'Winner notified' };
    const n0 = Math.max(1, tenders.length);
    $('[data-ov-life-note]').textContent = tenders.length ? 'Tenders in scope that reached each recorded milestone · median days from creation' : 'No authorized tenders in this scope.';
    $('[data-ov-funnel]').innerHTML = stages.map((k, i) => {
      const reached = tenders.filter(t => t.stages.find(x => x.key === k)?.at);
      const days = median(reached.map(t => (Date.parse(t.stages.find(x => x.key === k).at) - Date.parse(t.createdAt)) / 86400000));
      return `<li style="--w:${(reached.length / n0) * 100}%"><span class="oi-funnel-step">${i + 1}</span><div><b>${labels[k]}</b><small>${i === 0 ? 'start of the lifecycle' : days == null ? 'not recorded yet' : `median ${fmt.num(days, 1)} days from creation`}</small></div><i aria-hidden="true"><u></u></i><strong>${reached.length}</strong></li>`;
    }).join('');
    const items = [];
    const low = d.competition.rounds.filter(r => r.lowCompetition);
    if (low.length) items.push({ icon: 'chart', tone: 'orange', title: `${low.length} closed bidding cycle${low.length === 1 ? '' : 's'} below ${d.competition.minimumBids} offers`, sub: low.slice(0, 2).map(r => `${clean(r.tender)} · ${cycleLabel(r.roundNumber)}`).join(' · '), act: `data-open-tender="${low[0].tenderId}"`, cta: 'Open' });
    tenders.filter(t => t.state === 'In progress').sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt)).slice(0, 2).forEach(t => items.push({ icon: 'clock', tone: 'blue', title: clean(t.title), sub: `In progress for ${fmt.num((Date.now() - Date.parse(t.createdAt)) / 86400000, 0)} days · ${t.status}`, act: `data-open-tender="${t.tenderId}"`, cta: 'Lifecycle' }));
    const silent = d.suppliers.filter(x => x.invitations > 0 && x.participations === 0).length;
    if (silent) items.push({ icon: 'mail', tone: 'violet', title: `${silent} invited supplier${silent === 1 ? '' : 's'} never submitted an offer`, sub: 'Closed-round invitations without a submitted offer', act: 'data-open-silent', cta: 'Suppliers' });
    $('[data-ov-attention]').innerHTML = items.length ? items.map(x => `<li class="is-${x.tone}"><span class="oi-att-icon" aria-hidden="true"><svg viewBox="0 0 24 24">${ICON[x.icon]}</svg></span><div><b>${esc(x.title)}</b><small>${esc(x.sub)}</small></div><button type="button" class="rd-link" ${x.act}>${x.cta} →</button></li>`).join('')
      : '<li class="oi-att-empty"><div><b>Nothing needs attention</b><small>No low-competition round or long-running tender in this scope.</small></div></li>';
    const cards = [['suppliers', 'Suppliers', `${d.suppliers.length} suppliers in a living 3D universe`, ICON.users], ['tenders', 'Tenders', `${tenders.length} tenders · lifecycle, offers and outcome`, ICON.doc],
      ['studio', 'AI Studio', 'Ask, chart, upload PDF or Excel, export', ICON.spark]];
    $('[data-ov-shortcuts]').innerHTML = cards.map(([k, t, sub, ic]) => `<button type="button" class="oi-shortcut" data-tab-jump="${k}"><span aria-hidden="true"><svg viewBox="0 0 24 24">${ic}</svg></span><b>${t}</b><small>${esc(sub)}</small><i aria-hidden="true">→</i></button>`).join('');
  }

  // ---------- tenders ----------
  const tenderById = id => state.data?.tenders.find(t => t.tenderId === id);
  const stateTone = t => (t.state === 'Completed' ? 'ok' : t.state === 'Stopped' ? 'muted' : t.state === 'Awarded' ? 'award' : 'progress');
  function renderTenders() {
    const d = state.data; const list = d.tenders;
    $('[data-tender-count]').textContent = list.length;
    $('[data-tender-pick]').innerHTML = list.length ? list.map(t => `<option value="${t.tenderId}">#${t.tenderId} · ${esc(clean(t.title))}</option>`).join('') : '<option value="">No authorized tenders in this scope</option>';
    $('[data-tender-pick]').value = state.tenderSel ?? '';
    $('[data-tender-list]').innerHTML = list.map(t => `<li><button type="button" data-tender-open="${t.tenderId}" class="${t.tenderId === state.tenderSel ? 'is-on' : ''}" aria-current="${t.tenderId === state.tenderSel}">
      <span class="oi-tl-title">${esc(clean(t.title))}</span><span class="oi-tl-meta">#${t.tenderId} · ${esc(clean(t.department))} · ${fmt.date(t.createdAt)}</span>
      <span class="oi-pill is-${stateTone(t)}">${t.state === 'Completed' ? `${fmt.num(t.daysToNotification, 1)} days` : esc(t.state)}</span></button></li>`).join('') || '<li class="rd-muted">No authorized tenders in this scope.</li>';
    renderTender();
  }
  function renderTender() {
    const t = tenderById(state.tenderSel); const main = $('[data-tender-main]');
    main.classList.toggle('is-empty', !t);
    if (!t) { $('[data-tender-hero]').innerHTML = '<div class="rd-empty-card"><strong>No authorized tenders in this scope</strong><span>Tenders appear here when one of their rounds falls inside the selected period.</span></div>'; $('[data-tender-kpis]').innerHTML = ''; $('[data-tender-timeline]').innerHTML = ''; $('[data-tender-offers-section]').hidden = true; return; }
    const analysed = t.rounds.filter(r => r.analysed && r.offers);
    const latest = t.rounds[t.rounds.length - 1];
    const so = (Date.now() - Date.parse(t.createdAt)) / 86400000;
    $('[data-tender-hero]').innerHTML = `<div><p class="oi-kicker">Tender #${t.tenderId} · ${esc(clean(t.department))}</p><h3>${esc(clean(t.title))}</h3>
      <p class="oi-tender-tags"><span class="oi-pill">${esc(t.status)}</span>${t.state === 'Stopped' ? '' : `<span class="oi-pill is-${stateTone(t)}">${t.state === 'Awarded' ? 'Awarded · notification pending' : esc(t.state)}</span>`}<span>${t.rounds.length} round${t.rounds.length === 1 ? '' : 's'}</span><span>Estimated ${fmt.money(t.currency, t.estimatedValue)}</span></p></div>`;
    kpiTiles($('[data-tender-kpis]'), [
      { label: 'Invited suppliers', value: latest ? latest.invited : 0, sub: latest ? `${cycleLabel(latest.roundNumber)}${t.rounds.length > 1 ? ` · ${t.rounds.reduce((n, r) => n + r.invited, 0)} invitations across all bidding cycles` : ''}` : 'no bidding cycle', icon: 'mail', tone: 'violet' },
      { label: 'Submitted offers', value: latest?.analysed ? latest.offers : 'Sealed', sub: latest?.analysed ? cycleLabel(latest.roundNumber) : 'hidden until bidding closes', icon: 'send', tone: 'teal' },
      { label: t.state === 'Completed' ? 'Lifecycle' : 'Lifecycle so far', value: fmt.num(t.state === 'Completed' ? t.daysToNotification : so, 1), unit: ' days', sub: t.state === 'Completed' ? 'created → winner notified' : t.state === 'Stopped' ? `since ${fmt.date(t.createdAt)} · stopped` : `since ${fmt.date(t.createdAt)} · not notified yet`, icon: 'clock', tone: 'blue' },
      { label: 'Official result', value: t.awardedSupplier ? clean(t.awardedSupplier) : 'No award yet', sub: t.awardedSupplier ? `${fmt.money(t.awardCurrency, t.awardedValue)} · ${fmt.date(t.awardedAt)}` : 'recommendations never count', icon: 'award', tone: 'orange' }
    ]);
    $('[data-tender-timeline]').innerHTML = t.stages.map((st, i) => {
      const days = st.at ? (Date.parse(st.at) - Date.parse(t.createdAt)) / 86400000 : null;
      return `<li class="${st.at ? 'is-done' : 'is-missing'}" title="Source: ${esc(st.source)}"><span class="oi-tl-dot" aria-hidden="true"></span><b>${esc(st.label)}</b><time>${st.at ? new Date(st.at).toLocaleString('en-GB', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : 'Not recorded'}</time><small>${i === 0 ? 'start' : days == null ? '—' : `+${fmt.num(days, 1)} days`}</small></li>`;
    }).join('');
    tenderOffers(t, analysed);
  }
  let offerRoundSel = null, offerGroupSel = null, offerBidSel = null;
  const chosenOfferRound = t => t?.rounds.find(r => r.roundId === offerRoundSel && r.analysed && r.offers);
  function tenderOffers(t, analysed) {
    const c = chart('tenderOffers'); const p = palette();
    if (!analysed.some(r => r.roundId === offerRoundSel)) { offerRoundSel = analysed.at(-1)?.roundId ?? null; offerGroupSel = null; offerBidSel = null; }
    const r = chosenOfferRound(t);
    const roundPick = $('[data-offer-round]');
    roundPick.innerHTML = analysed.length ? analysed.map(x => `<option value="${x.roundId}">${esc(cycleLabel(x.roundNumber))} · #${x.roundId} · ${x.offers} offers</option>`).join('') : '<option value="">No closed bidding cycle with offers</option>';
    roundPick.value = offerRoundSel ?? ''; roundPick.disabled = !analysed.length;
    $('[data-tender-offers-section]').hidden = !t;
    $('[data-tender-offer-summary]').innerHTML = r ? `<span><small>Tender</small><b>${esc(clean(t.title))} <em>#${t.tenderId}</em></b></span><span><small>${tr('Bidding Cycle', 'دورة العطاء')}</small><b>${cycleLabel(r.roundNumber)} <em>#${r.roundId}</em></b></span><span><small>Invited</small><b>${fmt.int(r.invited)}</b></span><span><small>Submitted</small><b>${fmt.int(r.offers)}</b></span><span><small>Response rate</small><b>${r.invited ? fmt.pct(r.offers / r.invited) : '—'}</b></span>` : `<span><small>Tender</small><b>${esc(clean(t.title))} <em>#${t.tenderId}</em></b></span><span>No closed bidding cycle with submitted offers</span>`;
    if (!c) return;
    if (!r) { empty(c, t.rounds.length ? 'No closed bidding cycle with offers yet. Sealed offers are never shown.' : 'This tender has no bidding cycle yet.'); $('[data-tender-offers-note]').textContent = ''; $('[data-tender-offer-insight]').textContent = ''; $('[data-tender-offer-extremes]').innerHTML = ''; $('[data-offer-table-content]').innerHTML = ''; return; }
    const groups = [...new Set(r.bids.map(b => b.comparisonGroup).filter(x => x != null))].map(id => ({ id, bids: r.bids.filter(b => b.comparisonGroup === id) })).sort((a, b) => b.bids.length - a.bids.length || a.id - b.id);
    if (!groups.some(g => g.id === offerGroupSel)) offerGroupSel = groups[0]?.id ?? null;
    const groupPick = $('[data-offer-group]'); const groupWrap = $('[data-offer-group-wrap]');
    groupWrap.hidden = groups.length < 2;
    groupPick.innerHTML = groups.map(g => `<option value="${g.id}">${esc(g.bids[0].currency)} · ${g.bids.length} structurally comparable offer${g.bids.length === 1 ? '' : 's'}</option>`).join('');
    groupPick.value = offerGroupSel ?? '';
    const active = groups.find(g => g.id === offerGroupSel);
    const bids = (active?.bids || []).slice().sort((a, b) => b.total - a.total);
    const excluded = r.bids.length - bids.length;
    $('[data-tender-offers-note]').textContent = excluded ? `${excluded} offer${excluded === 1 ? ' is' : 's are'} outside this comparable group; see the table for currency and structure notes.` : 'Offers are grouped by bidding cycle, currency and recorded item structure. Check specifications separately.';
    if (!bids.length) { empty(c, 'No offers with a verified comparable currency and item structure. See the offer table for recorded totals.'); $('[data-tender-offer-insight]').textContent = 'No reliable median can be calculated for this round.'; $('[data-tender-offer-extremes]').innerHTML = ''; renderOfferTable(r, []); return; }
    const cur = bids[0].currency;
    const med = median(bids.map(b => b.total));
    const highest = bids[0], lowest = bids.at(-1);
    const chartEl = $('[data-chart="tenderOffers"]'); chartEl.style.height = `${Math.max(390, bids.length * 68 + 140)}px`; c.resize();
    c.setOption({ ...base(), grid: { left: 12, right: 112, top: 54, bottom: 38, containLabel: true },
      tooltip: { ...base().tooltip, formatter: d => { const b = d.data.row; return `<b>${esc(clean(b.supplier))}</b><br>Bid #${b.bidId} · ${fmt.money(b.currency, b.total)}<br>${b.relativeToMedian == null ? 'Median unavailable' : `${fmt.signed(b.relativeToMedian)} vs bidding cycle median`}<br>${esc(b.compliance)} · click for record details`; } },
      xAxis: { ...axis(), type: 'value', min: 0, name: `Offer value (${cur})`, nameLocation: 'middle', nameGap: 28, splitNumber: 5, axisLabel: { color: p.muted, fontSize: 14, formatter: v => fmt.num(v, 0) } },
      yAxis: { ...axis(), type: 'category', data: bids.map(b => clean(b.supplier)), axisLabel: { color: p.ink, fontSize: 16, fontWeight: 650, width: 230, overflow: 'break', lineHeight: 20 } },
      series: [{ type: 'bar', barMaxWidth: 36, barCategoryGap: '38%', data: bids.map(b => ({ value: b.total, row: b, itemStyle: { color: b.bidId === lowest.bidId ? hgrad('#ffae5c', '#ff7900') : hgrad('#ffe8d5', '#ffc58f'), borderRadius: [0, 15, 15, 0] } })),
        label: { show: true, position: 'right', color: p.ink, fontSize: 15, fontWeight: 750, formatter: d => `${cur} ${fmt.num(d.value, 0)}` },
        markLine: bids.length > 1 ? { silent: true, symbol: 'none', lineStyle: { color: p.ink2, type: [6, 5], width: 1.8 }, label: { formatter: `Bidding cycle median\n${cur} ${fmt.num(med, 0)}`, rotate: 0, color: p.surface, backgroundColor: p.ink2, borderRadius: 7, padding: [6, 10], fontSize: 13, position: 'insideEndTop' }, data: [{ xAxis: med }] } : undefined }] }, true);
    c.off('click'); c.on('click', d => { if (d.data?.row) { offerBidSel = d.data.row.bidId; renderOfferDetail(r); } });
    const medianOwners = bids.filter(b => b.total === med);
    $('[data-tender-offer-insight]').innerHTML = `<b>What this bidding cycle shows</b><span>${r.offers} of ${r.invited} invited suppliers submitted offers during ${esc(cycleLabel(r.roundNumber))}${r.invited ? ` (${fmt.pct(r.offers / r.invited)})` : ''}. ${esc(clean(lowest.supplier))} submitted the lowest total in this group (${fmt.money(cur, lowest.total)}); ${esc(clean(highest.supplier))} submitted the highest total in this group (${fmt.money(cur, highest.total)}). ${bids.length > 1 ? `The comparable-group median is ${fmt.money(cur, med)}.` : 'A median comparison needs another comparable offer.'} Lowest price does not establish compliance or an award.</span>`;
    const extreme = (kind, value, supplier, tone) => `<div class="is-${tone}"><span>${kind}</span><strong>${fmt.money(cur, value)}</strong><small>${esc(supplier)}</small></div>`;
    $('[data-tender-offer-extremes]').innerHTML = extreme('Lowest offer', lowest.total, clean(lowest.supplier), 'low') + extreme('Median offer', med, bids.length > 1 ? medianOwners.length === 1 ? clean(medianOwners[0].supplier) : medianOwners.length > 1 ? `${medianOwners.length} suppliers at the median` : 'Calculated midpoint; no single supplier' : 'One recorded offer; not a comparison', 'mid') + extreme('Highest offer', highest.total, clean(highest.supplier), 'high');
    renderOfferTable(r, bids); renderOfferDetail(r);
  }
  function renderOfferTable(round, shown) {
    const ids = new Set(shown.map(b => b.bidId));
    $('[data-offer-table-content]').innerHTML = `<table class="rd-table"><thead><tr><th>Bid</th><th>Supplier</th><th>Currency</th><th class="num">Total</th><th>Comparison</th><th>Compliance</th><th>Result</th></tr></thead><tbody>${round.bids.slice().sort((a, b) => a.total - b.total).map(b => `<tr><td><button type="button" class="rd-link" data-offer-pick="${b.bidId}">#${b.bidId}</button></td><td>${esc(clean(b.supplier))}</td><td>${esc(b.currency)}</td><td class="num">${fmt.num(b.total, 2)}</td><td>${ids.has(b.bidId) ? b.relativeToMedian == null ? 'No second comparable offer' : fmt.signed(b.relativeToMedian) + ' vs median' : esc(b.comparisonNote)}</td><td>${esc(b.compliance)}</td><td>${b.awarded ? 'Official award' : '—'}</td></tr>`).join('')}</tbody></table>`;
    $('[data-tender-offers-table] > summary').textContent = `View offer table (${round.bids.length})`;
  }
  function renderOfferDetail(round) {
    const box = $('[data-tender-offer-detail]'); const b = round.bids.find(x => x.bidId === offerBidSel);
    box.hidden = !b;
    if (!b) return;
    box.innerHTML = `<div><b>Bid #${b.bidId} · ${esc(clean(b.supplier))}</b><span>${fmt.money(b.currency, b.total)} · ${esc(b.comparisonNote)} · compliance: ${esc(b.compliance)}${b.awarded ? ' · official award' : ''}</span></div><button type="button" class="rd-link" data-offer-detail-close aria-label="Close offer details">Close</button>`;
  }
  function openTender(id, { tab = true } = {}) {
    if (!tenderById(id)) return false;
    state.tenderSel = id; state.tender = id;
    renderTenders(); competition();
    if (tab) setTab('tenders');
    window.dispatchEvent(new CustomEvent('radar:selection', { detail: context() }));
    return true;
  }
  async function exportTable(title, columns, rows, source) {
    try {
      const response = await post(urls.exporttable, { title, columns, rows: rows.map(r => r.map(v => String(v ?? ''))), source });
      const blob = await response.blob(); const link = document.createElement('a'); link.href = URL.createObjectURL(blob);
      link.download = (response.headers.get('content-disposition') || '').match(/filename="?([^";]+)/)?.[1] || 'Orange-Intelligence.xlsx';
      document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(link.href), 4000); toast('Excel file downloaded.');
    } catch (e) { toast(`Export failed: ${esc(e.message)}`); }
  }
  async function download(href, fileName) {
    try {
      const response = await fetch(href, { credentials: 'same-origin' });
      if (!response.ok) throw new Error((await response.json().catch(() => ({}))).error || `HTTP ${response.status}`);
      const link = document.createElement('a'); link.href = URL.createObjectURL(await response.blob()); link.download = fileName || 'Orange-Intelligence';
      document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(link.href), 4000); toast('File downloaded.');
    } catch (e) { toast(`Download failed: ${esc(e.message)}`); }
  }
  // Chat charts are exported from their spec: the server rebuilds every row in the user's scope.
  async function exportVisual(spec) {
    try {
      const response = await post(urls.exportvisual, { spec, ...period() });
      const link = document.createElement('a'); link.href = URL.createObjectURL(await response.blob());
      link.download = (response.headers.get('content-disposition') || '').match(/filename="?([^";]+)/)?.[1] || 'Orange-Intelligence.xlsx';
      document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(link.href), 4000); toast('Excel file downloaded.');
    } catch (e) { toast(`Export failed: ${esc(e.message)}`); }
  }
  async function exportFile(request) {
    try {
      const response = await post(urls.export, request);
      const blob = await response.blob(); const link = document.createElement('a'); link.href = URL.createObjectURL(blob);
      link.download = (response.headers.get('content-disposition') || '').match(/filename="?([^";]+)/)?.[1] || `Orange-Intelligence.${request.format}`;
      document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(link.href), 4000); toast(`${request.format.toUpperCase()} file downloaded.`);
    } catch (e) { status(`Export failed: ${e.message}`, true); }
  }
  function exportTender(kind) {
    const t = tenderById(state.tenderSel); if (!t) return;
    if (kind === 'tender-lifecycle') exportFile({ format: 'xlsx', reportType: 'tender_lifecycle', tenderId: t.tenderId, ...period() });
    else if (kind === 'tender-pdf') exportFile({ format: 'pdf', reportType: 'tender_summary', tenderId: t.tenderId, ...period() });
    else if (kind === 'tender-offers') { const round = chosenOfferRound(t); if (!round) return;
      exportFile({ format: 'xlsx', reportType: 'tender_offers', tenderId: t.tenderId, roundId: round.roundId, ...period() }); }
  }

  // ---------- global find (suppliers and tenders in scope) ----------
  function renderFind() {
    $('[data-global-find-list]').innerHTML = state.data.suppliers.map(x => `<option value="${esc(clean(x.name))}">Supplier · ${esc(catKey(x.category))}</option>`).join('') + state.data.tenders.map(t => `<option value="#${t.tenderId} ${esc(clean(t.title))}">Tender · ${esc(clean(t.department))}</option>`).join('');
  }
  function globalFind(text) {
    const q = String(text || '').trim().toLowerCase(); if (!q || !state.data) return;
    const tm = q.match(/^#(\d+)/);
    const t = tm ? tenderById(+tm[1]) : state.data.tenders.find(x => clean(x.title).toLowerCase() === q) || state.data.tenders.find(x => clean(x.title).toLowerCase().includes(q));
    const sup = tm ? null : state.data.suppliers.find(x => clean(x.name).toLowerCase() === q) || state.data.suppliers.find(x => clean(x.name).toLowerCase().includes(q));
    if (sup) { setTab('suppliers'); window.OrangeUniverse?.setView?.('3d'); select([sup.supplierId], { source: 'find', center: true }); }
    else if (t) openTender(t.tenderId);
    else { toast('No authorized supplier or tender matches.'); return; }
    $('[data-global-find]').value = '';
  }

  // ---------- charts ----------
  function chart(key) {
    const el = $(`[data-chart="${key}"]`);
    if (!el || !window.echarts) return null;
    if (!charts[key] || charts[key].getDom() !== el) {
      charts[key]?.dispose();
      charts[key] = echarts.init(el, null, { renderer: 'canvas' });
      new ResizeObserver(() => charts[key]?.resize()).observe(el);
    }
    return charts[key];
  }
  function base() {
    const p = palette();
    return {
      backgroundColor: 'transparent', textStyle: { color: p.ink2, fontFamily: getComputedStyle(root).fontFamily || 'Segoe UI, system-ui, sans-serif', fontSize: 14 }, animationDuration: dur(700), animationDurationUpdate: dur(420), animationEasing: 'cubicOut', animationEasingUpdate: 'cubicOut',
      tooltip: { backgroundColor: p.surface, borderColor: p.line, textStyle: { color: p.ink, fontSize: 14, lineHeight: 21 }, confine: true, extraCssText: 'border-radius:14px;box-shadow:0 18px 44px rgba(0,0,0,.24);padding:12px 14px;' },
      grid: { left: 14, right: 22, top: 34, bottom: 14, containLabel: true }
    };
  }
  const axis = (extra = {}) => { const p = palette(); return { axisLine: { lineStyle: { color: p.line } }, axisTick: { show: false }, axisLabel: { color: p.muted, fontSize: 14 }, splitLine: { lineStyle: { color: p.soft, type: [4, 6] } }, nameTextStyle: { color: p.muted, fontSize: 13.5, fontWeight: 600 }, ...extra }; };
  const empty = (c, text) => c.setOption({ title: { text, left: 'center', top: 'middle', textStyle: { color: palette().muted, fontSize: 15, fontWeight: 500, width: 360, overflow: 'break', lineHeight: 23 } }, xAxis: { show: false }, yAxis: { show: false }, series: [], legend: { show: false }, radar: [] }, true);
  const dim = id => state.selected.length && !state.selected.includes(id);
  const legendStyle = () => ({ textStyle: { color: palette().ink2, fontSize: 14 }, itemWidth: 12, itemHeight: 12, itemGap: 18, icon: 'circle' });
  const vgrad = (c1, c2) => new echarts.graphic.LinearGradient(0, 0, 0, 1, [{ offset: 0, color: c1 }, { offset: 1, color: c2 }]);
  const hgrad = (c1, c2) => new echarts.graphic.LinearGradient(0, 0, 1, 0, [{ offset: 0, color: c1 }, { offset: 1, color: c2 }]);
  const alpha = (hex, a) => { const h = String(hex).replace('#', ''); const n = parseInt(h.length === 3 ? h.split('').map(x => x + x).join('') : h.slice(0, 6), 16); return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`; };

  function renderMarket() { competition(); groups(); positions(); landscape(); awards(); renderInsights(); }

  const comparison = { a: null, b: null, tender: null, rows: [], request: null };
  const comparisonPanel = $('[data-offer-comparison]');
  const comparisonResult = $('[data-comparison-result]');
  const comparisonStatus = $('[data-comparison-status]');
  const ar = root.dataset.uiLanguage === 'ar';
  const tr = (en, arabic) => ar ? arabic : en;
  const locale = ar ? 'ar-JO' : 'en-JO';
  const cycleLabel = n => n === 1 ? tr('Initial Bidding', 'العطاء الأولي') : tr(`Re-tendering — Cycle ${n}`, `إعادة الطرح — الدورة ${n}`);
  const choice = (label, rows) => `<option value="">${esc(label)}</option>` +
    rows.map(([id, title]) => `<option value="${id}">${escapeHtml(title)}</option>`).join('');
  const selectedComparison = () => comparison.rows.find(row => row.tenderId === comparison.tender);
  const comparisonAmount = (currency, total) => total == null
    ? tr('Not recorded', 'غير مسجل')
    : `${total.toLocaleString(locale, { maximumFractionDigits: 6 })} ${currency || tr('(currency unrecorded)', '(العملة غير مسجلة)')}`;
  const comparisonDate = new Intl.DateTimeFormat(locale, { timeZone: 'Asia/Amman', dateStyle: 'medium' });
  const comparisonTime = (date, recorded) => date ? `${comparisonDate.format(new Date(date))} · ${recorded}` : tr('Not recorded', 'غير مسجل');
  function duration(seconds) {
    const days = Math.floor(seconds / 86400);
    const hours = Math.floor(seconds % 86400 / 3600);
    const minutes = Math.floor(seconds % 3600 / 60);
    const remainder = Number((seconds % 60).toFixed(7));
    const units = [[days, tr('d', 'يوم')], [hours, tr('h', 'ساعة')], [minutes, tr('min', 'د')], [remainder, tr('s', 'ث')]];
    return units.filter(([amount], i) => amount !== 0 || (seconds === 0 && i === 3))
      .map(([amount, unit]) => `${amount.toLocaleString(locale, { maximumFractionDigits: 7 })} ${unit}`).join(' ');
  }
  function renderComparisonControls() {
    const suppliers = [...state.suppliers.values()].sort((a, b) => a.name.localeCompare(b.name, locale));
    for (const [key, other] of [['a', 'b'], ['b', 'a']]) {
      const sel = $(`[data-comparison-supplier="${key}"]`);
      sel.innerHTML = choice(tr('Choose a supplier', 'اختر مورداً'), suppliers.filter(s => s.supplierId !== comparison[other]).map(s => [s.supplierId, s.name]));
      sel.value = comparison[key] ?? '';
      sel.disabled = suppliers.length === 0;
    }
    const tender = $('[data-comparison-tender]');
    tender.innerHTML = choice(tr('Choose a shared tender', 'اختر عطاءً مشتركاً'), comparison.rows.map(row => [row.tenderId, row.title]));
    tender.value = comparison.tender ?? '';
    tender.disabled = comparison.rows.length === 0;
  }
  function renderComparison(message) {
    const row = selectedComparison();
    comparisonResult.hidden = !row;
    comparisonStatus.hidden = !!row;
    if (!row) {
      comparisonResult.replaceChildren();
      comparisonStatus.textContent = message ?? (comparison.a && comparison.b
        ? tr('Choose a tender to see both suppliers’ recorded offers.', 'اختر عطاءً لعرض العرضين المسجلين.')
        : tr('Choose two different suppliers to find their shared tenders.', 'اختر موردين مختلفين للعثور على عطاءاتهما المشتركة.'));
      return;
    }
    const { a, b, priceIssue, difference, lowerOffer, firstSubmission, secondsApart } = row;
    const issueText = {
      missing_currency: tr('A currency is missing.', 'عملة أحد العرضين غير مسجلة.'),
      different_currency: tr('The offers use different currencies.', 'العرضان بعملتين مختلفتين.'),
      missing_price: tr('Both offers need recorded items and positive totals.', 'تتطلب المقارنة بنوداً مسجلة وإجماليات موجبة لكلا العرضين.'),
      different_structure: tr('The recorded items, quantities or units differ or are incomplete.', 'البنود أو الكميات أو الوحدات المسجلة مختلفة أو غير مكتملة.'),
      different_specifications: tr('The recorded offered specifications or deviations differ.', 'المواصفات أو الانحرافات المسجلة في العرضين مختلفة.')
    };
    const percent = row.relativeDifference?.toLocaleString(locale, { style: 'percent', maximumFractionDigits: 2 });
    const badges = [tr('Lower offer', 'العرض الأقل'), tr('Submitted first', 'قُدّم أولاً')];
    const badge = label => `<span class="oi-comparison-badge">${label}</span>`;
    const offerLink = bidId => urls.offerrecord
      ? `<a class="rd-link" href="${esc(urls.offerrecord)}?id=${bidId}">${tr('Open offer record', 'افتح سجل العرض')} #${bidId}</a>`
      : `<span class="oi-comparison-meta">${tr('Offer record', 'سجل العرض')} #${bidId}</span>`;
    const price = (offer, key) => {
      const lower = lowerOffer === key;
      const width = priceIssue ? null : offer.total / Math.max(a.total, b.total) * 100;
      return `<div class="oi-comparison-offer ${lower ? 'is-lower' : ''}">
        <div class="oi-comparison-name"><strong>${escapeHtml(offer.supplier)}</strong>${lower ? badge(badges[0]) : ''}</div>
        <bdi class="oi-comparison-amount">${esc(comparisonAmount(offer.currency, offer.total))}</bdi>
        ${width == null ? '' : `<div class="oi-comparison-bar" aria-hidden="true"><span style="width:${width}%"></span></div>`}
        ${offerLink(offer.bidId)}
      </div>`;
    };
    const timing = (offer, key) => {
      const before = offer.secondsBeforeDeadline;
      const deadline = before == null ? tr('Time to deadline unavailable', 'المدة حتى الموعد النهائي غير متاحة') : before === 0
        ? tr('At the deadline', 'عند الموعد النهائي')
        : before > 0 ? tr(`${duration(before)} before the deadline`, `قبل الموعد النهائي بـ ${duration(before)}`)
          : tr(`${duration(-before)} after the deadline`, `بعد الموعد النهائي بـ ${duration(-before)}`);
      return `<div class="oi-comparison-event">
        <div class="oi-comparison-name"><strong>${escapeHtml(offer.supplier)}</strong>${key === firstSubmission ? badge(badges[1]) : ''}</div>
        <bdi class="oi-comparison-time" title="${esc(offer.recordedTime)}">${esc(comparisonTime(offer.submittedAt, offer.recordedTime?.split('.')[0]))}</bdi>
        <span class="oi-comparison-meta">${deadline}</span>
      </div>`;
    };
    const lower = lowerOffer === 'a' ? a : b;
    const higher = lowerOffer === 'a' ? b : a;
    const priceText = lowerOffer
      ? tr(`${lower.supplier} offered ${percent} less than ${higher.supplier}. The percentage is the difference divided by the higher offer.`,
        `عرض ${lower.supplier} أقل بنسبة ${percent} من عرض ${higher.supplier}. تُحسب النسبة بقسمة الفرق على العرض الأعلى.`)
      : tr('Both financial totals are equal. The difference is 0% of either offer.', 'إجمالي العرضين متساوٍ. الفرق 0٪ من أيٍّ من العرضين.');
    const timeText = secondsApart === null ? tr('One or both submission timestamps are missing.', 'وقت تقديم أحد العرضين أو كليهما غير مسجل.')
      : secondsApart === 0 ? tr('Both offers have the same recorded timestamp.', 'للعرضين وقت تقديم مسجل متطابق.')
        : tr(`${firstSubmission === 'a' ? a.supplier : b.supplier} submitted first.`, `قدّم ${firstSubmission === 'a' ? a.supplier : b.supplier} عرضه أولاً.`);
    comparisonResult.innerHTML = `
      <div class="oi-comparison-context">
        <div><strong>${escapeHtml(row.title)} · ${esc(cycleLabel(row.roundNumber))}</strong>
          <span>${priceIssue ? tr('Latest shared closed bidding cycle.', 'أحدث دورة عطاء مغلقة مشتركة.') : tr('Based on the latest comparable closed bidding cycle.', 'استناداً إلى أحدث دورة عطاء مغلقة قابلة للمقارنة.')}</span></div>
        <button type="button" class="rd-btn" data-comparison-open>${tr('Open tender details', 'افتح تفاصيل العطاء')}</button>
      </div>
      <div class="oi-comparison-sections">
        <section class="oi-comparison-card" data-comparison-price aria-labelledby="oi-compare-price">
          <h4 id="oi-compare-price">${tr('Price Comparison', 'مقارنة الأسعار')}</h4>
          <p class="oi-comparison-meta">${tr('Total financial offers', 'إجمالي العروض المالية')}</p>
          <div class="oi-comparison-price-rows">${price(a, 'a')}${price(b, 'b')}</div>
          <div class="oi-comparison-summary"><small>${priceIssue ? tr('Price comparison', 'مقارنة الأسعار') : tr('Absolute difference', 'الفرق المطلق')}</small>
            <strong>${priceIssue ? tr('Not directly comparable', 'غير قابل للمقارنة المباشرة') : esc(comparisonAmount(a.currency, difference))}</strong>
            <p>${priceIssue ? issueText[priceIssue] : escapeHtml(priceText)}</p>
          </div>
          <p class="oi-comparison-meta">${tr('The award depends on the applicable evaluation criteria and official decision.', 'تخضع الترسية لمعايير التقييم والقرار الرسمي.')}</p>
        </section>
        <section class="oi-comparison-card" data-comparison-timing aria-labelledby="oi-compare-time">
          <h4 id="oi-compare-time">${tr('Submission Timing', 'توقيت التقديم')}</h4>
          <p class="oi-comparison-meta">${row.deadline
            ? `${tr('Deadline', 'الموعد النهائي')} · <bdi>${comparisonDate.format(new Date(row.deadline))} · ${new Intl.DateTimeFormat(locale, { timeZone: 'Asia/Amman', timeStyle: 'medium' }).format(new Date(row.deadline))}</bdi>`
            : tr('Deadline not recorded', 'الموعد النهائي غير مسجل')} · ${tr('Amman time', 'توقيت عمّان')}</p>
          <div class="oi-comparison-timeline">${firstSubmission === 'b' ? timing(b, 'b') + timing(a, 'a') : timing(a, 'a') + timing(b, 'b')}</div>
          <div class="oi-comparison-summary"><small>${tr('Time between submissions', 'المدة بين التقديمين')}</small>
            <strong>${secondsApart === null ? tr('Unavailable', 'غير متاح') : duration(secondsApart)}</strong><p>${escapeHtml(timeText)}</p>
          </div>
          <p class="oi-comparison-meta">${tr('Close submission times describe timing alone and do not establish wrongdoing.', 'تقارب أوقات التقديم يصف التوقيت وحده ولا يثبت أي مخالفة.')}</p>
        </section>
      </div>`;
  }
  async function loadComparison(wantedTender = comparison.tender) {
    comparison.request?.abort();
    comparison.request = null;
    comparison.rows = [];
    comparison.tender = null;
    renderComparisonControls();
    if (!comparison.a || !comparison.b || comparison.a === comparison.b) { renderComparison(); return false; }

    const request = new AbortController();
    comparison.request = request;
    renderComparison(tr('Finding shared tenders…', 'جارٍ البحث عن العطاءات المشتركة…'));
    try {
      const query = new URLSearchParams({ supplierA: comparison.a, supplierB: comparison.b });
      const response = await fetch(`${urls.offercomparison}?${query}`, { signal: request.signal });
      if (!response.ok || response.redirected) throw new Error('Comparison unavailable');
      const rows = await response.json();
      if (comparison.request !== request) return false;
      comparison.rows = rows;
      comparison.tender = rows.find(row => row.tenderId === wantedTender)?.tenderId ?? (rows.length === 1 ? rows[0].tenderId : null);
      renderComparisonControls();
      renderComparison(rows.length ? undefined : tr('These suppliers have no eligible shared tenders. Both must have submitted in the same closed bidding cycle.',
        'لا توجد عطاءات مشتركة مؤهلة لهذين الموردين. يجب أن يقدم كلاهما عرضاً في دورة العطاء المغلقة نفسها.'));
      return true;
    } catch {
      if (comparison.request === request) renderComparison(tr('The comparison could not be loaded. Select a supplier again to retry.', 'تعذر تحميل المقارنة. أعد اختيار أحد الموردين للمحاولة مجدداً.'));
      return false;
    }
  }
  async function openComparisonTender() {
    const row = selectedComparison();
    if (!row) return;
    if (!tenderById(row.tenderId)?.rounds.some(r => r.roundId === row.roundId)) {
      $('#rd-from').value = '';
      $('#rd-to').value = '';
      await load();
    }
    if (!tenderById(row.tenderId)) {
      toast(tr('The tender details could not be loaded.', 'تعذر تحميل تفاصيل العطاء.'));
      return;
    }
    offerRoundSel = row.roundId;
    openTender(row.tenderId);
    requestAnimationFrame(() => $('[data-tender-main]')?.scrollIntoView({ behavior: reduced.matches ? 'auto' : 'smooth', block: 'start' }));
  }

  const TAB_CHARTS = { overview: [], suppliers: ['snapCats', 'snapTrend', 'landscape', 'positions'], tenders: ['tenderOffers', 'competition', 'groups', 'awards'], studio: [] };
  function renderTab(tab) {
    if (!state.data) return;
    if (tab === 'suppliers') { renderSnapshot(); landscape(); positions(); renderInsights(); }
    else if (tab === 'tenders') { renderTender(); competition(); groups(); awards(); }
    (TAB_CHARTS[tab] || []).forEach(k => charts[k]?.resize());
    $$('.rd-chat-chart').forEach(el => window.echarts?.getInstanceByDom(el)?.resize());
  }

  function landscape() {
    const c = chart('landscape'); if (!c) return;
    const p = palette(); const sel = new Set(state.selected);
    const scoped = filteredSuppliers();
    const rows = scoped.filter(s => s.participations > 0);
    const silent = scoped.length - rows.length;
    // Win rate needs official awards as denominator; without awards the y axis switches to the invitation response rate.
    const useWin = state.data.awards.length > 0 && rows.some(r => r.winRate != null);
    $('[data-landscape-title]').textContent = useWin ? 'Participation vs win rate' : 'Participation vs response rate';
    $('[data-landscape-note]').textContent = `${useWin ? 'Win rate = awards ÷ awarded tenders bid on.' : 'No official awards in this period, so win rate is undefined; response = offers ÷ closed bidding cycle invitations.'} Colour = category.${silent ? ` ${silent} supplier${silent > 1 ? 's' : ''} without an offer ${silent > 1 ? 'are' : 'is'} not plotted.` : ''}`;
    if (!rows.length) return empty(c, 'No supplier has an offer in a closed bidding cycle of this period.');
    const values = rows.map(r => r.awardedValue[0]?.value || 0); const max = Math.max(1, ...values);
    const byCat = new Map();
    rows.forEach(r => { const k = catKey(r.category); if (!byCat.has(k)) byCat.set(k, []); byCat.get(k).push(r); });
    const yOf = r => (useWin ? r.winRate : r.participationRate);
    const series = [...byCat].map(([cat, list]) => ({
      name: cat, type: 'scatter', id: `cat-${cat}`, data: list.map(r => ({
        value: [r.participations, yOf(r) == null ? null : +(yOf(r) * 100).toFixed(1), r.awardedValue[0]?.value || 0], id: r.supplierId, row: r,
        itemStyle: { opacity: sel.size && !sel.has(r.supplierId) ? 0.22 : 0.95, borderColor: sel.has(r.supplierId) ? p.orange : p.surface, borderWidth: sel.has(r.supplierId) ? 3 : 1.5 }
      })).filter(x => x.value[1] != null),
      symbolSize: v => (useWin ? 18 + Math.sqrt(v[2] / max) * 34 : 22), color: catColor(cat), itemStyle: { shadowBlur: 14, shadowColor: alpha(catColor(cat), 0.45) },
      emphasis: { focus: 'self', scale: 1.2 },
      label: { show: true, position: 'right', distance: 6, formatter: d => (sel.size ? (sel.has(d.data.id) ? name(d.data.id) : '') : name(d.data.id)), color: p.ink2, fontSize: 14, fontWeight: 600 },
      labelLayout: { hideOverlap: true }
    }));
    c.setOption({
      ...base(), legend: { top: 0, left: 0, ...legendStyle(), type: 'scroll' }, grid: { left: 14, right: 40, top: 48, bottom: 30, containLabel: true },
      tooltip: { ...base().tooltip, formatter: d => { const r = d.data.row; return `<b>${esc(fullName(r.supplierId))}</b><br>${esc(catKey(r.category))}<br>Offers in ${r.participations} closed bidding cycles (invited ${r.invitations})<br>Response rate: ${fmt.pct(r.participationRate)}<br>Win rate: ${r.winRate == null ? 'undefined - no awarded tender bid on' : `${fmt.pct(r.winRate, 1)} (${r.wins} of ${r.awardedTendersBid})`}<br>Awarded: ${r.awardedValue.length ? r.awardedValue.map(v => fmt.money(v.currency, v.value)).join(', ') : 'none'}`; } },
      xAxis: { ...axis(), name: 'Closed bidding cycles with an offer', nameLocation: 'middle', nameGap: 32, minInterval: 1 },
      yAxis: { ...axis(), name: useWin ? 'Win rate %' : 'Response rate %', max: 100, min: 0 },
      series
    }, true);
    c.off('click'); c.on('click', d => d.data?.id && select([d.data.id], { additive: d.event?.event?.shiftKey, source: 'landscape' }));
  }

  function competition() {
    const c = chart('competition'); if (!c) return;
    const p = palette(); const comp = state.data.competition;
    if (!comp.rounds.length) return empty(c, 'No closed bidding cycles in this period. Sealed rounds are never shown.');
    const label = r => `${r.tender.replace(/^RADAR DEMO /, '').slice(0, 26)}${r.roundNumber > 1 ? ` · ${tr('Cycle', 'الدورة')} ${r.roundNumber}` : ''}`;
    const selRounds = new Set(state.selected.length ? state.data.network.rounds.filter(r => r.supplierIds.some(id => state.selected.includes(id))).map(r => r.roundId) : []);
    c.setOption({
      ...base(), legend: { top: 0, right: 0, ...legendStyle() }, grid: { left: 14, right: 30, top: 48, bottom: 14, containLabel: true },
      tooltip: { ...base().tooltip, trigger: 'axis', axisPointer: { type: 'shadow' }, formatter: ps => { const r = comp.rounds[ps[0].dataIndex]; const nr = state.data.network.rounds.find(x => x.roundId === r.roundId); return `<b>${esc(r.tender)}</b> · ${esc(cycleLabel(r.roundNumber))}<br>${fmt.date(r.date)} · ${esc(r.department)}<br>Invited ${r.invited} · submitted ${r.submitted} · compliant ${r.compliant}${r.lowCompetition ? `<br><span style="color:${p.signal}">Below the minimum of ${comp.minimumBids}</span>` : ''}${nr?.supplierIds.length ? `<br><span style="color:${p.muted}">${nr.supplierIds.map(name).map(esc).join(', ')}</span>` : ''}`; } },
      xAxis: { ...axis(), type: 'category', data: comp.rounds.map(label), axisLabel: { color: p.ink2, fontSize: 14, interval: 0, lineHeight: 18, ...(comp.rounds.length > 12 ? { rotate: 35, width: 150, overflow: 'truncate' } : { rotate: 0, width: Math.max(64, (c.getWidth() - 90) / comp.rounds.length - 8), overflow: 'break' }) } },
      yAxis: { ...axis(), minInterval: 1, name: 'Suppliers' },
      series: [
        { name: 'Invited', id: 'invited', type: 'bar', data: comp.rounds.map(r => ({ value: r.invited, itemStyle: { opacity: selRounds.size && !selRounds.has(r.roundId) ? 0.35 : 1 } })), itemStyle: { color: alpha(p.neutral, 0.22), borderColor: alpha(p.neutral, 0.6), borderWidth: 1, borderRadius: [10, 10, 4, 4] }, barGap: '-100%', barMaxWidth: 56, z: 1 },
        { name: 'Submitted', id: 'submitted', type: 'bar', color: p.orange, z: 2, label: { show: true, position: 'top', color: p.ink, fontSize: 14, fontWeight: 700 }, data: comp.rounds.map(r => ({ value: r.submitted, itemStyle: { color: r.lowCompetition ? vgrad(p.signal, alpha(p.signal, 0.6)) : vgrad(p.orange, alpha(p.orange, 0.6)), borderRadius: [8, 8, 3, 3], opacity: (state.tender && state.tender !== r.tenderId) || (selRounds.size && !selRounds.has(r.roundId)) ? 0.3 : 1 } })), barMaxWidth: 56,
          markLine: { silent: true, symbol: 'none', lineStyle: { color: p.signal, type: [6, 6], width: 1.6 }, label: { color: p.signal, fontSize: 13.5, fontWeight: 700, formatter: `minimum ${comp.minimumBids}`, position: 'insideEndTop' }, data: [{ yAxis: comp.minimumBids }] } }
      ]
    }, true);
    c.off('click'); c.on('click', d => { const r = comp.rounds[d.dataIndex]; selectTender(r.tenderId, r); });
  }

  function groups() {
    const c = chart('groups'); if (!c) return;
    const p = palette(); const g = state.data.competition.byDepartment;
    if (!g.length) empty(c, 'No closed bidding cycles.');
    else c.setOption({
      ...base(), grid: { left: 8, right: 44, top: 8, bottom: 8, containLabel: true },
      tooltip: { ...base().tooltip, formatter: d => `${esc(d.name)}<br>${fmt.num(d.value)} offers per round over ${g[d.dataIndex].rounds} rounds` },
      xAxis: { ...axis(), type: 'value', minInterval: 1 }, yAxis: { ...axis(), type: 'category', data: g.map(x => x.group.replace(/^RADAR DEMO - /, '')), axisLabel: { color: p.ink2, fontSize: 14, width: 150, overflow: 'truncate' } },
      series: [{ type: 'bar', showBackground: true, backgroundStyle: { color: alpha(p.neutral, 0.12), borderRadius: 8 }, data: g.map(x => { const col = x.avgBids < state.data.competition.minimumBids ? p.signal : p.c[0]; return { value: x.avgBids, itemStyle: { color: hgrad(alpha(col, 0.5), col), borderRadius: 8 } }; }), barMaxWidth: 20, label: { show: true, position: 'right', color: p.ink, fontSize: 14, fontWeight: 700, formatter: d => fmt.num(d.value) } }]
    }, true);
    $('[data-pairs]').innerHTML = state.data.competition.recurringCompetitors.length
      ? `<p class="rd-section-title">Most frequent competitor pairs</p>` + state.data.competition.recurringCompetitors.slice(0, 5).map(x => `<button type="button" data-pair="${x.supplierA},${x.supplierB}" class="${state.selected.includes(x.supplierA) && state.selected.includes(x.supplierB) ? 'is-on' : ''}"><span>${esc(name(x.supplierA))} · ${esc(name(x.supplierB))}</span><b title="Shared Bidding Cycles">${x.rounds}</b></button>`).join('')
      : '<p class="rd-muted">No supplier pair met in a closed bidding cycle.</p>';
  }

  function positions() {
    const c = chart('positions'); if (!c) return;
    const p = palette(); const scope = filteredSupplierIds(); const pos = state.data.positions.filter(x => !scope || scope.has(x.supplierId));
    if (!pos.length) return empty(c, 'No round had two comparable offers in the same currency, so relative prices cannot be calculated yet.');
    const counts = pos.reduce((m, x) => m.set(x.supplierId, (m.get(x.supplierId) || 0) + 1), new Map());
    const ids = state.selected.filter(id => counts.has(id)).slice(0, 5);
    const show = ids.length ? ids : [...counts].sort((a, b) => b[1] - a[1]).slice(0, 4).map(x => x[0]);
    c.setOption({
      ...base(), legend: { top: 'middle', right: 4, orient: 'vertical', ...legendStyle() }, grid: { left: 14, right: '23%', top: 22, bottom: 14, containLabel: true },
      tooltip: { ...base().tooltip, trigger: 'item', formatter: d => { const x = d.data.row; return `<b>${esc(fullName(x.supplierId))}</b><br>${fmt.date(x.date)} · round #${x.roundId}<br>Offer ${fmt.money(x.currency, x.total)} vs median ${fmt.money(x.currency, x.median)}<br>Position ${fmt.signed(x.relative)} (${x.comparable} comparable offers)`; } },
      xAxis: { ...axis(), type: 'time' }, yAxis: { ...axis(), type: 'value', name: 'vs bidding cycle median', axisLabel: { color: p.muted, fontSize: 14, formatter: v => `${v > 0 ? '+' : ''}${v}%` } },
      series: show.map(id => ({
        id: `s-${id}`, name: name(id), type: 'line', symbol: 'circle', symbolSize: 12, smooth: 0.3, emphasis: { focus: 'series' }, lineStyle: { width: 3.2, color: supplierColor(id), shadowBlur: 12, shadowColor: alpha(supplierColor(id), 0.35) }, itemStyle: { color: supplierColor(id), borderColor: p.surface, borderWidth: 2.5 },
        areaStyle: show.length === 1 ? { origin: 0, color: vgrad(alpha(supplierColor(id), 0.22), alpha(supplierColor(id), 0.02)) } : undefined,
        data: pos.filter(x => x.supplierId === id).sort((a, b) => new Date(a.date) - new Date(b.date)).map(x => ({ value: [x.date, +(x.relative * 100).toFixed(2)], row: x })),
        markLine: id === show[0] ? { silent: true, symbol: 'none', lineStyle: { color: p.muted, type: [6, 6], width: 1.6 }, label: { formatter: 'bidding cycle median', position: 'insideEndTop', color: p.muted, fontSize: 13.5, fontWeight: 600 }, data: [{ yAxis: 0 }] } : undefined
      }))
    }, true);
    c.off('click'); c.on('click', d => d.data?.row && (state.chartPoint = `${fullName(d.data.row.supplierId)} in round #${d.data.row.roundId}: ${fmt.signed(d.data.row.relative)} vs median`, select([d.data.row.supplierId], { additive: d.event?.event?.shiftKey, source: 'positions' })));
  }
  let insightTab = 'positions';
  function setInsightTab(tab, focus = false) {
    if (!['landscape', 'positions'].includes(tab)) return;
    insightTab = tab;
    $$('[data-insight-tab]').forEach(button => { const on = button.dataset.insightTab === tab; button.setAttribute('aria-selected', String(on)); button.tabIndex = on ? 0 : -1; if (on && focus) button.focus(); });
    $$('[data-insight-panel]').forEach(panel => { panel.hidden = panel.dataset.insightPanel !== tab; });
    renderInsights();
    requestAnimationFrame(() => { charts[tab]?.resize(); if (state.data) ({ landscape, positions })[tab](); });
  }
  function renderInsights() {
    if (!state.data || !$('[data-insight-panel]')) return;
    const scopeIds = filteredSupplierIds();
    const pos = (state.data.positions || []).filter(x => !scopeIds || scopeIds.has(x.supplierId));
    const counts = pos.reduce((m, x) => m.set(x.supplierId, (m.get(x.supplierId) || 0) + 1), new Map());
    const ids = state.selected.filter(id => counts.has(id)).slice(0, 5);
    const shown = ids.length ? ids : [...counts].sort((a, b) => b[1] - a[1]).slice(0, 4).map(x => x[0]);
    const scope = insightTab === 'positions' ? shown.length : state.selected.length || filteredSuppliers().length;
    $('[data-insight-suppliers]').textContent = `${scope} ${state.selected.length ? 'selected' : 'in view'}`;
    $('[data-insight-category]').textContent = state.filters.category || 'All categories';
    const range = period();
    $('[data-insight-period]').textContent = range.from || range.to ? `${range.from || 'Start'} – ${range.to || 'Today'}` : 'All history';
    $('[data-insight-compare]').textContent = insightTab === 'positions' ? 'vs. bidding cycle median' : 'offers ÷ invitations';
    let items;
    if (insightTab === 'positions') {
      const rows = pos.filter(x => shown.includes(x.supplierId) && Number.isFinite(x.relative));
      const avg = rows.length ? rows.reduce((n, x) => n + x.relative, 0) / rows.length : null;
      const means = shown.map(id => { const v = rows.filter(x => x.supplierId === id); return { id, avg: v.length ? v.reduce((n, x) => n + x.relative, 0) / v.length : null }; }).filter(x => x.avg != null);
      const lowest = means.sort((a, b) => a.avg - b.avg)[0];
      const spread = rows.length ? Math.max(...rows.map(x => x.relative)) - Math.min(...rows.map(x => x.relative)) : null;
      items = [
        [avg == null ? '—' : fmt.signed(avg, 0), 'Avg. price position (shown offers)'],
        [spread == null ? '—' : fmt.pct(spread, 0), 'Range across shown offers'],
        [lowest ? name(lowest.id) : '—', lowest ? `Lowest average (${fmt.signed(lowest.avg, 0)})` : 'No comparable offers'],
        [fmt.int(rows.length), 'Comparable offers shown']
      ];
    } else {
      const list = state.selected.length ? filteredSuppliers().filter(x => state.selected.includes(x.supplierId)) : filteredSuppliers();
      const active = list.filter(x => x.participations > 0);
      const invited = list.reduce((n, x) => n + x.invitations, 0);
      const offers = list.reduce((n, x) => n + x.participations, 0);
      const leader = active.slice().sort((a, b) => b.participations - a.participations)[0];
      items = [[fmt.int(list.length), 'Suppliers in focus'], [fmt.int(active.length), 'Submitted an offer'], [invited ? fmt.pct(offers / invited) : '—', 'Offers ÷ invitations'], [leader ? name(leader.supplierId) : '—', 'Most rounds with an offer']];
    }
    $('[data-insight-takeaways]').innerHTML = `<div class="oi-takeaway-label"><strong>✦ Key Takeaways</strong><small>Calculated from the current scope</small></div>${items.map(([value, label]) => `<div><strong>${esc(value)}</strong><small>${esc(label)}</small></div>`).join('')}`;
  }
  function awards() {
    const c = chart('awards'); if (!c) return;
    const p = palette(); const a = state.data.awards[0];
    $('[data-award-note]').textContent = a ? `Official award value in ${a.currency}${state.data.awards.length > 1 ? ' (other currencies in the table)' : ''}. Concentration (HHI) ${fmt.num(a.hhi, 3)}. Not payments.` : 'Official awards per currency. Recommendations are never counted as awards.';
    const card = c.getDom().closest('.rd-card');
    card.classList.toggle('is-sparse', !a);
    if (!a) return empty(c, 'No official award is recorded in this period - this is missing data, not a zero.');
    // Donut of official award value in one currency; the centre carries the total so even a single award reads well.
    const rows = a.bySupplier; const total = rows.reduce((sum, r) => sum + r.value, 0); const count = rows.reduce((n, r) => n + r.count, 0);
    c.setOption({
      ...base(),
      tooltip: { ...base().tooltip, formatter: d => `<b>${esc(d.data.full)}</b><br>${fmt.money(a.currency, d.data.value)} · ${fmt.pct(d.data.share, 1)} of ${a.currency} awards · ${d.data.count} award(s)` },
      title: { text: fmt.money(a.currency, total), subtext: `${count} official award${count === 1 ? '' : 's'}`, left: 'center', top: '44%', textStyle: { color: p.ink, fontSize: 17, fontWeight: 800 }, subtextStyle: { color: p.muted, fontSize: 14 }, itemGap: 4 },
      series: [{ type: 'pie', radius: ['48%', '68%'], center: ['50%', '52%'], padAngle: rows.length > 1 ? 2 : 0, itemStyle: { borderRadius: 10, borderColor: p.surface, borderWidth: 3 }, minShowLabelAngle: 22, labelLayout: { hideOverlap: true },
        label: { show: true, color: p.ink2, fontSize: 14, fontWeight: 600, alignTo: 'edge', edgeDistance: 6, width: 96, overflow: 'break', formatter: d => `${name(d.data.id)}\n{b|${fmt.pct(d.data.share, 0)}}`, rich: { b: { color: p.ink, fontSize: 15, fontWeight: 800, lineHeight: 22 } } },
        labelLine: { length: 10, length2: 12, lineStyle: { color: p.line } }, emphasis: { scale: true, scaleSize: 6 },
        data: rows.map(r => ({ value: r.value, name: name(r.supplierId), share: r.share, count: r.count, full: r.name, id: r.supplierId, itemStyle: { color: supplierColor(r.supplierId), opacity: dim(r.supplierId) ? 0.3 : 1 } })) }]
    }, true);
    c.off('click'); c.on('click', d => d.data?.id && select([d.data.id], { additive: d.event?.event?.shiftKey, source: 'awards' }));
  }

  // ---------- tables (accessible alternative to every chart) ----------
  function showTable(card, html) {
    card.querySelector('.rd-table-wrap')?.remove();
    const wrap = document.createElement('div'); wrap.className = 'rd-table-wrap'; wrap.tabIndex = -1;
    wrap.innerHTML = `<div class="rd-table-head"><button type="button" class="rd-link" data-close-table>Close table</button></div>${html}`;
    card.append(wrap); wrap.focus();
  }
  const tables = {
    landscape: () => `<table class="rd-table"><thead><tr><th>Supplier</th><th>Category</th><th class="num">Invited</th><th class="num">Bidding Cycles Participated In</th><th class="num">Response</th><th class="num">Wins / awarded bid</th><th>Awarded value</th><th class="num">Rel. price</th></tr></thead><tbody>${state.data.suppliers.slice().sort((a, b) => b.participations - a.participations).map(s => `<tr data-select="${s.supplierId}"><td>${esc(s.name)}</td><td>${esc(catKey(s.category))}</td><td class="num">${s.invitations}</td><td class="num">${s.participations}</td><td class="num">${fmt.pct(s.participationRate)}</td><td class="num">${s.wins} / ${s.awardedTendersBid}</td><td>${s.awardedValue.map(v => fmt.money(v.currency, v.value)).join(', ') || '—'}</td><td class="num">${fmt.signed(s.relativePrice)}</td></tr>`).join('')}</tbody></table>`,
    competition: () => `<table class="rd-table"><thead><tr><th>Tender</th><th>Bidding Cycle</th><th>Date</th><th>Department</th><th class="num">Invited</th><th class="num">Submitted</th><th class="num">Compliant</th></tr></thead><tbody>${state.data.competition.rounds.map(r => `<tr><td>${esc(r.tender)}</td><td>${esc(cycleLabel(r.roundNumber))}</td><td>${fmt.date(r.date)}</td><td>${esc(r.department)}</td><td class="num">${r.invited}</td><td class="num">${r.submitted}</td><td class="num">${r.compliant}</td></tr>`).join('')}</tbody></table>`,
    awards: () => state.data.awards.length ? state.data.awards.map(a => `<table class="rd-table"><thead><tr><th>Supplier (${esc(a.currency)})</th><th class="num">Value</th><th class="num">Share</th><th class="num">Awards</th></tr></thead><tbody>${a.bySupplier.map(r => `<tr data-select="${r.supplierId}"><td>${esc(r.name)}</td><td class="num">${fmt.num(r.value)}</td><td class="num">${fmt.pct(r.share, 1)}</td><td class="num">${r.count}</td></tr>`).join('')}${a.byDepartment.map(r => `<tr><td>Department: ${esc(r.name)}</td><td class="num">${fmt.num(r.value)}</td><td class="num">${fmt.pct(r.share, 1)}</td><td class="num">${r.count}</td></tr>`).join('')}</tbody></table>`).join('') : '<p class="rd-muted" style="padding:12px">No official awards in this period.</p>',
    positions: () => `<table class="rd-table"><thead><tr><th>Supplier</th><th>Date</th><th>Bidding Cycle ID</th><th class="num">Offer</th><th class="num">Median</th><th class="num">Position</th></tr></thead><tbody>${state.data.positions.map(x => `<tr data-select="${x.supplierId}"><td>${esc(fullName(x.supplierId))}</td><td>${fmt.date(x.date)}</td><td>#${x.roundId}</td><td class="num">${fmt.money(x.currency, x.total)}</td><td class="num">${fmt.money(x.currency, x.median)}</td><td class="num">${fmt.signed(x.relative)}</td></tr>`).join('')}</tbody></table>`
  };

  // ---------- selection & cross-highlighting ----------
  function select(ids, { additive = false, source = '', center = false } = {}) {
    let next = additive ? [...state.selected] : [];
    ids.forEach(id => { const i = next.indexOf(id); if (additive && i >= 0) next.splice(i, 1); else if (i < 0) next.push(id); });
    next = next.filter(id => state.suppliers.has(id)).slice(-MAX_SELECTED);
    state.selected = next;
    if (source !== 'network-silent') network.syncSelection(next, { center: center || (source && !source.startsWith('network') && source !== 'load'), inspect: source !== 'load' });
    renderSelection(source);
  }
  function renderSelection(source = '') {
    if (!state.data) return;
    landscape(); awards(); positions(); competition(); groups(); renderInsights();
    const chips = state.selected.map(id => `<span class="rd-sel-chip"><i class="rd-dot" style="background:${catColor(catKey(state.suppliers.get(id)?.category))}"></i>${esc(name(id))}<button type="button" data-unselect="${id}" aria-label="Remove ${esc(name(id))}">×</button></span>`).join('');
    $('[data-market-focus]').innerHTML = state.selected.length ? `<span class="rd-muted">Focused</span>${chips}<button type="button" class="rd-link" data-clear-selection>Reset</button>` : '<span class="rd-muted">Select suppliers in the network or any chart (Shift+click to compare up to five).</span>';
    renderScenarioPick(); renderStudio(); renderAiContext();
    if (state.tab === 'tenders') { const t = tenderById(state.tenderSel); if (t) tenderOffers(t, t.rounds.filter(r => r.analysed && r.offers)); }
    window.dispatchEvent(new CustomEvent('radar:selection', { detail: context() }));
  }
  function selectTender(tenderId, round) {
    openTender(tenderId, { tab: false });
    const r = round || state.data.competition.rounds.find(x => x.tenderId === tenderId);
    const nr = r && state.data.network.rounds.find(x => x.roundId === r.roundId);
    if (nr) toast(`<b>${esc(clean(r.tender))}</b> · ${esc(cycleLabel(r.roundNumber))}: ${nr.supplierIds.length} offer${nr.supplierIds.length === 1 ? '' : 's'}. <button type="button" class="rd-link" data-tender-network="${r.roundId}">See it in the Supplier Universe</button>`, 6500);
    $('[data-tender-main]')?.scrollIntoView({ behavior: reduced.matches ? 'auto' : 'smooth', block: 'start' });
  }

  function renderAiContext() {
    const el = $('[data-ai-context]'); if (!el || !state.data) return;
    const parts = [`<span>Tab: <b>${esc(state.tab)}</b></span>`, `<span>Scope: <b>${esc(state.data.scope)}</b></span>`];
    if (state.tab === 'tenders' && tenderById(state.tenderSel)) parts.push(`<span>Tender: <b>${esc(clean(tenderById(state.tenderSel).title))}</b></span>`);
    if (state.selected.length) parts.push(`<span>Suppliers: <b>${state.selected.map(name).map(esc).join(', ')}</b></span>`);
    if (state.replayAt != null) parts.push(`<span>Replay at <b>${fmt.date(state.replayAt)}</b></span>`);
    el.innerHTML = `<span class="rd-muted">Shared with the analyst</span>${parts.join('')}`;
  }

  // ---------- tabs (exclusive: a tab replaces the main content) ----------
  function setTab(tab, { push = true, focus = false } = {}) {
    tab = SCENE_TAB[tab] || tab; if (!TABS.includes(tab)) return;
    const changed = tab !== state.tab;
    state.tab = tab; state.scene = tab; root.dataset.tab = tab;
    $$('[data-tab-link]').forEach(b => { const on = b.dataset.tabLink === tab; b.setAttribute('aria-selected', String(on)); b.tabIndex = on ? 0 : -1; });
    $$('[data-tab-panel]').forEach(p => (p.hidden = p.dataset.tabPanel !== tab));
    // The single voice stage docks under the universe on Suppliers and in AI Studio elsewhere; the compact companion covers the rest.
    const stage = $('[data-voice-stage]'); const slot = $(`[data-voice-slot="${tab === 'suppliers' ? 'suppliers' : 'studio'}"]`);
    if (stage && slot && stage.parentElement !== slot) slot.append(stage);
    stage?.classList.toggle('is-bar', tab === 'suppliers');
    if (push) history.replaceState(null, '', `#${tab}`);
    if (changed) { const top = root.getBoundingClientRect().top + scrollY - 12; if (scrollY > top) window.scrollTo({ top, behavior: 'auto' }); }
    requestAnimationFrame(() => renderTab(tab));
    if (focus) $(`[data-tab-link="${tab}"]`)?.focus();
    renderAiContext();
    window.dispatchEvent(new CustomEvent('radar:tab', { detail: tab }));
    window.dispatchEvent(new CustomEvent('radar:scene', { detail: tab }));
  }
  root.classList.add('is-enhanced');
  const followAI = () => $('[data-follow-ai]')?.checked !== false;
  function goScene(scene, { guided = false, target = null } = {}) {
    const tab = SCENE_TAB[scene] || scene;
    // AI-directed moves respect "Follow the analyst"; otherwise the user gets a one-click jump instead of a forced tab change.
    if (guided && !followAI() && tab !== state.tab) { toast(`The analyst updated <b>${esc(tab)}</b>. <button type="button" class="rd-link" data-scene-jump="${esc(tab)}">Go there</button>`); return; }
    setTab(tab);
    if (target) requestAnimationFrame(() => target.scrollIntoView({ behavior: reduced.matches ? 'auto' : 'smooth', block: 'center' }));
  }

  // ---------- scenario ----------
  function renderScenarioPick(preselect) {
    if (!state.data) return;
    const box = $('[data-scenario-pick]');
    const chosen = new Set(preselect || (box.dataset.touched ? $$('[data-scenario-pick] input:checked').map(i => +i.value) : state.selected));
    box.innerHTML = state.data.suppliers.filter(s => s.participations > 0).sort((a, b) => b.participations - a.participations)
      .map(s => `<label class="${chosen.has(s.supplierId) ? 'is-on' : ''}"><input type="checkbox" value="${s.supplierId}" ${chosen.has(s.supplierId) ? 'checked' : ''} /><i class="rd-dot" style="background:${catColor(catKey(s.category))}"></i>${esc(name(s.supplierId))}<small>${s.participations}</small></label>`).join('') || '<span class="rd-muted">No participating suppliers.</span>';
  }
  async function simulate(ids) {
    if (ids) { $('[data-scenario-pick]').dataset.touched = ''; renderScenarioPick(ids); }
    const chosen = $$('[data-scenario-pick] input:checked').map(i => +i.value);
    const out = $('[data-scenario-result]');
    if (!chosen.length) { out.innerHTML = '<p class="rd-muted">Choose at least one supplier to exclude.</p>'; return; }
    out.innerHTML = '<p class="rd-muted">Replaying history without the chosen suppliers…</p>';
    try {
      const s = await (await post(urls.simulate, { ...period(), supplierIds: chosen })).json();
      state.simulation = `Without ${s.excludedNames.join(', ')}: ${s.roundsAffected} rounds affected, rounds below minimum ${s.roundsBelowMinimumBefore} → ${s.roundsBelowMinimumAfter}, ${s.roundsWithoutBidders} without any bidder.`;
      const max = Math.max(1, ...s.rounds.map(r => r.before));
      const compare = (label, before, after, f = v => v) => `<div class="rd-before-after"><span>${label}</span><b>${f(before)}</b><i aria-hidden="true">→</i><b class="${after > before ? 'is-worse' : ''}">${f(after)}</b></div>`;
      out.innerHTML = `<div class="rd-note"><b>Hypothetical.</b> ${esc(s.notice)}</div>
        <div class="rd-ba-grid">${compare(`Bidding cycles below ${state.data.competition.minimumBids} offers`, s.roundsBelowMinimumBefore, s.roundsBelowMinimumAfter)}${compare('Offer concentration (HHI)', s.hhiBefore, s.hhiAfter, v => fmt.num(v, 3))}
          <div class="rd-before-after"><span>Bidding cycles left with no bidder</span><b>${s.roundsWithoutBidders}</b></div><div class="rd-before-after"><span>Share of offers removed</span><b>${fmt.pct(s.bidShareRemoved, 1)}</b></div></div>
        ${kvBlock([['Excluded', s.excludedNames.map(esc).join('<br>')], ['Bidding cycles affected', s.roundsAffected], ['Award value previously won', s.awardExposure.map(v => `${fmt.money(v.currency, v.value)} (${v.count})`).join('<br>') || 'none']])}
        <h4 class="rd-section-title">Affected bidding cycles</h4><ul class="rd-sim-rounds">${s.rounds.map(r => `<li><span>${esc(r.tender.replace(/^RADAR DEMO /, ''))} · ${cycleLabel(r.roundNumber)}</span><i><u style="width:${(r.before / max) * 100}%"></u><u class="is-after" style="width:${(r.after / max) * 100}%"></u></i><b>${r.before} → ${r.after}</b>${r.dropsBelowMinimum ? '<em>below minimum</em>' : ''}${r.losesAward ? '<em>loses awarded supplier</em>' : ''}</li>`).join('') || '<li class="rd-muted">None</li>'}</ul>
        <h4 class="rd-section-title">Eligible alternatives in the same category</h4><div class="rd-chip-list">${s.alternatives.map(a => `<button type="button" data-pick="${a.supplierId}">${esc(name(a.supplierId))} · ${a.participations} rounds</button>`).join('') || '<span class="rd-muted">No other approved supplier in the same category.</span>'}</div>`;
    } catch (e) { out.innerHTML = `<p class="rd-msg rd-msg-error">${esc(e.message)}</p>`; }
  }
  const kvBlock = rows => `<dl class="rd-kv">${rows.map(([k, v]) => `<dt>${esc(k)}</dt><dd>${v}</dd>`).join('')}</dl>`;

  // ---------- report studio ----------
  function renderStudio() {
    if (!state.data) return;
    const p = period();
    $('[data-report-scope]').innerHTML = kvBlock([['Period', p.from || p.to ? `${esc(p.from || '…')} → ${esc(p.to || '…')}` : 'All recorded history'],
      ['Suppliers in focus', state.selected.length ? state.selected.map(name).map(esc).join(', ') : '<span class="rd-muted">none - market-wide report</span>']]);
  }

  // ---------- timeline / historical replay ----------
  let replayFrame = 0;
  function timelineRange() {
    const ev = state.data?.timeline || [];
    if (!ev.length) return null;
    const t = ev.map(e => Date.parse(e.at));
    const min = Math.min(...t), max = Math.max(...t);
    return { min: min - 86400000 * 7, max: max + 86400000 * 3 };
  }
  function renderTimeline() {
    const canvas = $('[data-timeline-canvas]'); const range = timelineRange();
    const ctx = canvas.getContext('2d'); const w = canvas.clientWidth; const h = 48; const ratio = devicePixelRatio || 1;
    if (!w) return;
    canvas.width = w * ratio; canvas.height = h * ratio; ctx.setTransform(ratio, 0, 0, ratio, 0, 0); ctx.clearRect(0, 0, w, h);
    if (!range) { $('[data-timeline-date]').textContent = 'No events'; return; }
    const p = palette(); const x = t => ((t - range.min) / (range.max - range.min)) * (w - 16) + 8;
    ctx.strokeStyle = p.line; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(8, h / 2); ctx.lineTo(w - 8, h / 2); ctx.stroke();
    if (state.replayAt != null) { ctx.strokeStyle = p.orange; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(8, h / 2); ctx.lineTo(x(state.replayAt), h / 2); ctx.stroke(); }
    for (const e of state.data.timeline) {
      const at = Date.parse(e.at); const past = state.replayAt == null || at <= state.replayAt;
      ctx.globalAlpha = past ? 1 : 0.22;
      ctx.fillStyle = e.type === 'award' ? p.award : e.type === 'pattern' ? p.signal : p.neutral;
      ctx.beginPath(); ctx.arc(x(at), h / 2 + (e.type === 'pattern' ? -11 : e.type === 'award' ? 11 : 0), e.type === 'round' ? 4 : 3.5, 0, Math.PI * 2); ctx.fill();
    }
    ctx.globalAlpha = 1;
    $('[data-timeline-date]').textContent = state.replayAt == null ? 'All history' : fmt.date(state.replayAt);
  }
  function setReplay(value) {
    const range = timelineRange(); if (!range) return;
    state.replayAt = value >= 1000 ? null : range.min + (range.max - range.min) * (value / 1000);
    $('[data-timeline-range]').value = value;
    network.setReplayAt(state.replayAt);
    renderTimeline(); renderAiContext();
    root.classList.toggle('is-replaying', state.replayAt != null);
  }
  function openReplay() { const r = $('[data-replay]'); if (r.hidden) { r.hidden = false; $('[data-net-tool="replay"]').setAttribute('aria-expanded', 'true'); renderTimeline(); } }
  function play(on = !state.playing) {
    state.playing = on;
    const btn = $('[data-timeline="play"]');
    btn.innerHTML = on ? '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5v14M16 5v14"/></svg>' : '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 4l13 8-13 8z"/></svg>';
    btn.setAttribute('aria-label', on ? 'Pause historical replay' : 'Play historical replay');
    cancelAnimationFrame(replayFrame);
    if (!on) return;
    openReplay();
    let value = +$('[data-timeline-range]').value; if (value >= 1000) value = 0;
    let last = performance.now();
    const step = now => {
      const speed = +$('[data-timeline-speed]').value;
      value = Math.min(1000, value + ((now - last) / 1000) * 45 * speed); last = now;
      setReplay(Math.round(value));
      if (value >= 1000) { play(false); return; }
      replayFrame = requestAnimationFrame(step);
    };
    replayFrame = requestAnimationFrame(step);
  }

  // ---------- briefing ----------
  const brief = { scenes: [], index: 0, paused: false, token: 0 };
  async function startBriefing() {
    const language = window.OrangeRadarVoice?.language?.() === 'ar' || root.dataset.uiLanguage === 'ar' ? 'ar' : 'en';
    status('Preparing a verified briefing…');
    try {
      const b = await (await post(urls.briefing, { ...period(), language, aiNarration: true })).json();
      status('');
      Object.assign(brief, { scenes: b.scenes, index: 0, paused: false, token: brief.token + 1, language });
      $('[data-briefing]').hidden = false;
      $('[data-brief-progress]').innerHTML = b.scenes.map(() => '<i></i>').join('');
      playScene();
    } catch (e) { status(`The briefing could not be prepared: ${e.message}`, true); }
  }
  async function playScene() {
    const token = ++brief.token; const scene = brief.scenes[brief.index];
    if (!scene) return stopBriefing();
    cancelAnimations();
    $$('[data-brief-progress] i').forEach((i, n) => { i.className = n < brief.index ? 'is-done' : n === brief.index ? 'is-current' : ''; i.style.setProperty('--p', 0); });
    $('[data-brief-kicker]').textContent = `Scene ${brief.index + 1} of ${brief.scenes.length} · ${scene.narrationSource}`;
    $('[data-brief-title]').textContent = scene.title.replace(/RADAR DEMO /g, '');
    $('[data-brief-text]').textContent = scene.narration.replace(/RADAR DEMO /g, '');
    $('[data-brief-text]').dir = brief.language === 'ar' ? 'rtl' : 'ltr';
    $('[data-brief-facts]').innerHTML = scene.facts.map(f => `<div><dt>${esc(f.label)}</dt><dd>${esc(f.value)}</dd></div>`).join('');
    // Visual direction: section, selection and camera. Briefing is explicit intent, so it may scroll.
    const ids = scene.supplierIds.filter(id => state.suppliers.has(id));
    if (scene.mode === 'network') {
      goScene('network');
      const analytic = scene.edgeIds.some(id => !/^e-(participation|award)-/.test(id));
      if (analytic && network.view !== 'category') network.setView('category', { animate: true });
      select(ids, { source: 'network-silent' }); network.syncSelection(ids, { inspect: false });
      if (analytic) later(() => network.showElements(scene.edgeIds), dur(620)); else if (scene.focus === 'fit') network.fit();
    } else { select(ids, { source: 'briefing' }); goScene('market', { target: scene.mode === 'price' ? $('[data-chart="positions"]').closest('.rd-card') : null }); }
    const started = performance.now();
    const minimum = Math.max(4500, scene.narration.split(/\s+/).length * 330);
    const spoken = window.OrangeRadarVoice?.narrate ? window.OrangeRadarVoice.narrate(scene.narration, brief.language) : null;
    const progress = () => { if (token !== brief.token) return; const el = $('[data-brief-progress] .is-current'); if (el) el.style.setProperty('--p', Math.min(1, (performance.now() - started) / minimum)); if (performance.now() - started < minimum) requestAnimationFrame(progress); };
    requestAnimationFrame(progress);
    await Promise.all([spoken || Promise.resolve(), new Promise(r => later(r, minimum))]);
    if (token !== brief.token || brief.paused) return;
    brief.index++;
    if (brief.index < brief.scenes.length) playScene(); else { $$('[data-brief-progress] i').forEach(i => (i.className = 'is-done')); $('[data-brief-kicker]').textContent = 'Briefing complete'; }
  }
  function stopBriefing() { brief.token++; window.OrangeRadarVoice?.stopSpeaking?.(); $('[data-briefing]').hidden = true; cancelAnimations(); }
  function pauseBriefing(pause = !brief.paused) {
    brief.paused = pause;
    $('[data-brief="toggle"]').textContent = pause ? '▶' : '❚❚';
    $('[data-brief="toggle"]').setAttribute('aria-label', pause ? 'Resume' : 'Pause');
    if (pause) { brief.token++; window.OrangeRadarVoice?.stopSpeaking?.(); } else playScene();
  }

  // ---------- report ----------
  async function generateReport() {
    status('Generating the PDF intelligence report from verified calculations…');
    const images = [];
    const grab = (key, title) => { const c = charts[key]; if (c && c.getDom().clientWidth > 0) { try { images.push({ title, dataUrl: c.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#ffffff' }) }); } catch { /* chart not drawable */ } } };
    grab('competition', 'Competition per round');
    grab('positions', 'Relative price position over time');
    try {
      const transcript = $('[data-report-transcript]')?.checked === false ? [] : window.OrangeRadarVoice?.transcript?.() || [];
      const response = await post(urls.report, { ...period(), supplierIds: state.selected, findingIds: [], transcript, images, aiInterpretation: $('[data-report-ai]')?.checked !== false });
      const blob = await response.blob();
      const link = document.createElement('a'); link.href = URL.createObjectURL(blob);
      link.download = (response.headers.get('content-disposition') || '').match(/filename="?([^";]+)/)?.[1] || 'Orange-Radar-Report.pdf';
      document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(link.href), 4000);
      status(''); toast('Report downloaded.');
      return true;
    } catch (e) { status(`The report could not be generated: ${e.message}`, true); return false; }
  }

  // ---------- context & commands (for the AI analyst) ----------
  function context() {
    const p = period();
    const mode = { suppliers: 'network', tenders: 'price', overview: 'overview', studio: 'overview' }[state.tab] || 'network';
    const f = state.filters; const filters = [f.department && `department ${f.department}`, f.category && `category ${f.category}`, f.activity !== 'all' && `activity ${f.activity}`].filter(Boolean).join(', ');
    return { mode, selectedSupplierIds: state.selected, selectedTenderId: state.tab === 'tenders' ? state.tenderSel : state.tender, selectedRoundId: offerRoundSel || null, from: p.from, to: p.to, selectedFindingId: null,
      comparisonSupplierA: comparison.a, comparisonSupplierB: comparison.b, comparisonTenderId: comparison.tender,
      selectedChartPoint: [state.chartPoint, filters && `Supplier filters: ${filters}`, `Open tab: ${state.tab}`].filter(Boolean).join(' · ') || null, edgeFilters: network.edgeFilters,
      simulationSummary: state.simulation, networkView: window.OrangeUniverse && window.OrangeUniverse.view === '3d' ? `universe-${window.OrangeUniverse.mode}` : network.view };
  }
  // ---------- verified navigation ----------
  // The user asked to see something: open it, select the record, wait until the component is rendered, bring it into view,
  // highlight it, and report what is actually visible. The analyst only states success when this returns ok.
  const frame = () => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
  const visible = el => { if (!el || el.closest('[hidden]')) return false; const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0 && r.bottom > 40 && r.top < innerHeight - 40; };
  async function until(check, ms = 2500) { const end = performance.now() + ms; while (performance.now() < end) { if (check()) return true; await new Promise(r => setTimeout(r, 80)); } return check(); }
  async function navigate(command) {
    if (!command || typeof command !== 'object' || !state.data) return { ok: false, reason: 'The page data is not loaded yet.' };
    const d = String(command.destination || ''); const tid = +command.tenderId || 0, sid = +command.supplierId || 0, rid = +command.roundId || 0;
    const uni = window.OrangeUniverse; let target = null; let check = () => true; let label = '';
    const universeMode = mode => { setTab('suppliers'); uni?.setView?.('3d'); if (uni && uni.mode !== mode) uni.setMode(mode); target = $('[data-universe]'); };
    cancelAnimations();
    if (['tender', 'tender_lifecycle', 'tender_offers'].includes(d)) {
      if (!tenderById(tid)) return { ok: false, reason: 'That tender is not on this page.' };
      if (rid && state.data.network.rounds.some(r => r.roundId === rid && r.tenderId === tid)) offerRoundSel = rid;
      openTender(tid, { tab: false }); setTab('tenders'); await frame();
      target = d === 'tender_offers' ? $('[data-tender-offers-section]') : d === 'tender_lifecycle' ? $('[data-tender-timeline]')?.closest('.rd-card') : $('[data-tender-main]');
      check = () => state.tenderSel === tid; label = `${clean(tenderById(tid).title)} · ${d === 'tender' ? 'tender' : d === 'tender_offers' ? 'financial offers' : 'lifecycle'}`;
    } else if (d === 'supplier' || (d === 'competition' && sid)) {
      if (!state.suppliers.has(sid)) return { ok: false, reason: 'That supplier is not on this page.' };
      universeMode(d === 'competition' ? 'competition' : 'universe'); select([sid], { source: 'ai', center: true });
      check = () => (uni?.selected || []).includes(sid) && (d !== 'competition' || uni?.mode === 'competition');
      label = `${name(sid)}${d === 'competition' ? ' · competitors' : ''}`;
    } else if (['supplier_market', 'categories', 'competition', 'tender_network'].includes(d)) {
      const mode = { supplier_market: 'universe', categories: 'category', competition: 'competition', tender_network: 'tender' }[d];
      if (state.selected.length) select([], { source: 'ai' });
      universeMode(mode);
      if (d === 'categories' && command.category && !uni?.focusCategory(command.category)) return { ok: false, reason: `Category ${command.category} is not on this page.` };
      check = () => !uni || uni.mode === mode; label = `Supplier Universe · ${mode} mode${command.category ? ` · ${command.category}` : ''}`;
    } else if (d === 'price_patterns' || d === 'submission_timing' || d === 'supplier_offer_comparison') {
      if (d === 'supplier_offer_comparison' && tid) {
        const second = Number(command.secondSupplierId);
        if (!state.suppliers.has(sid) || !state.suppliers.has(second) || sid === second)
          return { ok: false, reason: 'Both suppliers must be in the authorized supplier list.' };
        Object.assign(comparison, { a: sid, b: second });
        if (!(await loadComparison(tid)) || comparison.tender !== tid || (rid && selectedComparison()?.roundId !== rid))
          return { ok: false, reason: 'No shared eligible bidding cycle was loaded for that tender.' };
        check = () => comparison.a === sid && comparison.b === second && comparison.tender === tid && !comparisonResult.hidden;
      }
      setTab('suppliers');
      target = d === 'submission_timing' && !comparisonResult.hidden ? $('[data-comparison-timing]') :
        d === 'price_patterns' && !comparisonResult.hidden ? $('[data-comparison-price]') : comparisonPanel;
      label = tr('Supplier Offer Comparison', 'مقارنة عروض الموردين');
    } else if (d === 'tenders') { setTab('tenders'); target = $('[data-tab-panel="tenders"]'); label = 'Tenders'; }
    else if (d === 'overview') { setTab('overview'); target = $('[data-tab-panel="overview"]'); label = 'Overview'; }
    else return { ok: false, reason: 'That view does not exist.' };
    await frame();
    if (!(await until(check))) return { ok: false, label, reason: 'The view opened, but the requested selection could not be applied.' };
    if (target) target.scrollIntoView({ behavior: reduced.matches ? 'auto' : 'smooth', block: target === comparisonPanel ? 'start' : 'center' });
    // A slow or interrupted smooth scroll must not leave the target off screen: finish it instantly.
    let seen = await until(() => visible(target), 1800);
    if (!seen && target) { target.scrollIntoView({ behavior: 'auto', block: 'start' }); seen = await until(() => visible(target), 600); }
    if (target) { target.classList.remove('oi-ai-spotlight'); void target.offsetWidth; target.classList.add('oi-ai-spotlight'); later(() => target.classList.remove('oi-ai-spotlight'), 2400); }
    if (!seen) return { ok: false, label, reason: 'The view opened but is not visible on screen.' };
    lastAction = `Showing ${label}`;
    window.dispatchEvent(new CustomEvent('radar:action', { detail: lastAction }));
    return { ok: true, label };
  }

  // Whitelisted visual commands only; arguments were validated on the server and are re-checked here.
  let lastAction = '';
  function apply(command) {
    if (!command || typeof command !== 'object' || !state.data) return;
    try {
      lastAction = '';
      applyCommand(command);
      window.dispatchEvent(new CustomEvent('radar:action', { detail: lastAction }));
      // Visible feedback on the network whenever the analyst moved it.
      if (lastAction && ['network', 'category', 'highlight', 'compare', 'path', 'timeline'].includes(command.action)) {
        const touched = [...(Array.isArray(command.supplierIds) ? command.supplierIds : []), command.from, command.to].map(Number).filter(id => state.suppliers.has(id));
        later(() => network.aiCue(lastAction, touched), 500);
      }
    } catch (e) { console.warn('Orange Radar command ignored', e); }
  }
  function applyCommand(command) {
    const ids = (Array.isArray(command.supplierIds) ? command.supplierIds : []).map(Number).filter(id => state.suppliers.has(id)).slice(0, MAX_SELECTED);
    const guided = { guided: true };
    const spotlight = el => { if (!el) return; el.classList.remove('oi-ai-spotlight'); void el.offsetWidth; el.classList.add('oi-ai-spotlight'); el.scrollIntoView({ behavior: reduced.matches ? 'auto' : 'smooth', block: 'center' }); setTimeout(() => el.classList.remove('oi-ai-spotlight'), 2200); };
    switch (command.action) {
      case 'navigate': navigate(command); break;
      case 'mode': {
        const m = String(command.mode);
        if (m === 'network') { goScene('network', guided); lastAction = 'Showed the network'; }
        else if (m === 'price') { goScene('market', { ...guided, target: $('[data-chart="positions"]').closest('.rd-card') }); lastAction = 'Showed price intelligence'; }
        else if (m === 'overview') { goScene('market', guided); lastAction = 'Showed market patterns'; }
        break;
      }
      case 'network': if (['universe', 'category', 'competition', 'tender'].includes(command.view)) {
        if (command.view !== 'universe') network.setView(command.view); goScene('network', guided);
        if (window.OrangeUniverse && window.OrangeUniverse.view !== '2d') window.OrangeUniverse.setMode(command.view);
        lastAction = `Supplier universe: ${command.view} view`; } break;
      case 'category': if (typeof command.category === 'string' && (window.OrangeUniverse?.focusCategory(command.category) || network.focusCategory(command.category))) { goScene('network', guided); lastAction = `Focused category ${command.category}`; } break;
      case 'highlight':
        if (ids.length) { select(ids, { source: 'ai', center: true }); goScene('network', guided); lastAction = `Highlighted ${ids.map(name).join(', ')}`; }
        break;
      case 'compare': if (ids.length) { select(ids, { source: 'ai', center: true }); goScene('network', guided); lastAction = `Comparing ${ids.length} suppliers`; } break;
      case 'path': if (state.suppliers.has(+command.from) && state.suppliers.has(+command.to)) { goScene('network', guided); window.OrangeUniverse?.setView('2d'); setTimeout(() => network.tracePath(+command.from, +command.to), 120); lastAction = `Path ${name(+command.from)} → ${name(+command.to)}`; } break;
      case 'simulate': if (ids.length) { select(ids, { source: 'ai' }); goScene('reports', guided); simulate(ids); lastAction = 'Ran the dependency scenario'; } break;
      case 'period': $('#rd-from').value = /^\d{4}-\d{2}-\d{2}$/.test(command.from) ? command.from : ''; $('#rd-to').value = /^\d{4}-\d{2}-\d{2}$/.test(command.to) ? command.to : ''; load(); lastAction = 'Changed the period'; break;
      case 'timeline': if (command.value === 'reset') { play(false); setReplay(1000); } else { if (command.value === 'play') { goScene('network', guided); window.OrangeUniverse?.setView('2d'); } play(command.value === 'play'); } lastAction = `Replay ${command.value}`; break;
      case 'briefing': startBriefing(); lastAction = 'Started the briefing'; break;
      case 'report': generateReport(); lastAction = 'Generating the report'; break;
      case 'download': if (/^[0-9a-f]{32}$/.test(command.token)) { download(`${urls.download}?token=${command.token}`, command.fileName); lastAction = 'Downloading the generated file'; } break;
      case 'export': if (['xlsx', 'docx', 'pdf'].includes(command.format) && ['tender_lifecycle', 'tender_offers', 'tender_summary', 'supplier_offers', 'supplier_participation', 'market_overview', 'price_timing'].includes(command.reportType)) { exportFile({ format: command.format, reportType: command.reportType, tenderId: command.tenderId || null, supplierIds: command.supplierIds || [], roundId: command.roundId || null, from: command.from || null, to: command.to || null }); lastAction = 'Generating the requested file'; } break;
    }
  }
  window.OrangeRadar = Object.freeze({
    context, apply, cancelAnimations, name, fullName, goScene, setTab, get data() { return state.data; }, get selected() { return [...state.selected]; }, get scene() { return state.scene; }, get tab() { return state.tab; },
    stopBriefing, pauseBriefing: () => pauseBriefing(true), get briefingActive() { return !$('[data-briefing]').hidden; },
    select: (ids, o) => select(ids, o), catColor: c => catColor(catKey(c)), filteredSupplierIds, toast: t => toast(esc(t)), exportTable, exportVisual, navigate,
    openRoundTab: roundId => { const r = state.data?.network.rounds.find(x => x.roundId === roundId); if (r) { offerRoundSel = roundId; openTender(r.tenderId); } }
  });

  // ---------- events ----------
  root.addEventListener('click', e => {
    const t = e.target.closest('button, a[data-scene-link], [data-select]'); if (!t) return;
    const d = t.dataset;
    if (d.insightTab) setInsightTab(d.insightTab);
    else if (t.hasAttribute('data-comparison-open')) openComparisonTender();
    else if (t.hasAttribute('data-offer-table-toggle')) { const table = $('[data-tender-offers-table]'); table.open = !table.open; table.scrollIntoView({ behavior: reduced.matches ? 'auto' : 'smooth', block: 'nearest' }); }
    else if (d.offerPick) { offerBidSel = +d.offerPick; renderOfferDetail(chosenOfferRound(tenderById(state.tenderSel))); $('[data-tender-offer-detail]')?.scrollIntoView({ behavior: reduced.matches ? 'auto' : 'smooth', block: 'nearest' }); }
    else if (t.hasAttribute('data-offer-detail-close')) { offerBidSel = null; $('[data-tender-offer-detail]').hidden = true; }
    else if (d.tabLink) setTab(d.tabLink);
    else if (d.tabJump) { setTab(d.tabJump); if (d.tabJump === 'studio') setTimeout(() => $('[data-ask-input]')?.focus({ preventScroll: true }), 80); }
    else if (d.sceneJump) goScene(d.sceneJump);
    else if (d.clearFilter) clearFilter(d.clearFilter);
    else if (d.snapCat) setFilter('category', state.filters.category === d.snapCat ? '' : d.snapCat);
    else if (d.tenderOpen) openTender(+d.tenderOpen, { tab: false });
    else if (d.openTender) openTender(+d.openTender);
    else if (t.hasAttribute('data-open-silent')) { setTab('suppliers'); setFilter('activity', 'silent'); }
    else if (d.export) exportTender(d.export);
    else if (t.hasAttribute('data-tender-ask')) { const x = tenderById(state.tenderSel); if (x) { setTab('studio'); window.OrangeRadarVoice?.ask?.(`Analyze tender #${x.tenderId} "${clean(x.title)}": its lifecycle, offers and official outcome. Show its offers as a chart.`); } }
    else if (d.action === 'run') load(true);
    else if (d.action === 'brief') startBriefing();
    else if (d.action === 'report') generateReport();
    else if (d.action === 'simulate') simulate();
    else if (d.action === 'replay-network') { goScene('network'); setReplay(0); play(true); }
    else if (d.pick) select([+d.pick], { additive: e.shiftKey, source: 'pick' });
    else if (d.pair) select(d.pair.split(',').map(Number), { source: 'pair' });
    else if (d.unselect) select([+d.unselect], { additive: true, source: 'chips' });
    else if (t.hasAttribute('data-clear-selection')) select([], { source: 'chips' });
    else if (d.scenario) { goScene('reports'); simulate([+d.scenario]); }
    else if (d.tenderNetwork) { setTab('suppliers'); window.OrangeUniverse?.showTender(+d.tenderNetwork); }
    else if (d.ask) window.OrangeRadarVoice?.ask?.(d.ask);
    else if (d.table) { const card = t.closest('.rd-card'); if (tables[d.table]) showTable(card, tables[d.table]()); }
    else if (t.hasAttribute('data-close-table')) t.closest('.rd-table-wrap').remove();
    else if (d.select) select([+d.select], { additive: e.shiftKey, source: 'table' });
    else if (d.brief) ({ prev: () => { brief.index = Math.max(0, brief.index - 1); brief.paused = false; playScene(); }, next: () => { brief.index = Math.min(brief.scenes.length - 1, brief.index + 1); brief.paused = false; playScene(); }, toggle: () => pauseBriefing(), replay: () => { brief.index = 0; brief.paused = false; playScene(); }, stop: stopBriefing, ask: () => { pauseBriefing(true); const s = brief.scenes[brief.index]; window.OrangeRadarVoice?.prefill?.(`Tell me more about "${s?.title}".`); } })[d.brief]?.();
    else if (d.timeline === 'play') play();
    else if (d.timeline === 'reset') { play(false); setReplay(1000); }
  });
  $$('[data-insight-tab]').forEach(button => button.addEventListener('keydown', insightKeys));
  function insightKeys(e) {
    const order = ['landscape', 'positions'];
    const i = order.indexOf(e.currentTarget.dataset.insightTab);
    const next = e.key === 'ArrowRight' ? order[(i + 1) % order.length] : e.key === 'ArrowLeft' ? order[(i + order.length - 1) % order.length] : e.key === 'Home' ? order[0] : e.key === 'End' ? order[order.length - 1] : null;
    if (next) { e.preventDefault(); setInsightTab(next, true); }
  }
  $('[data-toast]').addEventListener('click', e => {
    const t = e.target.closest('button'); if (!t) return;
    if (t.dataset.sceneJump) goScene(t.dataset.sceneJump);
    else if (t.dataset.tenderNetwork) { setTab('suppliers'); window.OrangeUniverse?.showTender(+t.dataset.tenderNetwork); }
  });
  root.addEventListener('change', e => {
    const t = e.target;
    if (t.matches('[data-comparison-supplier]')) { comparison[t.dataset.comparisonSupplier] = +t.value || null; loadComparison(); }
    else if (t.matches('[data-comparison-tender]')) { comparison.tender = +t.value || null; renderComparison(); }
    else if (t.matches('[data-offer-round]')) { offerRoundSel = +t.value || null; offerGroupSel = null; offerBidSel = null; renderTender(); }
    else if (t.matches('[data-offer-group]')) { offerGroupSel = +t.value || null; offerBidSel = null; renderTender(); }
    else if (t.matches('#rd-from, #rd-to')) { load(); renderChips(); }
    else if (t.matches('[data-sup-filter]')) setFilter(t.dataset.supFilter, t.value);
    else if (t.matches('[data-tender-pick]')) openTender(+t.value, { tab: false });
    else if (t.matches('[data-global-find]')) globalFind(t.value);
    else if (t.closest('[data-scenario-pick]')) { $('[data-scenario-pick]').dataset.touched = '1'; t.closest('label').classList.toggle('is-on', t.checked); }
  });
  $('[data-timeline-range]').addEventListener('input', e => { play(false); setReplay(+e.target.value); });
  window.addEventListener('orange:themechange', () => { if (!state.data) return; requestAnimationFrame(() => { renderMarket(); renderSnapshot(); renderTender(); renderTimeline(); network.restyle(); }); });
  $('[data-global-find]').addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); globalFind(e.target.value); } });
  // Tabs: arrow keys move between tabs (WAI-ARIA tabs pattern).
  $('[data-tabs]').addEventListener('keydown', e => {
    const i = TABS.indexOf(state.tab); const rtl = document.documentElement.dir === 'rtl'; let n = null;
    if (e.key === 'ArrowRight') n = TABS[(i + (rtl ? -1 : 1) + TABS.length) % TABS.length];
    else if (e.key === 'ArrowLeft') n = TABS[(i + (rtl ? 1 : -1) + TABS.length) % TABS.length];
    else if (e.key === 'Home') n = TABS[0]; else if (e.key === 'End') n = TABS[TABS.length - 1];
    if (n) { e.preventDefault(); setTab(n, { focus: true }); }
  });
  let resizeTimer = 0;
  window.addEventListener('resize', () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(renderTimeline, 120); });
  // Any direct page interaction cancels pending guided animations (never the officer's own actions).
  ['wheel', 'touchstart'].forEach(ev => window.addEventListener(ev, () => { if (timers.size) cancelAnimations(); }, { passive: true }));

  const hash = location.hash.replace(/^#(rd-)?/, '');
  setTab(SCENE_TAB[hash] || 'suppliers', { push: false });
  load();
})();
