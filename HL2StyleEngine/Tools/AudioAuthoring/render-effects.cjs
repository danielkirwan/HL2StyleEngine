'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const source = process.argv[2] || 'F:/DangryGames/NewGames/FoodTruckSim/jsfxr';
const { Params, sfxr } = require(path.join(source, 'sfxr.js'));
const output = path.resolve(__dirname, '../../Game/Content/Audio');
fs.mkdirSync(output, { recursive: true });
const designs = [
  ['relay_01', { wave_type: 3, p_env_attack: .002, p_env_sustain: .025, p_env_decay: .16, p_base_freq: .19, p_lpf_freq: .34 }],
  ['relay_02', { wave_type: 3, p_env_attack: .002, p_env_sustain: .03, p_env_decay: .18, p_base_freq: .21, p_lpf_freq: .39 }],
  ['relay_03', { wave_type: 3, p_env_attack: .003, p_env_sustain: .02, p_env_decay: .19, p_base_freq: .17, p_lpf_freq: .3 }],
  ['panel_confirm', { wave_type: 2, p_env_attack: .025, p_env_sustain: .08, p_env_decay: .27, p_base_freq: .33, p_arp_mod: .35, p_arp_speed: .67 }],
];
const manifest = { tool: 'jsfxr', version: require(path.join(source, 'package.json')).version,
  sourceSha256: crypto.createHash('sha256').update(fs.readFileSync(path.join(source, 'sfxr.js'))).digest('hex'), sounds: [] };
for (let i = 0; i < designs.length; i++) {
  const [name, design] = designs[i];
  let seed = 7301 + i;
  Math.random = () => { seed = (Math.imul(1664525, seed) + 1013904223) >>> 0; return seed / 4294967296; };
  const params = Object.assign(new Params(), design, { sample_rate: 44100, sample_size: 16, sound_vol: .15 });
  const wave = sfxr.toWave(params), bytes = Buffer.from(wave.wav);
  if (wave.clipping) throw Error('Clipped effect: ' + name);
  let peak = 0, energy = 0;
  for (let o = 44; o < bytes.length; o += 2) { let v = bytes.readInt16LE(o) / 32768; peak = Math.max(peak, Math.abs(v)); energy += v * v; }
  if (peak < .001 || peak > .8) throw Error('Invalid effect level');
  fs.writeFileSync(path.join(output, name + '.wav'), bytes);
  fs.writeFileSync(path.join(__dirname, name + '.jsfxr.json'), JSON.stringify(params, null, 2) + '\n');
  manifest.sounds.push({ name, seed: 7301 + i, seconds: (bytes.length - 44) / 88200,
    peakDbfs: 20 * Math.log10(peak), rmsDbfs: 10 * Math.log10(energy / ((bytes.length - 44) / 2)),
    sha256: crypto.createHash('sha256').update(bytes).digest('hex') });
}
fs.writeFileSync(path.join(__dirname, 'effects-manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
console.log(JSON.stringify(manifest, null, 2));
