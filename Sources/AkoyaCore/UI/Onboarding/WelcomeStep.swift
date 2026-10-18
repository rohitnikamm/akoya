import SwiftUI

/// Step 1: the dot logo, the name and what Akoya does, and "Get started"
/// (docs/design/onboarding-screens-1-2-mockup.html, first card).
struct WelcomeStep: View {
    let getStarted: () -> Void

    var body: some View {
        VStack(spacing: 22) {
            DotLogo(dotSize: 10)
                .frame(height: 96)
                .padding(.top, 18)
            VStack(spacing: 8) {
                Text("Akoya")
                    .font(AkoyaFonts.outfit(34, .semibold))
                    .kerning(0.34)
                    .foregroundStyle(AkoyaTheme.textPrimary)
                Text("Talk instead of type, in any app.\nEverything stays on your Mac.")
                    .font(AkoyaFonts.outfit(15))
                    // CSS line-height 1.45 (21.75 pt lines against Outfit's 18.9).
                    .lineSpacing(2.85)
                    .padding(.vertical, 1.4)
                    .multilineTextAlignment(.center)
                    .foregroundStyle(AkoyaTheme.textOnboardingBody)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
            OnboardingButton(title: "Get started", style: .light, action: getStarted)
                .keyboardShortcut(.defaultAction)
            PageDots(current: OnboardingStep.welcome.pageIndex)
        }
        .padding(EdgeInsets(top: 48, leading: 32, bottom: 28, trailing: 32))
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .akoyaDotGrid(pitch: 12, opacity: 0.09)
    }
}
