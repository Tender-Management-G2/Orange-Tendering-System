# Our Team scene — character integration

`wwwroot/js/team-scene.js` renders the /team courtyard. Every frame is a pure function of the
scroll progress of `#tx` (no clock, no autoplay): stopping the scroll freezes it, scrolling back reverses it.

## Adding the character
1. Put a rigged, animated human at `wwwroot/models/team-character.glb` (glTF binary, Y-up, facing +Z,
   in-place clips with no root motion). The view only references it when the file exists.
2. The clips are matched by name (`CHARACTER.clips` in team-scene.js):
   - `walk`: a walk cycle, synced to distance (`CHARACTER.stride` metres per cycle)
   - `sit`: stand-to-sit
   - `sitIdle`: seated idle (optional)
   - `react`: head-hit or surprised reaction while seated
   - `lookUp`: seated, looking up
3. The model is scaled to `CHARACTER.height`. The falling orange targets the first bone named `*head*`
   at the moment of contact (scroll 55%).

Clip time is set from scroll (`action.time`) and the mixer is evaluated with a zero delta, so scrubbing is exact in both directions.
