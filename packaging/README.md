# Packages

- `homebrew/Casks/lumi.rb` — the cask for the tap repo `AlbertS15/homebrew-lumi`:
  `brew install --cask albertS15/lumi/lumi`. Copy it there on each release (version + sha256 from the release asset).
- `aur/PKGBUILD` — `lumi-launcher-bin` for the AUR (`lumi-bin` is taken by the H5P app Lumi Education). On each release bump `pkgver` and the AppImage `sha256sums`,
  regenerate `.SRCINFO` with `makepkg --printsrcinfo > .SRCINFO` and push both to `ssh://aur@aur.archlinux.org/lumi-launcher-bin.git`.
  `.github/workflows/packaging.yml` builds the package in an Arch Linux container to check it.
- `linux/nfpm.yaml` — the `.deb` and `.rpm` (package `lumi-launcher`). `linux/package.sh` builds them next to the AppImage
  when nfpm is installed; CI installs nfpm, checks that the `.deb` installs and attaches both to the release.
