# Runtime asset plan (not yet produced)

| Asset | Deliverable | Nodes / layers | Acceptance |
|---|---|---|---|
| paper-outline | SVG or procedural vectors | sheet, crease, registration mark, card outline, envelope, sleeve | editable paths; shared coordinate system |
| paper-base | GLB or procedural mesh + source | root, base, edge, card, sleeve hinge, pocket | projected silhouette aligns with 2D; valid pivots |
| building-kit | GLB or procedural mesh + source | wallFrontHinge, wallSideHinge, roofHinge, signAnchor | hinges from flat rest, no popping geometry |
| category-kits | modular GLBs / geometry | IT & Electronics, Office Supplies, Furniture & Fixtures, Construction & Maintenance, Transport & Logistics, Professional Services, Medical & Safety, Food & Hospitality, Cleaning & Facilities, Telecommunications, Other | 3 bespoke hero kits (IT, Construction, Telecom) and recognizable modular parts for all remaining categories; no mislabeled categories |
| portrait-kit | editable mesh + SVG mask | face facets, hinges, cameraPlane, card, identityPocket | real folded geometry with camera projection alignment |
| legal-documents | SVG / mesh | registration, tax, licenses, tabs | readable silhouette, correct placement |
| signal-core | GLB or procedural mesh | coreBase, SIM contactPads, circuitPlate, apertureSegments, cableAnchor | physical assembly, no floating UI stickers |
| fiber-path | procedural curve / TubeGeometry | cable sheath, exposed strands, pulse path | continuous curve, pulse travels along cable only |
| fingerprint | SVG or engraved mesh | fingerprint motif | disabled accessible control outside canvas |

Store original `.blend` files or geometry generator source, named animation clips, export script/notes, and license/provenance metadata. Do not use generated reference PNGs as production backdrops, texture the whole scene from a screenshot, or claim a PNG is a 3D model. Do not create empty GLB placeholders. Use legitimate local authoring (Blender or procedural Three.js). If Blender is unavailable, create actual procedural hinge meshes, or report the blocker and stop at the proof gate.

Suggested performance budget to validate on target device: first visible scene <= 1 MB compressed assets, deferred main assets <= 3 MB compressed total initial route, capped pixel ratio <= 1.75, aim for 60fps on representative desktop and 30fps minimum on representative mobile; profile, do not claim benchmark results without measurement. Use lazy loading, dispose materials/geometries/streams, pause offscreen, static endpoint fallback if WebGL fails. These are design targets, not measured performance.
