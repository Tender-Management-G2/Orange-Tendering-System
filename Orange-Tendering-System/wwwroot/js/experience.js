"use strict";
(() => {
  const root = document.documentElement;
  const toggle = document.querySelector("#theme-toggle");
  const system = matchMedia("(prefers-color-scheme: light)");
  let explicit = false;
  try {
    explicit = ["light", "dark"].includes(
      localStorage.getItem("orange-ui-theme"),
    );
  } catch (_) {}
  function apply(theme, announce = false) {
    root.dataset.theme = theme;
    root.style.colorScheme = theme;
    toggle.setAttribute(
      "aria-label",
      `Switch to ${theme === "dark" ? "light" : "dark"} mode`,
    );
    toggle.setAttribute("aria-pressed", String(theme === "light"));
    toggle.title = theme === "dark" ? "Light mode" : "Dark mode";
    document.querySelector('meta[name="theme-color"]').content =
      theme === "light" ? "#f7f7f4" : "#101010";
    if (announce)
      document.querySelector("#theme-status").textContent =
        `${theme === "light" ? "Light" : "Dark"} mode enabled`;
  }
  apply(root.dataset.theme);
  requestAnimationFrame(() => root.classList.add("theme-ready"));
  toggle.addEventListener("click", () => {
    const theme = root.dataset.theme === "dark" ? "light" : "dark";
    explicit = true;
    try {
      localStorage.setItem("orange-ui-theme", theme);
    } catch (_) {}
    apply(theme, true);
  });
  system.addEventListener("change", (event) => {
    if (!explicit) apply(event.matches ? "light" : "dark");
  });
  addEventListener("storage", (event) => {
    if (event.key !== "orange-ui-theme") return;
    explicit = ["light", "dark"].includes(event.newValue);
    apply(explicit ? event.newValue : system.matches ? "light" : "dark");
  });
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
