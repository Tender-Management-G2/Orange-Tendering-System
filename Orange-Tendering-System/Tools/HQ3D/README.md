# Orange HQ — 3D reconstruction

`reference.webp` is the approved design image. The homepage building is **not** made from it: it is a
procedural Three.js reconstruction in `wwwroot/js/home-hq3d.js` (real meshes — slabs, curtain-wall glass,
mullions, interiors, roof, podium, signage, landscaping), modelled on this reference.

Assembly components (final transforms verified by `window.__hq3.seams()`):
podium (static) · f1–f4 (4.2 m) · top floor (5 m) · roof · entrance canopy.
No Blender/GLB pipeline was available on the build machine; geometry is generated at runtime.
