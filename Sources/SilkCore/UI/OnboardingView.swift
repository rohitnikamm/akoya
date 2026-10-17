import Combine
import SwiftUI

private extension Permission {
    var title: String {
        switch self {
        case .microphone: "Microphone"
        case .accessibility: "Accessibility"
        case .inputMonitoring: "Input Monitoring"
        }
    }

    var reason: String {
        switch self {
        case .microphone: "Silk needs this to hear you while you hold the key."
        case .accessibility: "Silk needs this to paste text and catch the hotkey."
        case .inputMonitoring: "Silk needs this to listen for the hotkey."
        }
    }
}

/// Holds the current grant state. A class rather than `@State`, because the `@State` macro
/// plugin is not available when building with Command Line Tools only.
@MainActor
private final class PermissionStatus: ObservableObject {
    @Published private(set) var granted: [Permission: Bool] = [:]
    private let isGranted: (Permission) -> Bool

    init(isGranted: @escaping (Permission) -> Bool) {
        self.isGranted = isGranted
        refresh()
    }

    func refresh() {
        for permission in Permission.allCases {
            let now = isGranted(permission)
            if granted[permission] != now { granted[permission] = now }
        }
    }
}

public struct OnboardingView: View {
    private let request: (Permission) -> Void
    private let openSettings: (Permission) -> Void
    @StateObject private var status: PermissionStatus
    private let refresh = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    public init(
        isGranted: @escaping (Permission) -> Bool = Permissions.isGranted,
        request: @escaping (Permission) -> Void = Permissions.request(_:),
        openSettings: @escaping (Permission) -> Void = Permissions.openSettings(for:)
    ) {
        self.request = request
        self.openSettings = openSettings
        _status = StateObject(wrappedValue: PermissionStatus(isGranted: isGranted))
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Welcome to Silk").font(.title2.bold())
                Text("Silk needs three permissions before it can type for you.")
                    .foregroundStyle(.secondary)
            }
            ForEach(Permission.allCases, id: \.self) { permission in
                row(for: permission)
            }
        }
        .padding(24)
        .frame(width: 440)
        .onReceive(refresh) { _ in status.refresh() }
    }

    private func row(for permission: Permission) -> some View {
        let isOn = status.granted[permission] ?? false
        return HStack(alignment: .center, spacing: 12) {
            Circle()
                .fill(isOn ? Color.green : Color.orange)
                .frame(width: 10, height: 10)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(permission.title).font(.headline)
                Text(permission.reason).font(.callout).foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            if isOn {
                Text("Granted").foregroundStyle(.secondary)
            } else {
                Button("Open System Settings") {
                    Permissions.performOnboardingAction(
                        for: permission, granted: false, request: request, openSettings: openSettings
                    )
                }
            }
        }
        .accessibilityElement(children: .combine)
    }
}
