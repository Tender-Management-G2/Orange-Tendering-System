// Sourcing Officer dashboard: the AI Assistant card opens the existing chat panel (_AiChatWidget / ai-chat.js).
(() => {
  const card = document.querySelector('[data-open-ai-chat]');
  const chat = document.querySelector('[data-ai-chat]');
  if (!card || !chat) { if (card) card.hidden = true; return; }
  const fab = chat.querySelector('.ai-fab');
  const panel = chat.querySelector('.ai-panel');
  card.setAttribute('aria-expanded', 'false');
  let openedFromCard = false;
  card.addEventListener('click', () => {
    if (!panel.hidden) { panel.querySelector('textarea')?.focus(); return; }
    openedFromCard = true;
    fab.click();
  });
  // Keep the card's state in sync and return focus to it when the chat closes.
  new MutationObserver(() => {
    const open = !panel.hidden;
    card.setAttribute('aria-expanded', String(open));
    if (!open && openedFromCard) { openedFromCard = false; requestAnimationFrame(() => card.focus()); }
  }).observe(panel, { attributes: true, attributeFilter: ['hidden'] });
})();
