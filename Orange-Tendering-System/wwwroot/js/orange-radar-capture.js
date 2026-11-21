// ORANGE RADAR microphone capture (AudioWorklet). Posts mono Float32 frames and their RMS level to the page;
// voice-activity detection, resampling to 16 kHz and WAV encoding happen in orange-radar-voice.js. Nothing is stored.
class RadarCaptureProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.buffer = new Float32Array(2048);
    this.length = 0;
  }

  process(inputs) {
    const channel = inputs[0] && inputs[0][0];
    if (!channel) return true;
    this.buffer.set(channel, this.length);
    this.length += channel.length;
    if (this.length >= 1024) {
      const frame = this.buffer.slice(0, this.length);
      let sum = 0;
      for (let i = 0; i < frame.length; i++) sum += frame[i] * frame[i];
      this.port.postMessage({ frame, rms: Math.sqrt(sum / frame.length) }, [frame.buffer]);
      this.length = 0;
    }
    return true;
  }
}

registerProcessor('radar-capture', RadarCaptureProcessor);
