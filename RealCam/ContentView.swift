import SwiftUI
import UIKit

struct ContentView: View {
    @EnvironmentObject private var model: AppModel
    @State private var selection: CameraTab = .shoot

    var body: some View {
        TabView(selection: $selection) {
            Tab("Shoot", systemImage: "viewfinder", value: .shoot) {
                ShootView(onSetup: { selection = .setup })
            }
            Tab("Connect", systemImage: "dot.radiowaves.left.and.right", value: .setup) {
                SetupView()
            }
            Tab("Settings", systemImage: "slider.horizontal.3", value: .settings) {
                SettingsView()
            }
        }
        .tint(Color(red: 0.36, green: 0.82, blue: 1.0))
        .alert("RealCam", isPresented: Binding(
            get: { model.message != nil },
            set: { if !$0 { model.message = nil } }
        )) {
            Button("OK", role: .cancel) { model.message = nil }
        } message: {
            Text(model.message ?? "")
        }
    }
}

private struct ShootView: View {
    @EnvironmentObject private var model: AppModel
    let onSetup: () -> Void

    var body: some View {
        GeometryReader { geometry in
            ZStack {
                Color.black
                if let image = model.previewImage {
                    Image(uiImage: image)
                        .resizable()
                        .scaledToFill()
                        .frame(width: geometry.size.width, height: geometry.size.height)
                        .clipped()
                        .overlay(.black.opacity(0.14))
                } else {
                    previewPlaceholder
                }

                VStack(spacing: 0) {
                    header
                        .padding(.horizontal, 22)
                        .padding(.top, 14)

                    Spacer(minLength: 24)

                    if !model.isConnected {
                        Button(action: onSetup) {
                            Label("Connect to your PC", systemImage: "link")
                                .font(.headline)
                                .padding(.horizontal, 20)
                                .padding(.vertical, 13)
                        }
                        .buttonStyle(.glass(.regular))
                    }

                    Spacer(minLength: 36)

                    GlassEffectContainer(spacing: 12) {
                        VStack(spacing: 16) {
                            zoomControl
                            HStack(spacing: 12) {
                                roundButton("arrow.counterclockwise", label: "Reset camera") {
                                    model.resetCamera()
                                }
                                Button {
                                    if model.isTracking { model.stopTracking() }
                                    else { model.startTracking() }
                                } label: {
                                    Image(systemName: model.isTracking ? "pause.fill" : "move.3d")
                                        .font(.title2.weight(.semibold))
                                        .frame(width: 58, height: 58)
                                }
                                .buttonStyle(.glass(.regular.tint(model.isTracking ? .orange : Color(red: 0.18, green: 0.68, blue: 0.96))))
                                .accessibilityLabel(model.isTracking ? "Stop motion control" : "Start motion control")
                                roundButton("scope", label: "Recenter phone") {
                                    model.recenter()
                                }
                            }
                        }
                    }
                    .padding(.horizontal, 22)

                    VStack(spacing: 16) {
                        Button {
                            model.openPhotoMode()
                        } label: {
                            Label("OPEN FH6 PHOTO MODE", systemImage: "camera.aperture")
                                .font(.caption.weight(.bold))
                                .tracking(1.2)
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 15)
                        }
                        .buttonStyle(.glass(.regular))
                        .disabled(!model.isConnected)
                        .opacity(model.isConnected ? 1 : 0.45)
                    }
                    .padding(.horizontal, 22)
                    .padding(.bottom, 15)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .ignoresSafeArea(edges: .top)
        }
        .background(.black)
    }

    private var previewPlaceholder: some View {
        ZStack {
            LinearGradient(colors: [Color(red: 0.05, green: 0.10, blue: 0.14), .black], startPoint: .topLeading, endPoint: .bottomTrailing)
            Circle()
                .fill(Color.cyan.opacity(0.10))
                .frame(width: 260, height: 260)
                .blur(radius: 60)
                .offset(x: 110, y: -180)
            VStack(spacing: 12) {
                Image(systemName: "viewfinder")
                    .font(.system(size: 36, weight: .light))
                    .foregroundStyle(.white.opacity(0.5))
                Text("LIVE VIEW")
                    .font(.caption.weight(.bold))
                    .tracking(2.6)
                    .foregroundStyle(.white.opacity(0.55))
                Text("Your FH6 frame will appear here")
                    .font(.subheadline)
                    .foregroundStyle(.white.opacity(0.42))
            }
        }
    }

    private var header: some View {
        HStack {
            VStack(alignment: .leading, spacing: 3) {
                Text("REALCAM")
                    .font(.caption2.weight(.heavy))
                    .tracking(2.8)
                    .foregroundStyle(.white.opacity(0.7))
                Text("FORZA HORIZON 6")
                    .font(.caption.weight(.semibold))
                    .tracking(1.1)
            }
            Spacer()
            HStack(spacing: 7) {
                Circle()
                    .fill(model.isConnected ? Color.green : Color.white.opacity(0.4))
                    .frame(width: 7, height: 7)
                Text(model.previewFPS > 0 ? "\(model.previewFPS) FPS" : (model.isConnected ? "WAITING" : "OFFLINE"))
                    .font(.caption2.weight(.bold))
                    .tracking(0.8)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 9)
            .glassEffect(.regular, in: Capsule())
        }
        .foregroundStyle(.white)
    }

    private var zoomControl: some View {
        HStack(spacing: 14) {
            Image(systemName: "minus.magnifyingglass")
                .foregroundStyle(.white.opacity(0.72))
            Slider(value: Binding(get: { model.zoom }, set: model.setZoom), in: 0...1)
                .tint(Color(red: 0.36, green: 0.82, blue: 1.0))
            Image(systemName: "plus.magnifyingglass")
                .foregroundStyle(.white.opacity(0.72))
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 11)
        .glassEffect(.regular, in: Capsule())
    }

    private func roundButton(_ symbol: String, label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 17, weight: .medium))
                .frame(width: 48, height: 48)
        }
        .buttonStyle(.glass(.regular))
        .accessibilityLabel(label)
        .disabled(!model.isConnected)
        .opacity(model.isConnected ? 1 : 0.5)
    }
}

