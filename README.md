# Desktop Pet

A tiny pixel-art pet that wanders around your Windows desktop. It stays above every
window but ignores the mouse completely, so you can click and type straight through it.

Comes with a **koi** and a **heron**. Adding more animals means dropping in a folder.

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

Make a folder `pets\<name>\` with two files:

- `sheet.png`: one row of animation frames, drawn **facing right**, with a transparent background
- `pet.ini`:

  ```ini
  frameWidth=32   ; size of one frame in pixels
  frameHeight=32
  frames=6        ; number of frames in the row
  fps=8           ; animation speed while moving at full pace
  speed=50        ; pixels per second at 1x
  ```

Rebuild (or copy the folder into `bin\pets\`) and restart. It appears in the Pet menu,
named after the folder. The pet is mirrored automatically when it moves left.

## Redrawing the built-in sprites

The koi and heron are drawn by `tools/draw_sprites.py` (needs Pillow):

```
py tools/draw_sprites.py --preview
```

`--preview` also writes an enlarged `preview.png` beside each sheet for checking.
