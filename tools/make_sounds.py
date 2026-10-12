#!/usr/bin/env python3
"""Synthesizes the game's sound effects into ../Sounds as 16-bit mono WAV files.

Nothing is sampled from the original machine: every effect is built here from oscillators and noise,
so the sounds can be tweaked and regenerated. Run it from anywhere: python3 tools/make_sounds.py
(The spoken lines come from tools/make_voice.sh.)
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Sounds")
random.seed(1983)


def write(name, samples, volume=0.6):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def n(seconds):
    return int(RATE * seconds)


def env(i, total, attack=0.005, release=1.0):
    """Linear attack, then a decay shaped by `release` (higher = faster fade)."""
    t = i / RATE
    a = min(1.0, t / attack) if attack > 0 else 1.0
    return a * (1 - i / total) ** release


def sweep(seconds, f0, f1, wave_fn, release=1.0, attack=0.005):
    total, phase, out = n(seconds), 0.0, []
    for i in range(total):
        f = f0 + (f1 - f0) * i / total
        phase += f / RATE
        out.append(wave_fn(phase) * env(i, total, attack=attack, release=release))
    return out


def square(p):
    return 1.0 if (p % 1) < 0.5 else -1.0


def tri(p):
    return 4 * abs((p % 1) - 0.5) - 1


def saw(p):
    return 2 * (p % 1) - 1


def sine(p):
    return math.sin(2 * math.pi * p)


def noise(seconds, release=1.0, smooth=0.0, attack=0.002):
    total, last, out = n(seconds), 0.0, []
    for i in range(total):
        last = last * smooth + random.uniform(-1, 1) * (1 - smooth)
        out.append(last * env(i, total, attack=attack, release=release))
    return out


def mix(*tracks):
    length = max(len(t) for t in tracks)
    return [sum(t[i] for t in tracks if i < len(t)) for i in range(length)]


def scale(track, k):
    return [s * k for s in track]


def silence(seconds):
    return [0.0] * n(seconds)


os.makedirs(OUT, exist_ok=True)

# Phasers: a bright, buzzing beam with a fast warble, falling slightly in pitch.
total = n(0.32)
beam, p1, p2 = [], 0.0, 0.0
for i in range(total):
    t = i / RATE
    f = 1150 - 350 * i / total + 90 * math.sin(2 * math.pi * 38 * t)
    p1 += f / RATE
    p2 += f * 1.5 / RATE
    beam.append((0.6 * saw(p1) + 0.4 * square(p2)) * env(i, total, attack=0.004, release=0.8))
write("Phaser.wav", beam, 0.32)

# Photon torpedo launch: a rising, pulsing whoosh.
total = n(0.55)
launch, phase = [], 0.0
for i in range(total):
    t = i / RATE
    f = 220 + 700 * (i / total) ** 0.6
    phase += f / RATE
    pulse = 0.6 + 0.4 * math.sin(2 * math.pi * 22 * t)
    launch.append(tri(phase) * pulse * env(i, total, attack=0.01, release=1.4))
write("Torpedo.wav", mix(launch, scale(noise(0.55, release=2, smooth=0.5), 0.3)), 0.45)

# A torpedo bursting.
write("TorpedoBlast.wav", mix(noise(0.7, release=2.2, smooth=0.45), scale(sweep(0.7, 160, 40, tri, release=1.5), 0.8)), 0.6)

# Klingon disruptor: a harsh, falling zap.
write("Disruptor.wav", mix(sweep(0.24, 700, 160, square, release=1.3), scale(noise(0.24, release=2.5, smooth=0.2), 0.4)), 0.32)

# A Klingon cruiser destroyed.
write("KlingonBoom.wav", mix(noise(1.1, release=1.8, smooth=0.7), scale(sweep(1.1, 120, 35, tri, release=1.4), 0.8)), 0.7)

# A Klingon dropping out of warp into the sector: a shimmering chord that swells and fades.
total = n(0.6)
shimmer = []
for i in range(total):
    t = i / RATE
    s = sum(math.sin(2 * math.pi * f * t) for f in (440, 554, 659, 880 + 40 * math.sin(2 * math.pi * 7 * t)))
    shimmer.append(s * math.sin(math.pi * i / total) ** 2)
write("WarpIn.wav", shimmer, 0.22)

# The shields taking a hit: a metallic clang over a thump.
total = n(0.5)
clang = []
for i in range(total):
    t = i / RATE
    s = (math.sin(2 * math.pi * 211 * t) + 0.7 * math.sin(2 * math.pi * 347 * t)
         + 0.5 * math.sin(2 * math.pi * 563 * t) + 0.3 * math.sin(2 * math.pi * 911 * t))
    clang.append(s * math.exp(-t * 9))
write("ShieldHit.wav", mix(clang, scale(noise(0.25, release=3, smooth=0.8), 1.2)), 0.6)

# An anti-matter saucer arriving or latching on: a wobbling warble.
total = n(0.5)
wobble, phase = [], 0.0
for i in range(total):
    t = i / RATE
    f = 520 + 260 * math.sin(2 * math.pi * 17 * t)
    phase += f / RATE
    wobble.append(sine(phase) * env(i, total, attack=0.02, release=0.8))
write("SaucerAttach.wav", wobble, 0.35)

# A saucer popping.
write("SaucerBoom.wav", mix(noise(0.28, release=2.5, smooth=0.3), sweep(0.28, 900, 200, square, release=2)), 0.45)

# Nomad sowing a mine: a short double blip.
write("MineLaid.wav", sweep(0.06, 900, 1300, tri, release=0.5) + silence(0.04) + sweep(0.06, 1300, 900, tri, release=0.5), 0.25)

# A mine going off.
write("MineBoom.wav", mix(noise(0.55, release=2, smooth=0.55), scale(sweep(0.55, 200, 60, square, release=2), 0.5)), 0.6)

# A hit that didn't finish something off: a dull clunk.
write("NomadHit.wav", mix(sweep(0.12, 260, 140, tri, release=1.5), scale(noise(0.08, release=3, smooth=0.5), 0.5)), 0.45)

# Nomad or a starbase destroyed: a long, deep rumble.
write("BigBoom.wav", mix(noise(2.4, release=1.5, smooth=0.85), scale(sweep(2.4, 90, 25, tri, release=1.3), 0.9)), 0.85)

# The Enterprise destroyed: a crackling blast, then a long roll.
write("ShipExplosion.wav", mix(noise(1.8, release=1.6, smooth=0.6), scale(noise(0.3, release=3, smooth=0.0), 0.6),
                               scale(sweep(1.8, 140, 30, saw, release=1.6), 0.5)), 0.85)

# Warp engaged: a fast rising surge.
total = n(0.9)
surge, phase = [], 0.0
for i in range(total):
    f = 120 * (14 ** (i / total))
    phase += f / RATE
    surge.append((0.6 * tri(phase) + 0.4 * sine(phase * 2.01)) * env(i, total, attack=0.05, release=1.1))
write("WarpEngage.wav", mix(surge, scale(noise(0.9, release=1.2, smooth=0.3), 0.25)), 0.45)

# At warp: a high, steady shimmer, flat so it loops cleanly.
total = n(1.0)
hum = []
for i in range(total):
    t = i / RATE
    # Frequencies are whole numbers of cycles per second, so a one-second loop joins without a click.
    s = (math.sin(2 * math.pi * 330 * t) + 0.6 * math.sin(2 * math.pi * 331 * t + 1)
         + 0.4 * math.sin(2 * math.pi * 495 * t) + 0.25 * math.sin(2 * math.pi * 660 * t))
    hum.append(s * (0.8 + 0.2 * math.sin(2 * math.pi * 6 * t)))
write("Warp.wav", hum, 0.16)

# Impulse engines: a soft low rumble, flat so it loops cleanly.
total = n(1.0)
last, rumble = 0.0, []
for i in range(total):
    t = i / RATE
    last = last * 0.93 + random.uniform(-1, 1) * 0.07
    rumble.append(last * 2.5 + 0.35 * math.sin(2 * math.pi * 55 * t) + 0.2 * math.sin(2 * math.pi * 110 * t))
write("Impulse.wav", rumble, 0.3)

# Docking: a rising four-note chime.
chime = []
for f in (392, 523, 659, 784):
    chime += sweep(0.16, f, f, sine, release=0.4, attack=0.01)
chime += sweep(0.5, 1047, 1047, sine, release=1.2, attack=0.01)
write("Docking.wav", mix(chime, scale(sweep(1.14, 196, 196, tri, release=0.6), 0.25)), 0.4)

# Extra ship: a bright arpeggio.
notes = []
for f in (523, 659, 784, 1047, 784, 1047):
    notes += sweep(0.08, f, f, square, release=0.3)
write("ExtraShip.wav", notes, 0.3)

# Red alert: two rising klaxon whoops.
whoop = sweep(0.55, 380, 820, square, release=0.2, attack=0.02)
write("RedAlert.wav", mix(whoop + silence(0.12) + whoop), 0.28)

# Round complete: a short rising fanfare (an original figure, not anything from the show).
fanfare = []
for f, d in ((392, 0.14), (523, 0.14), (659, 0.14), (784, 0.5)):
    fanfare += mix(sweep(d, f, f, square, release=0.5), scale(sweep(d, f * 1.5, f * 1.5, tri, release=0.5), 0.5))
write("RoundClear.wav", fanfare, 0.3)

# Out of torpedoes: a dull double click.
write("Empty.wav", sweep(0.04, 200, 150, square, release=2) + silence(0.05) + sweep(0.04, 200, 150, square, release=2), 0.3)

# The bridge: a low hum with the computer's soft chirps and blips now and then. Ten seconds long and
# made of whole-cycle tones, so it loops without a click; the blips stay clear of the ends.
total = n(10.0)
bridge = []
for i in range(total):
    t = i / RATE
    bridge.append(0.5 * math.sin(2 * math.pi * 60 * t) + 0.25 * math.sin(2 * math.pi * 120 * t)
                  + 0.12 * math.sin(2 * math.pi * 181 * t) * (0.7 + 0.3 * math.sin(2 * math.pi * 0.3 * t)))
t0 = 0.6
while t0 < 9.2:
    f = random.choice((1180, 1320, 1480, 1760, 1980, 2350))
    length = random.choice((0.05, 0.07, 0.1))
    start = n(t0)
    for k in range(n(length)):
        bridge[start + k] += 0.35 * math.sin(2 * math.pi * f * k / RATE) * math.sin(math.pi * k / n(length))
    t0 += random.choice((0.12, 0.15, 0.3, 0.6, 1.1, 1.6))
write("Bridge.wav", bridge, 0.14)

print("Wrote sounds to", os.path.normpath(OUT))
