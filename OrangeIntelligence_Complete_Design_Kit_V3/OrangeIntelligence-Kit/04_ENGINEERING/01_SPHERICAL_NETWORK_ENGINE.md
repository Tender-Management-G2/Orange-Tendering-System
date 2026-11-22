# Supplier Universe — 3D network architecture

## Recommended renderer, subject to inspect
Prototype with **3d-force-graph + Three.js**, because it offers node object/label, camera control and force simulation. Existing custom canvas engine may be reusable for 2D fallback. Do not reflexively replace working code until a visual spike at the target dataset shows measurable improvement. Use existing npm/libman/CDN policy; pin versions after checking compatibility and license, not from this kit's assumptions.

## Visual model
Nodes must be **actual authorized suppliers**; optional tender/evidence nodes only in relevant mode. Use one physical 3D graph across Universe/Category/Competition, change transforms/mappings cleanly. Category membership is a **classification**, not itself a suspicious link. Category color, engagement size with explicit legend (or uniform size), selection ring distinct from review signals. Edge types are encoded with stroke/texture/color + accessible explanations. Zero-involvement suppliers remain reachable and count in the correct lifetime total.

The sphere should feel like a planet in motion but **not have a world map or false geography**. Default: orbital 3D projection of the dataset plus a very slow controlled camera orbit. Camera orbit can loop without constant chaotic node re-layout. Clamp navigation rotation, perspective, zoom and label occlusion. On hover, slow; on selection, **pause orbit**, smoothly move camera, center node, reveal a right inspector. On deselection return to a stable home view; `2D`, `Table` as fallback.

Avoid a central artificial `All Suppliers` entity. If a central visual decorative orb exists, it must be noninteractive and visually distinguished from data nodes. Do not infer a relationship from proximity or category color.

## Link visibility and edge clutter
At global overview default: zero or aggregated faint links with specific visible active relationship type. After selecting category/supplier/relationship type: show only supported linked edges. For exact shared documents, use document-hub/forensic view (suppliers→document), not N^2 all-to-all clutter. For bid co-participation, relationship strength = number of **distinct closed rounds** both actually offered in; do not treat invitations alone as submitted offers. Link click -> exact explanation, method, temporal scope, evidence source IDs, alternative explanation.

## Graph state and persistence
State machine: `universe`, `category`, `competition`, `tender-context`, `evidence`, `supplier-focus`, `path-trace`, `compare`. `selectedSupplierIds <= 5`; `activeCategory`, `relationshipType`, `dateWindow`, `selectedTender`, `motionEnabled`, `viewMode 3d|2d|table`. State synchronized with filters and AI tool output. No AI action bypasses server authorization.

## 3D usability & performance
- Separate simulation from camera animation; avoid infinite-force physics causing labels to migrate.
- Cache text labels or use DOM overlays for the selected few, not 50 full-resolution textures at once.
- Use raycasting for pointer selection; keyboard-accessible list focuses exact entities.
- Pick node size with explicit metric; color contrast with sphere; keep readable labels on chosen/hovered/high-priority nodes, full names in inspector and table.
- Stop rendering when hidden/other tab, free GPU objects, limit pixel ratio, test coexistence with local Ollama/Whisper.
- `prefers-reduced-motion`: static default with explicit manual rotation.
- Graph image is a concept, NOT a list of real edges or spatial positioning instructions.

## Non-trivial visual acceptance
At 1440 desktop, selected node must be readable **without browser zoom**, focused links must be visually disentangled, background labels do not obscure nodes, empty relation results are explained, paused mode is genuinely paused. User can find every supplier by search/table even when occluded behind sphere.
