"""Synthesizes the Christmas card's music and sound effects as WAV files (stdlib only).

Usage: python -I make_audio.py <output folder>
Songs are public-domain melodies (Jingle Bells, We Wish You a Merry Christmas).
File names contain the keywords ChristmasCardBuilder.FindClip looks for.
"""
import math
import os
import random
import struct
import sys
import wave

SR = 22050
random.seed(7)


def silence(sec):
    return [0.0] * int(sec * SR)


def mix(dst, src, at, wrap=False):
    """Adds src into dst at `at` seconds. wrap=True folds tails back to the start (seamless loops)."""
    n, i0 = len(dst), int(at * SR)
    for i, s in enumerate(src):
        j = i0 + i
        if wrap:
            dst[j % n] += s
        elif j < n:
            dst[j] += s


def hz(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


def bell(midi, dur=1.6, amp=0.3):
    """Music-box / celesta tone: a few partials, the bright ones die first."""
    f0, n = hz(midi), int(dur * SR)
    out = [0.0] * n
    for mult, a, decay in ((1, 1.0, 3.0), (2, 0.45, 5.0), (3, 0.18, 8.0), (4.2, 0.08, 12.0)):
        f = f0 * mult
        if f > SR / 2 - 500:
            continue
        w = 2 * math.pi * f / SR
        for i in range(n):
            out[i] += amp * a * math.exp(-decay * i / SR) * math.sin(w * i)
    attack = int(0.003 * SR)
    for i in range(min(n, attack)):
        out[i] *= i / attack
    return out


def bass(midi, dur, amp=0.22):
    f, n = hz(midi), int(dur * SR)
    w = 2 * math.pi * f / SR
    return [amp * math.exp(-2.5 * i / SR) * (math.sin(w * i) + 0.3 * math.sin(2 * w * i)) *
            min(1, i / (0.01 * SR)) for i in range(n)]


def pad(midis, dur, amp=0.035):
    """Soft sustained chord: slow attack and release so bars blend."""
    n = int(dur * SR)
    out = [0.0] * n
    for m in midis:
        w = 2 * math.pi * hz(m) / SR
        for i in range(n):
            t = i / SR
            env = min(1, t / 0.15) * min(1, (dur - t) / 0.3)
            out[i] += amp * env * (math.sin(w * i) + 0.15 * math.sin(3 * w * i))
    return out


def make_jingle():
    """One sleigh-bell shake: a cluster of high metallic partials plus a tick of noise."""
    n = int(0.25 * SR)
    out = [0.0] * n
    for _ in range(6):
        w = 2 * math.pi * random.uniform(2500, 7500) / SR
        d = random.uniform(25, 40)
        ph = random.uniform(0, 6.28)
        for i in range(n):
            out[i] += 0.025 * math.exp(-d * i / SR) * math.sin(w * i + ph)
    for i in range(n):
        out[i] += 0.04 * math.exp(-60 * i / SR) * random.uniform(-1, 1)
    return out


JINGLES = [make_jingle() for _ in range(4)]


def jingle(gain=1.0):
    return [s * gain for s in random.choice(JINGLES)]


def finish(samples, peak=0.85):
    """Soft-clip, normalize and fade the very ends to avoid clicks."""
    samples = [math.tanh(s) for s in samples]
    top = max(abs(s) for s in samples) or 1
    return [s * peak / top for s in samples]


def write(folder, name, samples, peak=0.85, fade=True):
    samples = finish(samples, peak)
    if fade:
        k = int(0.005 * SR)
        for i in range(min(k, len(samples))):
            samples[i] *= i / k
            samples[-1 - i] *= i / k
    path = os.path.join(folder, name)
    with wave.open(path, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes(b"".join(struct.pack("<h", int(s * 32767)) for s in samples))
    print(f"{name}: {len(samples) / SR:.1f}s")


# ------------------------------------------------------------------ songs

def song(melody, chords, beat, beats_per_bar, pickup=0.0, loop=True, tail=0.0):
    total_beats = pickup + len(chords) * beats_per_bar
    out = silence(total_beats * beat + tail)
    t = 0.0
    for midi, beats in melody:
        mix(out, bell(midi, max(1.2, beats * beat + 0.6)), t, wrap=loop)
        t += beats * beat
    for bar, (root, tones) in enumerate(chords):
        start = (pickup + bar * beats_per_bar) * beat
        bar_len = beats_per_bar * beat
        mix(out, pad(tones, bar_len + 0.2), start, wrap=loop)
        mix(out, bass(root, beat * 1.8), start, wrap=loop)
        mix(out, bass(root + 7, beat * 1.8), start + (beats_per_bar // 2 + beats_per_bar % 2) * beat, wrap=loop)
    # sleigh bells on every eighth note, accented on the beat
    eighth = 0
    while eighth * beat / 2 < total_beats * beat:
        mix(out, jingle(1.0 if eighth % 2 == 0 else 0.55), eighth * beat / 2, wrap=loop)
        eighth += 1
    return out


def jingle_bells():
    E, G, C, D, F = 76, 79, 72, 74, 77
    verse_a = [(E, 1), (E, 1), (E, 2), (E, 1), (E, 1), (E, 2), (E, 1), (G, 1), (C, 1.5), (D, 0.5), (E, 4)]
    melody = (verse_a +
              [(F, 1), (F, 1), (F, 1.5), (F, 0.5), (F, 1), (E, 1), (E, 1), (E, 0.5), (E, 0.5),
               (E, 1), (D, 1), (D, 1), (E, 1), (D, 2), (G, 2)] +
              verse_a +
              [(F, 1), (F, 1), (F, 1), (F, 1), (F, 1), (E, 1), (E, 1), (E, 0.5), (E, 0.5),
               (G, 1), (G, 1), (F, 1), (D, 1), (C, 4)])
    c, f, g, d7 = (48, [60, 64, 67]), (53, [60, 65, 69]), (43, [59, 62, 67]), (50, [60, 62, 66])
    chords = [c, c, c, c, f, c, d7, g, c, c, c, c, f, c, g, c]
    return song(melody, chords, beat=60 / 132, beats_per_bar=4)


def we_wish_you():
    D, E, Fs, G, A, B, C6 = 74, 76, 78, 79, 81, 83, 84
    melody = [(D, 1),
              (G, 1), (G, 0.5), (A, 0.5), (G, 0.5), (Fs, 0.5),
              (E, 1), (E, 1), (E, 1),
              (A, 1), (A, 0.5), (B, 0.5), (A, 0.5), (G, 0.5),
              (Fs, 1), (D, 1), (D, 1),
              (B, 1), (B, 0.5), (C6, 0.5), (B, 0.5), (A, 0.5),
              (G, 1), (E, 1), (D, 0.5), (D, 0.5),
              (E, 1), (A, 1), (Fs, 1),
              (G, 3)]
    chords = [(43, [55, 59, 62]), (48, [60, 64, 67]), (45, [57, 61, 64]), (50, [57, 62, 66]),
              (47, [59, 63, 66]), (40, [59, 64, 67]), (50, [57, 62, 66]), (43, [55, 59, 62])]
    beat = 60 / 150
    out = song(melody, chords, beat=beat, beats_per_bar=3, pickup=1, loop=False, tail=2.0)
    # big final chord with a shower of bells
    end = (1 + 7 * 3) * beat
    for m in (79, 83, 86, 91):
        mix(out, bell(m, 2.2, 0.22), end)
    for k in range(8):
        mix(out, jingle(1.2), end + k * 0.07)
    return out


# ------------------------------------------------------------------ effects

def sweep(f_from, f_to, dur, amp, decay=0.0, curve=1.0):
    out, ph, n = [], 0.0, int(dur * SR)
    for i in range(n):
        x = i / n
        f = f_from * (f_to / f_from) ** (x ** curve)
        ph += 2 * math.pi * f / SR
        out.append(amp * math.exp(-decay * i / SR) * math.sin(ph))
    return out


def noise(dur, amp, decay, smooth=0.0):
    out, y = [], 0.0
    for i in range(int(dur * SR)):
        x = random.uniform(-1, 1)
        y = y + (1 - smooth) * (x - y)  # smooth>0 = lowpass
        out.append(amp * math.exp(-decay * i / SR) * y)
    return out


def pop():
    out = sweep(1200, 250, 0.18, 0.6, decay=22)
    mix(out, noise(0.006, 0.5, 0), 0)
    return out


def land():
    out = sweep(140, 55, 0.3, 0.7, decay=14)
    mix(out, noise(0.2, 0.5, 30, smooth=0.9), 0)
    return out


def sparkle():
    out = silence(1.3)
    for k, m in enumerate((84, 88, 91, 96, 100)):
        mix(out, bell(m, 0.9, 0.22), k * 0.06)
    mix(out, jingle(0.8), 0)
    return out


def rare():
    out = silence(2.9)
    for k, m in enumerate((72, 76, 79, 84, 88, 91, 96)):
        mix(out, bell(m, 2.0, 0.2), k * 0.07)
    for m in (84, 88, 91):
        mix(out, bell(m, 2.2, 0.2), 0.55)
    for k in range(5):
        mix(out, jingle(1.0), 0.5 + k * 0.09)
    return out


def firework():
    out = silence(1.8)
    whistle, ph = [], 0.0
    n = int(0.5 * SR)
    for i in range(n):
        x = i / n
        f = 900 + 1500 * x + 40 * math.sin(2 * math.pi * 30 * i / SR)
        ph += 2 * math.pi * f / SR
        whistle.append(0.12 * math.sin(ph) * min(1, x * 6))
    mix(out, whistle, 0)
    mix(out, noise(1.0, 0.8, 6, smooth=0.85), 0.52)
    t = 0.6
    while t < 1.7:  # crackle: sparse clicks that thin out
        mix(out, noise(0.003, random.uniform(0.2, 0.5), 0), t)
        t += random.uniform(0.01, 0.05) * (1 + 2 * (t - 0.6))
    return out


def whoosh():
    out, y, n = [], 0.0, int(1.0 * SR)
    for i in range(n):
        x = i / n
        a = 0.02 + 0.3 * math.sin(math.pi * x)
        y += a * (random.uniform(-1, 1) - y)
        out.append(0.9 * math.sin(math.pi * x) ** 2 * y)
    return out


def sleigh_loop():
    beat = 60 / 132
    out = silence(4 * beat)
    for k in range(8):
        mix(out, jingle(1.0 if k % 2 == 0 else 0.6), k * beat / 2, wrap=True)
    for _ in range(6):  # extra shimmer
        mix(out, jingle(0.3), random.uniform(0, 4 * beat), wrap=True)
    return out


def tree_tap():
    out, ph = [], 0.0
    n = int(0.6 * SR)
    for i in range(n):
        t = i / SR
        f = 220 + 300 * min(1, t / 0.25) + 25 * math.exp(-4 * t) * math.sin(2 * math.pi * 18 * t)
        ph += 2 * math.pi * f / SR
        out.append(0.5 * math.exp(-5 * t) * math.sin(ph))
    mix(out, jingle(1.0), 0)
    mix(out, jingle(0.8), 0.08)
    return out


def hand_bell():
    """A big hand bell: low strong strike, bright overtones, long ring, a shake of jingles."""
    out = silence(2.6)
    mix(out, bell(79, 2.5, 0.35), 0)
    mix(out, bell(86, 2.0, 0.18), 0)
    mix(out, bell(91, 1.4, 0.1), 0.01)
    mix(out, jingle(1.2), 0)
    mix(out, jingle(0.7), 0.09)
    return out


def fire_crackle():
    """A fireplace: a low warm rumble under lots of random pops and crackles. Loops seamlessly."""
    dur, n, m = 4.0, int(4.0 * SR), int(0.25 * SR)
    # rumble: low-passed noise that swells and fades twice per loop; the last m samples are cross-faded
    # into the first m so there is no jump at the loop point
    r, y = [], 0.0
    for i in range(n + m):
        y += 0.03 * (random.uniform(-1, 1) - y)
        r.append(0.55 * y * (0.7 + 0.3 * math.sin(2 * math.pi * i / n * 2)))
    for i in range(m):
        w = i / m
        r[i] = r[i] * w + r[n + i] * (1 - w)
    out = r[:n]
    # pops: short bursts of noise that die away quickly; about one in five is loud
    t = 0.0
    while True:
        t += random.expovariate(16)
        if t >= dur:
            break
        amp = random.uniform(0.5, 1.0) if random.random() < 0.18 else random.uniform(0.1, 0.4)
        decay = random.uniform(300, 900)
        bright = random.uniform(0.3, 0.9)
        pop, y = [], 0.0
        for i in range(int(random.uniform(0.008, 0.04) * SR)):
            y += bright * (random.uniform(-1, 1) - y)
            pop.append(amp * math.exp(-decay * i / SR) * y)
        mix(out, pop, t, wrap=True)
    return out


if __name__ == "__main__":
    folder = sys.argv[1]
    os.makedirs(folder, exist_ok=True)
    # loops: no end fade (the wrap makes them seamless)
    write(folder, "music_christmas.wav", jingle_bells(), peak=0.8, fade=False)
    write(folder, "sleigh_bells_loop.wav", sleigh_loop(), peak=0.7, fade=False)
    write(folder, "finale_tune.wav", we_wish_you(), peak=0.8)
    write(folder, "pop.wav", pop())
    write(folder, "land_thud.wav", land())
    write(folder, "sparkle.wav", sparkle())
    write(folder, "rare_chime.wav", rare())
    write(folder, "firework.wav", firework(), peak=0.7)
    write(folder, "whoosh.wav", whoosh(), peak=0.6)
    write(folder, "tree_jingle.wav", tree_tap())
    write(folder, "hand_bell_ring.wav", hand_bell())
    write(folder, "fire_crackle_loop.wav", fire_crackle(), peak=0.8, fade=False)
