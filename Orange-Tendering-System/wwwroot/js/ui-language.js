// Top-bar language selector: one choice drives the interface language (cookie read by the server) and voice search.
// Pages that render both languages (body[data-bilingual="true"]) reload so the server returns Arabic RTL or English LTR.
(() => {
  const select = document.querySelector('[data-ui-language]');
  if (!select) return;
  const match = document.cookie.match(/(?:^|;\s*)orange-lang=(ar|en)(?:;|$)/);
  const current = match ? match[1] : (document.documentElement.lang === 'ar' ? 'ar' : 'en');
  select.value = current === 'ar' ? 'ar-JO' : 'en-US';
  try { localStorage.setItem('orange-search-language', select.value); } catch { /* voice search falls back to the select value */ }
  select.addEventListener('change', () => {
    const next = select.value.startsWith('ar') ? 'ar' : 'en';
    document.cookie = `orange-lang=${next}; path=/; max-age=31536000; samesite=lax${location.protocol === 'https:' ? '; secure' : ''}`;
    try { localStorage.setItem('orange-search-language', select.value); } catch { /* optional */ }
    if (document.body.dataset.bilingual === 'true') location.reload();
  });
})();
