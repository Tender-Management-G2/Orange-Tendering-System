"use strict";
(() => {
  // Theme switching is owned by theme-init.js on every layout.
  // New cards are inserted by sections.js after the shared reveal observer starts.
  document
    .querySelectorAll(".benefit-card")
    .forEach((card) => typeof observer !== "undefined" && observer.observe(card));
})();

(() => {
  const menu = document.querySelector('.site-header .menu');
  const nav = document.querySelector('#public-navigation');
  if (!menu || !nav) return;
  const close = () => { nav.classList.remove('open'); menu.setAttribute('aria-expanded', 'false'); };
  menu.addEventListener('click', () => {
    const open = nav.classList.toggle('open');
    menu.setAttribute('aria-expanded', String(open));
  });
  nav.querySelectorAll('a').forEach(link => link.addEventListener('click', close));
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && nav.classList.contains('open')) { close(); menu.focus(); }
  });
  document.addEventListener('click', event => { if (!event.target.closest('.site-header')) close(); });
  matchMedia('(min-width: 1201px)').addEventListener('change', close);
})();
