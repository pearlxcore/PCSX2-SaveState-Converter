#!/usr/bin/env bash
# Verifies that experimental/*.patch apply cleanly to PCSX2 v2.8.2, using a shallow, sparse clone
# of only the files the patches touch (nothing is built).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
tag="v2.8.2"
work="$(mktemp -d)"
trap 'rm -rf "${work:?}"' EXIT

# Anchored single-file patterns ("/path") for the non-cone sparse checkout.
mapfile -t files < <(grep -h '^diff --git' "$root"/experimental/*.patch | awk '{ print "/" substr($3, 3) }' | sort -u)
# .gitattributes too: it checks the project files out with CRLF (eol=crlf), and git apply needs it
# to normalize them back to the LF the patches were made against.
files+=("/.gitattributes")

git -c advice.detachedHead=false clone --quiet --depth 1 --branch "$tag" --filter=blob:none --sparse \
    https://github.com/PCSX2/pcsx2.git "$work/pcsx2"
cd "$work/pcsx2"
# Patterns go through stdin: Git for Windows' shell would rewrite "/path" arguments.
printf '%s\n' "${files[@]}" | git sparse-checkout set --no-cone --stdin

git apply --check "$root/experimental/pcsx2-v2.8.2-ffmpeg.patch"
git apply "$root/experimental/pcsx2-v2.8.2-ffmpeg.patch"
git apply --check "$root/experimental/pcsx2-v2.8.2-legacy-reader.patch"
git apply "$root/experimental/pcsx2-v2.8.2-legacy-reader.patch"
echo "Both patches apply to PCSX2 $tag (${#files[@]} files)."
