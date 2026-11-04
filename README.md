# Camera Updated

A client-only camera and optional desktop gameplay mode for **A Township Tale**. Stream a smooth desktop view while playing in VR, or enable keyboard and mouse character controls. Press **F6** for all settings.

## Install

1. Open the [release page](https://github.com/calebv2/Camera-Updated/releases/tag/v0.3.0) and download **Camera-Updated-0.3.0.zip** under Assets.
2. Close the player game. Copy `Mods/ATTSmoothCamera.dll` into the player's `Mods` folder, replacing the previous camera DLL.
3. Start and join normally through your launcher. Press **F6**.

Requires Windows, A Township Tale and MelonLoader (built against 0.7.3). No server installation or console token. The ZIP contains one DLL and one instruction file.

## Optional desktop gameplay

Desktop gameplay starts **off** each launch. Join as a normal character, rather than Fly. In **F6 > Desktop**, enable flatscreen controls, choose **Play / resume**, then click the game view. The first click only captures the mouse; release it before using an item. Keep the separate `FlatscreenATTMod.dll` out of the active Mods folder.

This mode takes over your character's head and hands. Turn it off in Desktop before returning to VR. Normal streaming with desktop controls off leaves VR input unchanged. Joining and account setup still use your launcher.

| Control | Action |
| --- | --- |
| WASD | Walk with the game's native locomotion and collisions |
| Mouse | Look; direct response in first-person desktop play |
| Shift | Run |
| Ctrl | Crouch |
| Q / E | Left / right grip (toggle by default; hold option available) |
| Left / right click | Squeeze or use the matching hand |
| Z / X + mouse | Position the left / right hand |
| Alt + Z / X + mouse | Rotate that hand |
| Mouse wheel | Adjust reach |
| Alt + mouse wheel | Roll the selected hand |
| R | Reset hand positions |
| Escape | Release the mouse and pause controls |
| F6 | Open or close settings |

Adjust sensitivity, invert look, eye heights, hand reach and grip behavior on Desktop. Interactions use the game's nearby hand interactions: place the virtual hand next to the object. Controls pause in settings, when the game loses focus, while Prefabulator is active, during camera-path playback or while a photo camera is held. Click the game view to resume after closing menus.

## Streaming tools

- **Camera:** smooth first-person, Behind, Front, Orbit and Crafting close-up. Smart framing includes your head, body, hands and tools. Adjust framing, smoothing, field of view and transitions. Horizon lock and wall avoidance are available.
- **Presets:** save complete camera views and load them with one click.
- **Paths:** record camera movement or add individual shots; save, play, pause, loop and adjust speed.
- **Photos:** hold the camera while you pose and save a PNG at 1x-4x desktop resolution.
- **Help:** setup guidance and saved-file locations.

Selecting a camera view enables it. **Save startup view** keeps camera and desktop preferences; desktop gameplay still starts off. Capture the **game desktop window** in OBS. **Clean capture** covers desktop overlays with the camera image; F6 returns to settings.

Paths move only the camera. Game activity stays live. Keep your player near the route so its chunks are loaded. Teleports or character changes stop recording and playback. A finished path holds its last shot. At least two points are required; limits are 32 presets, 16 paths and 2,048 points per path.

Photos exclude the settings menu and are limited to 7,680 pixels per side / about 33 megapixels. Holding the camera does not freeze the game. Clean capture follows the window's aspect ratio, up to 3840x2160.

## Saved files

Inside your game folder:

- Settings: `[ATTSmoothCamera]` in `UserData/MelonPreferences.cfg`
- Presets and paths: `UserData/ATTSmoothCamera/camera-library.xml`
- Previous library: `UserData/ATTSmoothCamera/camera-library.previous.xml`
- Photos: `UserData/ATTSmoothCamera/Screenshots/`

To uninstall, close the game and remove `Mods/ATTSmoothCamera.dll`. Saved files remain available.

## Build from source

Supply your own game installation, patched with MelonLoader, and the Windows .NET Framework C# compiler:

```powershell
.\build.ps1 -GamePath "C:\Games\A Township Tale"
```

Output: `dist/ATTSmoothCamera.dll`. The build does not install it. Game and MelonLoader assemblies come from your installation.

## Preview status

Version 0.3.0 is a preview. Desktop gameplay needs live in-game testing. Keyboard hands cannot reproduce every VR gesture; some tool and crafting actions require manual hand positioning. The streaming camera remains available with desktop controls off.
