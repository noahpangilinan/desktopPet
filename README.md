# Desktop Pet

A tiny pixel-art pet that wanders around your desktop on **Windows** or **macOS**. It stays
above every window but ignores the mouse completely, so you can click and type straight
through it.

Comes with four animals, and adding more means dropping in a folder:

| Pet | How it moves |
| --- | --- |
| **Koi** | Swims like a real one: a few strong tail beats, then a long glide, sometimes settling to rest. Turns to face any direction along curving, meandering paths |
| **Flatfish** | A flounder that swims the same way, its fin skirt rippling along its edges as it goes |
| **Manta ray** | Deep indigo with purple Filipino-weave motifs on its back (a diamond chain down the spine, zigzag bands across the wings). Flies through the screen with slow, rippling wingbeats and long glides |
| **Heron** | A Japanese-style heron in just white, black and a red crown, with ink-thin legs and bill. Flaps steadily along gentle arcs. Every so often it comes down to land on the taskbar (or the Dock on a Mac), stands around, hops and turns, then takes off again |

Both versions are tiny and need nothing extra installed beyond the platform's own compiler.
They use about 0.2% CPU, and the picture only changes when the animation frame does.

## Windows

```
build.cmd
bin\DesktopPet.exe
```

`build.cmd` uses the C# compiler that ships with Windows
(`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) and copies `pets\` next to the
~20 KB exe. Run it again after changing code or sprites.

**Controls:** everything lives in the **tray icon**, bottom-right near the clock. If it's not
there, click the `^` arrow to show hidden icons. **Right-click** the icon for the menu. To keep
it visible, drag it out onto the taskbar, or turn on DesktopPet under **Settings →
Personalization → Taskbar → Other system tray icons**.

Choices are saved in `bin\settings.ini`. **Start with Windows** adds an entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## macOS

Needs Apple's command line tools (run `xcode-select --install` once), macOS 11 or later.

```
sh mac/build.sh
open "mac/build/Desktop Pet.app"
```

This builds a menu-bar-only app (no Dock icon) with the pets inside it. You can drag
`Desktop Pet.app` into `/Applications` if you like. The pet floats above every window,
including full-screen apps, and follows you to every desktop (Space).

**Controls:** click the pet's icon in the **menu bar**, top-right of the screen.

Choices are saved in the app's preferences (`defaults read com.noahpangilinan.desktoppet`).
**Start at Login** writes `~/Library/LaunchAgents/com.noahpangilinan.desktoppet.plist`
pointing at the app. If you move the app afterwards, turn it off and on again.

## The menu

| Item | What it does |
| --- | --- |
| Pet | Switch between every animal in `pets/` |
| Size | 2x, 3x, 4x or 6x pixel scale |
| Pause | Freeze the pet; uses no CPU while paused |
| Start with Windows / Start at Login | Launch automatically when you sign in |
| Quit | Exit |

## Adding an animal

Make a folder `pets/<name>/` with a `sheet.png` (transparent background) and a `pet.ini`.
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
- **Top-down** (`headings=16`; e.g. the koi, flatfish and manta): each animation takes 16 rows,
  one per heading. Heading 0 faces right and each next row turns 22.5° clockwise. The
  pet turns to face exactly where it's going, so it can swim in any direction.

Animation names the app understands:

| Name | Used for |
| --- | --- |
| `swim` | The tail-beat burst. Played fast while the pet pushes forward |
| `glide` | Coasting and resting. If a pet has this, it swims in bursts and glides between them |
| `land`, `stand`, `takeoff` | Perching, for side-on pets. With all three, the pet sometimes flies down to the bottom of the screen (the top of the taskbar or Dock), plays `land` once, loops `stand`, then plays `takeoff` once and flies off. Draw standing frames with the feet on the bottom row |
| `hop` | Optional while perched: played once per short hop along the ground |
| anything else | The first animation is used for normal steady movement (the heron's `fly`) |

Rebuild and restart. The new animal appears in the Pet menu, named after its folder.
The same folder works on both Windows and macOS.

## Redrawing the built-in sprites

The koi, flatfish, manta and heron are drawn by `tools/draw_sprites.py` (needs Pillow):

```
py tools/draw_sprites.py --preview
```

`--preview` also writes an enlarged `preview.png` beside each sheet for checking. Top-down
pets are drawn from a shape description at every heading, not by rotating finished pixels,
so they stay crisp at every angle.

## Layout

```
src/            Windows app (C#)
mac/            macOS app (Swift): Sources/, Info.plist, build.sh
pets/           one folder per animal: sheet.png + pet.ini (shared by both apps)
tools/          sprite generator
build.cmd       Windows build
```
