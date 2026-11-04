# 128×64 Isometric Tile Migration

**Status:** Complete and validated (build + full test suite + rendered test scene).
**Date:** 2026-11-03

## Official Standard

The game's official isometric ground-tile standard is now **128×64 pixels per
tile** (2:1 projection). This is the **native artwork resolution**, not a
display-resolution requirement — logical tile dimensions never change with
monitor resolution.

### Critical distinction: footprint vs. canvas

The **128×64** value is the **ground-plane footprint** (the on-screen diamond),
not the full image size of every asset. The engine projects a **128×128 SQUARE**
seamless texture onto the 128×64 diamond via the 2:1 iso skew:

```
logical (x, y)  ->  screen  ( (x-y)/2 , (x+y)/4 ) * TileSize
```

So a ground tile is authored as a **128×128 square PNG** and stored in
`assets/tiles/128x64/`. The skew does the diamond projection — **never**
generate a 128×64 image that "looks like" a diamond.

Objects extending above the ground plane use larger **transparent** canvases:

| Asset                     | Canvas        | Ground footprint |
|---------------------------|---------------|------------------|
| Standard ground tile      | 128×128 (square) | 128×64 diamond |
| Small ground decoration   | 64×64 – 128×128 | ≤ 128×64         |
| Medium environmental prop | 128×128 – 256×256 | 128×64         |
| Large environmental prop  | 256×256 – 512×512 | 128×64         |
| Character sprite          | 128×192 – 128×256 | 128×64         |

## Audit (original configuration)

- **Engine:** Godot 4.7.2-stable Mono, C# (.NET 8), 2D, `gl_compatibility` renderer.
- **Old grid:** `TileSize = 64` → 64×64 logical square → **64×32** on-screen diamond.
  (AGENTS.md previously implied 64×32 tiles; measured: 64px logical, 64×32 screen.)
- **Projection:** `Iso` Node2D with skew `Transform2D(X=(.5,.25), Y=(-.5,.25))`;
  all ground/wall/highlight/tokens drawn in LOGICAL board pixels.
- **Tiles:** 64×64 seamless PNGs in `assets/textures/floor/`
  (`junk_concrete/asphalt/dirt/gravel` + a 1024×1024 set).
- **Coordinate convention preserved:** grid coords (`Walkable[y,x]`, `Vector2i`
  cells) are stored separately from rendered pixels; `TileSize` is the single
  scale factor between them.

## What changed

### Engine (C#)
| File | Change |
|------|--------|
| `scripts/Level01.cs` | `TileSize` 64→**128**; `LoadFloorTexture` path → `assets/tiles/128x64/` (with fallback); token-name-label lift −34→−68 |
| `scripts/IsoGrid.cs` | `WallHeight` 18→**36**; HP-label lift −34→−68 |
| `scripts/TokenNode.cs` | diamond `Pad` 9→**18** |
| `scripts/PathPreview.cs` | endpoint `EndRadius` 32→**64** |
| `dcyberpunkgam.csproj` | **exclude `assets_backup_pre_128_migration/**/*.cs`** from compilation (the backup's `Level01.cs` was being globbed by the Godot SDK → 282 duplicate-definition build errors) |

### Display / rendering (`project.godot`)
- Viewport 1280×720; `gl_compatibility`; **`texture_filter=0` (nearest-neighbor)**
  globally — crisp pixel art, no smoothing. Same logical tile size at every
  resolution (1080p / 1440p / 4K verified).

### Assets
- `assets/tiles/128x64/` — the official tile directory:
  - `junk_concrete/asphalt/dirt/gravel.png` — original junkyard tiles, **2×
    nearest-neighbor** enlarged 64×64→128×128 (Category A, no smoothing).
  - `grass, dirt, cobblestone, stone, sand, snow, wood, marble, dungeon,
    fantasy.png` — **10 native 128×128** textures generated via ComfyUI
    (Category C — regenerated at native resolution, not enlarged).
- `assets/props/tree.png` (128×256), `assets/props/rock.png` (128×128) —
  placeholder environmental props.
- `assets/characters/character_placeholder.png` (128×192) — placeholder figure.
- `assets_backup_pre_128_migration/` — full pre-migration backup (52 files).

### Tests
- `tests/flow_test.gd`, `tests/combat_test.gd`: `const TILE := 128`.
- `tests/flow_test.gd` camera zoom/pan section: bounds updated for the doubled
  iso diamond (x:[−1024,1792], y:[0,1408]).
- `tests/_tile_scene_shot.gd` + `tests/_tileboard.gd` — new rendered validation
  scene (6×6 material patchwork + tree/rock/character, depth-sorted).

## ComfyUI pipeline (for future assets)

- **Workflow:** `~/ComfyUI/workflows/Isometric_128x64_Tile_Generator.json`
  (SDXL 1.0 + NeriJS `pixel-art-xl` LoRA @1.0, 512×512, dpmpp_2m/karras 28
  steps, CFG 7.0). Generates a **seamless square** texture; the engine skew
  makes it the 128×64 diamond.
- **Batch script:** `~/ComfyUI/scripts/gen_128x64_tiles.py`
  ```
  ~/ComfyUI/.venv/bin/python ~/ComfyUI/scripts/gen_128x64_tiles.py \
      --only grass,dirt --seed 12345 \
      --out /home/bigal/aibranch2/branch2/assets/tiles/128x64
  ```
  Per preset it: generates 512×512 → seamless offset-blend → 128×128
  nearest-neighbor downscale → median-cut palette (48) → lossless PNG. Also
  writes `*_preview.png` (8×) and `*_diamond.png` (skewed) to the `--out` dir
  (move those out of the game dir after generation).
  The 10 presets (grass, dirt, cobblestone, stone, sand, snow, wood, marble,
  dungeon, fantasy) live in the script's `PRESETS` dict.

## Validation results

- **Build:** `dotnet build` → 0 errors.
- **Test suite (headless, Mono):** flow, combat, overwatch, cutscene,
  resolution + Rook click-repro — **all PASS**.
- **Rendered test scene** (`tests/_tile_scene_*.png` at 1920×1080, 2560×1440,
  3840×2160), analyzed with PIL:
  - Diamond geometry: exactly **128.0 × 64.0** px.
  - **All 36 tiles' edges align** (shared vertices, no gaps/overlap) —
    horizontal, vertical, and diagonal adjacency.
  - Materials render distinctly (grass/dirt/stone/cobble).
  - **Crisp:** 55 color runs across a 192px strip (nearest-neighbor, no
    smoothing/anti-aliasing).
  - Tree (green canopy) + character present, **depth-sorted** by board-space y.
  - Identical content (336 colors) at all 3 resolutions → consistent pixel
    density; logical tile size is resolution-independent.

## Unresolved / notes

- The 10 generated tiles and the tree/rock/character are **placeholders** —
  they are functional and on-grid, but a real art pass (Category B refinement:
  matched lighting direction, palette, shadow treatment) would raise them to
  production quality. The pipeline + standard are in place to regenerate or
  refine any of them.
- `assets/textures/floor/` (old 64px + 1024px sets) is left in place,
  superseded by `assets/tiles/128x64/`; `Level01` now loads from the new dir.
- Wall textures and per-tile texture selection are not part of this migration
  (the floor-texture system extends naturally).
