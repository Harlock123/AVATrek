#!/usr/bin/env python3
"""Turns a clean speech recording into something closer to a 1983 arcade speech chip.

The voice is cut down to 8 kHz, band-limited like a small cabinet speaker, given a faint
ring-modulated edge and quantised to 6 bits, then written back out at the game's 22050 Hz.
Usage: crunch_voice.py in.wav out.wav
"""
import math
import struct
import sys
import wave

src, dst = sys.argv[1], sys.argv[2]

with wave.open(src, "rb") as w:
    rate = w.getframerate()
    raw = w.readframes(w.getnframes())
samples = [s / 32768 for s in struct.unpack("<%dh" % (len(raw) // 2), raw)]

OUT_RATE = 22050
CHIP_RATE = 8000
LEVELS = 32  # 6-bit signed

# Sample-and-hold down to the chip's rate (this is what gives the gritty top end) and back up.
step = rate / CHIP_RATE
held, out = 0.0, []
next_sample = 0.0
for i, s in enumerate(samples):
    if i >= next_sample:
        held = round(s * LEVELS) / LEVELS
        next_sample += step
    out.append(held)

# A one-pole high-pass and low-pass: no deep bass, no sparkle, like a small speaker in a cabinet.
hp, lp, prev = 0.0, 0.0, 0.0
filtered = []
for s in out:
    hp = 0.97 * (hp + s - prev)
    prev = s
    lp += 0.45 * (hp - lp)
    filtered.append(lp)

# A touch of ring modulation for the machine edge, then trim the silence at either end.
edged = [s * (0.85 + 0.15 * math.sin(2 * math.pi * 60 * i / rate)) for i, s in enumerate(filtered)]
peak = max(1e-9, max(abs(s) for s in edged))
start = next((i for i, s in enumerate(edged) if abs(s) > peak * 0.02), 0)
end = len(edged) - next((i for i, s in enumerate(reversed(edged)) if abs(s) > peak * 0.02), 0)
edged = edged[max(0, start - 200):min(len(edged), end + 400)]

# Resample to the game's rate (linear interpolation is plenty for an already lo-fi voice).
ratio = rate / OUT_RATE
resampled = []
for k in range(int(len(edged) / ratio)):
    x = k * ratio
    i = int(x)
    f = x - i
    a = edged[i]
    b = edged[i + 1] if i + 1 < len(edged) else a
    resampled.append(a + (b - a) * f)

with wave.open(dst, "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(OUT_RATE)
    w.writeframes(b"".join(struct.pack("<h", int(s / peak * 0.8 * 32767)) for s in resampled))
