// ORANGE RADAR - Sourcing Officer market intelligence workspace (Areas/SourcingOfficer/Views/MarketIntelligence/Index.cshtml).
// Draws server-calculated data only: every metric, finding and edge comes from /SourcingOfficer/MarketIntelligence/Workspace.
// Exposes window.OrangeRadar for the AI analyst (orange-radar-voice.js), which may only call the whitelisted apply() commands.
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
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const fmt = {
    int: n => (n == null ? '—' : Number(n).toLocaleString('en-US')),
    num: (n, d = 2) => (n == null ? '—' : Number(n).toLocaleString('en-US', { maximumFractionDigits: d })),
    pct: (n, d = 0) => (n == null ? '—' : `${(n * 100).toFixed(d)}%`),
    signed: (n, d = 1) => (n == null ? '—' : `${n >= 0 ? '+' : ''}${(n * 100).toFixed(d)}%`),
    money: (c, v) => `${c} ${Number(v).toLocaleString('en-US', { maximumFractionDigits: 0 })}`,
    date: d => (d ? new Date(d).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }) : '—')
  };
  const EDGE_TYPES = {
    participation: { label: 'Submitted offer', slot: 0, style: 'solid' },
    award: { label: 'Official award', slot: 2, style: 'solid' },
    joint: { label: 'Joint participation', slot: 1, style: 'dashed' },
    contact: { label: 'Shared contact', slot: 3, style: 'dotted' },
    price: { label: 'Price pattern', slot: 7, style: 'solid' },
    'award-pattern': { label: 'Award pattern', slot: 8, style: 'solid' },
    document: { label: 'Document match', slot: 5, style: 'dashed' },
    timing: { label: 'Submission timing', slot: 4, style: 'dashed' }
  };

  const state = {
    data: null, mode: 'overview', selected: [], tender: null, finding: null, chartPoint: null, simulation: null,
    edgeTypes: new Set(Object.keys(EDGE_TYPES).filter(t => t !== 'participation')), nodeTypes: new Set(['supplier', 'contact', 'document']),
    expanded: new Set(), replayAt: null, playing: false, focus: false, suppliers: new Map(), categoryColor: new Map(), supplierSlot: new Map(), briefing: null
  };
  const charts = {};
  let cy = null;
  const timers = new Set();
  const later = (fn, ms) => { const t = setTimeout(() => { timers.delete(t); fn(); }, ms); timers.add(t); return t; };
  function cancelAnimations() { timers.forEach(clearTimeout); timers.clear(); cy?.stop(true, false); cy?.elements().removeClass('path pulse'); }

  // ---------- helpers ----------
  const css = name => getComputedStyle(root).getPropertyValue(name).trim();
  const palette = () => ({ s: [1, 2, 3, 4, 5, 6, 7, 8].map(i => css(`--s${i}`)), ink: css('--rd-ink'), ink2: css('--rd-ink-2'), muted: css('--rd-muted'), line: css('--rd-line'),
    soft: css('--rd-line-soft'), surface: css('--rd-surface'), surface2: css('--rd-surface-2'), orange: css('--rd-orange'), signal: css('--rd-signal') });
  const status = (text, error = false) => { const el = $('[data-status]'); el.textContent = text || ''; el.classList.toggle('is-error', !!error); };
  const period = () => ({ from: $('#rd-from').value || '', to: $('#rd-to').value || '' });
  const name = id => state.suppliers.get(id)?.name?.replace(/^RADAR DEMO /, '') ?? `Supplier #${id}`;
  const fullName = id => state.suppliers.get(id)?.name ?? `Supplier #${id}`;
  const neutral = () => (document.documentElement.dataset.theme === 'dark' ? '#5b6678' : '#9aa3b2');
  const supplierColor = id => { const slot = state.supplierSlot.get(id); return slot == null ? neutral() : palette().s[slot]; };

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

  // ---------- loading ----------
  async function load(refresh = false) {
    status('Running the analysis on recorded procurement data…');
    $$('.rd-kpi').forEach(k => k.classList.add('is-loading'));
    try {
      const q = new URLSearchParams({ ...period(), refresh: refresh ? 'true' : 'false' });
      ingest(await (await api(`${urls.workspace}?${q}`)).json());
      status(state.data.summary.analyzedRounds === 0 ? 'No closed bidding rounds fall inside this period - nothing to analyse yet. Widen the period.' : '');
    } catch (e) { status(`The analysis could not load: ${e.message}`, true); }
  }

  function ingest(data) {
    state.data = data;
    state.suppliers = new Map(data.suppliers.map(s => [s.supplierId, s]));
    state.selected = state.selected.filter(id => state.suppliers.has(id));
    const cats = [...data.suppliers.filter(s => s.participations > 0).reduce((m, s) => m.set(s.category, (m.get(s.category) || 0) + 1), new Map())].sort((a, b) => b[1] - a[1]);
    state.categoryColor = new Map(cats.slice(0, 7).map(([c], i) => [c, i]));
    const ranked = data.suppliers.filter(s => s.participations > 0).sort((a, b) => b.participations - a.participations || a.supplierId - b.supplierId);
    state.supplierSlot = new Map(ranked.slice(0, 7).map((s, i) => [s.supplierId, i]));
    renderKpis(); renderOverview(); renderPrice(); renderForensic(); buildGraph(); renderTimeline(); renderScenarioPick(); renderSelection();
    window.dispatchEvent(new CustomEvent('radar:data', { detail: data }));
  }

  // ---------- KPIs ----------
  function renderKpis() {
    const s = state.data.summary, d = state.data.definitions;
    const rel = state.data.graph.edges.filter(e => e.kind !== 'fact').length;
    const signals = state.data.findings.filter(f => f.level === 'ReviewSignal').length;
    const primary = s.awardedValue[0];
    const tiles = [
      ['Registered suppliers', s.registeredSuppliers, `${fmt.int(s.eligibleSuppliers)} eligible (approved)`, d.registeredSuppliers],
      ['Suppliers that bid', s.participatingSuppliers, `${fmt.pct(s.registeredSuppliers ? s.participatingSuppliers / s.registeredSuppliers : null)} of registered`, d.participatingSuppliers],
      ['Analysed tenders', s.analyzedTenders, `${fmt.int(s.analyzedRounds)} closed rounds`, d.analyzedTenders],
      ['Submitted offers', s.submittedBids, s.sealedBids ? `${s.sealedBids} sealed offers excluded` : 'no sealed offers pending', d.submittedBids],
      ['Avg. offers / round', s.avgBidsPerRound, `${fmt.num(s.avgBidsPerTender)} per tender`, d.avgBidsPerRound, 2],
      ['Historical awarded value', primary ? fmt.money(primary.currency, primary.value) : 'No awards', primary ? `${primary.count} official awards${s.awardedValue.length > 1 ? ` · +${s.awardedValue.length - 1} currency` : ''}` : 'no official award recorded', d.awardedValue],
      ['Detected relationships', rel, 'context + signal links in the network', 'Supplier links found by the detectors, excluding plain participation.'],
      ['Review signals', signals, signals ? 'patterns worth a human review' : 'none at current thresholds', d.reviewSignals]
    ];
    $('[data-kpis]').innerHTML = tiles.map(([label, value, sub, def, dec], i) =>
      `<div class="rd-kpi${i === 7 && signals ? ' is-signal' : ''}" title="${esc(def)}"><span class="rd-kpi-label">${esc(label)}</span><strong class="${typeof value === 'number' ? '' : 'rd-kpi-text'}" data-count="${typeof value === 'number' ? value : ''}" data-dec="${dec || 0}">${esc(typeof value === 'number' ? fmt.num(value, dec || 0) : value ?? '—')}</strong><small>${esc(sub)}</small></div>`).join('');
    $$('[data-count]').forEach(el => {
      const target = parseFloat(el.dataset.count); const dec = +el.dataset.dec;
      if (Number.isNaN(target) || reduced.matches) return;
      const start = performance.now();
      const step = now => { const t = Math.min(1, (now - start) / 700); el.textContent = fmt.num(target * (1 - Math.pow(1 - t, 3)), dec); if (t < 1) requestAnimationFrame(step); else el.textContent = fmt.num(target, dec); };
      requestAnimationFrame(step);
    });
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
      backgroundColor: 'transparent', textStyle: { color: p.ink2, fontFamily: 'inherit' }, animationDuration: dur(800), animationDurationUpdate: dur(550), animationEasing: 'cubicOut',
      tooltip: { backgroundColor: p.surface, borderColor: p.line, textStyle: { color: p.ink, fontSize: 12 }, confine: true, extraCssText: 'border-radius:10px;box-shadow:0 8px 24px rgba(0,0,0,.18);' },
      grid: { left: 12, right: 18, top: 30, bottom: 12, containLabel: true }
    };
  }
  const axis = (extra = {}) => { const p = palette(); return { axisLine: { lineStyle: { color: p.line } }, axisTick: { show: false }, axisLabel: { color: p.muted, fontSize: 11 }, splitLine: { lineStyle: { color: p.soft } }, nameTextStyle: { color: p.muted, fontSize: 11 }, ...extra }; };
  const empty = (c, text) => c.setOption({ title: { text, left: 'center', top: 'middle', textStyle: { color: palette().muted, fontSize: 13, fontWeight: 500, width: 320, overflow: 'break' } }, xAxis: { show: false }, yAxis: { show: false }, series: [] }, true);

  function renderOverview() { landscape(); competition(); awards(); dna(); groups(); }

  function landscape() {
    const c = chart('landscape'); if (!c) return;
    const p = palette(); const sel = new Set(state.selected);
    const rows = state.data.suppliers.filter(s => s.participations > 0);
    if (!rows.length) return empty(c, 'No supplier has an offer in a closed round of this period.');
    const values = rows.map(r => r.awardedValue[0]?.value || 0); const max = Math.max(1, ...values);
    const byCat = new Map();
    rows.forEach(r => { const k = state.categoryColor.has(r.category) ? r.category : 'Other'; if (!byCat.has(k)) byCat.set(k, []); byCat.get(k).push(r); });
    const series = [...byCat].map(([cat, list]) => ({
      name: cat, type: 'scatter', data: list.map(r => ({
        value: [r.participations, r.winRate == null ? 0 : +(r.winRate * 100).toFixed(1), r.awardedValue[0]?.value || 0], id: r.supplierId, row: r,
        symbol: r.winRate == null ? 'emptyCircle' : 'circle',
        itemStyle: { opacity: sel.size && !sel.has(r.supplierId) ? 0.28 : 0.92, borderColor: sel.has(r.supplierId) ? p.orange : p.surface, borderWidth: sel.has(r.supplierId) ? 3 : 2 }
      })),
      symbolSize: v => 12 + Math.sqrt(v[2] / max) * 34,
      color: cat === 'Other' ? neutral() : p.s[state.categoryColor.get(cat)],
      emphasis: { focus: 'self', scale: 1.15 },
      label: { show: true, position: 'right', formatter: d => (sel.size ? (sel.has(d.data.id) ? name(d.data.id) : '') : d.data.row.wins > 0 || d.data.row.participations >= 3 ? name(d.data.id) : ''), color: p.ink2, fontSize: 11 }
    }));
    c.setOption({
      ...base(), legend: { top: 0, right: 0, textStyle: { color: p.ink2, fontSize: 11 }, itemWidth: 10, itemHeight: 10 }, grid: { left: 12, right: 30, top: 34, bottom: 18, containLabel: true },
      tooltip: { ...base().tooltip, formatter: d => { const r = d.data.row; return `<b>${esc(fullName(r.supplierId))}</b><br>${esc(r.category)}<br>Offers in ${r.participations} closed rounds (invited ${r.invitations})<br>Win rate: ${r.winRate == null ? 'undefined - no awarded tender bid on' : `${fmt.pct(r.winRate, 1)} (${r.wins} of ${r.awardedTendersBid})`}<br>Awarded: ${r.awardedValue.length ? r.awardedValue.map(v => fmt.money(v.currency, v.value)).join(', ') : 'none'}`; } },
      xAxis: { ...axis(), name: 'Rounds with an offer', nameLocation: 'middle', nameGap: 26, minInterval: 1 },
      yAxis: { ...axis(), name: 'Win rate %', max: 100, min: 0 },
      series
    }, true);
    c.off('click'); c.on('click', d => d.data?.id && select([d.data.id], { additive: d.event?.event?.shiftKey, source: 'landscape' }));
  }

  function competition() {
    const c = chart('competition'); if (!c) return;
    const p = palette(); const comp = state.data.competition;
    if (!comp.rounds.length) return empty(c, 'No closed rounds in this period.');
    const label = r => `${r.tender.replace(/^RADAR DEMO /, '').slice(0, 14)}${r.roundNumber > 1 ? ` R${r.roundNumber}` : ''}`;
    c.setOption({
      ...base(), legend: { top: 0, right: 0, textStyle: { color: p.ink2, fontSize: 11 }, itemWidth: 10, itemHeight: 10 },
      tooltip: { ...base().tooltip, trigger: 'axis', axisPointer: { type: 'shadow' }, formatter: ps => { const r = comp.rounds[ps[0].dataIndex]; return `<b>${esc(r.tender)}</b> · round ${r.roundNumber}<br>${fmt.date(r.date)} · ${esc(r.department)}<br>Invited ${r.invited} · submitted ${r.submitted} · compliant ${r.compliant}${r.lowCompetition ? `<br><span style="color:${p.signal}">Below the minimum of ${comp.minimumBids}</span>` : ''}`; } },
      xAxis: { ...axis(), type: 'category', data: comp.rounds.map(label), axisLabel: { color: p.muted, fontSize: 10, rotate: comp.rounds.length > 6 ? 30 : 0, interval: 0 } },
      yAxis: { ...axis(), minInterval: 1, name: 'Suppliers' },
      series: [
        { name: 'Invited', type: 'bar', data: comp.rounds.map(r => r.invited), itemStyle: { color: neutral(), borderRadius: [4, 4, 0, 0] }, barGap: '8%', barMaxWidth: 18 },
        { name: 'Submitted', type: 'bar', color: p.orange, data: comp.rounds.map(r => ({ value: r.submitted, itemStyle: { color: r.lowCompetition ? p.signal : p.orange, borderRadius: [4, 4, 0, 0], opacity: state.tender && state.tender !== r.tenderId ? 0.35 : 1 } })), barMaxWidth: 18,
          markLine: { silent: true, symbol: 'none', lineStyle: { color: p.muted, type: 'dashed' }, label: { color: p.muted, formatter: `min ${comp.minimumBids}` }, data: [{ yAxis: comp.minimumBids }] } }
      ]
    }, true);
    c.off('click'); c.on('click', d => { const r = comp.rounds[d.dataIndex]; selectTender(r.tenderId, r); });
  }

  function awards() {
    const c = chart('awards'); if (!c) return;
    const p = palette(); const a = state.data.awards[0];
    $('[data-award-note]').textContent = a ? `Official award value in ${a.currency}${state.data.awards.length > 1 ? ` (other currencies in the table)` : ''}. Concentration (HHI) ${fmt.num(a.hhi, 3)}. Not payments.` : 'No official award is recorded in this period.';
    if (!a) return empty(c, 'No official awards recorded in this period. Recommendations are not awards.');
    const rows = a.bySupplier.slice().reverse();
    c.setOption({
      ...base(), grid: { left: 8, right: 60, top: 10, bottom: 8, containLabel: true },
      tooltip: { ...base().tooltip, formatter: d => `<b>${esc(d.data.full)}</b><br>${fmt.money(a.currency, d.data.value)} · ${fmt.pct(d.data.share, 1)} of ${a.currency} awards · ${d.data.count} award(s)` },
      xAxis: { ...axis(), type: 'value', axisLabel: { color: p.muted, fontSize: 10, formatter: v => (v >= 1000 ? `${v / 1000}k` : v) } },
      yAxis: { ...axis(), type: 'category', data: rows.map(r => r.name.replace(/^RADAR DEMO /, '')), axisLabel: { color: p.ink2, fontSize: 11 } },
      series: [{ type: 'bar', barMaxWidth: 16, data: rows.map(r => ({ value: r.value, share: r.share, count: r.count, full: r.name, id: r.supplierId, itemStyle: { color: supplierColor(r.supplierId), borderRadius: [0, 4, 4, 0], opacity: state.selected.length && !state.selected.includes(r.supplierId) ? 0.35 : 1 } })),
        label: { show: true, position: 'right', color: p.ink2, fontSize: 11, formatter: d => fmt.pct(d.data.share, 0) } }]
    }, true);
    c.off('click'); c.on('click', d => d.data?.id && select([d.data.id], { additive: d.event?.event?.shiftKey, source: 'awards' }));
  }

  function dnaFor(ids) { return ids.map(id => state.data.dna.find(d => d.supplierId === id)).filter(Boolean); }
  function dnaChart(c, ids, basisEl) {
    const p = palette(); const list = dnaFor(ids);
    if (!list.length) { empty(c, 'Select a supplier to see its DNA.'); if (basisEl) basisEl.innerHTML = ''; return; }
    const dims = list[0].dimensions.filter(dim => list.every(x => x.dimensions.find(y => y.key === dim.key)?.value != null));
    const missing = list[0].dimensions.filter(dim => !dims.includes(dim)).map(d => d.label);
    if (basisEl) basisEl.innerHTML = list.map(x => `<div><b>${esc(name(x.supplierId))}:</b> ${x.dimensions.map(d => `${esc(d.label)} - ${esc(d.basis)}`).join(' · ')}</div>`).join('') + (missing.length ? `<div>Not shown (no data for every selected supplier): ${esc(missing.join(', '))}</div>` : '');
    if (dims.length < 3) return empty(c, `Not enough measured dimensions for a profile (${dims.length} of 6). See the basis below.`);
    c.setOption({
      ...base(), legend: { bottom: 0, textStyle: { color: p.ink2, fontSize: 11 }, itemWidth: 10, itemHeight: 10 },
      radar: { radius: '62%', center: ['50%', '48%'], indicator: dims.map(d => ({ name: d.label, max: 100 })), axisName: { color: p.muted, fontSize: 10.5 }, splitLine: { lineStyle: { color: p.soft } }, splitArea: { show: false }, axisLine: { lineStyle: { color: p.soft } } },
      tooltip: { ...base().tooltip },
      series: [{ type: 'radar', symbolSize: 5, data: list.map(x => ({ name: name(x.supplierId), value: dims.map(d => Math.round(x.dimensions.find(y => y.key === d.key).value * 100)), lineStyle: { color: supplierColor(x.supplierId), width: 2 }, itemStyle: { color: supplierColor(x.supplierId) }, areaStyle: { color: supplierColor(x.supplierId), opacity: 0.12 } })) }]
    }, true);
  }
  function dna() {
    const c = chart('dna'); if (!c) return;
    const top = state.data.suppliers.filter(s => s.participations > 0).sort((a, b) => b.participations - a.participations)[0];
    dnaChart(c, state.selected.length ? state.selected.slice(0, 3) : top ? [top.supplierId] : [], $('[data-dna-basis]'));
  }

  function groups() {
    const c = chart('groups'); if (!c) return;
    const p = palette(); const g = state.data.competition.byDepartment;
    if (!g.length) empty(c, 'No closed rounds.');
    else c.setOption({
      ...base(), grid: { left: 8, right: 40, top: 6, bottom: 6, containLabel: true },
      tooltip: { ...base().tooltip, formatter: d => `${esc(d.name)}<br>${fmt.num(d.value)} offers per round over ${g[d.dataIndex].rounds} rounds` },
      xAxis: { ...axis(), type: 'value', minInterval: 1 }, yAxis: { ...axis(), type: 'category', data: g.map(x => x.group.replace(/^RADAR DEMO - /, '')) },
      series: [{ type: 'bar', data: g.map(x => ({ value: x.avgBids, itemStyle: { color: x.avgBids < state.data.competition.minimumBids ? p.signal : p.s[0], borderRadius: [0, 4, 4, 0] } })), barMaxWidth: 14, label: { show: true, position: 'right', color: p.ink2, fontSize: 11 } }]
    }, true);
    $('[data-pairs]').innerHTML = state.data.competition.recurringCompetitors.length
      ? `<span class="rd-muted">Most frequent competitor pairs (shared closed rounds):</span>` + state.data.competition.recurringCompetitors.slice(0, 4).map(x => `<button type="button" data-pair="${x.supplierA},${x.supplierB}"><span>${esc(name(x.supplierA))} · ${esc(name(x.supplierB))}</span><b>${x.rounds}</b></button>`).join('')
      : '<span class="rd-muted">No supplier pair met in a closed round.</span>';
  }

  function renderPrice() { positions(); itemSelect(); items(); }
  function positions() {
    const c = chart('positions'); if (!c) return;
    const p = palette(); const pos = state.data.positions;
    if (!pos.length) return empty(c, 'No round had two comparable offers in the same currency, so relative prices cannot be calculated yet.');
    const counts = pos.reduce((m, x) => m.set(x.supplierId, (m.get(x.supplierId) || 0) + 1), new Map());
    const ids = state.selected.filter(id => counts.has(id)).slice(0, 5);
    const show = ids.length ? ids : [...counts].sort((a, b) => b[1] - a[1]).slice(0, 4).map(x => x[0]);
    c.setOption({
      ...base(), legend: { top: 0, right: 0, textStyle: { color: p.ink2, fontSize: 11 } }, grid: { left: 12, right: 24, top: 34, bottom: 12, containLabel: true },
      tooltip: { ...base().tooltip, trigger: 'item', formatter: d => { const x = d.data.row; return `<b>${esc(fullName(x.supplierId))}</b><br>${fmt.date(x.date)} · round #${x.roundId}<br>Offer ${fmt.money(x.currency, x.total)} vs median ${fmt.money(x.currency, x.median)}<br>Position ${fmt.signed(x.relative)} (${x.comparable} comparable offers)`; } },
      xAxis: { ...axis(), type: 'time' }, yAxis: { ...axis(), type: 'value', name: 'vs round median', axisLabel: { color: p.muted, formatter: v => `${v > 0 ? '+' : ''}${v}%` } },
      series: show.map(id => ({
        name: name(id), type: 'line', symbol: 'circle', symbolSize: 9, lineStyle: { width: 2, color: supplierColor(id), type: state.supplierSlot.has(id) ? 'solid' : 'dashed' }, itemStyle: { color: supplierColor(id), borderColor: p.surface, borderWidth: 2 },
        data: pos.filter(x => x.supplierId === id).sort((a, b) => new Date(a.date) - new Date(b.date)).map(x => ({ value: [x.date, +(x.relative * 100).toFixed(2)], row: x })),
        markLine: id === show[0] ? { silent: true, symbol: 'none', lineStyle: { color: p.muted, type: 'dashed' }, label: { formatter: 'median', color: p.muted }, data: [{ yAxis: 0 }] } : undefined
      }))
    }, true);
    c.off('click'); c.on('click', d => d.data?.row && (state.chartPoint = `${fullName(d.data.row.supplierId)} in round #${d.data.row.roundId}: ${fmt.signed(d.data.row.relative)} vs median`, select([d.data.row.supplierId], { additive: d.event?.event?.shiftKey, source: 'positions' })));
  }
  function itemSelect() {
    const sel = $('[data-item-select]'); const items = state.data.items;
    const current = sel.value;
    sel.innerHTML = items.length ? items.map((x, i) => `<option value="${i}">${esc(x.tender.replace(/^RADAR DEMO /, ''))} · ${esc(x.item)} (${esc(x.unit)})</option>`).join('') : '<option>No comparable items</option>';
    if (current && current < items.length) sel.value = current;
    else { const widest = items.reduce((b, x, i) => (x.spread > (items[b]?.spread ?? -1) ? i : b), 0); sel.value = String(widest); }
  }
  function items() {
    const c = chart('items'); if (!c) return;
    const p = palette(); const x = state.data.items[+$('[data-item-select]').value];
    if (!x) return empty(c, 'No tender item was priced by two suppliers in the same round and currency.');
    const rows = x.prices.slice().sort((a, b) => b.unitPrice - a.unitPrice);
    c.setOption({
      ...base(), grid: { left: 8, right: 70, top: 26, bottom: 10, containLabel: true },
      title: { text: `Spread ${fmt.pct(x.spread, 1)} · median ${x.currency} ${fmt.num(x.median)} · qty ${fmt.num(x.quantity)} ${x.unit}`, left: 0, top: 0, textStyle: { color: p.muted, fontSize: 11.5, fontWeight: 500 } },
      tooltip: { ...base().tooltip, formatter: d => `<b>${esc(d.data.full)}</b><br>${x.currency} ${fmt.num(d.value)} per ${esc(x.unit)}<br>${fmt.signed(d.value / x.median - 1)} vs median` },
      xAxis: { ...axis(), type: 'value', min: v => Math.floor(v.min * 0.97) }, yAxis: { ...axis(), type: 'category', data: rows.map(r => name(r.supplierId)) },
      series: [{ type: 'bar', barMaxWidth: 16, data: rows.map(r => ({ value: r.unitPrice, full: r.supplier, id: r.supplierId, itemStyle: { color: supplierColor(r.supplierId), borderRadius: [0, 4, 4, 0], opacity: state.selected.length && !state.selected.includes(r.supplierId) ? 0.35 : 1 } })),
        label: { show: true, position: 'right', color: p.ink2, fontSize: 11, formatter: d => fmt.num(d.value) },
        markLine: { silent: true, symbol: 'none', lineStyle: { color: p.orange, width: 2 }, label: { formatter: 'median', color: p.orange }, data: [{ xAxis: x.median }] } }]
    }, true);
    c.off('click'); c.on('click', d => d.data?.id && select([d.data.id], { additive: d.event?.event?.shiftKey, source: 'items' }));
  }

  // ---------- tables (accessible alternative to every chart) ----------
  function showTable(card, html) {
    card.querySelector('.rd-table-wrap')?.remove();
    const wrap = document.createElement('div'); wrap.className = 'rd-table-wrap'; wrap.tabIndex = -1;
    wrap.innerHTML = `<div style="display:flex;justify-content:flex-end;padding:6px"><button type="button" class="rd-link" data-close-table>Close table</button></div>${html}`;
    card.append(wrap); wrap.focus();
  }
  const tables = {
    landscape: () => `<table class="rd-table"><thead><tr><th>Supplier</th><th>Category</th><th class="num">Invited</th><th class="num">Rounds with offer</th><th class="num">Wins / awarded bid</th><th class="num">Win rate</th><th>Awarded value</th><th class="num">Rel. price</th></tr></thead><tbody>${state.data.suppliers.slice().sort((a, b) => b.participations - a.participations).map(s => `<tr data-select="${s.supplierId}"><td>${esc(s.name)}</td><td>${esc(s.category)}</td><td class="num">${s.invitations}</td><td class="num">${s.participations}</td><td class="num">${s.wins} / ${s.awardedTendersBid}</td><td class="num">${fmt.pct(s.winRate, 1)}</td><td>${s.awardedValue.map(v => fmt.money(v.currency, v.value)).join(', ') || '—'}</td><td class="num">${fmt.signed(s.relativePrice)}</td></tr>`).join('')}</tbody></table>`,
    competition: () => `<table class="rd-table"><thead><tr><th>Tender</th><th>Round</th><th>Date</th><th>Department</th><th class="num">Invited</th><th class="num">Submitted</th><th class="num">Compliant</th></tr></thead><tbody>${state.data.competition.rounds.map(r => `<tr><td>${esc(r.tender)}</td><td>${r.roundNumber}</td><td>${fmt.date(r.date)}</td><td>${esc(r.department)}</td><td class="num">${r.invited}</td><td class="num">${r.submitted}</td><td class="num">${r.compliant}</td></tr>`).join('')}</tbody></table>`,
    awards: () => state.data.awards.length ? state.data.awards.map(a => `<table class="rd-table"><thead><tr><th>Supplier (${esc(a.currency)})</th><th class="num">Value</th><th class="num">Share</th><th class="num">Awards</th></tr></thead><tbody>${a.bySupplier.map(r => `<tr data-select="${r.supplierId}"><td>${esc(r.name)}</td><td class="num">${fmt.num(r.value)}</td><td class="num">${fmt.pct(r.share, 1)}</td><td class="num">${r.count}</td></tr>`).join('')}${a.byDepartment.map(r => `<tr><td>Department: ${esc(r.name)}</td><td class="num">${fmt.num(r.value)}</td><td class="num">${fmt.pct(r.share, 1)}</td><td class="num">${r.count}</td></tr>`).join('')}</tbody></table>`).join('') : '<p class="rd-muted" style="padding:12px">No official awards.</p>',
    positions: () => `<table class="rd-table"><thead><tr><th>Supplier</th><th>Date</th><th>Round</th><th class="num">Offer</th><th class="num">Median</th><th class="num">Position</th></tr></thead><tbody>${state.data.positions.map(x => `<tr data-select="${x.supplierId}"><td>${esc(fullName(x.supplierId))}</td><td>${fmt.date(x.date)}</td><td>#${x.roundId}</td><td class="num">${fmt.money(x.currency, x.total)}</td><td class="num">${fmt.money(x.currency, x.median)}</td><td class="num">${fmt.signed(x.relative)}</td></tr>`).join('')}</tbody></table>`
  };

  // ---------- forensic ----------
  function renderForensic() {
    const d = state.data;
    const badge = st => ({ ok: '<span class="rd-badge is-ok">✓ analysed</span>', insufficient: '<span class="rd-badge is-muted">◌ insufficient data</span>', notRun: '<span class="rd-badge is-muted">○ not run</span>', unavailable: '<span class="rd-badge is-muted">⚠ unavailable</span>' }[st] || st);
    $('[data-detectors]').innerHTML = d.detectors.map(x => `<div class="rd-detector"><strong>${esc(x.label)}</strong>${badge(x.state)}<span>${x.findings} finding(s). ${esc(x.note)}</span><span title="Threshold">${esc(x.threshold)}</span></div>`).join('');
    const dnaState = d.documentDna;
    $('[data-dna-state]').textContent = dnaState.state === 'ok' ? `Semantic comparison done: ${dnaState.readable} readable of ${dnaState.bidDocuments} bid documents, ${dnaState.matches} pair(s) above the threshold.${dnaState.message ? ' ' + dnaState.message : ''}`
      : dnaState.state === 'unavailable' ? `Embedding model unavailable: ${dnaState.message}` : `SHA-256 exact matching ran on ${d.findings.length >= 0 ? 'all accessible files' : ''}. Semantic similarity (local qwen3-embedding) has not run yet.`;
    const list = $('[data-findings]');
    if (!d.findings.length) { list.innerHTML = '<div class="rd-empty" style="position:static"><div><strong>No relationships or review signals at the current thresholds</strong>Check the detector cards: some may lack enough data, which is not the same as a clean result.</div></div>'; return; }
    list.innerHTML = d.findings.map(f => `<button type="button" class="rd-finding${state.finding === f.id ? ' is-selected' : ''}" data-finding="${f.id}">
      <header><h3>${esc(f.title)}</h3><span class="rd-badge ${f.level === 'ReviewSignal' ? 'is-signal">Review signal' : 'is-context">Context'}</span></header>
      <span>${esc(f.explanation)}</span>
      <footer><code>${f.id}</code><span class="rd-badge">sample ${f.sampleSize}</span>${f.supplierIds.slice(0, 3).map(id => `<span class="rd-badge" style="border-color:${supplierColor(id)}">${esc(name(id))}</span>`).join('')}</footer></button>`).join('');
  }

  async function runDocumentDna() {
    const button = $('[data-action="dna"]'); button.disabled = true;
    $('[data-dna-state]').textContent = 'Extracting text and computing local embeddings for readable bid documents (cached afterwards)…';
    try {
      const result = await (await post(urls.dna, period())).json();
      ingest(result.workspace);
      if (result.state.state !== 'ok') status(result.state.message || 'Document DNA could not run.', true);
    } catch (e) { status(`Document DNA failed: ${e.message}`, true); }
    finally { button.disabled = false; }
  }

  // ---------- network ----------
  function graphStyle() {
    const p = palette(); const dark = document.documentElement.dataset.theme === 'dark';
    const color = t => (EDGE_TYPES[t].slot === 0 ? neutral() : p.s[EDGE_TYPES[t].slot - 1]);
    const styles = [
      { selector: 'node', style: { label: 'data(short)', 'font-size': 10, color: p.ink2, 'text-valign': 'bottom', 'text-margin-y': 5, 'text-outline-width': 3, 'text-outline-color': dark ? '#0d1626' : '#f4f6fa', 'min-zoomed-font-size': 7, 'transition-property': 'opacity, background-color, border-width, border-color, width, height', 'transition-duration': dur(300) } },
      { selector: 'node[type="supplier"]', style: { shape: 'ellipse', width: 'mapData(weight, 0, 8, 26, 62)', height: 'mapData(weight, 0, 8, 26, 62)', 'background-color': p.orange, 'background-fill': 'radial-gradient', 'background-gradient-stop-colors': `#ffc48a ${p.orange} #b54700`, 'background-gradient-stop-positions': '0% 55% 100%', 'border-width': 3, 'border-color': dark ? 'rgba(255,170,90,.35)' : 'rgba(255,121,0,.25)', 'font-size': 11.5, 'font-weight': 600, color: p.ink } },
      { selector: 'node[type="supplier"][wins > 0]', style: { 'border-width': 5, 'border-color': dark ? 'rgba(255,200,140,.75)' : 'rgba(210,90,0,.55)' } },
      { selector: 'node[type="tender"]', style: { shape: 'round-rectangle', width: 24, height: 24, 'background-color': p.s[0], 'border-width': 2, 'border-color': dark ? 'rgba(160,200,255,.35)' : '#ffffff', 'font-size': 9.5 } },
      { selector: 'node[type="contact"]', style: { shape: 'diamond', width: 20, height: 20, 'background-color': p.s[2], 'font-size': 9 } },
      { selector: 'node[type="document"]', style: { shape: 'tag', width: 20, height: 20, 'background-color': p.s[4], 'font-size': 9 } },
      { selector: 'edge', style: { 'curve-style': 'bezier', width: 1.4, opacity: 0.85, 'line-color': neutral(), 'target-arrow-shape': 'none', 'transition-property': 'opacity, line-color, width', 'transition-duration': dur(300) } },
      ...Object.entries(EDGE_TYPES).map(([t, v]) => ({ selector: `edge[type="${t}"]`, style: { 'line-color': color(t), 'line-style': v.style, 'line-dash-pattern': v.style === 'dotted' ? [2, 5] : [7, 5] } })),
      { selector: 'edge[type="participation"]', style: { width: 1, opacity: 0.45 } },
      { selector: 'edge[type="award"]', style: { width: 2.6, 'target-arrow-shape': 'triangle', 'target-arrow-color': p.s[1], 'arrow-scale': 0.9 } },
      { selector: 'edge[kind="context"]', style: { width: 'mapData(weight, 1, 6, 2, 4)' } },
      { selector: 'edge[kind="signal"]', style: { width: 3.4, 'line-style': 'dashed', 'line-dash-pattern': [10, 6], 'underlay-color': color('price'), 'underlay-opacity': 0, 'underlay-padding': 3 } },
      ...Object.keys(EDGE_TYPES).map(t => ({ selector: `edge[kind="signal"][type="${t}"]`, style: { 'underlay-color': color(t) } })),
      { selector: '.hidden', style: { display: 'none' } },
      { selector: '.future', style: { display: 'none' } },
      { selector: '.faded', style: { opacity: 0.1, 'text-opacity': 0.15 } },
      { selector: 'node.selected-node', style: { 'border-width': 6, 'border-color': dark ? '#ffffff' : '#101828', 'z-index': 20 } },
      { selector: 'edge.hl', style: { opacity: 1, 'underlay-opacity': 0.25, 'z-index': 15 } },
      { selector: 'edge.path', style: { 'line-color': p.orange, 'line-style': 'solid', width: 6, opacity: 1, 'underlay-opacity': 0.35, 'underlay-color': p.orange, 'z-index': 30 } },
      { selector: 'node.path', style: { 'border-width': 7, 'border-color': p.orange } },
      { selector: 'edge.pulse', style: { 'underlay-opacity': 0.4, 'underlay-color': p.orange, 'underlay-padding': 6 } },
      { selector: 'node.pulse', style: { 'border-width': 8, 'border-color': p.orange } },
      { selector: ':selected', style: { 'overlay-opacity': 0 } }
    ];
    return styles;
  }

  function buildGraph() {
    const g = state.data.graph; const el = $('[data-cy]');
    renderEdgeFilters(); renderLegend();
    const emptyEl = $('[data-network-empty]');
    if (!g.nodes.length) { emptyEl.hidden = false; emptyEl.innerHTML = '<div><strong>No supplier relationships to draw</strong>No supplier submitted an offer in a closed round of this period.</div>'; cy?.destroy(); cy = null; return; }
    emptyEl.hidden = true;
    if (!window.cytoscape) return;
    const short = n => (n.type === 'supplier' ? n.label.replace(/^RADAR DEMO /, '') : n.type === 'tender' ? n.label.replace(/^RADAR DEMO /, '').slice(0, 22) : n.label.slice(0, 22));
    const elements = [
      ...g.nodes.map(n => ({ group: 'nodes', data: { ...n, id: n.id, short: short(n), avail: n.availableAt ? Date.parse(n.availableAt) : 0 } })),
      ...g.edges.map(e => ({ group: 'edges', data: { ...e, id: e.id, weight: Math.max(1, e.weight), avail: e.availableAt ? Date.parse(e.availableAt) : 0 } }))
    ];
    cy?.destroy();
    cy = cytoscape({ container: el, elements, style: graphStyle(), minZoom: 0.2, maxZoom: 3.5, boxSelectionEnabled: false, selectionType: 'additive' });
    cy.on('tap', 'node', e => {
      const n = e.target.data();
      if (n.type === 'supplier') select([n.entityId], { additive: e.originalEvent?.shiftKey || e.originalEvent?.ctrlKey || e.originalEvent?.metaKey, source: 'graph' });
      else if (n.type === 'tender') selectTender(n.entityId);
      else { const f = state.data.findings.find(x => n.id.endsWith(x.id.slice(6))); if (f) openFinding(f.id); }
    });
    cy.on('dbltap', 'node[type="supplier"]', e => { toggleExpand(e.target.data('entityId')); });
    cy.on('tap', 'edge', e => openEdge(e.target.data()));
    cy.on('tap', e => { if (e.target === cy) { select([], { source: 'graph' }); } });
    cy.on('mouseover', 'node, edge', e => { el.style.cursor = 'pointer'; e.target.addClass('hl'); });
    cy.on('mouseout', 'node, edge', e => { el.style.cursor = ''; e.target.removeClass('hl'); });
    applyVisibility();
    // The reveal runs when the network is on screen; a hidden canvas has no reliable size to lay out and fit against.
    state.revealed = false;
    if (state.mode === 'network') reveal();
    startSignalAnimation();
  }

  function visibleElements() { return cy ? cy.elements().not('.hidden').not('.future') : null; }

  function applyVisibility() {
    if (!cy) return;
    cy.batch(() => {
      cy.nodes().forEach(n => {
        const t = n.data('type');
        const expandedTender = t === 'tender' && n.connectedEdges('[type="participation"],[type="award"]').connectedNodes('[type="supplier"]').some(s => state.expanded.has(s.data('entityId')));
        n.toggleClass('hidden', !(state.nodeTypes.has(t) || expandedTender));
      });
      cy.edges().forEach(e => {
        const t = e.data('type');
        const toTender = e.target().data('type') === 'tender' || e.source().data('type') === 'tender';
        const typeOn = state.edgeTypes.has(t) || (toTender && (t === 'participation' || t === 'award'));
        e.toggleClass('hidden', !typeOn || e.source().hasClass('hidden') || e.target().hasClass('hidden'));
      });
      // Suppliers with no visible link stay visible (they are part of the market) unless the replay hides them.
      const at = state.replayAt;
      cy.elements().forEach(x => x.toggleClass('future', at != null && x.data('avail') > at));
    });
  }

  function layoutOptions(extra = {}) {
    return { name: 'cose', animate: false, randomize: true, padding: 60, nodeRepulsion: () => 9000, idealEdgeLength: e => (e.data('type') === 'participation' ? 90 : 130 - Math.min(40, e.data('weight') * 6)), edgeElasticity: () => 80, gravity: 0.35, numIter: 1200, nodeDimensionsIncludeLabels: true, ...extra };
  }

  // Network Reveal: compute the readable layout first, then fly every node from the centre to its place.
  function reveal() {
    if (!cy) return;
    state.revealed = true; state.revealAt = performance.now();
    cy.resize();
    const eles = visibleElements();
    eles.layout(layoutOptions()).run();
    cy.fit(eles, 60);
    if (reduced.matches) return;
    const center = { x: cy.width() / 2, y: cy.height() / 2 };
    const model = { x: (center.x - cy.pan().x) / cy.zoom(), y: (center.y - cy.pan().y) / cy.zoom() };
    const nodes = eles.nodes().sort((a, b) => b.data('weight') - a.data('weight'));
    const targets = new Map(nodes.map(n => [n.id(), { ...n.position() }]));
    cy.batch(() => { nodes.positions(() => ({ ...model })); nodes.style('opacity', 0); eles.edges().style('opacity', 0); });
    nodes.forEach((n, i) => later(() => n.animate({ position: targets.get(n.id()), style: { opacity: 1 } }, { duration: 750, easing: 'ease-out-cubic' }), 120 + i * 45));
    later(() => eles.edges().forEach((e, i) => later(() => e.animate({ style: { opacity: e.data('type') === 'participation' ? 0.45 : 0.85 } }, { duration: 400 }), i * 12)), 300 + nodes.length * 45);
    later(() => { cy?.elements().removeStyle('opacity'); }, 1500 + nodes.length * 45 + eles.edges().length * 12);
  }

  function relayout(eles, options = {}) {
    if (!cy) return;
    const target = eles || visibleElements();
    target.layout(layoutOptions({ animate: !reduced.matches, animationDuration: 900, animationEasing: 'ease-in-out-cubic', randomize: false, fit: true, ...options })).run();
  }

  // Group Formation: re-run the force layout using only the chosen relationship types as springs.
  function groupByRelationship() {
    const types = [...state.edgeTypes].filter(t => t !== 'participation');
    const eles = visibleElements();
    const springs = eles.edges().filter(e => types.includes(e.data('type')));
    relayout(eles.nodes().union(springs), { idealEdgeLength: () => 70, nodeRepulsion: () => 14000, gravity: 0.6 });
    status(springs.length ? `Suppliers regrouped by: ${types.map(t => EDGE_TYPES[t].label).join(', ')}.` : 'No visible relationships of the chosen types to group by.');
    later(() => status(''), 4000);
  }

  function toggleExpand(supplierId) {
    if (state.expanded.has(supplierId)) state.expanded.delete(supplierId); else state.expanded.add(supplierId);
    const before = visibleElements().nodes();
    applyVisibility();
    const added = visibleElements().nodes().difference(before);
    const anchor = cy.getElementById(`s-${supplierId}`);
    added.positions(() => ({ x: anchor.position('x') + (Math.random() - 0.5) * 40, y: anchor.position('y') + (Math.random() - 0.5) * 40 }));
    relayout();
  }

  let antsFrame = 0;
  function startSignalAnimation() {
    cancelAnimationFrame(antsFrame);
    if (reduced.matches || !cy) return;
    let offset = 0; let last = 0;
    const step = now => {
      if (!cy) return;
      if (now - last > 50 && state.mode === 'network') { offset = (offset + 1) % 32; cy.edges('[kind="signal"]').not('.hidden').style('line-dash-offset', -offset); last = now; }
      antsFrame = requestAnimationFrame(step);
    };
    antsFrame = requestAnimationFrame(step);
  }

  // Dynamic Focus: zoom to the selection, highlight its connections, fade the rest.
  function focusGraph(ids, animate = true) {
    if (!cy) return;
    if (!state.revealed) animate = false;
    cy.batch(() => {
      cy.elements().removeClass('faded selected-node');
      if (!ids.length) return;
      const nodes = cy.collection(ids.map(id => cy.getElementById(`s-${id}`)).filter(n => n.nonempty()));
      nodes.addClass('selected-node');
      if (state.focus || ids.length) {
        const keep = nodes.closedNeighborhood().not('.hidden');
        cy.elements().not(keep).not('.hidden').addClass('faded');
      }
    });
    const target = ids.length ? cy.collection(ids.map(id => cy.getElementById(`s-${id}`))).closedNeighborhood().not('.hidden').not('.future') : visibleElements();
    if (target.nonempty()) cy.animate({ fit: { eles: target, padding: ids.length ? 90 : 50 } }, { duration: animate ? dur(700) : 0, easing: 'ease-in-out-cubic' });
  }

  // Evidence Path Animation: light up the path element by element, then open the evidence.
  function illuminate(collection, done) {
    if (!cy || collection.empty()) return done?.();
    const settling = 1700 - (performance.now() - (state.revealAt || 0));
    if (settling > 0 && !reduced.matches) { later(() => illuminate(collection, done), settling); return; }
    cancelAnimations();
    cy.elements().addClass('faded');
    const ordered = collection.toArray();
    cy.animate({ fit: { eles: collection, padding: 110 } }, { duration: dur(600) });
    ordered.forEach((el, i) => later(() => { el.removeClass('faded').addClass('path'); el.connectedNodes?.().removeClass('faded'); }, dur(500) + i * dur(280)));
    later(() => done?.(), dur(600) + ordered.length * dur(280));
  }

  function showPath(fromId, toId) {
    if (!cy) return;
    setMode('network');
    const a = cy.getElementById(`s-${fromId}`), b = cy.getElementById(`s-${toId}`);
    const usable = cy.elements().not('.future').filter(x => x.isNode() || state.edgeTypes.has(x.data('type')) || x.data('type') === 'participation');
    if (a.empty() || b.empty()) { status('One of these suppliers is not in the network for this period.', true); return; }
    // Hidden tender nodes may lie on the path; reveal what the path needs.
    const d = usable.dijkstra({ root: a, weight: e => (e.data('type') === 'participation' ? 2 : 1) });
    const path = d.pathTo(b);
    if (!path || path.length < 2 || d.distanceTo(b) === Infinity) { status(`No relationship path connects ${name(fromId)} and ${name(toId)} with the current filters.`); return; }
    path.removeClass('hidden');
    status(`Shortest relationship path: ${path.nodes().map(n => n.data('short')).join(' → ')}`);
    illuminate(path, () => { const e = path.edges().filter(x => x.data('findingIds')?.length)[0]; if (e) openFinding(e.data('findingIds')[0], { animate: false }); });
  }

  function renderEdgeFilters() {
    const present = new Set(state.data.graph.edges.map(e => e.type));
    const p = palette();
    $('[data-edge-filters]').innerHTML = '<legend>Relationships</legend>' + Object.entries(EDGE_TYPES).map(([t, v]) =>
      `<label${present.has(t) ? '' : ' style="opacity:.5"'}><input type="checkbox" data-edge-type="${t}" ${state.edgeTypes.has(t) ? 'checked' : ''} /> <i class="rd-edge-sw ${v.style === 'dashed' ? 'is-dashed' : v.style === 'dotted' ? 'is-dotted' : ''}" style="color:${v.slot === 0 ? neutral() : p.s[v.slot - 1]}"></i>${v.label}${present.has(t) ? '' : ' (none)'}</label>`).join('');
  }
  function renderLegend() {
    const p = palette();
    const types = [...new Set(state.data.graph.edges.map(e => e.type))];
    $('[data-legend]').innerHTML = `<span class="rd-legend-kind">Thin = recorded fact · medium = context · thick, moving dashes = review signal (not proof)</span>` +
      `<span><i class="rd-sw rd-sw-supplier"></i>Supplier (size = rounds)</span><span><i class="rd-sw rd-sw-tender"></i>Tender</span><span><i class="rd-sw rd-sw-contact"></i>Shared contact</span><span><i class="rd-sw rd-sw-document"></i>Document</span>` +
      types.map(t => `<span><i class="rd-edge-sw ${EDGE_TYPES[t].style === 'dashed' ? 'is-dashed' : EDGE_TYPES[t].style === 'dotted' ? 'is-dotted' : ''}" style="color:${EDGE_TYPES[t].slot === 0 ? neutral() : p.s[EDGE_TYPES[t].slot - 1]}"></i>${EDGE_TYPES[t].label}</span>`).join('');
  }
  function graphTable() {
    const box = $('[data-graph-table]');
    const edges = state.data.graph.edges.filter(e => e.type !== 'participation' || state.edgeTypes.has('participation'));
    const label = id => cy?.getElementById(id).data('short') ?? id;
    box.innerHTML = `<table class="rd-table"><caption class="ws-sr-only">Relationships in the supplier network</caption><thead><tr><th>Relationship</th><th>From</th><th>To</th><th>Kind</th><th>Detail</th><th>Evidence</th></tr></thead><tbody>${edges.map(e => `<tr data-edge="${esc(e.id)}" tabindex="0"><td>${esc(EDGE_TYPES[e.type]?.label || e.type)}</td><td>${esc(label(e.source))}</td><td>${esc(label(e.target))}</td><td>${e.kind === 'signal' ? '<span class="rd-badge is-signal">signal</span>' : e.kind === 'context' ? '<span class="rd-badge is-context">context</span>' : 'fact'}</td><td>${esc(e.label)}</td><td>${e.findingIds.map(f => `<code>${f}</code>`).join(' ')}</td></tr>`).join('')}</tbody></table>`;
  }

  // ---------- selection & cross-highlighting ----------
  function select(ids, { additive = false, source = '' } = {}) {
    let next = additive ? [...state.selected] : [];
    ids.forEach(id => { const i = next.indexOf(id); if (additive && i >= 0) next.splice(i, 1); else if (i < 0) next.push(id); });
    next = next.filter(id => state.suppliers.has(id)).slice(-4);
    state.selected = next;
    if (source !== 'finding') state.finding = null;
    renderSelection(source);
  }
  function renderSelection(source = '') {
    if (!state.data) return;
    landscape(); awards(); dna(); positions(); items(); competition();
    if (source !== 'graph-silent') focusGraph(state.selected, true);
    $$('.rd-finding').forEach(b => b.classList.toggle('is-selected', b.dataset.finding === state.finding));
    renderDetails();
    renderScenarioPick();
    window.dispatchEvent(new CustomEvent('radar:selection', { detail: context() }));
  }
  function selectTender(tenderId, round) {
    state.tender = tenderId; competition();
    const rounds = state.data.competition.rounds.filter(r => r.tenderId === tenderId);
    const bidders = [...new Set(state.data.positions.filter(p => rounds.some(r => r.roundId === p.roundId)).map(p => p.supplierId))];
    const r = round || rounds[0];
    $('[data-details]').innerHTML = r ? `<div class="rd-details"><h3>${esc(r.tender)}</h3><span class="rd-muted">${esc(r.department)} · round ${r.roundNumber} · ${fmt.date(r.date)}</span>
      <dl class="rd-kv"><dt>Invited suppliers</dt><dd>${r.invited}</dd><dt>Submitted offers</dt><dd>${r.submitted}</dd><dt>Compliant offers</dt><dd>${r.compliant}</dd><dt>Below minimum (${state.data.competition.minimumBids})</dt><dd>${r.lowCompetition ? 'Yes' : 'No'}</dd></dl>
      ${bidders.length ? `<p class="rd-section-title">Priced bidders</p><div class="rd-chip-list">${bidders.map(id => `<button type="button" data-pick="${id}">${esc(name(id))}</button>`).join('')}</div>` : ''}</div>` : '<p class="rd-muted">Tender not in this period.</p>';
    openPanel('details');
    if (cy) { const n = cy.getElementById(`t-${tenderId}`); if (n.nonempty()) { n.removeClass('hidden'); cy.animate({ center: { eles: n }, zoom: 1.4 }, { duration: dur(600) }); } }
  }

  function renderDetails() {
    const box = $('[data-details]');
    if (!state.selected.length) { box.innerHTML = '<p class="rd-muted">Select a supplier in any chart or in the network. Shift+click to compare up to four suppliers.</p>'; return; }
    const findings = id => state.data.findings.filter(f => f.supplierIds.includes(id));
    if (state.selected.length === 1) {
      const s = state.suppliers.get(state.selected[0]);
      const d = state.data.dna.find(x => x.supplierId === s.supplierId);
      box.innerHTML = `<div class="rd-details"><h3>${esc(s.name)}</h3><span class="rd-muted">${esc(s.category)} · status ${esc(s.status)} · supplier #${s.supplierId}</span>
        <dl class="rd-kv">
          <dt title="${esc(state.data.definitions.participationRate)}">Invited (closed rounds)</dt><dd>${s.invitations}</dd>
          <dt>Rounds with an offer</dt><dd>${s.participations}</dd>
          <dt title="${esc(state.data.definitions.participationRate)}">Response rate</dt><dd>${fmt.pct(s.participationRate)}</dd>
          <dt title="${esc(state.data.definitions.winRate)}">Win rate</dt><dd>${s.winRate == null ? 'undefined' : `${fmt.pct(s.winRate, 1)} (${s.wins}/${s.awardedTendersBid})`}</dd>
          <dt>Historical awarded value</dt><dd>${s.awardedValue.map(v => fmt.money(v.currency, v.value)).join('<br>') || '—'}</dd>
          <dt title="${esc(state.data.definitions.relativePrice)}">Avg. price vs median</dt><dd>${s.pricedRounds ? `${fmt.signed(s.relativePrice)} (${s.pricedRounds} rounds)` : '—'}</dd>
          <dt>Compliance</dt><dd>${s.complianceChecked ? `${s.compliant}/${s.complianceChecked}` : '—'}</dd>
          <dt>Activity</dt><dd>${s.firstActivity ? `${fmt.date(s.firstActivity)} – ${fmt.date(s.lastActivity)}` : '—'}</dd>
        </dl>
        <p class="rd-section-title">Supplier DNA</p>
        ${d.dimensions.map(x => `<div style="margin-bottom:7px" title="${esc(x.basis)}"><div style="display:flex;justify-content:space-between;font-size:12px"><span>${esc(x.label)}</span><b>${x.value == null ? 'no data' : fmt.pct(x.value)}</b></div><div class="rd-bar">${x.value == null ? '' : `<i style="width:${Math.round(x.value * 100)}%"></i>`}</div></div>`).join('')}
        <p class="rd-section-title">Relationships & signals (${findings(s.supplierId).length})</p>
        <div class="rd-chip-list">${findings(s.supplierId).map(f => `<button type="button" data-finding="${f.id}">${f.level === 'ReviewSignal' ? '⚑ ' : ''}${esc(f.title)}</button>`).join('') || '<span class="rd-muted">None detected.</span>'}</div>
        <div class="rd-chip-list" style="margin-top:12px"><button type="button" data-ask="Tell me about ${esc(name(s.supplierId))}.">Ask the analyst</button><button type="button" data-scenario="${s.supplierId}">What if unavailable?</button><button type="button" data-expand="${s.supplierId}">Show its tenders</button></div></div>`;
    } else {
      const rows = state.selected.map(id => state.suppliers.get(id));
      box.innerHTML = `<div class="rd-details"><h3>Comparing ${rows.length} suppliers</h3>
        <table class="rd-table" style="margin:8px 0"><thead><tr><th></th>${rows.map(r => `<th><span style="color:${supplierColor(r.supplierId)}">●</span> ${esc(name(r.supplierId))}</th>`).join('')}</tr></thead><tbody>
        ${[['Rounds with offer', r => r.participations], ['Invited', r => r.invitations], ['Response rate', r => fmt.pct(r.participationRate)], ['Wins / awarded bid', r => `${r.wins}/${r.awardedTendersBid}`], ['Win rate', r => fmt.pct(r.winRate, 1)], ['Avg. vs median', r => fmt.signed(r.relativePrice)], ['Compliance', r => (r.complianceChecked ? `${r.compliant}/${r.complianceChecked}` : '—')]]
          .map(([k, f]) => `<tr><td>${k}</td>${rows.map(r => `<td class="num">${f(r)}</td>`).join('')}</tr>`).join('')}</tbody></table>
        <div class="rd-compare-mini" data-chart="compare"></div>
        <p class="rd-section-title">Findings between them</p>
        <div class="rd-chip-list">${state.data.findings.filter(f => state.selected.filter(id => f.supplierIds.includes(id)).length >= 2).map(f => `<button type="button" data-finding="${f.id}">${f.level === 'ReviewSignal' ? '⚑ ' : ''}${esc(f.title)}</button>`).join('') || '<span class="rd-muted">No relationship detected between the selected suppliers.</span>'}</div>
        <div class="rd-chip-list" style="margin-top:12px"><button type="button" data-ask="Compare these suppliers.">Ask the analyst to compare</button><button type="button" data-path="${state.selected[0]},${state.selected[1]}">Show relationship path</button><button type="button" data-ask="Are their documents similar?">Compare documents</button></div></div>`;
      requestAnimationFrame(() => { const c = chart('compare'); if (c) dnaChart(c, state.selected, null); });
    }
  }

  // ---------- evidence ----------
  function openFinding(id, { animate = true } = {}) {
    const f = state.data.findings.find(x => x.id === id);
    if (!f) return;
    state.finding = id;
    state.selected = f.supplierIds.filter(x => state.suppliers.has(x)).slice(0, 4);
    renderSelection('finding');
    const docs = f.records.filter(r => r.type === 'Document');
    $('[data-evidence]').innerHTML = `<div class="rd-evidence"><span class="rd-badge ${f.level === 'ReviewSignal' ? 'is-signal">Review signal - not proof' : 'is-context">Context'}</span>
      <h3 style="margin-top:6px">${esc(f.title)}</h3><code class="rd-muted">${f.id}</code>
      <p>${esc(f.explanation)}</p>
      <dl class="rd-kv">${Object.entries(f.metrics).map(([k, v]) => `<dt>${esc(k.replace(/([A-Z])/g, ' $1').toLowerCase())}</dt><dd>${esc(v)}</dd>`).join('')}<dt>sample size</dt><dd>${f.sampleSize}</dd><dt>period</dt><dd>${esc(f.period)}</dd><dt>pattern visible since</dt><dd>${fmt.date(f.availableAt)}</dd></dl>
      <p class="rd-section-title">Detection method</p><p>${esc(f.method)}</p>
      <div class="rd-note"><b>Alternative explanations & limitations.</b> ${esc(f.limitations)}</div>
      <p class="rd-section-title">Supporting records (${f.records.length})</p>
      <ul class="rd-records">${f.records.map(r => `<li><code>${esc(r.type)} #${r.id}</code> ${esc(r.label)}</li>`).join('')}</ul>
      <div class="rd-chip-list" style="margin-top:10px"><button type="button" data-show-finding="${f.id}">Show in network</button><button type="button" data-ask="Why are these companies connected? Explain finding ${f.id}.">Ask the analyst</button>${docs.length >= 2 ? `<button type="button" data-compare-docs="${docs[0].id},${docs[1].id}">Compare documents side by side</button>` : ''}</div>
      <div data-doc-compare></div></div>`;
    openPanel('evidence');
    if (animate && state.mode === 'network') showFindingInGraph(f.id);
  }
  function showFindingInGraph(id) {
    if (!cy) return;
    const edges = cy.edges().filter(e => (e.data('findingIds') || []).includes(id));
    edges.removeClass('hidden'); edges.connectedNodes().removeClass('hidden');
    illuminate(edges.union(edges.connectedNodes()));
  }
  function openEdge(e) {
    if (e.findingIds?.length) return openFinding(e.findingIds[0]);
    const src = cy.getElementById(e.source).data(), dst = cy.getElementById(e.target).data();
    $('[data-evidence]').innerHTML = `<div class="rd-evidence"><span class="rd-badge">Recorded fact</span><h3 style="margin-top:6px">${esc(EDGE_TYPES[e.type]?.label || e.type)}</h3>
      <p>${esc(src.label)} → ${esc(dst.label)}</p><dl class="rd-kv"><dt>Detail</dt><dd>${esc(e.label)}</dd><dt>Visible since</dt><dd>${fmt.date(e.availableAt)}</dd></dl>
      <p class="rd-muted">${e.type === 'participation' ? 'A submitted offer in a closed round (latest revision). Offer totals include extra costs.' : 'The official award recorded on the tender.'}</p></div>`;
    openPanel('evidence');
  }
  async function compareDocuments(a, b) {
    const box = $('[data-doc-compare]') || $('[data-evidence]');
    box.insertAdjacentHTML('beforeend', '<p class="rd-muted" data-doc-loading>Comparing documents locally…</p>');
    try {
      const r = await (await post(urls.compare, { ...period(), documentA: a, documentB: b })).json();
      const mark = (text, phrases) => { let html = esc(text); phrases.forEach(p => { const re = new RegExp(p.split(' ').map(w => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('[^\\p{L}\\p{N}]+'), 'giu'); html = html.replace(re, m => `<mark>${m}</mark>`); }); return html; };
      box.querySelector('[data-doc-loading]')?.remove();
      box.insertAdjacentHTML('beforeend', `<p class="rd-section-title">Document comparison</p>
        <dl class="rd-kv"><dt>${esc(r.fileA)}</dt><dd>${esc(name(r.supplierA))}</dd><dt>${esc(r.fileB)}</dt><dd>${esc(name(r.supplierB))}</dd>
        <dt>Exact match (SHA-256)</dt><dd>${r.identical ? 'Identical bytes' : 'Different'}</dd><dt>Best non-template similarity</dt><dd>${r.bestSimilarity == null ? '—' : r.bestSimilarity.toFixed(3)}</dd><dt>Template passages</dt><dd>${r.templatePassagesExcluded}</dd></dl>
        ${r.stateA !== 'ok' || r.stateB !== 'ok' ? `<div class="rd-note">Extraction: A ${esc(r.stateA)} · B ${esc(r.stateB)}</div>` : ''}
        ${r.passages.map(p => `<p class="rd-muted" style="margin:8px 0 4px">Passage similarity ${p.similarity ? p.similarity.toFixed(3) : '—'}${p.template ? ' · tender template text (ignored)' : ''}</p><div class="rd-doccompare"><blockquote class="${p.template ? 'is-template' : ''}">${mark(p.textA, p.sharedPhrases)}</blockquote><blockquote class="${p.template ? 'is-template' : ''}">${mark(p.textB, p.sharedPhrases)}</blockquote></div>`).join('')}
        <div class="rd-note">${esc(r.notice)}</div>`);
    } catch (e) { box.querySelector('[data-doc-loading]')?.remove(); box.insertAdjacentHTML('beforeend', `<p class="rd-msg rd-msg-error">${esc(e.message)}</p>`); }
  }

  // ---------- scenario ----------
  function renderScenarioPick(preselect) {
    if (!state.data) return;
    const chosen = new Set(preselect || state.selected);
    $('[data-scenario-pick]').innerHTML = state.data.suppliers.filter(s => s.participations > 0).sort((a, b) => b.participations - a.participations)
      .map(s => `<label><input type="checkbox" value="${s.supplierId}" ${chosen.has(s.supplierId) ? 'checked' : ''} /> ${esc(name(s.supplierId))}</label>`).join('') || '<span class="rd-muted">No participating suppliers.</span>';
  }
  async function simulate(ids) {
    if (ids) renderScenarioPick(ids);
    const chosen = $$('[data-scenario-pick] input:checked').map(i => +i.value);
    openPanel('scenario');
    const out = $('[data-scenario-result]');
    if (!chosen.length) { out.innerHTML = '<p class="rd-muted">Choose at least one supplier to exclude.</p>'; return; }
    out.innerHTML = '<p class="rd-muted">Replaying history without the chosen suppliers…</p>';
    try {
      const s = await (await post(urls.simulate, { ...period(), supplierIds: chosen })).json();
      state.simulation = `Without ${s.excludedNames.join(', ')}: ${s.roundsAffected} rounds affected, rounds below minimum ${s.roundsBelowMinimumBefore} → ${s.roundsBelowMinimumAfter}, ${s.roundsWithoutBidders} without any bidder.`;
      const bar = (label, before, after, f = v => v) => `<div style="margin:8px 0"><div style="display:flex;justify-content:space-between;font-size:12px"><span>${label}</span><b>${f(before)} → ${f(after)}</b></div></div>`;
      out.innerHTML = `<div class="rd-note"><b>Hypothetical scenario.</b> ${esc(s.notice)}</div>
        <dl class="rd-kv"><dt>Excluded</dt><dd>${s.excludedNames.map(esc).join('<br>')}</dd><dt>Rounds affected</dt><dd>${s.roundsAffected}</dd><dt>Share of all offers removed</dt><dd>${fmt.pct(s.bidShareRemoved, 1)}</dd>
        <dt>Rounds left with no bidder</dt><dd>${s.roundsWithoutBidders}</dd><dt>Award value previously won</dt><dd>${s.awardExposure.map(v => `${fmt.money(v.currency, v.value)} (${v.count})`).join('<br>') || 'none'}</dd></dl>
        ${bar(`Rounds below ${state.data.competition.minimumBids} offers`, s.roundsBelowMinimumBefore, s.roundsBelowMinimumAfter)}
        ${bar('Offer concentration (HHI)', s.hhiBefore, s.hhiAfter, v => fmt.num(v, 3))}
        <p class="rd-section-title">Affected rounds</p><ul class="rd-records">${s.rounds.map(r => `<li>${esc(r.tender)} · R${r.roundNumber}: ${r.before} → <b>${r.after}</b> offers${r.dropsBelowMinimum ? ' · falls below minimum' : ''}${r.losesAward ? ' · lost its awarded supplier' : ''}</li>`).join('') || '<li>None</li>'}</ul>
        <p class="rd-section-title">Eligible alternatives in the same category</p><div class="rd-chip-list">${s.alternatives.map(a => `<button type="button" data-pick="${a.supplierId}">${esc(name(a.supplierId))} · ${a.participations} rounds</button>`).join('') || '<span class="rd-muted">No other approved supplier in the same category.</span>'}</div>`;
    } catch (e) { out.innerHTML = `<p class="rd-msg rd-msg-error">${esc(e.message)}</p>`; }
  }

  // ---------- modes & panel ----------
  function setMode(mode) {
    if (!['overview', 'network', 'price', 'forensic'].includes(mode)) return;
    state.mode = mode;
    $$('[role="tab"][data-mode]').forEach(t => { const on = t.dataset.mode === mode; t.setAttribute('aria-selected', on); t.tabIndex = on ? 0 : -1; });
    $$('[data-mode-panel]').forEach(p => p.classList.toggle('is-active', p.dataset.modePanel === mode));
    requestAnimationFrame(() => {
      Object.values(charts).forEach(c => c.resize());
      if (mode === 'network' && cy) {
        cy.resize();
        if (!state.revealed) { reveal(); if (state.selected.length) later(() => focusGraph(state.selected), 1600); }
        else if (state.selected.length) focusGraph(state.selected);
      }
    });
    window.dispatchEvent(new CustomEvent('radar:selection', { detail: context() }));
  }
  // While the officer is talking to the analyst, AI-driven updates do not hide the transcript: the updated tab is marked instead.
  let softPanel = false;
  function openPanel(tab) {
    const current = $('[data-panel-tab][aria-selected="true"]')?.dataset.panelTab;
    if (softPanel && current === 'analyst' && tab !== 'analyst') { $(`[data-panel-tab="${tab}"]`)?.classList.add('has-update'); return; }
    $(`[data-panel-tab="${tab}"]`)?.classList.remove('has-update');
    $('[data-body]').classList.remove('is-collapsed');
    $$('[data-panel-tab]').forEach(t => t.setAttribute('aria-selected', t.dataset.panelTab === tab));
    $$('[data-panel-body]').forEach(b => b.classList.toggle('is-active', b.dataset.panelBody === tab));
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
    const ctx = canvas.getContext('2d'); const w = canvas.clientWidth; const h = 44; const ratio = devicePixelRatio || 1;
    canvas.width = w * ratio; canvas.height = h * ratio; ctx.setTransform(ratio, 0, 0, ratio, 0, 0); ctx.clearRect(0, 0, w, h);
    if (!range) { $('[data-timeline-date]').textContent = 'No events'; return; }
    const p = palette(); const x = t => ((t - range.min) / (range.max - range.min)) * (w - 16) + 8;
    ctx.strokeStyle = p.line; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(8, h / 2); ctx.lineTo(w - 8, h / 2); ctx.stroke();
    for (const e of state.data.timeline) {
      const at = Date.parse(e.at); const past = state.replayAt == null || at <= state.replayAt;
      ctx.globalAlpha = past ? 1 : 0.25;
      ctx.fillStyle = e.type === 'award' ? p.s[1] : e.type === 'pattern' ? p.signal : p.s[0];
      ctx.beginPath(); ctx.arc(x(at), h / 2 + (e.type === 'pattern' ? -9 : e.type === 'award' ? 9 : 0), e.type === 'round' ? 3.5 : 3, 0, Math.PI * 2); ctx.fill();
    }
    ctx.globalAlpha = 1;
    const label = state.replayAt == null ? 'All history' : fmt.date(state.replayAt);
    $('[data-timeline-date]').textContent = label;
  }
  function setReplay(value) {
    const range = timelineRange(); if (!range) return;
    state.replayAt = value >= 1000 ? null : range.min + (range.max - range.min) * (value / 1000);
    $('[data-timeline-range]').value = value;
    const before = cy ? visibleElements().map(x => x.id()) : [];
    applyVisibility(); renderTimeline();
    if (cy) {
      const fresh = visibleElements().filter(x => !before.includes(x.id()));
      fresh.addClass('pulse'); later(() => fresh.removeClass('pulse'), 700);
    }
    root.classList.toggle('is-replaying', state.replayAt != null);
  }
  function play(on = !state.playing) {
    state.playing = on;
    $('[data-timeline="play"]').innerHTML = on ? '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5v14M16 5v14"/></svg>' : '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 4l13 8-13 8z"/></svg>';
    $('[data-timeline="play"]').setAttribute('aria-label', on ? 'Pause historical replay' : 'Play historical replay');
    cancelAnimationFrame(replayFrame);
    if (!on) return;
    if (state.mode !== 'network' && state.mode !== 'overview') setMode('network');
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
    $('[data-brief-title]').textContent = scene.title;
    $('[data-brief-text]').textContent = scene.narration;
    $('[data-brief-text]').dir = brief.language === 'ar' ? 'rtl' : 'ltr';
    $('[data-brief-facts]').innerHTML = scene.facts.map(f => `<div><dt>${esc(f.label)}</dt><dd>${esc(f.value)}</dd></div>`).join('');
    // Visual direction: mode, selection, camera, evidence - every reference was validated on the server.
    setMode(scene.mode);
    state.selected = scene.supplierIds.filter(id => state.suppliers.has(id)); state.finding = scene.findingId;
    renderSelection('graph-silent');
    if (scene.mode === 'network' && cy) {
      const els = cy.collection(scene.edgeIds.map(id => cy.getElementById(id)).filter(x => x.nonempty()));
      els.removeClass('hidden'); els.connectedNodes().removeClass('hidden');
      if (els.nonempty()) illuminate(els.union(els.connectedNodes())); else focusGraph(state.selected);
    } else if (scene.mode === 'forensic' && scene.findingId) { openFinding(scene.findingId, { animate: false }); }
    else if (scene.focus === 'fit') cy?.fit(visibleElements(), 50);
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
    grab('landscape', 'Supplier market landscape'); grab('competition', 'Competition per round');
    if (cy && cy.nodes().nonempty()) { try { images.push({ title: 'Supplier relationship network', dataUrl: cy.png({ full: true, scale: 1.5, bg: '#ffffff', maxWidth: 2200 }) }); } catch { /* skip */ } }
    grab('positions', 'Relative price position over time');
    try {
      const transcript = window.OrangeRadarVoice?.transcript?.() || [];
      const response = await post(urls.report, { ...period(), supplierIds: state.selected, findingIds: state.finding ? [state.finding] : [], transcript, images, aiInterpretation: true });
      const blob = await response.blob();
      const link = document.createElement('a'); link.href = URL.createObjectURL(blob);
      link.download = (response.headers.get('content-disposition') || '').match(/filename="?([^";]+)/)?.[1] || 'Orange-Radar-Report.pdf';
      document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(link.href), 4000);
      status('Report downloaded.'); later(() => status(''), 4000);
      return true;
    } catch (e) { status(`The report could not be generated: ${e.message}`, true); return false; }
  }

  // ---------- context & commands (for the AI analyst) ----------
  function context() {
    const p = period();
    return { mode: state.mode, selectedSupplierIds: state.selected, selectedTenderId: state.tender, from: p.from, to: p.to, selectedFindingId: state.finding,
      selectedChartPoint: state.chartPoint, edgeFilters: [...state.edgeTypes], simulationSummary: state.simulation };
  }
  // Whitelisted visual commands only; arguments were validated on the server and are re-checked here.
  function apply(command) {
    if (!command || typeof command !== 'object' || !state.data) return;
    softPanel = true;
    try { applyCommand(command); } finally { softPanel = false; }
  }
  function applyCommand(command) {
    const ids = (Array.isArray(command.supplierIds) ? command.supplierIds : []).map(Number).filter(id => state.suppliers.has(id));
    switch (command.action) {
      case 'mode': setMode(String(command.mode)); break;
      case 'highlight': if (command.findingId && state.data.findings.some(f => f.id === command.findingId)) openFinding(command.findingId); else { select(ids); if (ids.length) openPanel('details'); } break;
      case 'compare': select(ids); openPanel('details'); break;
      case 'evidence': openFinding(String(command.findingId)); break;
      case 'path': if (state.suppliers.has(+command.from) && state.suppliers.has(+command.to)) { select([+command.from, +command.to]); showPath(+command.from, +command.to); } break;
      case 'simulate': select(ids); simulate(ids); break;
      case 'documents': openPanel('evidence'); compareDocuments(+command.documentA, +command.documentB); break;
      case 'period': $('#rd-from').value = /^\d{4}-\d{2}-\d{2}$/.test(command.from) ? command.from : ''; $('#rd-to').value = /^\d{4}-\d{2}-\d{2}$/.test(command.to) ? command.to : ''; load(); break;
      case 'timeline': if (command.value === 'reset') { play(false); setReplay(1000); } else play(command.value === 'play'); break;
      case 'briefing': startBriefing(); break;
      case 'report': generateReport(); break;
    }
  }
  window.OrangeRadar = Object.freeze({ context, apply, cancelAnimations, name, fullName, openPanel, get data() { return state.data; }, get selected() { return [...state.selected]; }, stopBriefing, pauseBriefing: () => pauseBriefing(true), get briefingActive() { return !$('[data-briefing]').hidden; } });

  // ---------- events ----------
  root.addEventListener('click', e => {
    const t = e.target.closest('button, [data-select], tr[data-edge]'); if (!t) return;
    const d = t.dataset;
    if (d.mode && t.getAttribute('role') === 'tab') setMode(d.mode);
    else if (d.action === 'run') load(true);
    else if (d.action === 'brief') startBriefing();
    else if (d.action === 'report') generateReport();
    else if (d.action === 'dna') runDocumentDna();
    else if (d.action === 'simulate') simulate();
    else if (d.action === 'fullscreen') { const s = $('[data-stage]'); if (document.fullscreenElement) document.exitFullscreen(); else s.requestFullscreen?.(); }
    else if (d.action === 'collapse') { const body = $('[data-body]'); body.classList.toggle('is-collapsed'); t.setAttribute('aria-expanded', !body.classList.contains('is-collapsed')); requestAnimationFrame(() => { Object.values(charts).forEach(c => c.resize()); cy?.resize(); }); }
    else if (d.panelTab) openPanel(d.panelTab);
    else if (d.finding) { openFinding(d.finding); }
    else if (d.showFinding) { setMode('network'); showFindingInGraph(d.showFinding); }
    else if (d.compareDocs) { const [a, b] = d.compareDocs.split(',').map(Number); compareDocuments(a, b); }
    else if (d.pick) select([+d.pick], { additive: e.shiftKey });
    else if (d.pair) { select(d.pair.split(',').map(Number)); openPanel('details'); }
    else if (d.path) { const [a, b] = d.path.split(',').map(Number); showPath(a, b); }
    else if (d.scenario) simulate([+d.scenario]);
    else if (d.expand) { setMode('network'); toggleExpand(+d.expand); }
    else if (d.ask) window.OrangeRadarVoice?.ask?.(d.ask);
    else if (d.table) { const card = t.closest('.rd-card'); if (tables[d.table]) showTable(card, tables[d.table]()); }
    else if (t.hasAttribute('data-close-table')) t.closest('.rd-table-wrap').remove();
    else if (d.select) select([+d.select], { additive: e.shiftKey });
    else if (d.edge) { const edge = state.data.graph.edges.find(x => x.id === d.edge); if (edge) openEdge(edge); }
    else if (d.graph) graphAction(d.graph, t);
    else if (d.brief) ({ prev: () => { brief.index = Math.max(0, brief.index - 1); brief.paused = false; playScene(); }, next: () => { brief.index = Math.min(brief.scenes.length - 1, brief.index + 1); brief.paused = false; playScene(); }, toggle: () => pauseBriefing(), replay: () => { brief.index = 0; brief.paused = false; playScene(); }, stop: stopBriefing, ask: () => { pauseBriefing(true); const s = brief.scenes[brief.index]; window.OrangeRadarVoice?.prefill?.(`Tell me more about "${s?.title}".`); } })[d.brief]?.();
    else if (d.timeline === 'play') play();
    else if (d.timeline === 'reset') { play(false); setReplay(1000); }
  });
  root.addEventListener('keydown', e => {
    if (e.target.matches('tr[data-edge]') && e.key === 'Enter') e.target.click();
    if (e.target.matches('[role="tab"][data-mode]') && (e.key === 'ArrowRight' || e.key === 'ArrowLeft')) {
      const tabs = $$('[role="tab"][data-mode]'); const i = tabs.indexOf(e.target) + (e.key === 'ArrowRight' ? 1 : -1);
      const next = tabs[(i + tabs.length) % tabs.length]; next.focus(); setMode(next.dataset.mode);
    }
  });
  function graphAction(action, button) {
    if (!cy && action !== 'table') return;
    if (action === 'fit') cy.animate({ fit: { eles: visibleElements(), padding: 50 } }, { duration: dur(600) });
    else if (action === 'focus') { state.focus = !state.focus; button.setAttribute('aria-pressed', state.focus); focusGraph(state.selected); }
    else if (action === 'expand') { (state.selected.length ? state.selected : [...state.suppliers.keys()]).forEach(id => state.expanded.add(id)); applyVisibility(); relayout(); }
    else if (action === 'collapse') { state.expanded.clear(); applyVisibility(); relayout(); }
    else if (action === 'group') groupByRelationship();
    else if (action === 'relayout') relayout(null, { randomize: true });
    else if (action === 'path') { if (state.selected.length >= 2) showPath(state.selected[0], state.selected[1]); else status('Select two suppliers (Shift+click) to find the relationship path between them.'); }
    else if (action === 'png') { const a = document.createElement('a'); a.href = cy.png({ full: true, scale: 2, bg: css('--rd-surface') || '#ffffff' }); a.download = 'orange-radar-network.png'; a.click(); }
    else if (action === 'table') { const box = $('[data-graph-table]'); const on = box.hidden; box.hidden = !on; button.setAttribute('aria-pressed', on); if (on) graphTable(); }
  }
  root.addEventListener('change', e => {
    const t = e.target;
    if (t.dataset.edgeType) { t.checked ? state.edgeTypes.add(t.dataset.edgeType) : state.edgeTypes.delete(t.dataset.edgeType); applyVisibility(); if (!$('[data-graph-table]').hidden) graphTable(); }
    else if (t.dataset.nodeType) { t.checked ? state.nodeTypes.add(t.dataset.nodeType) : state.nodeTypes.delete(t.dataset.nodeType); applyVisibility(); relayout(); }
    else if (t.matches('[data-item-select]')) items();
    else if (t.matches('#rd-from, #rd-to')) load();
  });
  $('[data-timeline-range]').addEventListener('input', e => { play(false); setReplay(+e.target.value); });
  $('[data-graph-search]').addEventListener('input', e => {
    const q = e.target.value.trim().toLowerCase(); if (!cy || q.length < 2) return;
    const hit = cy.nodes('[type="supplier"]').filter(n => n.data('label').toLowerCase().includes(q))[0];
    if (hit) select([hit.data('entityId')]);
  });
  // Resizable context panel (pointer and keyboard).
  const resizer = $('[data-resizer]'); const body = $('[data-body]');
  const setWidth = w => { body.style.setProperty('--rd-panel-w', `${Math.max(300, Math.min(680, w))}px`); Object.values(charts).forEach(c => c.resize()); cy?.resize(); };
  resizer.addEventListener('pointerdown', e => {
    resizer.setPointerCapture(e.pointerId);
    const rtl = getComputedStyle(root).direction === 'rtl'; const startX = e.clientX; const start = $('[data-panel]').getBoundingClientRect().width;
    const move = ev => setWidth(start + (rtl ? ev.clientX - startX : startX - ev.clientX));
    resizer.addEventListener('pointermove', move);
    resizer.addEventListener('pointerup', () => resizer.removeEventListener('pointermove', move), { once: true });
  });
  resizer.addEventListener('keydown', e => { if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') setWidth($('[data-panel]').getBoundingClientRect().width + (e.key === 'ArrowLeft' ? 24 : -24)); });
  document.addEventListener('fullscreenchange', () => requestAnimationFrame(() => { Object.values(charts).forEach(c => c.resize()); cy?.resize(); cy?.fit(visibleElements(), 50); }));
  window.addEventListener('orange:themechange', () => { if (!state.data) return; renderOverview(); renderPrice(); renderForensic(); renderTimeline(); renderEdgeFilters(); renderLegend(); cy?.style(graphStyle()); });
  window.addEventListener('resize', () => renderTimeline());
  reduced.addEventListener?.('change', () => startSignalAnimation());

  load();
})();
