#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
git -C "$root" config core.hooksPath .githooks
echo "Installed OpenIGI hooks for $root"
