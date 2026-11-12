"use strict";
(() => {
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const elements = [...document.querySelectorAll('.public-reveal')];
  if (!elements.length || reduced.matches || !('IntersectionObserver' in window)) return;
  const observer = new IntersectionObserver(entries => {
    entries.forEach(entry => {
      if (entry.isIntersecting) { entry.target.classList.add('is-revealed'); observer.unobserve(entry.target); }
    });
  }, { threshold: .08, rootMargin: '0px 0px 40px 0px' });
  elements.forEach(node => { node.classList.add('reveal-enabled'); observer.observe(node); });
  function showAll() { elements.forEach(node => node.classList.add('is-revealed')); observer.disconnect(); }
  reduced.addEventListener('change', showAll, { once: true });
  document.addEventListener('focusin', event => event.target.closest('.public-reveal')?.classList.add('is-revealed'));
  // Preserve content if the page is restored from the back-forward cache.
  addEventListener('pageshow', event => { if (event.persisted) showAll(); });
})();
