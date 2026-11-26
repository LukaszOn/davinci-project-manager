#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  printf 'Usage: %s <Linux-x64.tar.gz> <output.deb>\n' "$0" >&2
  exit 2
fi

project=$(cd "$(dirname "$0")/../.." && pwd)
version=$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$project/ProjectLibraryDesk.csproj")
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z]+\.[0-9]+)?$ ]]
debian_version=${version/-/\~}
root="DaVinci-Project-Manager-$version"
portable=$(realpath "$1")
output=$(realpath -m "$2")

staging=$(mktemp -d "${TMPDIR:-/tmp}/dpm-deb.XXXXXX")
trap 'rm -rf -- "$staging"' EXIT
tree="$staging/package"
app="$tree/usr/lib/davinci-project-manager"
docs="$tree/usr/share/doc/davinci-project-manager"
mkdir -p "$app" "$docs" "$tree/DEBIAN" "$tree/usr/bin" "$tree/usr/share/applications" "$tree/usr/share/pixmaps"

tar -xzf "$portable" --directory "$app" --strip-components=1 --no-same-owner --wildcards "$root/*"
test -f "$app/DaVinciProjectManager.dll" && test -x "$app/DaVinciProjectManager" && test -f "$app/sql/control.sql"
mv "$app/LICENSE" "$docs/copyright"
mv "$app/README.md" "$app/THIRD-PARTY-NOTICES.md" "$app/licenses" "$docs/"
install -m 0644 "$project/Assets/Desk.png" "$tree/usr/share/pixmaps/davinci-project-manager.png"

cat > "$tree/usr/bin/davinci-project-manager" <<'LAUNCHER'
#!/bin/sh
exec /usr/lib/davinci-project-manager/DaVinciProjectManager "$@"
LAUNCHER
chmod 0755 "$tree/usr/bin/davinci-project-manager"

cat > "$tree/usr/share/applications/davinci-project-manager.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=DaVinci Project Manager
Comment=Manage PostgreSQL libraries for DaVinci Resolve
Exec=davinci-project-manager
Icon=davinci-project-manager
Terminal=false
Categories=AudioVideo;Video;
DESKTOP

cat > "$tree/DEBIAN/control" <<CONTROL
Package: davinci-project-manager
Version: $debian_version
Section: video
Priority: optional
Architecture: amd64
Maintainer: DaVinci Project Manager contributors
Installed-Size: $(du -sk "$tree/usr" | cut -f1)
Depends: ca-certificates, libc6, libgcc-s1, libgssapi-krb5-2, libicu72 | libicu74 | libicu76 | libicu77 | libicu78, libssl3t64 | libssl3, libstdc++6, tzdata, libx11-6, libice6, libsm6, libfontconfig1, libxext6, libxrender1, libxrandr2, libxcursor1, libxi6, libxkbcommon0, libgl1 | libegl1
Description: PostgreSQL library manager for DaVinci Resolve
 Manage server profiles, library access, backups and restores from a desktop app.
CONTROL

mkdir -p "$(dirname "$output")"
dpkg-deb --root-owner-group --build "$tree" "$output"
