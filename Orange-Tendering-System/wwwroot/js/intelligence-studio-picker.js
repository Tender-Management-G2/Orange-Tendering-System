// ORANGE INTELLIGENCE - AI Studio: the searchable "Choose a supplier" list (Views/Intelligence/Index.cshtml).
// Presentation only. The real choice is still the hidden <select data-studio-supplier>: intelligence-ai.js fills it from the
// authorized supplier list ("radar:data" event / window.OrangeRadar.data) and reads it for Summary and Chat, and
// intelligence-audio.js reads it for Live Voice. This file draws that <select> as a list and writes the clicked supplier back
// into it (then fires "change"), so no request, prompt or permission changes.
(function () {
  'use strict';
  const studio = document.querySelector('[data-studio]');
  const select = studio?.querySelector('[data-studio-supplier]');
  const list = studio?.querySelector('[data-picker-list]');
  if (!studio || !select || !list) return;

  const search = studio.querySelector('[data-picker-search]');
  const countText = studio.querySelector('[data-picker-count]');
  const current = studio.querySelector('[data-picker-selected]');
  const page = document.querySelector('[data-radar]');
  const arabic = page?.dataset.uiLanguage === 'ar';
  const T = (en, ar) => (arabic ? ar : en);
  const meta = new Map();   // supplierId -> { category, categoryColor, participations } from the same authorized list
  let loaded = false;
  let activeId = null;      // the row highlighted by the keyboard

  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const css = name => getComputedStyle(page || document.documentElement).getPropertyValue(name).trim();
  const colorOf = index => (index == null || index < 0 ? css('--rd-neutral') : css(`--c${(index % 8) + 1}`)) || '#7f8ea3';
  const initials = name => {
    const words = String(name).replace(/[^\p{L}\p{N}\s]/gu, ' ').trim().split(/\s+/).filter(Boolean);
    return ((words[0]?.[0] || '?') + (words[1]?.[0] || '')).toUpperCase();
  };
  const options = () => Array.from(select.options).filter(o => o.value).map(o => ({ id: o.value, name: o.textContent }));
  const chosenId = () => select.value || null;

  function row(option) {
    const info = meta.get(option.id) || {};
    const color = colorOf(info.categoryColor);
    const selected = option.id === chosenId();
    const cycles = info.participations == null ? '' : ` · ${info.participations} ${info.participations === 1 ? T('cycle', 'دورة') : T('cycles', 'دورات')}`;
    const title = info.participations == null ? '' : ` title="${info.participations} ${T('closed bidding cycles with an offer', 'دورات عطاء مغلقة بعرض')}"`;
    return `<li role="option" id="ais-opt-${esc(option.id)}" data-id="${esc(option.id)}" aria-selected="${selected}" class="${option.id === activeId ? 'is-active' : ''}" style="--c:${color}"${title}>
      <span class="ais-avatar" aria-hidden="true">${esc(initials(option.name))}</span>
      <span class="ais-opt-text"><b dir="auto">${esc(option.name)}</b><small><i aria-hidden="true"></i>${esc(info.category || T('Uncategorized', 'غير مصنف'))}${esc(cycles)}</small></span>
      <svg class="ais-opt-check" viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 5 5 9-10" /></svg></li>`;
  }

  function filtered() {
    const q = (search?.value || '').trim().toLowerCase();
    if (!q) return options();
    return options().filter(o => o.name.toLowerCase().includes(q) || (meta.get(o.id)?.category || '').toLowerCase().includes(q));
  }

  function render() {
    const all = options(); const items = filtered();
    list.setAttribute('aria-busy', String(!loaded));
    if (!loaded) {
      list.innerHTML = Array.from({ length: 5 }, () => '<li class="ais-skeleton" aria-hidden="true"><span></span><span></span></li>').join('');
      countText.textContent = T('Loading authorized suppliers…', 'جارٍ تحميل الموردين المصرح بهم…');
    } else if (!all.length) {
      list.innerHTML = `<li class="ais-picker-empty">${T('No suppliers in your authorized scope for this period.', 'لا يوجد موردون ضمن نطاقك المصرح لهذه الفترة.')}</li>`;
      countText.textContent = T('0 suppliers', '0 مورد');
    } else if (!items.length) {
      list.innerHTML = `<li class="ais-picker-empty">${T('No supplier matches', 'لا يوجد مورد يطابق')} “${esc(search.value.trim())}”.</li>`;
      countText.textContent = `0 / ${all.length}`;
    } else {
      list.innerHTML = items.map(row).join('');
      countText.textContent = items.length === all.length
        ? `${all.length} ${T('suppliers in your scope', 'مورداً في نطاقك')}`
        : `${items.length} / ${all.length} ${T('suppliers', 'مورد')}`;
    }
    if (activeId && !items.some(o => o.id === activeId)) activeId = null;
    if (activeId) list.setAttribute('aria-activedescendant', `ais-opt-${activeId}`); else list.removeAttribute('aria-activedescendant');
    renderCurrent();
  }

  // The card above the modes: which supplier Summary, Chat and Voice will use.
  function renderCurrent() {
    if (!current) return;
    const id = chosenId(); const option = options().find(o => o.id === id);
    studio.classList.toggle('has-supplier', !!option);
    if (!option) {
      current.innerHTML = `<span class="ais-avatar is-empty" aria-hidden="true">?</span><span class="ais-opt-text"><b>${T('No supplier selected', 'لم يتم اختيار مورد')}</b><small>${T('Choose one from the list to start.', 'اختر مورداً من القائمة للبدء.')}</small></span>`;
      current.style.removeProperty('--c');
      return;
    }
    const info = meta.get(id) || {};
    current.style.setProperty('--c', colorOf(info.categoryColor));
    current.innerHTML = `<span class="ais-avatar" aria-hidden="true">${esc(initials(option.name))}</span><span class="ais-opt-text"><small>${T('Selected supplier', 'المورد المختار')}</small><b dir="auto">${esc(option.name)}</b><small><i aria-hidden="true"></i>${esc(info.category || T('Uncategorized', 'غير مصنف'))}</small></span>`;
  }

  function choose(id) {
    if (!id) return;
    select.value = id;
    select.dispatchEvent(new Event('change', { bubbles: true })); // intelligence-ai.js listens to "change" (it clears the error line)
  }

  function markChosen() {
    const id = chosenId();
    list.querySelectorAll('[role="option"]').forEach(li => li.setAttribute('aria-selected', String(li.dataset.id === id)));
    renderCurrent();
    list.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
  }

  function moveActive(step, toEnd) {
    const items = Array.from(list.querySelectorAll('[role="option"]')); if (!items.length) return;
    let index = items.findIndex(li => li.dataset.id === activeId);
    if (toEnd === 'first') index = 0; else if (toEnd === 'last') index = items.length - 1;
    else index = index < 0 ? items.findIndex(li => li.getAttribute('aria-selected') === 'true') : index + step;
    index = Math.max(0, Math.min(items.length - 1, index < 0 ? 0 : index));
    activeId = items[index].dataset.id;
    items.forEach(li => li.classList.toggle('is-active', li.dataset.id === activeId));
    list.setAttribute('aria-activedescendant', `ais-opt-${activeId}`);
    items[index].scrollIntoView({ block: 'nearest' });
  }

  list.addEventListener('click', e => { const li = e.target.closest('[role="option"]'); if (li) { activeId = li.dataset.id; choose(li.dataset.id); } });
  list.addEventListener('keydown', e => {
    if (e.key === 'ArrowDown') { e.preventDefault(); moveActive(1); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); if (list.querySelector('[role="option"]')?.dataset.id === activeId && search) search.focus(); else moveActive(-1); }
    else if (e.key === 'Home') { e.preventDefault(); moveActive(0, 'first'); }
    else if (e.key === 'End') { e.preventDefault(); moveActive(0, 'last'); }
    else if ((e.key === 'Enter' || e.key === ' ') && activeId) { e.preventDefault(); choose(activeId); }
  });
  list.addEventListener('focus', () => { if (!activeId) moveActive(0); });
  search?.addEventListener('input', () => { activeId = null; render(); });
  search?.addEventListener('keydown', e => {
    if (e.key === 'ArrowDown') { e.preventDefault(); list.focus(); moveActive(0, 'first'); }
    else if (e.key === 'Enter') { e.preventDefault(); const first = filtered()[0]; if (first) choose(first.id); }
  });
  select.addEventListener('change', markChosen);

  // intelligence-ai.js rebuilds the <select> options whenever the supplier list reloads.
  new MutationObserver(render).observe(select, { childList: true });
  function readSuppliers(suppliers) {
    meta.clear();
    (suppliers || []).forEach(s => meta.set(String(s.supplierId), { category: s.category, categoryColor: s.categoryColor, participations: s.participations }));
    loaded = true; render();
  }
  window.addEventListener('radar:data', e => readSuppliers(e.detail.suppliers));
  window.addEventListener('orange:themechange', () => requestAnimationFrame(render));
  if (window.OrangeRadar?.data) readSuppliers(window.OrangeRadar.data.suppliers); else render();
})();
