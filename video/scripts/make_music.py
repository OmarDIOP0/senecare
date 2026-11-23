"""Background music made from scratch (no licence to clear): a warm, light loop — soft pad chords
(Fmaj9 · Dm9 · Bbmaj9 · C6add9) and a gentle kalimba-like arpeggio at 92 bpm. Written as a 44.1 kHz stereo WAV.
Usage: python make_music.py out.wav seconds"""
import sys
import wave

import numpy as np

RATE = 44100
out, seconds = sys.argv[1], float(sys.argv[2])
n = int(RATE * seconds)
t = np.arange(n) / RATE
beat = 60 / 92


def hz(note):  # MIDI note number → frequency
    return 440.0 * 2 ** ((note - 69) / 12)


chords = [
    [53, 57, 60, 64, 67],  # Fmaj9
    [50, 53, 57, 60, 64],  # Dm9
    [46, 50, 53, 57, 60],  # Bbmaj9
    [48, 52, 55, 57, 62],  # C6add9
]
bar = beat * 4
left = np.zeros(n)
right = np.zeros(n)

# Pad: each chord fades in and out over its bar (slow attack, soft release), two detuned voices for width.
for k in range(int(seconds / bar) + 1):
    chord = chords[k % len(chords)]
    start = k * bar
    seg = (t >= start - 0.6) & (t < start + bar + 0.6)
    local = t[seg] - start
    env = np.clip(np.minimum((local + 0.6) / 1.2, (bar + 0.6 - local) / 1.2), 0, 1) ** 1.5
    for note in chord:
        f = hz(note)
        voice = np.sin(2 * np.pi * f * t[seg]) + 0.35 * np.sin(2 * np.pi * 2 * f * t[seg])
        left[seg] += 0.05 * env * (voice + 0.5 * np.sin(2 * np.pi * f * 1.003 * t[seg]))
        right[seg] += 0.05 * env * (voice + 0.5 * np.sin(2 * np.pi * f * 0.997 * t[seg]))

# Arpeggio: eighth notes over the chord, plucked (fast attack, exponential decay), alternating sides.
step = beat / 2
for i in range(int(seconds / step)):
    chord = chords[int(i * step / bar) % len(chords)]
    note = chord[[0, 2, 4, 3, 1, 3, 2, 4][i % 8]] + 12
    start = i * step
    seg = (t >= start) & (t < start + 1.6)
    local = t[seg] - start
    env = (1 - np.exp(-local * 120)) * np.exp(-local * 3.2)
    f = hz(note)
    tone = np.sin(2 * np.pi * f * local) + 0.25 * np.sin(2 * np.pi * 3 * f * local) * np.exp(-local * 9)
    pan = 0.35 if i % 2 else 0.65
    left[seg] += 0.07 * env * tone * (1 - pan) * 2
    right[seg] += 0.07 * env * tone * pan * 2

# Soft low-pass (moving average) and fade in / out, peak at −3 dBFS.
kernel = np.ones(6) / 6
left = np.convolve(left, kernel, mode="same")
right = np.convolve(right, kernel, mode="same")
fade = np.clip(np.minimum(t / 2.0, (seconds - t) / 3.0), 0, 1)
stereo = np.stack([left * fade, right * fade], axis=1)
stereo /= np.max(np.abs(stereo)) / 0.707
with wave.open(out, "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(RATE)
    w.writeframes((stereo * 32767).astype("<i2").tobytes())
print("music:", out)
