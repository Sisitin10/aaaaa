# RealCam FH6

A native SwiftUI iPhone controller and a Windows companion for framing shots in Forza Horizon 6 Photo Mode.

## What is here

- **iPhone:** SwiftUI tabs for Shoot, Connect, and Settings; ARKit world tracking for phone position and rotation; a live preview; zoom, recenter, reset, and Photo Mode controls. Custom controls use Apple's `glassEffect` Liquid Glass API, and navigation uses the system `TabView`.
- **Windows:** WPF Hub that shows a local address and session pairing code, accepts the iPhone over a local WebSocket, sends standard keyboard and mouse input to FH6, and streams a JPEG preview of the FH6 window.
- **Game support:** Forza Horizon 6 only. This prototype uses the game's Photo Mode controls and does not install a game companion or alter game files.

## Run the Windows Hub

Install the .NET 10 SDK, then run:

```powershell
dotnet run --project WindowsHub/RealCam.WindowsHub.csproj
```

Allow the Hub through Windows Firewall on your private network if prompted. Keep the Hub and iPhone on the same Wi-Fi/LAN. The Hub displays its PC address and six-digit code; enter both in the iPhone app's **Connect** tab.

## Build the iPhone app

Open `RealCam.xcodeproj` in Xcode 26 or later, select the `RealCamFH6` scheme, set your Apple development team, and run on an ARKit-capable iPhone running iOS 26 or later. The iOS deployment target is 26.0 so the app can use Apple's current Liquid Glass SwiftUI APIs.

### Build an IPA with GitHub Actions

The `Build and release iOS IPA` workflow runs on GitHub's macOS runner when you push a version tag. It builds only `RealCam.xcodeproj`; `WindowsHub` remains in the repository for the PC companion and is not included in the iOS build. The workflow attaches `RealCamFH6-unsigned.ipa` to a GitHub Release.

From a clone of the GitHub repository, create and push a version tag to start a release:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

This workflow creates an **unsigned** IPA because it has no Apple signing certificate or provisioning profile. iOS requires signing before the app can be installed on an iPhone, so sign the downloaded IPA with your own Apple account using an iOS sideloading/signing tool. A directly installable, Apple-distributed build would require Apple signing credentials and the appropriate provisioning setup in GitHub Actions.

## Use it

1. Open the Windows Hub and connect from the iPhone.
2. Launch FH6 and leave it in the foreground in free roam.
3. Tap **Open FH6 Photo Mode** in Shoot. The Hub sends the default `P` shortcut.
4. Tap the motion button and grant camera access. Move the phone to translate and rotate the FH6 camera; use the slider to zoom, and the reset/recenter buttons to reframe.

The Hub targets 30 preview frames per second and the iPhone shows the measured incoming rate. Actual frame rate depends on the PC's capture performance and the local network. Borderless or windowed mode is recommended. FH6's Photo Mode must accept its default keyboard/mouse mappings for movement and aiming. The Hub only sends input while an FH6 process is the foreground Windows app.

## References

- [Grow1nly RealCam setup and FH6 workflow](https://www.patreon.com/Grow1nly/posts/start-here-set-1-171615951)
- [Grow1nly's FH6 feature overview](https://www.patreon.com/Grow1nly/posts/realcam-hub-1-0-169724493)
- [Apple: Applying Liquid Glass to custom views](https://developer.apple.com/documentation/swiftui/applying-liquid-glass-to-custom-views)
- [Apple: SwiftUI TabView](https://developer.apple.com/documentation/swiftui/tabview)
- [FH6 Photo Mode key controls](https://forzahorizon6game.wiki/photo-mode-controls/)
