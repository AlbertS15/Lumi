cask "lumi" do
  version "2.0.1"
  sha256 "b6a12ac4f6b1584182ecdb920e6949805106642c595d538fa00c48ac0e4474cf"

  url "https://github.com/AlbertS15/Lumi/releases/download/v#{version}/Lumi-macOS.dmg"
  name "Lumi"
  desc "Spotlight-style bar that also answers with AI"
  homepage "https://alberts15.github.io/Lumi/"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on macos: ">= :sonoma"

  app "Lumi.app"

  zap trash: "~/Library/Preferences/io.github.alberts15.claudelight.plist"

  caveats <<~EOS
    Lumi is not notarized by Apple yet. On first launch macOS may block it:
    open System Settings → Privacy & Security and click "Open Anyway".
  EOS
end
