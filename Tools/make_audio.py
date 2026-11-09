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


# ------------------------------------------------------------------ business card

# The business card's music: a lo-fi hip-hop loop played like a person would. Jazzy electric-piano chords
# (FM Rhodes) with a little strum and uneven touch, a plucked bass, swung brushy drums with ghost notes, a
# written melody, and tape wobble plus vinyl crackle on top. 16 bars at 78 BPM (about 49 s), loops seamlessly.
# Bars 0-3 are a light intro (no kick), the melody plays bars 4-11, bars 12-15 are the groove alone.
LOFI_BPM = 78


def rhodes(midi, dur, vel, bright=1.0):
    """FM electric piano: the bark of the hammer fades into a round tone with a faint bell tine on top."""
    f, n = hz(midi), int((dur + 0.25) * SR)
    w, out = 2 * math.pi * f / SR, []
    rel = int(0.08 * SR)
    end = int(dur * SR)
    for i in range(n):
        t = i / SR
        index = (1.6 * math.exp(-t * 5.5) + 0.35) * vel * bright
        env = min(1, i / (0.004 * SR)) * math.exp(-t * 1.5)
        if i > end:
            env *= max(0, 1 - (i - end) / rel)
        tone = math.sin(w * i + index * math.sin(w * i))
        tine = 0.1 * math.exp(-t * 11) * math.sin(4.0 * w * i)
        out.append(0.16 * vel * env * (tone + tine))
    return out


def pluck_bass(midi, dur, vel):
    f, n = hz(midi), int((dur + 0.08) * SR)
    w, out, y = 2 * math.pi * f / SR, [], 0.0
    end = int(dur * SR)
    for i in range(n):
        t = i / SR
        env = min(1, i / (0.006 * SR)) * math.exp(-t * 2.0)
        if i > end:
            env *= max(0, 1 - (i - end) / (0.08 * SR))
        x = math.sin(w * i) + 0.28 * math.sin(2 * w * i) + 0.08 * math.sin(3 * w * i)
        y += 0.35 * (x - y)  # soften the edges, like fingers on a string
        out.append(0.3 * vel * env * y)
    return out


def lofi_kick(vel):
    out, ph = [], 0.0
    for i in range(int(0.35 * SR)):
        t = i / SR
        f = 48 + 70 * math.exp(-t * 30)
        ph += 2 * math.pi * f / SR
        out.append(0.62 * vel * math.exp(-t * 11) * math.sin(ph))
    for i in range(int(0.004 * SR)):
        out[i] += 0.12 * vel * random.uniform(-1, 1)
    return out


def lofi_snare(vel, ghost=False):
    """Soft and dusty, closer to a brush than a crack."""
    n = int(0.28 * SR)
    out, y = [], 0.0
    for i in range(n):
        t = i / SR
        y += 0.45 * (random.uniform(-1, 1) - y)
        body = 0.12 * math.exp(-t * 35) * math.sin(2 * math.pi * 185 * t)
        out.append(vel * (0.24 * math.exp(-t * 16) * y + body) * (0.45 if ghost else 1))
    return out


def lofi_hat(vel, open_=False):
    n = int((0.3 if open_ else 0.06) * SR)
    out, prev = [], 0.0
    for i in range(n):
        x = random.uniform(-1, 1)
        hp, prev = x - prev, x  # high-passed noise
        out.append(0.05 * vel * math.exp(-i / SR * (9 if open_ else 55)) * hp)
    return out


