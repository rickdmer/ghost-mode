# Developing Ghost Mode

## Building

```powershell
.\build.ps1
```

This compiles `bin\GhostMode.exe` with the C# compiler that ships with Windows (.NET Framework 4.8,
`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`), so no SDK or Visual Studio is needed. That compiler only
supports C# 5: no `$"..."` interpolation, `?.`, `nameof`, expression-bodied members or auto-property initialisers.
XAML can't be compiled by it either, so `src\MainWindow.xaml` is embedded as a resource and loaded at runtime with
`XamlReader`; event handlers are attached in `App.cs` by element name. Close the app before building.

Releases are built by `.github/workflows/release.yml`: pushing a `v*` tag builds on a Windows runner, zips
`GhostMode.exe` + `GhostMode.exe.config`, and publishes a GitHub release using the annotated tag's message as the
release notes. Bump the version in `src\AssemblyInfo.cs` first.

## Command line

| Argument | Effect |
|---|---|
| *(none)* | Open the window, or bring the running instance's window forward |
| `--toggle` | Toggle everything. Forwarded to the running instance; if none is running, starts in the tray and toggles |
| `--tray` | Start in the tray without a window (used by Start with Windows) |
| `--welcome` | Show the first-run welcome card again |

Only one instance runs at a time (a named mutex); other launches signal it through named events.

## Layout

| Path | What |
|---|---|
| `src\App.cs` | Entry point, single instance, window wiring, tray behaviour |
| `src\ViewModels.cs` | Main view model, per-app rows, settings options, the automation worker thread |
| `src\Providers\` | One class per supported app, plus `Provider.cs` (contract and shared helpers) |
| `src\ProviderRegistry.cs` | The list of supported apps, in display order |
| `src\SystemIntegration.cs` | Optional shortcuts, the hotkey shortcut, Start with Windows |
| `src\TrayIcon.cs`, `src\TaskbarIdentity.cs` | Notification-area icon; taskbar name/icon |
| `src\Native.cs` | Win32 helpers: window lookup, clicks, PrintWindow snapshots |
| `assets\make-icon.ps1` | Regenerates `app.ico` / `app.png` |

User data lives in `%APPDATA%\GhostMode\` (`settings.json`, `log.txt`).

## How each app is driven

| App | Set status | Read status |
|---|---|---|
| Discord | UI Automation on the profile popout | User panel text (readable while in the tray) |
| Steam | `steam://friends/status/...` | `ePersonaState` in `userdata\<id>\config\localconfig.vdf` |
| Xbox | UI Automation on the profile menu (real clicks; items have no Invoke pattern) | Only by opening the menu ("Check status") |
| Battle.net | Clicks at offsets from the window's top-right | Colour of the avatar's status ring (window open only) |
| GOG Galaxy | Clicks at offsets from the window's top-left | Colour of the avatar's status dot (window open only) |

All automation runs on one background STA thread that is per-monitor DPI aware, so UI Automation rectangles,
window rectangles and clicks all use physical pixels. Discord and Xbox are matched by their English UI text.

### Battle.net and GOG Galaxy

Neither exposes an accessibility tree (Battle.net is CEF and ignores `--force-renderer-accessibility`; GOG is Qt
WebEngine), so they're driven by click offsets in 96-DPI units, scaled by the window's DPI. The offsets are at the top of
`BattleNetProvider.cs` and `GogGalaxyProvider.cs`, and they're the first thing to re-measure after a redesign.

- Status is read by sampling a pixel of the avatar's presence dot with `PrintWindow`, which works while the window
  is covered but not while it's hidden or minimised. Battle.net's "Appear Offline" indicator is a hollow ring, so
  the sample point sits on the ring rather than the centre.
- Right after reopening from the tray the page shows a plain dark background before it renders. Dark greys are
  classified as Unknown rather than Invisible, and every read waits for a recognisable colour.
- The page also ignores clicks for a moment after reopening, so opening the status menu is retried until a pixel
  inside the menu confirms it's open.
- Never hide either window with `ShowWindow(SW_HIDE)`: their tray icons can no longer bring them back afterwards.
  They're returned to the tray with `WM_CLOSE` (their own close button). GOG's close behaviour is read from its
  `config.json`; Battle.net's isn't recorded in its config, so close-to-tray (its default) is assumed.
- Battle.net destroys its window while in the tray; GOG recreates its window (new HWND) each time it reopens.
  Always look windows up again after showing them.

### Xbox

The profile menu item is an action label: it reads "Appear offline" while you're online. Closed UWP frames linger as
cloaked windows that `IsWindowVisible` still reports as visible, so the XBOX window is found through UI Automation's
top-level list, which skips them.

## Adding another app

1. Add `src\Providers\<Name>Provider.cs` deriving from `ProviderBase`. Implement `Detect` and `Set`, and if
   possible `ReadLive` (cheap, no visible side effects) and `Probe` (may briefly open the window).
   `WindowSession` handles bringing a window up and putting it back in the tray afterwards.
2. Add it to `ProviderRegistry.All()` in `src\ProviderRegistry.cs`.
3. Run `build.ps1`. The app list, switches, per-app menu and toggle pick it up automatically.
