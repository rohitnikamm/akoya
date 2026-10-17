import AppKit

/// The light taps onboarding gives on a Force Touch trackpad (spec: "Haptics"). Nothing taps
/// during dictation.
public enum Haptics {
    /// A permission just turned green.
    public static func permissionGranted() {
        NSHapticFeedbackManager.defaultPerformer.perform(.levelChange, performanceTime: .now)
    }

    /// Onboarding finished with Done.
    public static func finished() {
        NSHapticFeedbackManager.defaultPerformer.perform(.generic, performanceTime: .now)
    }
}