def biz_lofi():
    beat = 60.0 / LOFI_BPM
    bar = 4 * beat
    bars = 16
    n = int(bars * bar * SR)
    dur = n / SR
    rng = random.Random(11)
    keys, low, drums = [0.0] * n, [0.0] * n, [0.0] * n

    def at(b, x):  # bar b, beat x, with a little human timing slop
        return (b * 4 + x) * beat + rng.uniform(-0.012, 0.012)

    def swing(x):  # swung 8ths: the off-beat lands late
        return x + (0.09 if (x * 2) % 2 == 1 else 0)

    # Fmaj9 | Em7 A7b9 | Dm9 | Gm9 C13   (voicings flow into each other)
    prog = [
        [(0, 41, [57, 64, 67, 72])],
        [(0, 40, [55, 59, 62, 64]), (2, 45, [55, 58, 61, 64])],
        [(0, 38, [53, 57, 60, 64])],
        [(0, 43, [53, 57, 58, 62]), (2, 36, [52, 57, 58, 62])],
    ]
    comps = [[(0, 1.9), (2.5, 0.6)], [(0, 1.4), (1.75, 0.2), (2.5, 1.2)], [(0, 2.4), (3.5, 0.45)], [(0, 1.0), (1.5, 0.4), (2.5, 1.3)]]
    kicks = []
    for b in range(bars):
        chords = prog[b % 4]
        comp = comps[(b + b // 4) % len(comps)]  # the rhythm shifts each time round, so the bars never repeat exactly
        for start, length in comp:
            # the chord sounding at this point of the bar
            root, voicing = chords[0][1], chords[0][2]
            for cs, r, v in chords:
                if start >= cs:
                    root, voicing = r, v
            length = min(length, 4 - start) if len(chords) == 1 else min(length, (2 if start < 2 else 4) - start)
            vel = rng.uniform(0.75, 1.0) * (1 if start == 0 else 0.8)
            t0 = at(b, start)
            for k, m in enumerate(voicing):  # rolled from the bottom up, unevenly
                mix(keys, rhodes(m, length * beat, vel * rng.uniform(0.85, 1.05)), t0 + k * rng.uniform(0.008, 0.022), wrap=True)
        # bass: root on 1, a push on the and-of-2 or a walk-up into the next chord
        for cs, r, _ in chords:
            mix(low, pluck_bass(r, (1.4 if len(chords) == 2 else 2.2) * beat, rng.uniform(0.85, 1.0)), at(b, cs), wrap=True)
        if len(chords) == 1:
            nxt = prog[(b + 1) % 4][0][1]
            mix(low, pluck_bass(chords[0][1] + 7, 0.45 * beat, 0.7), at(b, swing(2.5)), wrap=True)
            mix(low, pluck_bass(nxt + (1 if rng.random() < 0.5 else -1), 0.4 * beat, 0.65), at(b, swing(3.5)), wrap=True)

        # drums
        intro = b < 4
        if not intro:
            for x in ([0, 2.5] if b % 2 == 0 else [0, 1.75, 2.5]):
                t = at(b, x)
                kicks.append(t)
                mix(drums, lofi_kick(rng.uniform(0.85, 1.0)), t, wrap=True)
        for x in (1, 3):
            mix(drums, lofi_snare(rng.uniform(0.8, 1.0) * (0.6 if intro else 1)), at(b, x) + 0.015, wrap=True)  # laid back
        if not intro and rng.random() < 0.6:
            mix(drums, lofi_snare(0.6, ghost=True), at(b, swing(3.5) + 0.25), wrap=True)
        for k in range(8):
            x = swing(k * 0.5)
            open_ = b % 4 == 3 and k == 7
            mix(drums, lofi_hat((1.0 if k % 2 == 0 else 0.6) * rng.uniform(0.7, 1.0), open_), at(b, x), wrap=True)

    # the tune, written out (bar, beat, note, beats), played an octave up on the same piano, a little softer
    tune = [
        (0, 1.0, 72, 0.5), (0, 1.5, 69, 0.5), (0, 2.0, 67, 1.5),
        (1, 0.5, 67, 0.5), (1, 1.0, 69, 0.5), (1, 1.5, 71, 0.75), (1, 2.5, 73, 0.5), (1, 3.0, 76, 1.0),
        (2, 0.0, 77, 1.5), (2, 2.0, 76, 0.5), (2, 2.5, 72, 0.5), (2, 3.0, 69, 1.0),
        (3, 0.5, 70, 0.5), (3, 1.0, 69, 0.5), (3, 1.5, 67, 1.0), (3, 3.0, 65, 0.5), (3, 3.5, 67, 0.5),
        (4, 0.0, 69, 1.0), (4, 1.5, 72, 0.5), (4, 2.0, 76, 1.0), (4, 3.0, 74, 0.5),
        (5, 0.0, 74, 1.0), (5, 1.5, 71, 0.5), (5, 2.5, 73, 0.5), (5, 3.0, 70, 1.0),
        (6, 0.0, 69, 2.0), (6, 2.5, 65, 0.5), (6, 3.0, 64, 0.5), (6, 3.5, 65, 0.5),
        (7, 0.0, 62, 1.0), (7, 1.0, 65, 0.5), (7, 1.5, 67, 2.0),
    ]
    for bb, x, m, length in tune:
        vel = rng.uniform(0.6, 0.8)
        mix(keys, rhodes(m + 12, length * beat * 1.1, vel * 0.85, bright=1.0), at(4 + bb, swing(x)), wrap=True)

    # the keys duck a touch under each kick, like an old sampler pumping
    duck = [1.0] * n
    for t in kicks:
        i0 = int(t * SR)
        for i in range(int(0.25 * SR)):
            j = (i0 + i) % n
            duck[j] = min(duck[j], 1 - 0.28 * math.exp(-i / SR / 0.07))
    mixdown = [keys[i] * duck[i] + low[i] + 0.85 * drums[i] for i in range(n)]

    # warm it: a gentle low-pass, run twice round the loop so the start and end match
    y, a = 0.0, 0.45
    for _ in range(2):
        for i in range(n):
            y += a * (mixdown[i] - y)
            mixdown[i] = y

    # tape wobble: the read point drifts slowly (whole cycles per loop, so it still loops)
    wobbled = [0.0] * n
    for i in range(n):
        ph = i / n
        d = SR * (0.004 + 0.0018 * math.sin(2 * math.pi * 19 * ph) + 0.0004 * math.sin(2 * math.pi * 211 * ph))
        pos = i - d
        k = math.floor(pos)
        f = pos - k
        wobbled[i] = mixdown[k % n] * (1 - f) + mixdown[(k + 1) % n] * f

    # vinyl: a quiet hiss with soft clicks and the odd pop
    y = 0.0
    for i in range(n):
        y += 0.2 * (rng.uniform(-1, 1) - y)
        wobbled[i] += 0.006 * y
    for _ in range(int(dur * 4)):
        i = rng.randrange(n)
        amp = rng.uniform(0.02, 0.07) * (3 if rng.random() < 0.05 else 1)
        for k in range(rng.randint(2, 5)):
            wobbled[(i + k) % n] += amp * rng.choice((-1, 1)) * (0.6 ** k)
    return wobbled


# ------------------------------------------------------------------ business card effects
# (open, pulse and power-up are Kenney's Interface Sounds, CC0, copied into Assets/Resources/Audio by hand.)
# Built from physical-sounding parts instead of clean beeps: noise through resonant filters (a key, a slap, air),
# the same electric piano as the music (in its key, F major), and metal-bar chimes with uneven partials.
# Every note is a little off in time and loudness, like a hand played it.

def bandpass(x, f, q, f_end=None):
    """Resonant band-pass (RBJ biquad). With f_end the centre sweeps from f to f_end across the sound."""
    out, x1, x2, y1, y2, n = [], 0.0, 0.0, 0.0, 0.0, len(x)
    for i, v in enumerate(x):
        fc = f if f_end is None else f * (f_end / f) ** (i / n)
        w = 2 * math.pi * fc / SR
        al = math.sin(w) / (2 * q)
        b0, a0, a1, a2 = al, 1 + al, -2 * math.cos(w), 1 - al
        y = (b0 * v - b0 * x2 - a1 * y1 - a2 * y2) / a0
        x2, x1, y2, y1 = x1, v, y1, y
        out.append(y)
    return out


def burst(dur, decay, rng):
    return [rng.uniform(-1, 1) * math.exp(-decay * i / SR) for i in range(int(dur * SR))]


def key_click(rng, vel=1.0):
    """A mechanical keyboard key: the click going down, the plastic thock, then a softer bottom-out."""
    out = silence(0.14)
    for at, v in ((0.0, 1.0), (0.022 + rng.uniform(0, 0.006), 0.45)):
        tail = [0.0] * int(0.06 * SR)  # room for the filters to ring out after the tap
        click = bandpass(burst(0.012, 300, rng) + tail, 2800 * rng.uniform(0.9, 1.1), 1.8)
        thock = bandpass(burst(0.006, 900, rng) + tail, 380 * rng.uniform(0.95, 1.05), 9)
        mix(out, [vel * v * 0.5 * c for c in click], at)
        mix(out, [vel * v * 1.6 * t for t in thock], at)
    return out


def chime(midi, rng, amp=0.2, dur=1.4):
    """A small metal bar: its overtones are not whole multiples, which is what makes it sound real."""
    f0, n = hz(midi), int(dur * SR)
    out = [0.0] * n
    for ratio, a, decay in ((1, 1.0, 2.2), (2.756, 0.5, 4.5), (5.404, 0.22, 9.0)):
        f = f0 * ratio * rng.uniform(0.997, 1.003)
        if f > SR / 2 - 800:
            continue
        w, ph = 2 * math.pi * f / SR, rng.uniform(0, 6.28)
        for i in range(n):
            out[i] += amp * a * math.exp(-decay * i / SR) * math.sin(w * i + ph)
    for i in range(min(n, int(0.002 * SR))):
        out[i] *= i / (0.002 * SR)
    return out


def biz_tick():
    """Taps and small UI moves: one key press."""
    return key_click(random.Random(3))


def biz_pop():
    """A soft mouth pop (a cheek flick): a short blip that bends up, with a puff of air."""
    rng = random.Random(17)
    n = int(0.12 * SR)
    out, ph = [], 0.0
    for i in range(n):
        t = i / SR
        ph += 2 * math.pi * (380 + 700 * (1 - math.exp(-t * 60))) / SR
        out.append(0.7 * math.exp(-t * 38) * math.sin(ph))
    mix(out, [0.25 * v for v in bandpass(burst(0.02, 200, rng), 1500, 1.0)], 0)
    return out


def biz_sparkle():
    """A few wind chimes brushed by a hand: notes of F major pentatonic, unevenly spaced and loud."""
    rng = random.Random(21)
    out = silence(1.8)
    t = 0.0
    for m in (84, 81, 88, 86, 91, 84):
        mix(out, chime(m, rng, rng.uniform(0.08, 0.16), 1.3), t)
        t += rng.uniform(0.03, 0.09)
    return out


def splat():
    """Bug Hunt: the swatter's slap on the card and a little wet squish under it."""
    rng = random.Random(25)
    out = silence(0.35)
    slap = bandpass(burst(0.03, 160, rng), 1300, 0.8)
    mix(out, [1.2 * v for v in slap], 0)
    mix(out, [0.5 * math.exp(-i / SR * 40) * math.sin(2 * math.pi * 130 * i / SR) for i in range(int(0.08 * SR))], 0)
    squish = bandpass([rng.uniform(-1, 1) * math.sin(math.pi * i / (0.2 * SR)) for i in range(int(0.2 * SR))], 900, 3.5, 260)
    mix(out, [0.9 * v for v in squish], 0.02)
    return out


def biz_rumble():
    """Easter egg: the rocket's engine, a low rolling roar that swells and crackles."""
    rng = random.Random(29)
    n = int(1.6 * SR)
    out, y, y2 = [], 0.0, 0.0
    for i in range(n):
        t = i / SR
        y += 0.08 * (rng.uniform(-1, 1) - y)     # deep roar
        y2 += 0.5 * (rng.uniform(-1, 1) - y2)    # hiss of the flame
        env = min(1, t / 0.5) * min(1, (1.6 - t) / 0.4)
        out.append(env * (2.2 * y + 0.15 * y2) * (1 + 0.25 * math.sin(2 * math.pi * 9 * t)))
    for _ in range(25):  # crackles
        mix(out, [0.25 * v for v in bandpass(burst(0.01, 400, rng), rng.uniform(800, 2500), 2)], rng.uniform(0.1, 1.4))
    return out


def biz_bang():
    """Easter egg: a firework going off, a thump and a crack with sparks fizzling after."""
    rng = random.Random(31)
    out = silence(1.4)
    mix(out, [0.9 * math.exp(-i / SR * 14) * math.sin(2 * math.pi * (60 + 40 * math.exp(-i / SR * 20)) * i / SR) for i in range(int(0.4 * SR))], 0)
    mix(out, [1.0 * v for v in bandpass(burst(0.05, 70, rng), 900, 0.6)], 0)
    for _ in range(40):
        mix(out, [0.12 * v for v in bandpass(burst(0.006, 600, rng), rng.uniform(2500, 6000), 3)], rng.uniform(0.15, 1.2) ** 1.5)
    return out


def biz_drum():
    """Bug Hunt countdown: a deep floor-tom hit, a pitch-dropping thump with a felt beater click and a short room tail."""
    rng = random.Random(37)
    out = silence(0.9)
    mix(out, [math.exp(-i / SR * 7) * math.sin(2 * math.pi * (52 * i / SR + 55 / 18 * (1 - math.exp(-i / SR * 18)))) for i in range(int(0.8 * SR))], 0)
    mix(out, [0.6 * v for v in bandpass(burst(0.02, 220, rng), 1800, 1.2)], 0)
    mix(out, [0.25 * v for v in bandpass(burst(0.5, 9, rng), 300, 0.7)], 0.005)
    return out


# vowel formants (Hz) for a young male voice: a, e, i, o, u
VOWELS = [(730, 1090, 2440), (530, 1840, 2480), (300, 2200, 2900), (570, 840, 2410), (330, 900, 2240)]


def talk_syllable(k):
    """The mini-me's mumble: one soft syllable, a buzzy voice pulse shaped by vowel formants (so it sounds like a
    mouth, not a beep), with a gentle consonant at the front and a falling pitch."""
    rng = random.Random(100 + k)
    f1, f2, f3 = VOWELS[k % len(VOWELS)]
    dur = 0.11 + 0.03 * rng.random()
    n, f0 = int(dur * SR), 165 * rng.uniform(0.94, 1.08)
    src, ph = [], 0.0
    for i in range(n):
        t = i / SR
        ph += (f0 * (1 - 0.12 * t / dur) * (1 + 0.01 * rng.uniform(-1, 1))) / SR
        if ph >= 1:
            ph -= 1
        src.append((1 - 2 * ph) + 0.15 * rng.uniform(-1, 1))  # sawtooth-ish glottal buzz plus breath
    voice = [0.0] * n
    for f, q, g in ((f1, 6, 1.0), (f2, 9, 0.5), (f3, 12, 0.18)):
        for i, v in enumerate(bandpass(src, f, q)):
            voice[i] += g * v
    for i in range(n):  # soft attack, round release
        t = i / n
        voice[i] *= min(1, t / 0.12) * (1 - t) ** 1.5
    out = [0.0] * (n + int(0.02 * SR))
    if k % 3 != 2:  # most syllables start with a soft b/d/m-like consonant
        mix(out, [0.25 * v for v in bandpass(burst(0.012, 350, rng), rng.choice([400, 1500, 2600]), 1.5)], 0)
    mix(out, voice, 0.006)
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
    write(folder, "biz_ambient_loop.wav", biz_lofi(), peak=0.7, fade=False)
    write(folder, "biz_tick.wav", biz_tick(), peak=0.7)
    # business-card-only sounds loaded at runtime with Resources.Load (pop and sparkle replace the shared ones
    # for the business card only), so they live under Assets/Resources/Audio
    res = os.path.join(folder, "..", "Resources", "Audio")
    os.makedirs(res, exist_ok=True)
    write(res, "biz_splat.wav", splat(), peak=0.75)
    write(res, "biz_pop.wav", biz_pop(), peak=0.6)
    write(res, "biz_sparkle.wav", biz_sparkle(), peak=0.6)
    write(res, "biz_rumble.wav", biz_rumble(), peak=0.7)
    write(res, "biz_bang.wav", biz_bang(), peak=0.8)
    write(res, "biz_drum.wav", biz_drum(), peak=0.85)
    for k in range(8):
        write(res, "biz_talk_%d.wav" % k, talk_syllable(k), peak=0.5)
