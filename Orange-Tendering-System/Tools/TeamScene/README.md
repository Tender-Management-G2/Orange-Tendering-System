# Our Team page — "The Minds Behind Orange"

`wwwroot/js/team-scene.js` renders /team: a dark Orange innovation room with a miniature Orange HQ.
The seven team cards (`wwwroot/data/team.json`) sit above it. Every state is a pure function of the
scroll progress of `#tx` (GSAP ScrollTrigger, no clock), so the page stops when scrolling stops and reverses exactly.

- 0.00–0.08 welcome
- 0.08–0.82 one stage per member: the card is enlarged, an Orange line is drawn to that member's
  building section (podium, levels 1–4, top floor, roof and sign), the section lights up, and the right panel
  shows the member's details
- 0.82–0.91 the camera pulls back and all seven lines connect
- 0.91–1.00 "Seven Minds. One Connected Vision."

## Team data
- Photos: `wwwroot/images/team/*.jpg` (640×800, cropped from the supplied files).
- Names come from the supplied photo filenames.
- Roles and contributions stay "to be confirmed" until the team supplies them. Edit `role` and
  `contributions` in team.json (separate several contributions with `;`) and set `placeholder` to false.
