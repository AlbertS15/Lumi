# Packages

- `homebrew/Casks/lumi.rb` — the cask for the tap repo `AlbertS15/homebrew-lumi`:
  `brew install --cask albertS15/lumi/lumi`. Copy it there on each release (version + sha256 from the release asset).
- `aur/PKGBUILD` — `lumi-bin` for the AUR. On each release bump `pkgver` and the AppImage `sha256sums`,
  regenerate `.SRCINFO` with `makepkg --printsrcinfo > .SRCINFO` and push both to `ssh://aur@aur.archlinux.org/lumi-bin.git`.
  `.github/workflows/packaging.yml` builds the package in an Arch Linux container to check it.
