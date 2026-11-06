"""Original HS2 test loops using installed Surge XT, not the other game's audio."""
import importlib.util
import json
import hashlib
from pathlib import Path
import sys

HERE = Path(__file__).resolve().parent
OTHER = Path('F:/DangryGames/Games/2DSpaceShooterRemix')
sys.path.insert(0, str(OTHER / '.codex-check/music-deps'))
import numpy as np
import soundfile as sf
import mido

# Reuse the user's existing verified preset-loading workflow, read-only.
helper = OTHER / 'ProjectDocs/AudioSources/Music/surge_render.py'
spec = importlib.util.spec_from_file_location('surge_workflow', helper)
workflow = importlib.util.module_from_spec(spec)
spec.loader.exec_module(workflow)
renderer = workflow.SurgeRenderer()
out = HERE.parents[1] / 'Game/Content/Audio'
out.mkdir(parents=True, exist_ok=True)
rate = 44100
manifest = {'synth': 'Surge XT 1.3.4', 'renderer': 'Python/Pedalboard',
            'workflow': str(helper), 'sounds': []}
for name, patch, pitches, peak, mono in [
    ('machine_hum', 'Basses/FM Bass 2.fxp', [31], .16, True),
    ('basement_drone', 'Pads/Bell Pad.fxp', [48, 54, 55], .09, False),
]:
    renderer.load(patch, HERE / (name + '.vstpreset'))
    notes = [(0, 28, p, 52) for p in pitches]
    data = renderer.render(notes, 60, 30)
    # Select the sustained portion and crossfade its ends to avoid a hard loop seam.
    data = data[:, 10 * rate:23 * rate]
    fade = rate
    ramp = np.linspace(0, 1, fade, endpoint=False, dtype=np.float32)
    blend = data[:, -fade:] * (1 - ramp) + data[:, :fade] * ramp
    data = np.concatenate((data[:, fade:-fade], blend), axis=1)
    if mono:
        data = np.mean(data, axis=0, keepdims=True)
    data -= np.mean(data, axis=1, keepdims=True)
    data *= peak / np.max(np.abs(data))
    wav = out / (name + '.wav')
    sf.write(wav, data.T, rate, subtype='PCM_16')
    midi = mido.MidiFile()
    track = mido.MidiTrack()
    midi.tracks.append(track)
    track.append(mido.MetaMessage('set_tempo', tempo=1000000))
    for p in pitches:
        track.append(mido.Message('note_on', note=p, velocity=52, time=0))
    for i, p in enumerate(pitches):
        track.append(mido.Message('note_off', note=p, velocity=0, time=28 * 480 if i == 0 else 0))
    midi.save(HERE / (name + '.mid'))
    manifest['sounds'].append({'name': name, 'patch': patch, 'pitches': pitches,
        'seconds': data.shape[1] / rate, 'channels': data.shape[0],
        'peakDbfs': float(20 * np.log10(np.max(np.abs(data)))),
        'rmsDbfs': float(10 * np.log10(np.mean(data ** 2))),
        'loopSeamDelta': float(np.max(np.abs(data[:, -1] - data[:, 0]))),
        'sha256': hashlib.sha256(wav.read_bytes()).hexdigest()})
(HERE / 'surge-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
print(json.dumps(manifest, indent=2))
