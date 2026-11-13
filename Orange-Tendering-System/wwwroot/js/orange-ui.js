"use strict";
// Orange UI kit for every signed-in workspace: global search, voice search, popup-first modals/drawers, the professional
// table kit (search, filters, sorting, pagination, status pills) and the supplier navbar toggle.
// Nothing here grants access: every URL it opens is a normal server-authorized page or JSON endpoint.
(() => {
  const body = document.body;
  if (!body || !body.classList.contains('ws-shell')) return;
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };

  /* ---------- Supplier horizontal navbar (mobile toggle) ---------- */
  const navToggle = $('.ws-topnav-toggle');
  if (navToggle) {
    navToggle.addEventListener('click', () => {
      const open = body.classList.toggle('ws-topnav-open');
      navToggle.setAttribute('aria-expanded', String(open));
    });
  }

  /* ---------- Toasts ---------- */
  function toast(message, kind = 'success') {
    let region = $('.ui-toasts');
    if (!region) { region = el('div', 'ui-toasts'); region.setAttribute('aria-live', 'polite'); body.append(region); }
    const t = el('div', 'ui-toast ui-toast--' + kind, message);
    region.append(t);
    setTimeout(() => t.classList.add('is-leaving'), 3800);
    setTimeout(() => t.remove(), 4300);
  }
  window.orangeUi = { toast };
  try { const flash = sessionStorage.getItem('orange-flash'); if (flash) { sessionStorage.removeItem('orange-flash'); toast(flash); } } catch { /* storage optional */ }

  /* ---------- Global search ---------- */
  const search = $('[data-global-search]');
  if (search) {
    const input = $('input', search), results = $('.ws-search-results', search);
    let timer = 0, seq = 0, active = -1;
    const close = () => { results.hidden = true; input.setAttribute('aria-expanded', 'false'); active = -1; };
    const show = items => {
      results.replaceChildren();
      if (!items.length) { results.append(el('p', 'ws-search-empty', 'No matching records you can access.')); }
      let last = '';
      items.forEach(i => {
        if (i.type !== last) { results.append(el('span', 'ws-search-group', i.type)); last = i.type; }
        const a = el('a', 'ws-search-item'); a.href = i.url; a.setAttribute('role', 'option');
        a.append(el('strong', null, i.title), el('small', null, i.subtitle));
        results.append(a);
      });
      results.hidden = false; input.setAttribute('aria-expanded', 'true');
    };
    input.addEventListener('input', () => {
      clearTimeout(timer);
      const q = input.value.trim();
      if (q.length < 2) { close(); return; }
      timer = setTimeout(async () => {
        const mine = ++seq;
        try {
          const r = await fetch(search.dataset.searchUrl + '?q=' + encodeURIComponent(q), { credentials: 'same-origin', headers: { Accept: 'application/json' } });
          if (r.ok && mine === seq) show((await r.json()).items);
        } catch { /* keep previous results */ }
      }, 220);
    });
    input.addEventListener('keydown', ev => {
      const links = $$('.ws-search-item', results);
      if (ev.key === 'Escape') { close(); return; }
      if (!links.length || (ev.key !== 'ArrowDown' && ev.key !== 'ArrowUp')) return;
      ev.preventDefault();
      active = (active + (ev.key === 'ArrowDown' ? 1 : -1) + links.length) % links.length;
      links.forEach((l, i) => l.classList.toggle('is-active', i === active)); links[active].scrollIntoView({ block: 'nearest' });
    });
    input.addEventListener('keydown', ev => { if (ev.key === 'Enter') { const a = $('.ws-search-item.is-active', results) || $('.ws-search-item', results); if (a) { ev.preventDefault(); location.href = a.href; } } });
    document.addEventListener('click', ev => { if (!search.contains(ev.target)) close(); });
    const voice = $('[data-voice-search]');
    if (voice) {
      const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
      voice.addEventListener('click', () => {
        if (!Recognition) { toast('Voice search is not supported by this browser. Type your search instead.', 'info'); input.focus(); return; }
        const rec = new Recognition(); rec.lang = document.documentElement.lang || 'en-US'; rec.interimResults = false;
        voice.classList.add('is-listening');
        rec.onresult = e => { input.value = e.results[0][0].transcript; input.dispatchEvent(new Event('input')); input.focus(); };
        rec.onerror = () => toast('Voice search could not hear you. Check the microphone permission.', 'info');
        rec.onend = () => voice.classList.remove('is-listening');
        try { rec.start(); } catch { voice.classList.remove('is-listening'); }
      });
    }
  }

  /* ---------- Popup-first modals and drawers ---------- */
  const ACTION = /\/(Create|Edit|Details|Review|Approve|Reject|Return|Forward|Upload|Sign|Confirm|Respond|Submit|Decide|Delete|Remove|Add|New|Compliance|Compare)(\/|\?|$)/i;
  const DRAWER = /\/(Details|Review|Compare|Compliance)(\/|\?|$)/i;
  const COMPLEX = /\/(Workflow|Identity|Biometrics|Notifications|Tasks)(\/|\?|$)/i;
  const dialog = el('dialog', 'ui-dialog');
  dialog.setAttribute('aria-labelledby', 'ui-dialog-title');
  dialog.innerHTML = '<div class="ui-dialog-head"><h2 id="ui-dialog-title"></h2><button type="button" class="ui-dialog-close" aria-label="Close" data-dialog-close>×</button></div><div class="ui-dialog-body" tabindex="-1"></div>';
  body.append(dialog);
  const dTitle = $('#ui-dialog-title', dialog), dBody = $('.ui-dialog-body', dialog);
  const closeDialog = () => { if (dialog.open) dialog.close(); };
  dialog.addEventListener('click', ev => { if (ev.target === dialog || ev.target.closest('[data-dialog-close]')) closeDialog(); });
  dialog.addEventListener('close', () => { dBody.replaceChildren(); dialog.classList.remove('ui-dialog--drawer'); });
  let opener = null;

  function absorb(doc, url) {
    const page = doc.querySelector('#workspace-main .pg-page') || doc.querySelector('.pg-page') || doc.querySelector('main');
    if (!page) return null;
    const frag = el('div', 'ui-dialog-content');
    frag.innerHTML = page.innerHTML;
    $$('.pg-header, .pg-breadcrumb, script, .pg-note, h1', frag).forEach(n => n.remove());
    const h = page.querySelector('h1');
    return { node: frag, title: (h ? h.textContent : doc.title.split('|')[0]).trim() };
  }

  function wire(container, baseUrl) {
    // Cancel / Back links close the popup instead of navigating.
    $$('a', container).forEach(a => {
      const t = a.textContent.trim().toLowerCase();
      if (/^(cancel|back|close|back to .*)$/.test(t)) a.addEventListener('click', ev => { ev.preventDefault(); closeDialog(); });
    });
    $$('form', container).forEach(form => form.addEventListener('submit', ev => {
      ev.preventDefault();
      submit(form, baseUrl, ev.submitter);
    }));
  }

  async function submit(form, baseUrl, submitter) {
    const data = new FormData(form);
    if (submitter && submitter.name) data.append(submitter.name, submitter.value);
    const buttons = $$('button[type=submit], button:not([type])', form); buttons.forEach(b => b.disabled = true);
    try {
      const res = await fetch(form.getAttribute('action') || baseUrl, { method: (form.method || 'post').toUpperCase(), body: data, credentials: 'same-origin', headers: { 'X-Requested-With': 'orange-modal' } });
      const html = await res.text();
      const doc = new DOMParser().parseFromString(html, 'text/html');
      const stillForm = res.ok && !res.redirected && doc.querySelector('#workspace-main form [class*="validation"], #workspace-main .field-error:not(:empty), #workspace-main .validation-summary-errors');
      if (res.ok && (res.redirected || !stillForm) && !doc.querySelector('#workspace-main .validation-summary-errors')) {
        try { sessionStorage.setItem('orange-flash', 'Saved successfully.'); } catch { /* optional */ }
        location.href = res.redirected && new URL(res.url).origin === location.origin && !COMPLEX.test(new URL(res.url).pathname) ? res.url : location.href;
        return;
      }
      const absorbed = absorb(doc, res.url);
      if (absorbed) { dBody.replaceChildren(absorbed.node); wire(absorbed.node, res.url); }
      else toast('The request could not be completed.', 'danger');
    } catch { toast('Network error. Please try again.', 'danger'); }
    finally { buttons.forEach(b => b.disabled = false); }
  }

  async function open(url, label, drawer) {
    dTitle.textContent = label || 'Loading…'; dBody.replaceChildren(el('p', 'ui-loading', 'Loading…'));
    dialog.classList.toggle('ui-dialog--drawer', !!drawer);
    if (!dialog.open) dialog.showModal();
    try {
      const res = await fetch(url, { credentials: 'same-origin', headers: { 'X-Requested-With': 'orange-modal' } });
      if (res.redirected && (COMPLEX.test(new URL(res.url).pathname) || /Login/i.test(res.url))) { location.href = res.url; return; }
      if (!res.ok) throw new Error(String(res.status));
      const absorbed = absorb(new DOMParser().parseFromString(await res.text(), 'text/html'), res.url);
      if (!absorbed) { location.href = url; return; }
      dTitle.textContent = absorbed.title; dBody.replaceChildren(absorbed.node); wire(absorbed.node, res.url);
      dBody.focus();
    } catch { closeDialog(); location.href = url; }
  }

  document.addEventListener('click', ev => {
    const a = ev.target.closest('a[href]');
    if (!a || ev.defaultPrevented || ev.metaKey || ev.ctrlKey || ev.shiftKey || a.target || a.hasAttribute('download') || a.hasAttribute('data-no-modal')) return;
    if (a.closest('dialog, nav, header, footer, .ws-sidebar, .ws-bell, .ws-account, .ws-search-results, .pg-breadcrumb')) return;
    const url = new URL(a.href, location.href);
    if (url.origin !== location.origin || url.pathname === location.pathname) return;
    const explicit = a.hasAttribute('data-modal');
    if (!explicit && (!ACTION.test(url.pathname + url.search) || COMPLEX.test(url.pathname) || !a.closest('main'))) return;
    ev.preventDefault(); opener = a;
    open(url.href, a.dataset.modalTitle || a.textContent.trim(), explicit ? a.dataset.modal === 'drawer' : DRAWER.test(url.pathname));
  });

  /* ---------- Table kit ---------- */
  const TONES = [
    [/approved|accepted|compliant|awarded|active|verified|submitted|open|answered|completed|signed|yes|live data/i, 'good'],
    [/pending|draft|in progress|waiting|under|review|evaluation|bidding|not checked|returned|clarification/i, 'warn'],
    [/rejected|non-?compliant|failed|expired|declined|cancelled|suspended|withdrawn|closed|no$/i, 'bad']
  ];
  const tone = text => (TONES.find(([re]) => re.test(text)) || [0, 'info'])[1];

  function enhance(table, index) {
    if (table.dataset.noEnhance !== undefined || table.closest('dialog.ui-dialog, .dash-panel') || table.dataset.enhanced) return;
    if (!table.tHead && table.rows.length > 1 && table.rows[0].querySelector('th')) {
      const th = table.createTHead(); th.append(table.rows[0]);
    }
    if (!table.closest('.pg-table-wrap') && !table.classList.contains('pg-table')) { const w = el('div', 'pg-table-wrap'); table.before(w); w.append(table); table.classList.add('pg-table'); }
    const tbody = table.tBodies[0]; const head = table.tHead && table.tHead.rows[0];
    if (!tbody || !head) return;
    let rows = [...tbody.rows];
    if (!rows.length || (rows.length === 1 && rows[0].cells.length === 1 && rows[0].cells[0].colSpan > 1)) return;
    table.dataset.enhanced = '1';
    const heads = [...head.cells];
    const statusCols = heads.map((h, i) => /status|state|decision|compliance|result|priority/i.test(h.textContent) ? i : -1).filter(i => i >= 0);
    // status pills
    rows.forEach(r => statusCols.forEach(i => {
      const c = r.cells[i]; if (!c || c.children.length > 0 && !c.querySelector('.pg-status, .dash-status-pill')) return;
      const text = c.textContent.trim(); if (!text) return;
      if (!c.querySelector('.ui-pill')) { const pill = el('span', 'ui-pill ui-pill--' + tone(text), text); c.replaceChildren(pill); }
    }));
    const wrap = table.closest('.pg-table-wrap') || table.parentElement;
    const bar = el('div', 'ui-table-bar');
    const q = el('input', 'ui-table-search'); q.type = 'search'; q.placeholder = 'Search this table…'; q.setAttribute('aria-label', 'Search this table');
    bar.append(q);
    let filter = null;
    if (statusCols.length) {
      const col = statusCols[0];
      const values = [...new Set(rows.map(r => (r.cells[col] || {}).textContent?.trim()).filter(Boolean))];
      if (values.length > 1 && values.length < 14) {
        filter = el('select', 'ui-table-filter'); filter.setAttribute('aria-label', 'Filter by ' + heads[col].textContent.trim());
        filter.append(new Option('All ' + heads[col].textContent.trim().toLowerCase().replace(/^(.)/, c => c), ''));
        values.sort().forEach(v => filter.append(new Option(v, v)));
        bar.append(filter);
      }
    }
    const count = el('span', 'ui-table-count'); bar.append(count);
    wrap.before(bar);
    const pager = el('nav', 'ui-pager'); pager.setAttribute('aria-label', 'Table pages'); wrap.after(pager);
    const size = 10; let page = 0, sortCol = -1, asc = true;
    heads.forEach((h, i) => {
      if (h.hasAttribute('data-nosort') || /action/i.test(h.textContent) || !h.textContent.trim()) return;
      h.classList.add('ui-sortable'); h.tabIndex = 0; h.setAttribute('role', 'columnheader');
      const sort = () => { asc = sortCol === i ? !asc : true; sortCol = i; heads.forEach(x => x.removeAttribute('aria-sort')); h.setAttribute('aria-sort', asc ? 'ascending' : 'descending'); draw(); };
      h.addEventListener('click', sort); h.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); sort(); } });
    });
    const cellText = (r, i) => (r.cells[i] ? r.cells[i].textContent.trim() : '');
    function draw() {
      const term = q.value.trim().toLowerCase(), fv = filter ? filter.value : '';
      let list = rows.filter(r => (!term || r.textContent.toLowerCase().includes(term)) && (!fv || cellText(r, statusCols[0]) === fv));
      if (sortCol >= 0) list.sort((a, b) => {
        const x = cellText(a, sortCol), y = cellText(b, sortCol), nx = parseFloat(x.replace(/[^0-9.\-]/g, '')), ny = parseFloat(y.replace(/[^0-9.\-]/g, ''));
        const cmp = !isNaN(nx) && !isNaN(ny) && /\d/.test(x) && /\d/.test(y) ? nx - ny : x.localeCompare(y, undefined, { numeric: true });
        return asc ? cmp : -cmp;
      });
      const pages = Math.max(1, Math.ceil(list.length / size)); page = Math.min(page, pages - 1);
      tbody.replaceChildren(...rows.filter(r => !list.includes(r)).map(r => { r.hidden = true; return r; }), ...list.map((r, i) => { r.hidden = i < page * size || i >= (page + 1) * size; return r; }));
      count.textContent = list.length === rows.length ? `${rows.length} record${rows.length === 1 ? '' : 's'}` : `${list.length} of ${rows.length} records`;
      pager.replaceChildren(); pager.hidden = pages <= 1;
      if (pages > 1) {
        const mk = (label, p, disabled, current) => { const b = el('button', 'ui-page' + (current ? ' is-current' : ''), label); b.type = 'button'; b.disabled = disabled; if (current) b.setAttribute('aria-current', 'page'); b.addEventListener('click', () => { page = p; draw(); }); return b; };
        pager.append(mk('‹', page - 1, page === 0));
        for (let p = 0; p < pages; p++) pager.append(mk(String(p + 1), p, false, p === page));
        pager.append(mk('›', page + 1, page === pages - 1));
      }
      let none = tbody.querySelector('.ui-empty-row');
      if (!list.length) { if (!none) { none = el('tr', 'ui-empty-row'); const td = el('td', null, 'No records match your search.'); td.colSpan = heads.length; none.append(td); tbody.append(none); } }
      else if (none) none.remove();
    }
    q.addEventListener('input', () => { page = 0; draw(); });
    if (filter) filter.addEventListener('change', () => { page = 0; draw(); });
    draw();
  }
  $$('main table').forEach(enhance);
})();
