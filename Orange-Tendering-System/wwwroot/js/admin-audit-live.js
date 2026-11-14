"use strict";
(() => {
  const form = document.querySelector('[data-audit-filters]');
  if (!form || document.body.dataset.workspaceRole !== 'Admin') return;
  const results = document.querySelector('[data-audit-results]');
  const status = document.querySelector('[data-audit-status]');
  let timer, controller, generation = 0;

  function filterUrl() {
    const url = new URL(form.action, location.href);
    new FormData(form).forEach((value, key) => {
      if (typeof value === 'string' && value.trim()) url.searchParams.set(key, value.trim());
    });
    return url;
  }

  async function load(url, mine) {
    const from = form.elements.namedItem('fromDate').value;
    const to = form.elements.namedItem('toDate').value;
    if (from && to && from > to) {
      status.textContent = 'From Date cannot be later than To Date.';
      return;
    }
    controller = new AbortController();
    results.setAttribute('aria-busy', 'true');
    status.textContent = 'Searching…';
    try {
      const response = await fetch(url.href, {
        credentials: 'same-origin', signal: controller.signal,
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
      });
      if (mine !== generation) return;
      if (response.redirected) { location.assign(response.url); return; }
      if (!response.ok) throw new Error('Search failed');
      const doc = new DOMParser().parseFromString(await response.text(), 'text/html');
      if (mine !== generation) return;
      const updated = doc.querySelector('[data-audit-results]');
      if (!updated) throw new Error('Missing results');
      results.replaceChildren(...updated.childNodes);
      history.replaceState(null, '', url.href);
      status.textContent = 'Results updated.';
    } catch (error) {
      if (mine === generation && error.name !== 'AbortError')
        status.textContent = 'Could not update results. Try typing again or press Enter to retry.';
    } finally {
      if (mine === generation) results.removeAttribute('aria-busy');
    }
  }

  function schedule(delay = 300, url) {
    clearTimeout(timer);
    controller?.abort();
    const mine = ++generation;
    results.removeAttribute('aria-busy');
    timer = setTimeout(() => load(url || filterUrl(), mine), delay);
  }
  form.querySelector('[name="search"]').addEventListener('input', event => {
    if (!event.isComposing) schedule();
  });
  form.querySelector('[name="search"]').addEventListener('compositionend', () => schedule());
  form.querySelectorAll('input[type="date"]').forEach(input => input.addEventListener('change', () => schedule(0)));
  form.addEventListener('submit', event => { event.preventDefault(); schedule(0); });
  results.addEventListener('click', event => {
    const link = event.target.closest('a[href]');
    if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return;
    const url = new URL(link.href, location.href);
    if (url.origin !== location.origin || url.pathname !== new URL(form.action, location.href).pathname) return;
    event.preventDefault();
    schedule(0, url);
  });
})();
