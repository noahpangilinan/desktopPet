# Desktop Pet

A tiny pixel-art pet that wanders around your Windows desktop. It stays above every
window but ignores the mouse completely, so you can click and type straight through it.

Comes with a **koi** and a **heron**. Adding more animals means dropping in a folder.

The koi swims like a real one: a few strong tail beats, then a long glide, sometimes
settling to rest. It turns to face any direction, following curving, meandering paths.
The heron flaps steadily along gentle arcs.

- ~20 KB exe, nothing to install: it builds with the C# compiler that ships with Windows
- About 0.2% CPU and ~30 MB of RAM. Frames are pre-rendered once, so each tick only moves the window
- Never steals focus and stays out of the taskbar and Alt-Tab

## Build and run

```
build.cmd
bin\DesktopPet.exe
```

`build.cmd` uses `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` and copies
`pets\` next to the exe. Run it again after changing code or sprites.

## Controls

The pet can't be clicked, so everything lives in the **tray icon** (bottom-right, maybe
under the `^` arrow). Right-click it for:

| Item | What it does |
| --- | --- |
| Pet | Switch between every animal in `pets\` |
| Size | 2x, 3x, 4x or 6x pixel scale |
| Pause | Freeze the pet; uses no CPU while paused |
| Start with Windows | Adds/removes a `HKCU\...\Run` entry for the exe |
| Quit | Exit |

Choices are saved in `settings.ini` next to the exe.

## Adding an animal

Make a folder `pets\<name>\` with a `sheet.png` (transparent background) and a `pet.ini`.
Each `[section]` in `pet.ini` is one animation, and `row` says where it starts in the sheet:

```ini
frameWidth=32   ; size of one frame in pixels
frameHeight=32
speed=50        ; pixels per second at 1x
headings=1      ; see below

[fly]
row=0           ; first row of this animation in sheet.png
frames=6        ; frames across the row
fps=8           ; animation speed at full pace
```

There are two kinds of pet:

- **Side-on** (`headings=1`, the default; e.g. the heron): one row per animation, drawn
  **facing right**. The app mirrors it when it moves left and keeps it mostly level.
- **Top-down** (`headings=16`; e.g. the koi): each animation takes 16 rows, one per
  heading. Heading 0 faces right and each next row turns 22.5° clockwise. The pet
  turns to face exactly where it's going, so it can swim in any direction.

Animation names the app understands:

| Name | Used for |
| --- | --- |
| `swim` | The tail-beat burst. Played fast while the pet pushes forward |
| `glide` | Coasting and resting. If a pet has this, it swims in bursts and glides between them |
| anything else | The first animation is used for normal steady movement (the heron's `fly`) |

Rebuild (or copy the folder into `bin\pets\`) and restart. It appears in the Pet menu,
named after the folder.

## Redrawing the built-in sprites

The koi and heron are drawn by `tools/draw_sprites.py` (needs Pillow):

```
py tools/draw_sprites.py --preview
```

`--preview` also writes an enlarged `preview.png` beside each sheet for checking.
