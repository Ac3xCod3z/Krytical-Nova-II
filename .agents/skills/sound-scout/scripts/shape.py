"""Makes a library sound ready for the game: cut to its sounding part, faded, levelled. The audition page and the
bake both use these, so what is auditioned is what is baked."""
import numpy as np
import soundfile

import common

RATE = 44100
ONE_SHOT_LOUDNESS = -16.0   # the loudest 400 ms, dBFS RMS
LOOP_LOUDNESS = -24.0       # the whole loop, dBFS RMS
PEAK_CEILING = -2.0


def _gain_to(audio, measured, target_db):
    gain = 10 ** (target_db / 20) / max(measured, 1e-6)
    return audio * min(gain, 10 ** (PEAK_CEILING / 20) / max(float(np.abs(audio).max()), 1e-6))


def _loudest(mono, seconds=0.4):
    win = min(len(mono), int(RATE * seconds))
    power = np.cumsum(np.concatenate(([0.0], mono.astype(np.float64) ** 2)))
    return float(np.sqrt((power[win:] - power[:-win]).max() / win)) if len(mono) > win else float(np.sqrt(np.mean(mono ** 2)))


def vary(audio, pitch=0.0, tilt=0.0):
    """A different take of the same sound: `pitch` semitones up or down (it gets shorter or longer with it, as a
    real object would), `tilt` dB per octave brighter (+) or darker (-) around 1 kHz."""
    if pitch:
        ratio = 2 ** (pitch / 12)
        at = np.arange(0, len(audio) - 1, ratio)
        audio = np.stack([np.interp(at, np.arange(len(audio)), audio[:, c]) for c in range(audio.shape[1])], axis=1)
    if tilt:
        spectrum = np.fft.rfft(audio, axis=0)
        freqs = np.maximum(np.fft.rfftfreq(len(audio), 1 / RATE), 20.0)
        gain = 10 ** (np.clip(tilt * np.log2(freqs / 1000.0), -12, 12) / 20)
        audio = np.fft.irfft(spectrum * gain[:, None], n=len(audio), axis=0)
    return audio.astype(np.float32)


def one_shot(path, stereo=False, max_seconds=None, start=None, pitch=0.0, tilt=0.0):
    """(samples, channels) float32: from the first sound to the end of the tail, 2 ms in, 20 ms out, levelled."""
    audio = common.decode(common.absolute(path), channels=2, rate=RATE)
    if start:
        audio = audio[int(start * RATE):]
    mono = audio.mean(axis=1)
    hop = RATE // 400
    usable = len(mono) // hop * hop
    env = np.abs(mono[:usable]).reshape(-1, hop).max(axis=1)
    peak = env.max()
    first = int(np.nonzero(env >= peak * 0.01)[0][0])      # -40 dB
    last = int(np.nonzero(env >= peak * 0.003)[0][-1])     # -50 dB
    a = max(0, first * hop - RATE // 300)
    b = min(len(mono), (last + 1) * hop + RATE // 50)
    cut = max_seconds is not None and b - a > int(max_seconds * RATE)
    if cut:
        b = a + int(max_seconds * RATE)
    audio = audio[a:b].copy()
    fade_in = min(RATE // 500, len(audio) // 8)
    fade_out = min(int(RATE * (0.6 if cut else 0.02)), len(audio) // 3)
    if fade_in:
        audio[:fade_in] *= np.linspace(0, 1, fade_in)[:, None]
    if fade_out:
        audio[-fade_out:] *= np.linspace(1, 0, fade_out)[:, None]
    if not stereo or np.allclose(audio[:, 0], audio[:, 1], atol=1e-4):
        audio = audio.mean(axis=1, keepdims=True)
    audio = vary(audio, pitch, tilt)
    return _gain_to(audio, _loudest(audio.mean(axis=1)), ONE_SHOT_LOUDNESS).astype(np.float32)


def mix(layers, stereo=False, max_seconds=None, pitch=0.0, tilt=0.0):
    """Several one-shots laid over each other: [{"path", "gain" (1), "delay" seconds (0), "start", "max_seconds"}]. Levelled as one."""
    parts = []
    for layer in layers:
        audio = one_shot(layer["path"], stereo=True, max_seconds=layer.get("max_seconds", max_seconds), start=layer.get("start"),
                         pitch=layer.get("pitch", 0.0), tilt=layer.get("tilt", 0.0))
        if audio.shape[1] == 1:
            audio = np.repeat(audio, 2, axis=1)
        parts.append((int(layer.get("delay", 0) * RATE), audio * layer.get("gain", 1.0)))
    out = np.zeros((max(d + len(a) for d, a in parts), 2), dtype=np.float32)
    for delay, audio in parts:
        out[delay:delay + len(audio)] += audio
    if max_seconds and len(out) > int(max_seconds * RATE):
        out = out[:int(max_seconds * RATE)]
        out[-RATE // 2:] *= np.linspace(1, 0, RATE // 2)[:, None]
    if not stereo:
        out = out.mean(axis=1, keepdims=True)
    out = vary(out, pitch, tilt)
    return _gain_to(out, _loudest(out.mean(axis=1)), ONE_SHOT_LOUDNESS).astype(np.float32)


def steadiest(mono, seconds):
    """Start (in samples) of the stretch whose loudness changes least from second to second."""
    step = RATE // 2
    count = len(mono) // step
    if count * step <= int(seconds * RATE):
        return 0
    level = 20 * np.log10(np.sqrt((mono[:count * step].reshape(count, step) ** 2).mean(axis=1)) + 1e-6)
    span = int(seconds * 2)
    best, best_start = None, 0
    for i in range(2, count - span - 2):   # not the first or last second: recordings fade there
        piece = level[i:i + span]
        wobble = piece.max() - piece.min()
        if best is None or wobble < best:
            best, best_start = wobble, i
    return best_start * step


def loop(path, seconds=24.0, fade=2.0, start=None):
    """A seamless stereo loop: the steadiest stretch (or the one from `start`), its end crossfaded into its start."""
    audio = common.decode(common.absolute(path), channels=2, rate=RATE)
    a = int(start * RATE) if start is not None else steadiest(audio.mean(axis=1), seconds + fade)
    piece = audio[a:a + int((seconds + fade) * RATE)]
    x = int(fade * RATE)
    if len(piece) < 3 * x:
        raise ValueError("too short to loop: " + path)
    t = np.linspace(0, np.pi / 2, x)[:, None]
    out = np.concatenate([piece[x:-x], piece[-x:] * np.cos(t) + piece[:x] * np.sin(t)])
    return _gain_to(out, float(np.sqrt(np.mean(out ** 2))), LOOP_LOUDNESS).astype(np.float32)


def write(path, audio):
    soundfile.write(path, audio, RATE, subtype="PCM_16")
