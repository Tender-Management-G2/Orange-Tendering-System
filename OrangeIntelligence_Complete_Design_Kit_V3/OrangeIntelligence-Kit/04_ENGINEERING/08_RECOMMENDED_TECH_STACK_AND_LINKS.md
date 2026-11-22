# Small, pragmatic toolset — not a shopping list

Existing baseline: ASP.NET Core MVC / EF Core / SQL Server; local Ollama + Qwen3.5:4b + qwen3-embedding:0.6b; Whisper (speech-to-text); Piper (Arabic/English TTS); ECharts (already in Radar in previous report). New visual candidate: `3d-force-graph` + `three.js` for spherical scene. Optional GSAP only if transitions require it. Use browser Web Audio API for voice visualizations. Do NOT automatically install all libraries; evaluate compatibility and license first and pin only necessary versions.

Official/public docs checked 2026-11-21:
- 3D Force Graph API: https://github.com/vasturiano/3d-force-graph/blob/master/README.md
- 3D Force Graph demos: https://vasturiano.github.io/3d-force-graph/
- Three.js renderer and animation loop: https://threejs.org/docs/pages/WebGLRenderer
- Apache ECharts: https://echarts.apache.org/en/ and https://echarts.apache.org/en/api
- GSAP official GitHub: https://github.com/greensock/GSAP
- Web Audio API AnalyserNode: https://developer.mozilla.org/en-US/docs/Web/API/AnalyserNode
- Ollama REST API: https://docs.ollama.com/api
- OWASP LLM Top 10: https://genai.owasp.org/llm-top-10/
- Claude Code Skills: https://code.claude.com/docs/en/skills

Local model files, binary libraries and font files **are not bundled**. References linked, not blindly copied, to avoid license/version drift. The original reference images are generated design concepts rather than product-library screenshots.
