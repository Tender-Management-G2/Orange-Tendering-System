# Visual language and typographic hierarchy

Primary dark palette: background `#0B1320`, surface `#122338`, surface elevated `#182C43`, border `#2E4259`, Orange `#FF7900`, text `#ECF3FB`, muted `#A9BACC`, positive `#55C9A3`, information `#57A9DD`, warning `#F1B35A`. Light mode is **a complete alternate palette**, not a color filter.

Type: English `Inter` / system sans; Arabic `IBM Plex Sans Arabic` / locally available Arabic fallback. Do not package proprietary font binaries. Typical desktop targets: H1 34–44px, H2 24–28px, KPI 34–46px, readable body 16px, graph labels on display 14–16px, micro metadata minimum 12px only when secondary. Ensure 200% browser zoom and high contrast readability.

Rhythm: 8px base spacing, 16px card padding min, 24–32px section gap, 48px touch controls, radius 14–18px, shadows subtle. Keep visual-to-label ratio balanced. Main globe at least ~55–65% available horizontal width at 1440+, depending inspector visibility.

Rendering tokens in `tokens.css` and `tokens.json` are starter values. Claude should adapt to existing app's classes and not blindly overwrite shared global CSS.
