#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"
command -v dotnet >/dev/null || { echo "error: dotnet is required" >&2; exit 1; }
dotnet build OpenIGI.slnx -c Release --no-restore -m:1 --nologo
while IFS= read -r project; do
  dotnet test "$project" -c Release --no-build --no-restore --nologo
done < <(find tests -name '*Tests.csproj' -print | sort)
