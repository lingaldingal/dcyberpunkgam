# DCYBERPUNK — Game Design Outline

Top-down, sprite-based cyberpunk RPG.

## Core loop

1. Player controls a party of up to 5 characters.
2. Stages: enemy encounters, plot screens, and missions.
3. Defeat enemies → gain EXP → level up → choose an upgrade.
4. Progress through stages, story, and missions.

## Current scaffold (what exists today)

- **Main menu** (`scenes/main_menu.tscn`) — title screen with Start Game / Quit.
  This is the project's main scene; the old test arena no longer loads directly.
- **GameManager** autoload (`scripts/game_manager.gd`) — holds the party
  (max 5), the active character index, and scene flow (menu → stage).
- **CharacterData** (`scripts/character_data.gd`) — Resource with stats
  (hp/mp/attack/defense), level, EXP, and a simple level-up rule.
- **Stage 01** (`scenes/stage_01.tscn`) — the old test arena, now a proper
  stage scene: hosts the active character and shows party status in the HUD.

## Planned architecture (not built yet)

- `scenes/stages/` — one scene per stage; a stage config (id, enemies,
  rewards) drives what loads.
- **Enemies** — CharacterBody2D with HP, AI, and EXP drops.
- **Combat** — turn-based or real-time (decision pending); stage clears on
  enemy wipe.
- **Upgrade selection** — after level-ups / stage clears, pick 1 of N upgrades.
- **Plot screens / missions** — dialogue nodes between stages.
- **Party control** — switch between up to 5 characters (active one is
  player-controlled; others follow or wait, decision pending).
- **Save system** — party + stage progress persisted to disk.

## Controls

- WASD / arrow keys — move the active character.

## Validation

Headless Godot checks and a functional scene-flow test; see `AGENTS.md`.
