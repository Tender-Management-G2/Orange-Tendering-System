// Optional UI utility. Attach only to an analyser connected by the EXISTING audio pipeline.
// Does not start microphone, create audio context, route audio or record anything.
export function attachAudioEnvelope(analyser, onLevel, { smoothing=0.22, gain=3 }={}) {
  const samples=new Float32Array(analyser.fftSize);
  let level=0, raf=0, running=true;
  function draw(){
    if(!running)return;
    analyser.getFloatTimeDomainData(samples);
    let sum=0;for(const x of samples)sum+=x*x;
    const rms=Math.sqrt(sum/samples.length);
    const target=Math.max(0,Math.min(1,rms*gain));
    level+= (target-level)*smoothing;
    onLevel(level);raf=requestAnimationFrame(draw);
  }
  raf=requestAnimationFrame(draw);
  return ()=>{running=false;cancelAnimationFrame(raf);onLevel(0);};
}
// Caller stops on idle/offscreen/hidden/end and respects reduced-motion. Avoid duplicate attach.
