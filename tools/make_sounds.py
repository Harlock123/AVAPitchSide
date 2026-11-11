#!/usr/bin/env python3
"""Synthesises AVAPitchSide's sound effects and tunes into ../src/AVAPitchSide/Sounds as 16-bit mono WAV.

Nothing is sampled: every effect is built here from square, triangle, sine and noise voices, and the tunes are new
compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPitchSide", "Sounds")
RNG = random.Random(1966)


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


def echo(samples, delay=0.11, feedback=0.35, repeats=3):
    """The voice in a big hall: quieter copies of itself, later and later."""
    d = int(RATE * delay)
    out = samples + [0.0] * (d * repeats)
    for k in range(1, repeats + 1):
        g = feedback ** k
        for i, v in enumerate(samples):
            out[i + d * k] += v * g
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def line(bpm, notes, kind="pulse", mult=1.0, gain=0.55, release=0.6):
    """A tune as (note, beats), '-' a rest, a list of notes a chord; mult transposes it."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
        elif isinstance(note, list):
            out.extend(mix(*[tone(d, hz(n) * mult, kind=kind, release=release, gain=gain / len(note) ** 0.5) for n in note]))
        else:
            out.extend(tone(d, hz(note) * mult, kind=kind, release=release, gain=gain))
    return out


def kick(seconds, gain=1.0):
    return cat(tone(0.07, 150, 45, "sine", attack=0.001, release=1.0, gain=gain), silence(max(0, seconds - 0.07)))


def hat(seconds, gain=0.3):
    return cat(tone(0.03, 9000, kind="noise", attack=0.001, release=2.0, gain=gain), silence(max(0, seconds - 0.03)))


def snare(seconds, gain=0.6):
    return cat(mix(tone(0.12, 3000, kind="noise", attack=0.001, release=1.6, gain=gain),
                   tone(0.08, 220, 160, "tri", attack=0.001, release=1.0, gain=gain * 0.6)), silence(max(0, seconds - 0.12)))



def drums(bpm, bars, fill=False):
    """Four bars of a rock beat: kick on one and three, snare on two and four, eighth-note hats."""
    beat = 60.0 / bpm
    out = []
    for bar in range(bars):
        for b in range(4):
            last = fill and bar == bars - 1 and b >= 2
            if last:
                out += mix(snare(beat * 0.25, 0.4), hat(beat * 0.25)) * 1
                out += snare(beat * 0.25, 0.3) + snare(beat * 0.25, 0.35) + snare(beat * 0.25, 0.45)
                continue
            first = kick(beat * 0.5, 0.7) if b % 2 == 0 else snare(beat * 0.5, 0.5)
            out += mix(first, hat(beat * 0.5))
            out += mix(kick(beat * 0.5, 0.45) if b == 3 else silence(beat * 0.5), hat(beat * 0.5, 0.2))
    return out


def crowd(seconds, swell=0.0, gain=1.0):
    """A crowd: rough noise at a few pitches, each rising and falling on its own, with a swell (0 to 1) toward the end."""
    n = int(RATE * seconds)
    out = []
    voices = [(380, 0.5, 0.31), (900, 0.35, 0.53), (2100, 0.2, 0.79)]
    held = [0.0] * len(voices)
    phase = [0.0] * len(voices)
    for i in range(n):
        t = i / RATE
        s = 0.0
        for k, (rate, amp, wob) in enumerate(voices):
            phase[k] += rate / RATE
            if int(phase[k]) != int(phase[k] - rate / RATE):
                held[k] = RNG.uniform(-1, 1)
            s += held[k] * amp * (0.75 + 0.25 * math.sin(2 * math.pi * wob * t + k))
        k = i / max(1, n - 1)
        env = min(1.0, t / 0.3) * min(1.0, (seconds - t) / 0.3) * (1 + swell * 2.5 * k * k)
        out.append(s * env * gain)
    return out


def whistle(seconds, gain=1.0):
    """A referee's whistle: a high tone with the pea's warble."""
    n = int(RATE * seconds)
    out, phase = [], 0.0
    for i in range(n):
        t = i / RATE
        f = 2900 * (1 + 0.035 * math.sin(2 * math.pi * 38 * t))
        phase += f / RATE
        env = min(1.0, t / 0.01) * min(1.0, (seconds - t) / 0.02)
        out.append((math.sin(2 * math.pi * phase) * 0.8 + RNG.uniform(-0.15, 0.15)) * env * gain)
    return out


