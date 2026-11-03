# Camera Updated

A client-only streaming camera for **A Township Tale**. Play normally in VR while viewers see a smooth desktop camera. Press **F6** for all settings.

## Download and install

1. Open the [release page](https://github.com/calebv2/Camera-Updated/releases/tag/v0.2.1) and download **Camera-Updated-0.2.1.zip** under Assets.
2. Close your player game.
3. Copy `Mods/ATTSmoothCamera.dll` from the ZIP into your player game's `Mods` folder. Replace the previous camera DLL if updating.
4. Start the game, join your server, and press **F6**.
5. In OBS, capture the **A Township Tale desktop/game window**, rather than the SteamVR mirror.

Requires a Windows PC with A Township Tale and MelonLoader already installed. Built against MelonLoader 0.7.3. Third-person views need your VR character; Fly mode has no avatar to show.

## Camera controls

**F6 is the only shortcut.** Everything else is in the settings window:

- **Camera:** smooth first-person, Behind, Front, Orbit and Crafting close-up. Adjust distance, height, shoulder offset, field of view and smoothing. Smart framing includes your head, body, hands and held tools. Horizon lock keeps the first-person stream level. Wall avoidance finds a clearer angle and recovers smoothly. View changes blend through a transition.
- **Presets:** name and save a complete camera setup, then load it with one click.
- **Paths:** record camera movement or add individual shots; save, play, pause, loop and adjust playback speed.
- **Photos:** hold the desktop camera in place while you pose, then save a PNG at 1x-4x desktop resolution.
- **Help:** setup instructions and saved-file locations.

Selecting a camera view enables it. **Save startup view** keeps the current settings for the next launch. Smooth first-person is the initial default.

### Clean capture

Choose **Start clean capture** on the Camera tab to hide desktop menus and overlays behind the camera image. Press **F6** to return to settings. The clean image follows the window's aspect ratio, up to 3840x2160.

### Camera paths

Use **Record movement**, move through the route in VR or change your camera framing, then stop, name the path and save it. For manual paths, use **Add current shot**; the spacing control sets the time between shots. At least two points are required.

Paths move only the camera. Game activity remains live. Stay near the route so the world is loaded. Teleporting or changing characters stops recording and playback. A finished path holds its last shot until you replay or return to your player. Match the first and last shots for a seamless loop.

Limits: 32 presets, 16 paths and 2,048 points per path, or about eight minutes of recorded movement.

### Photos

Photos exclude the camera settings menu. Large images are limited to 7,680 pixels per side and about 33 megapixels. The menu shows the saved filename. Holding the camera does not freeze the game or your character.

## Saved files

Inside your game folder:

- Presets and paths: `UserData/ATTSmoothCamera/camera-library.xml`
- Previous library: `UserData/ATTSmoothCamera/camera-library.previous.xml`
- Photos: `UserData/ATTSmoothCamera/Screenshots/`
- Startup settings: `[ATTSmoothCamera]` in `UserData/MelonPreferences.cfg`

To uninstall, close the game and remove `Mods/ATTSmoothCamera.dll`. Your saved files remain available.

## Build from source

Use your own game installation, patched with MelonLoader, and the Windows .NET Framework C# compiler:

```powershell
.\build.ps1 -GamePath "C:\Games\A Township Tale"
```

The DLL is written to `dist/ATTSmoothCamera.dll`. The build does not install it. Game and MelonLoader assemblies are supplied by your installation.

## Preview status

Version 0.2.1 is a preview. Compilation and automated checks pass; live VR, OBS clean capture, photos and the updated menu still need in-game confirmation.
