/* Shared public navbar (_SiteNav) on the non-home pages: burger menu. The homepage wires the same markup in home-cinema.js. */
(function () {
  "use strict";
  var burger = document.querySelector("#hc-nav .hc-burger"), links = document.getElementById("hc-links");
  if (!burger || !links) return;
  function set(open) {
    links.classList.toggle("is-open", open);
    burger.setAttribute("aria-expanded", String(open));
    burger.setAttribute("aria-label", open ? "Close navigation" : "Open navigation");
  }
  burger.addEventListener("click", function () { set(burger.getAttribute("aria-expanded") !== "true"); });
  links.addEventListener("click", function (e) { if (e.target.closest("a")) set(false); });
  document.addEventListener("keydown", function (e) { if (e.key === "Escape") set(false); });
  window.addEventListener("resize", function () { if (window.innerWidth > 1180) set(false); });
})();
