#!/usr/bin/env python3
"""Synthesises AVAChoplifter's sound effects and tunes into ../src/AVAChoplifter/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAChoplifter", "Sounds")
RNG = random.Random(1985)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def drums(bpm, bars):
    """A marching snare: a roll on the first beat of each bar, single taps on the rest, and a bass drum under it."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            if b == 0:
                hit = cat(*[tone(beat / 4, 1800, 900, "noise", attack=0.001, release=1.5, gain=0.5) for _ in range(4)])
            else:
                hit = cat(tone(beat / 2, 2000, 700, "noise", attack=0.001, release=2.5, gain=0.6), silence(beat / 2))
            kick = cat(tone(beat / 2, 110, 45, "sine", attack=0.002, release=1.5, gain=0.9), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out


def rotor(bpm, bars):
    """The chopper's blades: a soft thump of noise and a low knock on every eighth note."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars * 8):
        whup = mix(tone(beat / 2, 260, 90, "noise", attack=0.003, release=2.2, gain=0.45), tone(beat / 2, 70, 50, "sine", attack=0.003, release=2.0, gain=0.5))
        out.extend(whup[: int(RATE * beat / 2)])
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The chopper's gun: a short hard rattle. A bomb let go: a falling whistle.
    write("Fire.wav", mix(tone(0.07, 2600, 700, "noise", attack=0.001, release=1.4), tone(0.05, 300, 120, "square", release=1, gain=0.5)), 0.22)
    write("Bomb.wav", tone(0.5, 1600, 500, "sine", release=0.3, vibrato=0.01), 0.18)
    write("Blast.wav", mix(tone(0.7, 900, 40, "noise", attack=0.002, release=0.35), tone(0.6, 120, 35, "sine", release=0.4, gain=0.9)), 0.48)
    # The barracks: a clang on the walls, and broken open.
    write("BarracksHit.wav", mix(tone(0.12, 900, 600, "square", release=1.2, gain=0.4), tone(0.1, 1400, 500, "noise", release=1.5, gain=0.6)), 0.28)
    write("BarracksOpen.wav", cat(mix(tone(0.8, 700, 30, "noise", release=0.3), tone(0.7, 90, 30, "saw", release=0.3, gain=0.6)), tone(0.4, 300, 100, "noise", release=0.8, gain=0.4)), 0.5)
    # Someone aboard: a rising blip. Off at the base: a falling one.
    write("Board.wav", cat(tone(0.04, hz("G5"), kind="pulse", release=0.3), tone(0.07, hz("D6"), kind="pulse", release=0.4)), 0.24)
    write("Unload.wav", cat(tone(0.04, hz("D6"), kind="pulse", release=0.3), tone(0.07, hz("G5"), kind="pulse", release=0.4)), 0.24)
    # The enemy: a tank gun's boom, a missile's hiss, a jet tearing past.
    write("TankFire.wav", mix(tone(0.3, 600, 60, "noise", attack=0.001, release=0.8), tone(0.25, 150, 60, "square", release=0.8, gain=0.5)), 0.3)
    write("JetFire.wav", tone(0.45, 3000, 1200, "noise", attack=0.01, release=0.6), 0.2)
    write("JetPass.wav", mix(tone(1.4, 800, 3200, "noise", attack=0.6, release=1.5, gain=0.7), tone(1.4, 420, 160, "saw", attack=0.5, release=1.5, gain=0.15)), 0.26)
    # Down: a long tumbling whine into a crash.
    write("Crash.wav", cat(tone(0.9, 900, 120, "saw", vibrato=0.08, vib_rate=11, release=0.3, gain=0.6), mix(tone(0.9, 1000, 40, "noise", release=0.35), tone(0.8, 100, 30, "sine", release=0.4))), 0.5)
    write("HostageLost.wav", cat(*[tone(0.18, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4"]]), 0.34)
    write("Land.wav", mix(tone(0.12, 200, 80, "sine", release=1.2), tone(0.08, 500, 200, "noise", release=1.5, gain=0.4)), 0.3)
    write("LevelStart.wav", cat(*[tone(d, hz(n), kind="pulse", release=0.25) for n, d in [("G4", 0.12), ("C5", 0.12), ("E5", 0.12), ("G5", 0.36)]]), 0.3)
    write("LevelClear.wav", tune(140, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("A5", 0.5), ("B5", 0.5), ("C6", 1.5)], [("C3", 1), ("G2", 1), ("F2", 1), ("G2", 0.5), ("C3", 1.5)], kind="pulse"), 0.42)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "G6", "C6", "G6", "C7"]]), 0.36)

    # Out over the border: a minor, driving line over the rotor's beat (an original composition), a whole
    # number of beats long so it loops cleanly.
    lead = [("A4", 1), ("C5", 0.5), ("D5", 0.5), ("E5", 1), ("D5", 0.5), ("C5", 0.5),
            ("A4", 1), ("G4", 0.5), ("A4", 0.5), ("E4", 2),
            ("A4", 1), ("C5", 0.5), ("D5", 0.5), ("E5", 1), ("G5", 0.5), ("E5", 0.5),
            ("D5", 1), ("C5", 0.5), ("B4", 0.5), ("A4", 2)]
    bass = [("A2", 1), ("A2", 1), ("F2", 1), ("F2", 1), ("G2", 1), ("G2", 1), ("E2", 1), ("E2", 1),
            ("A2", 1), ("A2", 1), ("C3", 1), ("C3", 1), ("D3", 1), ("E2", 1), ("A2", 2)]
    melody = tune(116, lead, bass, kind="pulse")
    write("Music.wav", mix([v * 0.5 for v in melody], rotor(116, 4)), 0.3)

    # The title: the same tune, a brighter second strain, and a march.
    title_lead = lead + [("C5", 1), ("E5", 0.5), ("G5", 0.5), ("A5", 1), ("G5", 1), ("F5", 1), ("E5", 0.5), ("D5", 0.5), ("E5", 2),
                         ("C5", 1), ("D5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 1), ("D5", 0.5), ("C5", 0.5), ("B4", 0.5), ("D5", 0.5), ("A4", 2)]
    title_bass = bass + [("C3", 1), ("C3", 1), ("F2", 1), ("F2", 1), ("D3", 1), ("D3", 1), ("C3", 1), ("C3", 1),
                         ("A2", 1), ("A2", 1), ("E2", 1), ("E2", 1), ("G2", 1), ("E2", 1), ("A2", 2)]
    write("TitleTune.wav", cat(mix(tune(122, title_lead, title_bass, kind="pulse"), drums(122, 8)), silence(1.0)), 0.4)
    write("StartTune.wav", tune(140, [("E4", 0.5), ("A4", 0.5), ("C5", 0.5), ("E5", 1), ("C5", 0.5), ("E5", 1)], [("A2", 1), ("E2", 1), ("A2", 2)], kind="pulse"), 0.42)
    write("GameOver.wav", tune(66, [("E5", 1), ("C5", 1), ("B4", 1), ("G#4", 1), ("A4", 4)], [("A2", 2), ("F2", 2), ("E2", 2), ("A2", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
