# OmniDoomFX

**DOOM‑style graphics + audio for R.E.P.O (Unity, BepInEx mod)**  
Auto‑loads `PLAYPAL`/`COLORMAP` from a DOOM WAD if present, applies a retro palette
pipeline, and “doom‑ifies” runtime SFX. Optional background music plays from a
single track in your mod folder.  
**NEW:** When OmniDoom is ON, enemies **and valuables** are flattened to a 2D
“paper” look; turning it OFF restores them. Re‑toggling ON re‑applies flattening
to everything currently in the scene.

---

## Features

- **True DOOM palette mode**  
  Auto‑detects `PLAYPAL` + `COLORMAP` inside a WAD (`doom.wad`, `doom2.wad`,
  Freedoom, etc.). Falls back to a curated DOOM‑ish look if no WAD is found.

- **2D “Flat” enemies & valuables**  
  While OmniDoom is enabled, newly spawned **enemies** and **valuables** are
  squashed along Z for a classic billboard vibe. OFF restores originals; ON
  re‑flattens existing scene objects.

- **Retro post‑processing**  
  Downscale, scanlines, light/sharpen controls, posterization, and palette
  quantization.

- **SFX “Doomify”**  
  Downmix to mono, sample‑hold ~11 kHz, 8‑bit‑style quantization, soft
  saturation.

- **Simple music toggle (single track)**  
  Loops `DoomMusic.mp3` from your mod folder. Music starts **OFF** and follows
  the OmniDoom master toggle.

---

## Installation

**Requires:** BepInEx (Unity) on the target game.

1) Build or drop the compiled plugin DLL into:

```
BepInEx/plugins/Omniscye-DoomFX/
```

2) Place your WAD (optional but recommended) anywhere under:

```
BepInEx/plugins/Omniscye-DoomFX/
```

Common names like `doom.wad`, `doom2.wad`, `freedoom1.wad`, `freedoom2.wad`,
`freedm.wad` are auto‑scanned.

3) (Optional music) Put your music file here:

```
BepInEx/plugins/Omniscye-DoomFX/Music/DoomMusic.mp3
```

(`.ogg` or `.wav` with the same base name also work as fallbacks.)

4) Launch the game.

> The mod uses BepInEx’s `Paths.PluginPath` to resolve a **portable, per‑mod**
> folder. No user profile/AppData paths.

---

## Controls (Hotkeys)

- **F8** – Toggle OmniDoom on/off (visuals + 2D flatten + music readiness)
- **F9** – Cycle style (DOOM / DMG_GREEN / DMG_GRAY / RGB_332 / NESISH)
- **F7** – Toggle scanlines
- **[ / ]** – Sharpen − / +
- **- / =** – Posterize − / +
- **PgUp / PgDn** – Target resolution + / −
- **; / '** – DOOM light scale − / + (palette mode)
- **, / .** – DOOM light bias − / + (palette mode)
- **F10** – Music play/pause (single track)
- **F11** – Toggle SFX doomify on/off
- **F12** – Reload music

The in‑game HUD header shows **“OmniDoom”** and notes if a WAD palette is
active.

---

## Folder Layout (example)

```
BepInEx/
  plugins/
    Omniscye-DoomFX/
      OmniDoom.dll
      doom.wad
      freedoom2.wad
      Music/
        DoomMusic.mp3
```

---

## How it works (short)

- **Graphics:** Captures the render to a low‑res buffer, then maps via DOOM
  palette (if WAD found) or a tuned fallback (gamma/contrast/warmth/posterize/
  scanlines/sharpen).
- **2D Flatten:** Hooks enemy/valuable spawns to squash Z‑scale while OmniDoom
  is ON; OFF restores original scales and clears markers. Re‑toggling ON
  re‑applies flattening to existing enemies/valuables.
- **Audio SFX:** Hooks `Sound.Play` to downmix mono, sample‑hold ~11 kHz, soft
  clip, then 8‑bit‑style quantize.
- **Music:** Loads `DoomMusic.mp3` from the mod’s `Music` folder; user toggles
  playback.

---

## Troubleshooting

- **“No music file found”**  
  Ensure the file exists at:  
  `BepInEx/plugins/Omniscye-DoomFX/Music/DoomMusic.mp3`  
  (Check filename/case on Linux.)

- **Not detecting WAD**  
  Put the WAD in `BepInEx/plugins/Omniscye-DoomFX/`. Supported names include
  `doom.wad`, `doom2.wad`, `freedm.wad`, `freedoom1.wad`, `freedoom2.wad`.
  If none found, fallback mode engages.

- **Flatten didn’t apply to an existing object**  
  Toggle **F8 OFF → ON**; it re‑applies to existing enemies/valuables.

---

## License

SPDX‑License‑Identifier: **MIT**

## Credits

- DOOM palette & colormap concept by id Software.
- Freedoom assets supported (see their respective licenses).
- Built with Harmony and BepInEx.
- OmniDoom by **Omniscye**.
