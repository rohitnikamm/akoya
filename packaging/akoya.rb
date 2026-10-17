cask "akoya" do
  version "__VERSION__"
  sha256 "__SHA256__"
  url "https://github.com/rohitnikamm/akoya/releases/download/v#{version}/Akoya-v#{version}.zip"
  name "Akoya"
  desc "Push-to-talk dictation that lives in the menu bar"
  homepage "https://github.com/rohitnikamm/akoya"
  depends_on macos: ">= :sonoma"
  depends_on arch: :arm64
  app "Akoya.app"
  postflight do
    system_command "/usr/bin/xattr", args: ["-dr", "com.apple.quarantine", "#{appdir}/Akoya.app"], must_succeed: false
  end
  uninstall quit: "com.rohitnikamm.akoya"
  zap trash: ["~/Library/Application Support/Akoya", "~/Library/Preferences/com.rohitnikamm.akoya.plist"]
end
