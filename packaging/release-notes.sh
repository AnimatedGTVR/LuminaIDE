#!/usr/bin/env bash
# Prints the CHANGELOG.md section for a version, e.g.  packaging/release-notes.sh 0.2.0
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1#v}"
awk -v v="$VERSION" '
  /^## \[/ { if (found) exit; if (index($0, "[" v "]")) { found = 1; next } }
  found { print }
' CHANGELOG.md | sed -e :a -e '/^\n*$/{$d;N;ba' -e '}'
