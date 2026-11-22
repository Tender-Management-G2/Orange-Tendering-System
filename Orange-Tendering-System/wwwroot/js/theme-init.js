// One controller for public, Identity and all role layouts. Runs before CSS to avoid a theme flash.
(() => {
  if (window.orangeTheme) return;
  const root = document.documentElement;
  const scope = root.dataset.themeScope;
  const key = scope ? 'orange-ui-theme-' + scope : 'orange-ui-theme';
  const system = window.matchMedia?.('(prefers-color-scheme: light)');
  const valid = value => value === 'light' || value === 'dark';
  const read = () => { try { return localStorage.getItem(key); } catch { return null; } };
  let preference = read();
  const preferred = () => valid(preference) ? preference : !scope && system?.matches ? 'light' : 'dark';
  const controls = '#theme-toggle,.ws-theme-toggle,[data-theme-toggle]';
  function synchronize() {
    const light = root.dataset.theme === 'light';
    document.querySelectorAll(controls).forEach(button => {
      button.setAttribute('aria-pressed', String(light));
      button.setAttribute('aria-label', `Switch to ${light ? 'dark' : 'light'} mode`);
      button.title = light ? 'Dark mode' : 'Light mode';
      if (button.tagName === 'BUTTON') button.type = 'button';
    });
  }
  function apply(theme, announce = false) {
    const changed = root.dataset.theme !== theme;
    root.dataset.theme = theme;
    root.style.colorScheme = theme;
    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.content = theme === 'light' ? '#f7f7f4' : '#101010';
    synchronize();
    if (announce) document.querySelectorAll('#theme-status,#workspace-theme-status,[data-theme-status]').forEach(node => {
      node.textContent = `${theme === 'light' ? 'Light' : 'Dark'} mode enabled`;
    });
    if (changed) window.dispatchEvent(new CustomEvent('orange:themechange', { detail: { theme } }));
  }
  function set(theme) {
    if (!valid(theme)) return;
    preference = theme;
    try { localStorage.setItem(key, theme); } catch { /* The current page still switches when storage is unavailable. */ }
    apply(theme, true);
  }
  window.orangeTheme = Object.freeze({ set, reset() {
    preference = null;
    try { localStorage.removeItem(key); } catch { /* System preference remains usable. */ }
    apply(preferred(), true);
  }, get current() { return root.dataset.theme; } });
  apply(preferred());
  document.addEventListener('click', event => {
    const button = event.target.closest?.(controls);
    if (button && !button.disabled) { event.preventDefault(); set(root.dataset.theme === 'light' ? 'dark' : 'light'); }
  });
  const systemChanged = () => { if (!valid(preference)) apply(preferred()); };
  if (system?.addEventListener) system.addEventListener('change', systemChanged);
  else system?.addListener?.(systemChanged);
  window.addEventListener('storage', event => {
    if (event.key !== key && event.key !== null) return;
    preference = event.key === key ? event.newValue : read();
    apply(preferred());
  });
  const ready = () => { synchronize(); requestAnimationFrame(() => root.classList.add('theme-ready')); };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready, { once: true });
  else ready();
})();
