#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_dir="$(cd -- "$script_dir/.." && pwd)"
runtime="${1:-linux-x64}"
version="${2:-0.1.0-local}"
output_dir="$repo_dir/artifacts/publish/$runtime"

dotnet publish "$repo_dir/WorkstationAgent.Ubuntu/WorkstationAgent.Ubuntu.csproj" \
  --configuration Release \
  --runtime "$runtime" \
  --self-contained true \
  --output "$output_dir" \
  -p:PublishSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:Version="$version" \
  -p:InformationalVersion="$version"

install -m 0755 "$script_dir/install-ubuntu-agent.sh" "$output_dir/install-ubuntu-agent.sh"
install -m 0644 "$repo_dir/deploy/ubuntu/avtoforward-agent.service" "$output_dir/avtoforward-agent.service"

echo "Ubuntu agent published to $output_dir"