def terrace():
    """The title: a terrace anthem in G, a sing-along tune over a stomp and a clap."""
    bpm = 126
    tune = [("D5", 1), ("B4", 0.5), ("D5", 0.5), ("G5", 1.5), ("F#5", 0.5), ("E5", 1), ("D5", 1), ("B4", 2),
            ("C5", 1), ("A4", 0.5), ("C5", 0.5), ("E5", 1.5), ("D5", 0.5), ("C5", 1), ("B4", 1), ("A4", 2),
            ("D5", 1), ("B4", 0.5), ("D5", 0.5), ("G5", 1.5), ("A5", 0.5), ("B5", 1), ("A5", 1), ("G5", 1), ("E5", 1),
            ("D5", 1), ("E5", 0.5), ("F#5", 0.5), ("A5", 1), ("F#5", 1), ("G5", 3), ("-", 1)]
    chords = [(["G3", "D4"], 4), (["G3", "D4"], 4), (["C3", "G3"], 4), (["D3", "A3"], 4),
              (["G3", "D4"], 4), (["E3", "B3"], 4), (["C3", "G3"], 2), (["D3", "A3"], 2), (["G3", "D4"], 4)]
    beat = 60.0 / bpm
    beats = int(sum(b for _, b in tune))
    stomp = []
    for i in range(beats):
        stomp += kick(beat, 0.7) if i % 2 == 0 else mix(snare(beat, 0.45), tone(0.05, 2600, kind="noise", attack=0.001, release=2.0, gain=0.4) + silence(beat - 0.05))
    return mix(line(bpm, tune, "pulse", gain=0.5), line(bpm, chords, "tri", gain=0.45, release=0.3), stomp)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))

    # Boot on ball: a pass is a soft thump, a shot a hard one with a slap.
    write("Kick.wav", mix(tone(0.08, 220, 90, "sine", attack=0.001, release=1.6), tone(0.03, 1800, kind="noise", attack=0.001, release=2.0, gain=0.4)), 0.45)
    write("KickHard.wav", mix(tone(0.14, 180, 60, "sine", attack=0.001, release=1.4), tone(0.05, 2400, 900, "noise", attack=0.001, release=1.8, gain=0.8)), 0.7)
    write("Bounce.wav", tone(0.06, 160, 90, "sine", attack=0.001, release=1.8), 0.3)
    # The woodwork: a ringing clang.
    write("Post.wav", mix(tone(0.5, 520, kind="tri", attack=0.001, release=2.4), tone(0.5, 1370, kind="sine", attack=0.001, release=3.0, gain=0.5),
                          tone(0.04, 3000, kind="noise", attack=0.001, release=2.0, gain=0.5)), 0.55)
    write("Header.wav", mix(tone(0.07, 300, 150, "sine", attack=0.001, release=1.5), tone(0.03, 900, kind="noise", attack=0.001, release=2.0, gain=0.3)), 0.4)
    write("Tackle.wav", mix(tone(0.18, 700, 300, "noise", attack=0.005, release=1.2), tone(0.08, 140, 80, "sine", attack=0.001, release=1.5, gain=0.6)), 0.45)
    # The referee: a short peep; full time, three.
    write("Whistle.wav", whistle(0.28), 0.4)
    write("WhistleLong.wav", cat(whistle(0.25), silence(0.12), whistle(0.25), silence(0.12), whistle(0.9)), 0.45)
    # The crowd: a roar for a goal, a groan for a miss, and the hum of a full ground under the game.
    write("Goal.wav", mix(crowd(3.2, swell=1.0), tone(0.12, 180, 60, "sine", attack=0.001, release=1.4, gain=0.6)), 0.75)
    write("Miss.wav", mix(crowd(1.4, gain=0.8), tone(1.2, 260, 180, "saw", attack=0.2, release=1.2, gain=0.15)), 0.45)
    write("Crowd.wav", crowd(8.0), 0.22)
    write("Save.wav", mix(tone(0.12, 200, 80, "sine", attack=0.001, release=1.3), crowd(0.9, gain=0.6)), 0.5)
    write("Catch.wav", mix(tone(0.06, 400, 200, "noise", attack=0.001, release=2.0), tone(0.06, 180, 120, "sine", attack=0.001, release=1.6, gain=0.5)), 0.35)
    write("Throw.wav", tone(0.15, 1200, 500, "noise", attack=0.03, release=1.3), 0.2)
    write("Select.wav", tone(0.05, 1000, kind="pulse", release=0.8), 0.16)
    write("CupWin.wav", mix(line(120, [("G4", 0.5), ("B4", 0.5), ("D5", 0.5), ("G5", 1.5), ("F#5", 0.5), ("G5", 0.5), ("A5", 0.5), (["B5", "D6"], 3)], "pulse"),
                            line(120, [("G2", 1), ("D3", 1), ("G2", 1), ("D3", 1), ("G2", 3)], "tri"), crowd(5.2, swell=0.6, gain=0.5)), 0.5)
    write("Defeat.wav", mix(line(84, [("D5", 1), ("C5", 1), ("Bb4", 1), ("A4", 1), (["G4", "Bb4"], 3)], "tri", gain=0.6),
                            line(84, [("G2", 2), ("Eb2", 2), ("D2", 3)], "saw", gain=0.3)), 0.4)
    write("TitleTune.wav", cat(terrace(), silence(0.5)), 0.32)


if __name__ == "__main__":
    main()
