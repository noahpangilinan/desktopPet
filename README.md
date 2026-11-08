# Desktop Pet

Tiny pixel-art pets that wander around your desktop on **Windows** or **macOS**. They stay
above every window but ignore clicks completely, so you can click and type straight
through them. Run one or several at once, across all your monitors.

**Download:** grab `DesktopPet-windows.zip` or `DesktopPet-macos.zip` from the
[latest release](../../releases/latest), unzip it and run it. To build it yourself, see
[Windows](#windows) or [macOS](#macos) below.

Comes with four animals, and adding more means dropping in a folder:

| Pet | How it moves |
| --- | --- |
| **Koi** | Swims like a real one: a few strong tail beats, then a long glide, sometimes settling to rest. Turns to face any direction along curving, meandering paths, leaving ripples |
| **Flatfish** | A flounder that swims the same way, its fin skirt rippling along its edges as it goes |
| **Manta ray** | Deep indigo with purple Filipino-weave motifs on its back (a diamond chain down the spine, zigzag bands across the wings). Flies through the screen with slow, rippling wingbeats and long glides |
| **Heron** | A Japanese-style heron in just white, black and a red crown, with ink-thin legs and bill. Flaps steadily along gentle arcs. Every so often it comes down to land on the taskbar (or the Dock on a Mac) or on the top edge of a window, stands around, hops and turns, then takes off again |

### They notice you

- **Fish are moody about the cursor.** Swipe past and they dart away. Let the cursor drift
  near and they're sometimes spooked, sometimes hungry: they swim up and nibble it a few
  times, with a ripple at each bite, like fish taking food.
- **The heron is shy.** If the cursor comes close it takes off, and if the window it's standing
  on moves, closes or gets covered, it startles and flies away.
- **Pets give each other room** and steer around one another.
- **Full-screen apps stay clear.** Pets hide while a game, video or presentation is
  full screen (a menu option keeps them showing).

### Light on the computer

Both versions need nothing installed beyond the platform's own compiler, or you can download
a ready-made build from the [Releases](../../releases) page. The **Activity** slider in the menu
trades awareness for CPU. Measured on Windows with two pets:

| Activity | Updates | What runs | CPU |
| --- | --- | --- | --- |
| Calm | 12/s | Just wandering (birds land only on the taskbar) | ~0.2% |
| Relaxed | 20/s | + ripples, window perching (checked every 2 s), steering around each other | ~0.4% |
| Normal | 30/s | + cursor reactions, window perching checked twice a second | ~0.5% |
| Alert | 40/s | Everything, more responsive | ~0.7% |
| Lively | 60/s | Everything, smoothest | ~1% |

## Windows

```
build.cmd
bin\DesktopPet.exe
```

`build.cmd` uses the C# compiler that ships with Windows
(`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) and copies `pets\` and `effects\`
next to the small exe. Run it again after changing code or sprites.

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
`Desktop Pet.app` into `/Applications` if you like. Pets float above every window and follow
you to every desktop (Space).

A downloaded build isn't signed with an Apple developer account, so the first time,
right-click the app and choose **Open**.

**Controls:** click the pet's icon in the **menu bar**, top-right of the screen.

Choices are saved in the app's preferences (`defaults read com.noahpangilinan.desktoppet`).
**Start at Login** writes `~/Library/LaunchAgents/com.noahpangilinan.desktoppet.plist`
pointing at the app. If you move the app afterwards, turn it off and on again.

## The menu

| Item | What it does |
| --- | --- |
| Pets | Tick any animals in `pets/` to show them together (at least one stays) |
| Size | 2x, 3x, 4x or 6x pixel scale |
| Activity | Slider from Calm to Lively (see above) |
| Show over full-screen apps | Keep pets visible during games, videos and presentations |
| Pause | Freeze the pets; uses no CPU while paused |
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
| `land`, `stand`, `takeoff` | Perching, for side-on pets. With all three, the pet sometimes flies down to the taskbar, the Dock or a window's top edge, plays `land` once, loops `stand`, then plays `takeoff` once and flies off. Draw standing frames with the feet on the bottom row |
| `hop` | Optional while perched: played once per short hop along the perch |
| anything else | The first animation is used for normal steady movement (the heron's `fly`) |

Rebuild and restart. The new animal appears in the Pets menu, named after its folder.
The same folder works on both Windows and macOS.

## Redrawing the built-in sprites

The koi, flatfish, manta, heron and the ripple effect are drawn by `tools/draw_sprites.py`
(needs Pillow):

```
py tools/draw_sprites.py --preview
```

`--preview` also writes an enlarged `preview.png` beside each sheet for checking. Top-down
pets are drawn from a shape description at every heading, not by rotating finished pixels,
so they stay crisp at every angle, with a soft shadow that falls the same way at every
heading.

## Builds and releases

GitHub Actions builds both apps on every push (`.github/workflows/build.yml`), so a change that
breaks the Mac build shows up even when working on Windows. Pushing a tag such as `v1.0`
also publishes `DesktopPet-windows.zip` and `DesktopPet-macos.zip` as a release.

## Layout

```
src/            Windows app (C#): World (desktop snapshot), Wanderer (behaviour),
                Stage (timer), Pet, PetWindow, Program (tray menu)
mac/            macOS app (Swift), same structure: Sources/, Info.plist, build.sh
pets/           one folder per animal: sheet.png + pet.ini (shared by both apps)
effects/        the ripple, in the same format
tools/          sprite generator
build.cmd       Windows build
.github/        CI: builds both apps, publishes releases on tags
```
