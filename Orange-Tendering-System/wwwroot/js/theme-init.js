// Synchronous, before styles: apply the preference before the first paint.
(() => {
  const system = matchMedia("(prefers-color-scheme: light)");
  let saved = null;
  try {
    saved = localStorage.getItem("orange-ui-theme");
  } catch (_) {}
  const theme =
    saved === "light" || saved === "dark"
      ? saved
      : system.matches
        ? "light"
        : "dark";
  document.documentElement.dataset.theme = theme;
  document.documentElement.style.colorScheme = theme;
  document.querySelector('meta[name="theme-color"]').content =
    theme === "light" ? "#f7f7f4" : "#101010";
})();