private struct SetupView: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    LabeledContent("Status", value: model.connectionStatus)
                    TextField("PC address (for example 192.168.1.20)", text: $model.host)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .keyboardType(.numbersAndPunctuation)
                    TextField("6-digit pairing code", text: $model.pairingCode)
                        .keyboardType(.numberPad)
                        .onChange(of: model.pairingCode) { _, value in
                            model.pairingCode = String(value.filter(\.isNumber).prefix(6))
                        }
                    if model.isConnected {
                        Button("Disconnect", role: .destructive) { model.disconnect() }
                    } else {
                        Button {
                            model.connect()
                        } label: {
                            Label("Connect to FH6 Hub", systemImage: "link")
                        }
                        .buttonStyle(.borderedProminent)
                        .disabled(model.host.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || model.pairingCode.count != 6)
                    }
                } header: {
                    Text("PC connection")
                } footer: {
                    Text("Enter the address and code shown in the Windows Hub. Both devices must be on the same local network.")
                }

                Section("Quick start") {
                    Label("Open the Windows Hub and pair this iPhone.", systemImage: "1.circle.fill")
                    Label("Launch FH6 and enter Photo Mode in free roam.", systemImage: "2.circle.fill")
                    Label("Start motion control, then move the phone to frame the shot.", systemImage: "3.circle.fill")
                }
            }
            .navigationTitle("Connect")
        }
    }
}

private struct SettingsView: View {
    var body: some View {
        NavigationStack {
            Form {
                Section("Forza Horizon 6") {
                    LabeledContent("Supported game", value: "Forza Horizon 6 for Windows")
                    LabeledContent("Camera mode", value: "Photo Mode")
                    LabeledContent("PC bridge", value: "RealCam Windows Hub")
                }
                Section("Controls") {
                    LabeledContent("Enter Photo Mode", value: "P on the PC")
                    LabeledContent("Move camera", value: "Move iPhone in space")
                    LabeledContent("Aim camera", value: "Rotate iPhone")
                    LabeledContent("Zoom", value: "Zoom slider")
                    LabeledContent("Reset in game", value: "Reset camera button")
                }
                Section {
                    Text("The Windows Hub sends standard keyboard and mouse input to FH6 and streams a PC preview over your local network. Keep FH6 in the foreground and use Photo Mode. The Hub does not install a game mod or change game files.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                } header: {
                    Text("About this build")
                }
            }
            .navigationTitle("Settings")
        }
    }
}
