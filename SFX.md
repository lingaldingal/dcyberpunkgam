# Sound Effects — MOSS-SoundEffect v2.0 Pipeline

All game sound effects are generated with **MOSS-SoundEffect v2.0** (OpenMOSS,
Apache-2.0). Do NOT source SFX from anywhere else.

## Where the files live

| What | Path |
|---|---|
| **Game SFX (OGG)** | `assets/audio/sfx/` in this repo |
| **Raw WAVs (pre-OGG)** | `~/MOSS-TTS/moss_soundeffect_v2/sfx_raw/` |
| **Generation script** | `~/MOSS-TTS/moss_soundeffect_v2/generate_sfx.py` |
| **MOSS-TTS repo** | `~/MOSS-TTS/` (cloned from `github.com/OpenMOSS/MOSS-TTS`) |
| **Model weights** | `~/.cache/huggingface/hub/models--OpenMOSS-Team--MOSS-SoundEffect-v2.0/` (~5.3 GB) |
| **Python venv** | `~/MOSS-TTS/moss_soundeffect_v2/.venv/` (Python 3.12, torch 2.9.0+cu128) |

## Current SFX inventory

| File | Duration | Description |
|---|---|---|
| `pistol_shot.ogg` | 2.0 s | Sharp, bright 9 mm handgun crack (high-freq, 12 kHz peak). **+6dB gain applied** so it sits clearly below the rifle. |
| `rifle_shot.ogg` | 3.0 s | Deep, booming assault rifle bang (strong low-end). **−3dB gain applied** to de-clip. Rifle is ~13 dB louder in RMS than the pistol. |
| `knife_slash.ogg` | 1.5 s | Quick, light combat knife swish (short, mid-weighted) |
| `katana_slash.ogg` | 2.5 s | Heavy, sustained katana whoosh (longer, deeper, metallic ring) |
| `grenade_throw.ogg` | 1.5 s | Small hand grenade arcing through the air |
| `grenade_explosion.ogg` | 3.0 s | Concussive grenade blast (deep low-frequency boom, 54% energy below 200 Hz) |

## How to generate new SFX

```bash
cd ~/MOSS-TTS/moss_soundeffect_v2

# Generate specific SFX by name (comma-separated):
TORCHDYNAMO_DISABLE=1 PYTORCH_ALLOC_CONF=expandable_segments:True \
  .venv/bin/python generate_sfx.py \
  --steps 50 --max-sec 5 \
  --out sfx_raw \
  --only "my_new_sfx" \
  --seed 42

# Then convert to OGG for Godot:
ffmpeg -y -i sfx_raw/my_new_sfx.wav \
  -ac 1 -codec:a libvorbis -q:a 5 \
  /home/bigal/aibranch1/branch1/assets/audio/sfx/my_new_sfx.ogg

# Re-import into Godot:
/home/bigal/Applications/GodotMono/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64 \
  --headless --path /home/bigal/aibranch1/branch1 --import
```

### To add a new SFX to the script

Edit the `SFX` list in `generate_sfx.py`. Each entry is:
```python
(
    "filename_without_extension",   # output name
    "Natural language prompt…",     # describe the sound clearly
    duration_in_seconds,            # 1.0 – 30.0 (keep ≤ 5 for game SFX)
),
```

### Prompt-writing tips

- Be **specific** about the sound's character: "sharp, bright, short crack" vs
  "deep, booming, thunderous bang"
- Mention the **context**: "in an empty concrete room" / "in an open junkyard"
- Contrast weapons explicitly: "much bigger and darker than a pistol"
- The model prepends `duration: Xs` automatically (`append_duration_suffix=True`)
- **Seed matters**: the same prompt can produce silence or garbage with a bad
  seed. If a file comes out near-silent (RMS < 0.005), delete the WAV and
  regenerate with a different `--seed`

## Key generation parameters

| Parameter | Value used | Notes |
|---|---|---|
| `--steps` | 50 | 100 = max quality, 50 = good enough for game SFX (~1.7 s per clip) |
| `--max-sec` | 5 | **Critical for 16 GB VRAM**. The model denoises the full latent then crops. 30 s default OOMs the 5080. Keep at 5 for game SFX (max clip is 3 s). |
| `--cfg` | 4.0 | Default classifier-free guidance |
| `--seed` | 42 / 1337 | Some seeds produce silence. Try 1337, 7, 99, 2024 if a clip is silent. |

## Environment notes

- **RTX 5080, 16 GB VRAM** — runs fine in bfloat16 with `--max-sec 5`
- `TORCHDYNAMO_DISABLE=1` avoids Triton CUDA Graph compile errors
- `PYTORCH_ALLOC_CONF=expandable_segments:True` reduces fragmentation OOM
- The venv is **isolated** (Python 3.12, numpy 1.26, transformers 4.57.1,
  diffusers 0.37.1, torch 2.9.0+cu128) — do NOT mix with other Python envs
- Model auto-downloads from HuggingFace on first use (cached in
  `~/.cache/huggingface/hub/`)

## Godot integration

SFX files are in `assets/audio/sfx/`. Load them in C# like this:
```csharp
var shot = AudioStreamLoader.Load("res://assets/audio/sfx/pistol_shot.ogg") as AudioStreamOggVorbis;
var player = new AudioStreamPlayer { Stream = shot, VolumeDb = -6.0f };
AddChild(player);
player.Play();
```
Or use `AudioServer.PlaySound()` with a bus for more control.
Run `--import` after adding new OGG files so Godot creates the `.import` sidecars.
