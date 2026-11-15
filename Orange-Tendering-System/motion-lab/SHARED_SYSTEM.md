# G2 shared visual system — Paper Genesis / Orange Signal

Status: isolated visual development. No production authentication contract is changed.

## Shared design tokens

| Token | Value | Use |
|---|---|---|
| Canvas | `#F8F5EF` | Both chapter backgrounds and initial empty illustration field |
| Paper | `#FFFBF3` | Paper surfaces, form panels, and optical inset highlights |
| Warm edge | `#D6C9B8` | Paper thickness, divider lines, soft contact shadows |
| Ink | `#242321` | Text and 1–2 px editorial drawing strokes |
| Orange | `#FF7900` | One restrained registration/signal marker and action accents |
| Satin graphite | `#252321` | Electronic component body only, never the whole Login page |
| Copper | `#C18C5A` | SIM contacts, circuit tracks, and optical hardware details |

The two chapters share a 58/42 illustration/form layout, the same type scale, field geometry, radius, line weight, shadow direction, key/fill light positions, 35° perspective lens, eased camera targets, and named scene roots. Their opening illustrations are empty at time zero; a thin line draws automatically before the form drives later geometry. On narrow screens, the illustration and form stack without a trapped form scroller. The development inspector remains separate from the form.

## Revised Registration shots

| Progress | Trigger | Movement |
|---|---|---|
| 0–0.14 | Page load, automatic | Blank macro field; the connected card/letter perimeter and fine fold line draw from zero. |
| 0.14–0.25 | Contact name entered | Card ink and cut edges develop; card begins to lift on its scored base. |
| 0.25–0.29 | Work email entered | Envelope fold detail is drawn on the same card. |
| 0.29–0.39 | Password entered | Bottom, side, and top sleeve leaves close in sequence. No verification claim. |
| 0.39–0.70 | Company name entered | Camera pulls back to the original sheet; walls rise and roof follows. |
| 0.70–0.83 | Category selected | Category kit folds out while the foundation remains fixed. |
| 0.83–1 | Explicit paper iris preview | Connected leaf rises and curved paper blades open. No camera or enrollment result. |

## Orange Signal shots

| Progress | Trigger | Movement |
|---|---|---|
| 0–0.12 | Page load, automatic | Blank ivory field; Orange and graphite technical lines trace the one compact module and curved cable. |
| 0.12–0.32 | Email field | Fine printed paths extend toward the module. Typed content never appears on the scene. |
| 0.32–0.55 | Password field | SIM contacts align on rails; layered hardware begins to gain depth. No credential result. |
| 0.55–0.78 | Explicit supplier demo gate | Compact hardware extrudes; the optical housing rises. Internal employee demo path does not show biometrics. |
| 0.78–0.94 | Supplier chooses Face demo | A pin-indexed metal iris opens toward an abstract lens. No camera permission or verification. Fingerprint remains disabled. |
| 0.94–1 | Separate post-auth *visual preview* control | One Orange pulse travels in the existing fiber path. This is a storyboard of a future server-authorized transition, never a login result. |

The final approved production event sequence remains unresolved because the supplier credential/biometric progression differs from the present implementation, and liveness protection is missing. The lab's explicit demo controls cannot grant access.

## Research and original reference

- [ETSI USIM conformance specification](https://www.etsi.org/deliver/etsi_TS/131100_131199/131122/14.00.00_60/ts_131122v140000p.pdf) anchors the contact-plate layout in a real physical contact system; the lab is an artistic interpretation, not a standards-compliant SIM.
- [Corning's optical-fiber explanation](https://www.corning.com/worldwide/en/innovation/the-glass-age/science-of-glass/how-it-works-optical-fiber.html) motivates a contained light pulse in a core and cladding, rather than unrelated glowing nodes.
- [Iris diaphragm linkage patent](https://patents.google.com/patent/US3631787A) informs the common-ring and blade-pivot motion. The G2 rig is visibly synchronized but is not a manufactured mechanism.
- `references/unified-signal-direction-v3.png` is an original generated raster **reference only**. Prompt set: a single warm ivory studio world comparing a paper enrollment iris and miniature office with a satin graphite SIM/optical module, copper contacts, a restrained Orange pulse, matching camera/light, no words or UI. Mode: new image generation, no edit. It is not a runtime backdrop or model texture.
