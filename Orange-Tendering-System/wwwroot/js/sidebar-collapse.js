// Collapsible workspace sidebar (desktop widths, where the sidebar is fixed). The officer's choice is a local display
// preference only. Loaded in <head> so the saved state applies before the first paint.
(() => {
  'use strict';
  const KEY = 'orange-sidebar-collapsed';
  const html = document.documentElement;
  try { if (localStorage.getItem(KEY) === '1') html.classList.add('ws-sidebar-collapsed'); } catch { /* storage blocked */ }

  function set(collapsed, button) {
    html.classList.toggle('ws-sidebar-collapsed', collapsed);
    try { localStorage.setItem(KEY, collapsed ? '1' : '0'); } catch { /* storage blocked */ }
    if (button) { button.setAttribute('aria-expanded', String(!collapsed)); button.setAttribute('aria-label', collapsed ? 'Expand navigation' : 'Collapse navigation'); button.title = button.getAttribute('aria-label'); }
    // Pages with canvases (Orange Radar) listen for this to resize after the width animation.
    window.dispatchEvent(new CustomEvent('orange:sidebar', { detail: { collapsed } }));
  }

  document.addEventListener('DOMContentLoaded', () => {
    const sidebar = document.querySelector('.ws-sidebar');
    if (!sidebar) return;
    // Icon-only navigation keeps every destination reachable: each link gets its visible text as tooltip.
    sidebar.querySelectorAll('.ws-nav-item').forEach(a => { if (!a.title) a.title = a.textContent.trim(); });
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'ws-sidebar-collapse';
    button.setAttribute('aria-controls', sidebar.id || 'workspace-navigation');
    button.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M15 6l-6 6 6 6"/></svg>';
    sidebar.append(button);
    set(html.classList.contains('ws-sidebar-collapsed'), button);
    button.addEventListener('click', () => set(!html.classList.contains('ws-sidebar-collapsed'), button));
    document.addEventListener('keydown', e => { if (e.key === '[' && e.ctrlKey) { e.preventDefault(); button.click(); } });
  });
})();
