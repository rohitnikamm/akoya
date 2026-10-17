cask "silk" do
  version "__VERSION__"
  sha256 "__SHA256__"
  url "https://github.com/rohitnikamm/silk/releases/download/v#{version}/Silk-v#{version}.zip"
  name "Silk"
  desc "Push-to-talk dictation that lives in the menu bar"
  homepage "https://github.com/rohitnikamm/silk"
  depends_on macos: ">= :sonoma"
  depends_on arch: :arm64
  app "Silk.app"
  postflight do
    system_command "/usr/bin/xattr", args: ["-dr", "com.apple.quarantine", "#{appdir}/Silk.app"], must_succeed: false
  end
  zap trash: ["~/Library/Application Support/Silk", "~/Library/Preferences/com.rohitnikamm.silk.plist"]
end
