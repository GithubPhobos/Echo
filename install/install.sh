#!/bin/sh
# Installs Echo for the current user, without administrator rights:
#
#   curl -fsSL https://githubphobos.github.io/Echo/install.sh | sh
#
# A version can be given, which is also how an earlier version is put back:
#
#   curl -fsSL https://githubphobos.github.io/Echo/install.sh | sh -s 2.0.1
#
# Every version goes into a folder of its own, ~/.local/share/echooo/versions/<version>, and the link
# ~/.local/share/echooo/current leads to the one in use. The command ~/.local/bin/echooo leads through that link.
# Echo keeps the same layout when it updates itself. The system packages Echo needs are installed by Echo's own
# setup at its first start. ECHOOO_RELEASES points the script at other releases, such as a test server.

set -eu

releases="${ECHOOO_RELEASES:-https://github.com/GithubPhobos/Echo/releases}"
archive="echooo-linux-x64.tar.gz"

fail() {
    echo "Echo was not installed: $1" >&2
    exit 1
}

case "$(uname -s)" in
    Linux) ;;
    *) fail "this script is for Linux; on Windows run: irm https://githubphobos.github.io/Echo/install.ps1 | iex" ;;
esac

[ "$(uname -m)" = "x86_64" ] || fail "Echo runs on 64-bit Intel and AMD processors (x86_64) only, and this one is $(uname -m)."

for program in curl tar sha256sum; do
    command -v "$program" > /dev/null 2>&1 || fail "the '$program' program is missing."
done

# The data folder of the XDG standard, which Echo uses for its models too; a relative path does not count
case "${XDG_DATA_HOME:-}" in
    /*) root="$XDG_DATA_HOME/echooo" ;;
    *) root="$HOME/.local/share/echooo" ;;
esac

commands="$HOME/.local/bin"

# The latest release is where GitHub redirects ".../releases/latest" to: ".../releases/tag/v2.0.1"
version="${1:-}"
version="${version#v}"

if [ -z "$version" ]; then
    latest=$(curl -fsSLI -o /dev/null -w '%{url_effective}' "$releases/latest") \
        || fail "GitHub cannot be reached."

    case "$latest" in
        */releases/tag/v*) version="${latest##*/releases/tag/v}" ;;
        *) fail "there is no release of Echo yet." ;;
    esac
fi

download="$releases/download/v$version"
temporary=$(mktemp -d)
trap 'rm -rf "$temporary"' EXIT INT TERM

echo "Downloading Echo $version..."

curl -fSL --progress-bar "$download/$archive" -o "$temporary/$archive" \
    || fail "Echo $version could not be downloaded. Is there such a version? See $releases"
curl -fsSL "$download/SHA256SUMS" -o "$temporary/SHA256SUMS" \
    || fail "the checksums of Echo $version could not be downloaded."

# Only the line of this archive is checked: the list names the archives of every system
grep -E "^[0-9a-fA-F]{64} [ *]$archive\$" "$temporary/SHA256SUMS" > "$temporary/checksum" \
    || fail "the checksums of Echo $version do not name $archive."

(cd "$temporary" && sha256sum -c --status checksum) \
    || fail "the downloaded archive is damaged (its checksum does not match). Try again."

# Unpacked next to its place and moved there whole, so the folder of a version is never half full
versions="$root/versions"
target="$versions/$version"

mkdir -p "$versions"
rm -rf "$target.part"
mkdir "$target.part"
tar -xzf "$temporary/$archive" -C "$target.part" || fail "the archive could not be unpacked."
[ -x "$target.part/echooo" ] || fail "the archive of Echo $version has no echooo program."

rm -rf "$target"
mv "$target.part" "$target"

# The new link is renamed over the old one in one step, so a running Echo never sees the link missing
ln -sfn "versions/$version" "$root/current.new"
mv -T "$root/current.new" "$root/current" || fail "$root/current could not be pointed at the new version."

mkdir -p "$commands"
ln -sfn "$root/current/echooo" "$commands/echooo"

echo "Echo $version is installed."

case ":$PATH:" in
    *":$commands:"*)
        echo "Start it with: echooo"
        ;;
    *)
        echo "The folder $commands is not on the search path of this terminal yet."
        echo "Add this line to ~/.profile (or to the settings file of your shell), then open a new terminal:"
        echo "    export PATH=\"\$HOME/.local/bin:\$PATH\""
        echo "Until then, start Echo with: $commands/echooo"
        ;;
esac
