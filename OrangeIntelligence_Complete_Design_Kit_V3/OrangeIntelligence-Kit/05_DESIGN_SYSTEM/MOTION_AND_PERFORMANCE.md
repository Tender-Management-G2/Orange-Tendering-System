# Motion orchestration, real meaning, lower GPU cost

**Default scene:** slow camera orbit (~40–70 seconds/revolution), no full-speed simulation perpetual chaos. Nodes have tiny bounded float/relaxation; positions remain interpretable. Separate camera orbit from graph layout stability. Auto orbit stops on hover/drag/selection, resumes after inactivity only if enabled by user. Prevent label drifting across unrelated nodes.

Transitions: enter focus ~500–900ms, group rearrange ~450–800ms, contextual inspector ~200–320ms, chart data change ~250–450ms, evidence highlighting ~160–250ms per edge. Timings are guidance, not fake performance claims.

GPU budget: user has an RTX 3070 Laptop (8GB VRAM), but browser competes with Ollama and Whisper. Test GPU contention, cap pixel ratio if necessary, pause rendering offscreen/hidden tabs, dispose 3D resources on route change, avoid multiple WebGL contexts, avoid hundreds of expensive text textures. With 20–50 suppliers, optimize **legibility**, not extreme scalability. Bench real FPS and input response, don't make unrealistic promises.

Use `prefers-reduced-motion`: stop automatic orbit and pulsing, keep deliberate short focus animations. Provide Pause/Live visible control. Keyboard accessible alternate actions. Voice visualizer maps RMS/frequency amplitude to controlled deformation, and never invents mic levels.

Reference: Three.js/WebGLRenderer setAnimationLoop, 3D Force Graph APIs, Web Audio AnalyserNode; see library-links below.
