#!/usr/bin/env bash
# Generates the ship's spoken reports into ../Sounds/Voice*.wav with the Piper text-to-speech engine,
# then gives them the grainy, band-limited sound of an early-80s speech chip (tools/crunch_voice.py).
#
# Needs a Piper binary and the en_GB-vctk-medium voice (VCTK corpus, CC BY 4.0). Override the paths with
# PIPER=/path/to/piper and VOICE=/path/to/model.onnx. Speaker 24 in that model is VCTK speaker p360.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
out="$here/../Sounds"
piper="${PIPER:-$HOME/.local/share/piper/bin/piper}"
voice="${VOICE:-$HOME/.local/share/piper/voices/en_GB-vctk-medium.onnx}"
speaker="${SPEAKER:-24}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

say() {
  local name="$1" text="$2"
  echo "$text" | "$piper" --model "$voice" --speaker "$speaker" --length_scale 1.08 \
    --noise_scale 0.4 --noise_w 0.5 --output_file "$tmp/$name.wav" >/dev/null 2>&1
  python3 "$here/crunch_voice.py" "$tmp/$name.wav" "$out/Voice$name.wav"
}

mkdir -p "$out"
say Welcome       "Welcome aboard, Captain."
say Klingons      "Klingon vessels approaching."
say Saucers       "Red alert. Anti-matter saucers detected."
say Nomad         "Red alert. Nomad has entered the sector."
say Docking       "Docking complete. All systems replenished."
say ShieldsLow    "Shields failing, Captain."
say StarbaseLost  "Starbase destroyed."
say RoundComplete "Round complete."
say SectorSecured "Sector secured. Well done, Captain."
say PhotonsOut    "Photon torpedoes depleted."
say ShipLost      "We have lost the ship."
say GameOver      "Simulation terminated."
echo "Wrote voices to $(cd "$out" && pwd)"
