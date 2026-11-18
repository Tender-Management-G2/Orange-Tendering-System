# G2 revision direction — shot first

Status: G2 rejected by the reviewer. This file directs a new visual proof and does not authorize G3.

## Research translated into design decisions

- Professional product films use close material studies, deliberate shifts between macro and hero views, and one visual idea per shot. [ManvsMachine's Paper Pro campaign](https://mvsm.com/project/paper-pro) is the material and framing benchmark; its output is a reference, never a runtime backdrop. Its [Affinity film](https://mvsm.com/project/canva-affinity) also demonstrates coordinated 2D, 3D and type instead of independent visual layers.
- Pop-up structures must account for foldability and collisions through the full motion, not only at the endpoint. The [V-style pop-up geometry study](https://cg.cs.tsinghua.edu.cn/papers/vpopup.pdf) and [kinematic survey of paper mechanisms](https://fab.cba.mit.edu/classes/865.18/discrete/folding/KinematicPaperMechanisms.pdf) inform the crease, hinge and clearance review. Our architecture remains a directed paper sculpture, not a claim of a manufactured fold pattern.
- Camera position and target need separate authored paths so attention can move from card to architecture to aperture. [Maxon's camera guidance](https://help.maxon.net/c4d/s22/us/html/OCAMERA.html) describes distinct camera and target paths with shaped motion curves. [Disney Animation](https://disneyanimation.com/process/animation/) identifies clear staging, anticipation, follow-through and secondary action as animation principles; the revision applies those to paper and lens motion.
- An actual [iris diaphragm patent](https://patents.google.com/patent/US3685418A/en) uses overlapping leaves with pivots and a controlling ring. The paper iris will simplify that linkage into visible common ring rotation and individually hinged blades; the Signal aperture will echo its radial rhythm in metal.
- Authored Blender GLB animation was investigated. Blender is not installed on this machine. Its [glTF exporter](https://docs.blender.org/manual/en/5.4/addons/scene_gltf2.html) supports object, bone and shape-key animation, but replacing the existing interactive, reversible hinge system with a baked clip now would slow iteration on these three shots. The revision will author curves, pivots and material geometry explicitly in code, then assess the rendered result. Blender remains an option if this authored geometry still falls short.

## Face concept evaluation

| Direction | Strength | Current concern | Decision |
|---|---|---|---|
| A — Paper Iris | Shares a visual grammar with the login aperture; circular opening gives the future camera interface a strong frame; blades can be inspected for real hinge motion. | Must avoid flat pie wedges and an arbitrary black circle. | **Selected.** Use layered, curved paper leaves, a common control ring, scored pivots and a recessed abstract preview. |
| B — Portrait Atelier | Strong parallax and possible editorial framing. | Multiple independent layers risk the disconnected motion criticized in the first G2. | Keep as fallback if the iris cannot meet the physical and visual review. |
| C — Paper Lens | Natural link to camera optics and a memorable macro move. | The small camera object could overtake the identity/dossier story. | Reserve lens detailing for inside the paper iris. |

The previous polygonal human head is rejected and will be removed. The lab will show only an abstract circular optical preview and an explicit no-camera label. A later production camera plane would require real permission and stream readiness; this G2 scene has neither.

## Priority shot keyframes

All progress values below are editable timeline positions in the standalone lab. Moving parts are attached to stable named pivots and are reversible. Camera motion is authored, not an idle orbit.

### A — Identity card and sleeve (0.03–0.39)

| Progress | Object choreography | Camera intention |
|---|---|---|
| 0.03 | Planar paper, scored cutout and empty card silhouette. | Clean overhead composition. |
| 0.09 | Card perimeter draws while its paper stays flush with the source sheet. | Slow dolly to the card area. |
| 0.15 | Identity rules and anonymous portrait mark draw in sequence. | Hold near-macro so the marks are legible. |
| 0.21 | Side folds prepare outward a few degrees; the card lifts subtly from its bottom crease. | A restrained three-quarter parallax reveals paper thickness. |
| 0.27 | Lower triangular fold rises, then side leaves fold inward with a short offset. | Target tracks the closing seam. |
| 0.34 | Card settles behind the leaves; final closure and Orange seal land with a small follow-through. | Camera holds until all contact and shadows settle. |
| 0.39 | Sleeve rests as one connected paper object. | Begin pulling back toward the full sheet. |

### B — Architecture from the same sheet (0.43–0.82)

| Progress | Object choreography | Camera intention |
|---|---|---|
| 0.43 | Still flat; wall and roof cut lines visible. | Overhead silhouette still matches the source sheet. |
| 0.53 | Creases flex a few degrees in anticipation; no wall is swapped in. | Camera lowers into a composed three-quarter view. |
| 0.62 | Front, back and side walls rise in a linked, slightly offset action. | Slow push toward the entrance, keeping all hinges visible. |
| 0.70 | Roof clears the walls and closes over them; entrance lighting becomes visible through real openings. | Camera follows the roofline and settles on the facade. |
| 0.78 | Landscape and category details follow through. | Restrained 10–15° orbit reveals depth without circling aimlessly. |
| 0.82 | The miniature rests on the original foundation. | Hold a hero composition before shifting attention to enrollment. |

### C — Paper Iris enrollment study (0.82–1.00)

| Progress | Object choreography | Camera intention |
|---|---|---|
| 0.82 | Circular die-cut and ring lie flush within the right-front dossier area. | Leave the building hero pose deliberately. |
| 0.88 | The connected iris leaf lifts; outer ring and scored pivots gain parallax. | Dolly toward the iris and roll into a near-frontal portrait framing. |
| 0.93 | One control ring turns; eight overlapping curved leaves open in a short spiral cascade. | Hold on the pivot and aperture so the mechanics read. |
| 0.98 | Aperture frames a recessed, abstract optical preview with quiet Orange registration details. | Near-frontal macro, centered and uncropped. |
| 1.00 | A controlled settle; no verification or success symbol. | Hold the camera interface geometry for inspection. |

Reverse playback demonstrates closure. A future confirmed enrollment flow would turn the closed paper iris into a personal card returning to the dossier; this lab will not imply that enrollment has succeeded.

## Reference assets

The three generated material studies in `references/` are reference-only and never appear as scene backgrounds. They establish the target for paper fiber, edge thickness, contact shadow, layered cut geometry and restrained Orange accents. The runtime shot geometry remains editable in JavaScript.

## Acceptance checks for this revision

Record each priority shot separately at normal and reduced playback speed. Review frames between the endpoints, check the card and sleeve clearances, inspect architectural wall/roof contact during the fold, and confirm the iris stays centered at desktop and mobile sizes. Keep before and after captures side by side. G2 remains rejected until the user approves the revised motion.
