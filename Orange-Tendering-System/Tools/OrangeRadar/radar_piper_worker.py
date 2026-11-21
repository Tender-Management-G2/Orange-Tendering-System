"""ORANGE RADAR - persistent local Piper speech worker.

Started and stopped by the ASP.NET application (Services/Radar/RadarSpeech.cs). Loads the Arabic and English Piper voices once
and keeps them in memory, so each spoken sentence costs only synthesis time on the CPU.

Protocol (no network port): one JSON request per line on stdin
    {"text": "...", "voice": "ar" | "en", "length_scale": 1.0}
and one binary frame per request on stdout:
    1 byte kind ('W' = WAV audio, 'E' = UTF-8 error message) + 4-byte big-endian length + payload.
"READY" is written to stderr once both voices are loaded.
"""

import argparse
import io
import json
import struct
import sys
import wave

from piper import PiperVoice
from piper.config import SynthesisConfig


def frame(kind: bytes, payload: bytes) -> None:
    sys.stdout.buffer.write(kind + struct.pack(">I", len(payload)) + payload)
    sys.stdout.buffer.flush()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--ar", required=True, help="Arabic voice .onnx (config: same path + .json)")
    parser.add_argument("--en", required=True, help="English voice .onnx (config: same path + .json)")
    args = parser.parse_args()

    voices = {"ar": PiperVoice.load(args.ar), "en": PiperVoice.load(args.en)}
    # Warm both voices (first synthesis initialises espeak-ng and, for Arabic, the tashkeel diacritizer).
    for code, sample in (("ar", "مرحبا"), ("en", "Ready")):
        for _ in voices[code].synthesize(sample):
            pass
    print("READY", file=sys.stderr, flush=True)

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            request = json.loads(line)
            text = str(request.get("text", ""))[:600]
            voice = voices["ar" if request.get("voice") == "ar" else "en"]
            scale = float(request.get("length_scale") or 1.0)
            config = SynthesisConfig(length_scale=max(0.6, min(1.7, scale)))
            buffer = io.BytesIO()
            with wave.open(buffer, "wb") as wav:
                voice.synthesize_wav(text, wav, syn_config=config)
            frame(b"W", buffer.getvalue())
        except Exception as error:  # report and keep serving; the app decides whether to restart
            frame(b"E", str(error).encode("utf-8")[:2000])


if __name__ == "__main__":
    main()
