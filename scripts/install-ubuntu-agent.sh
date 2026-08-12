#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run this installer as root (for example with sudo)." >&2
  exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_dir="$(cd -- "$script_dir/.." && pwd)"
source_dir="${1:-$script_dir}"
install_dir="/opt/avtoforward-agent"
config_dir="/etc/avtoforward-agent"
state_dir="/var/lib/avtoforward-agent"
service_source="$script_dir/avtoforward-agent.service"

if [[ ! -x "$source_dir/WorkstationAgent.Ubuntu" ]]; then
  echo "Published WorkstationAgent.Ubuntu binary was not found in $source_dir." >&2
  exit 1
fi

if [[ ! -f "$service_source" ]]; then
  service_source="$repo_dir/deploy/ubuntu/avtoforward-agent.service"
fi
if [[ ! -f "$service_source" ]]; then
  echo "avtoforward-agent.service was not found." >&2
  exit 1
fi

if ! getent group avtoforward-agent >/dev/null; then
  groupadd --system avtoforward-agent
fi
if ! id avtoforward-agent >/dev/null 2>&1; then
  useradd --system --gid avtoforward-agent --home-dir "$state_dir" --shell /usr/sbin/nologin avtoforward-agent
fi
if getent group lp >/dev/null; then
  usermod --append --groups lp avtoforward-agent
fi

if ! command -v lp >/dev/null 2>&1; then
  echo "Warning: CUPS client is missing. Install cups-client before using transportMode=cups." >&2
fi
if ! command -v magick >/dev/null 2>&1 && ! command -v convert >/dev/null 2>&1; then
  echo "Warning: ImageMagick is missing. Install imagemagick before printing image or bitmap-text blocks." >&2
fi

install -d -m 0755 -o root -g root "$install_dir"
install -d -m 0750 -o root -g avtoforward-agent "$config_dir"
install -d -m 0750 -o avtoforward-agent -g avtoforward-agent "$state_dir"
cp -a "$source_dir/." "$install_dir/"
chown -R root:root "$install_dir"
chmod 0755 "$install_dir/WorkstationAgent.Ubuntu"

if [[ ! -f "$config_dir/agentsettings.json" ]]; then
  install -m 0640 -o root -g avtoforward-agent \
    "$source_dir/agentsettings.example.json" \
    "$config_dir/agentsettings.json"
  echo "Created $config_dir/agentsettings.json; edit it before starting the agent."
fi

install -m 0644 -o root -g root "$service_source" /etc/systemd/system/avtoforward-agent.service
systemctl daemon-reload
systemctl enable avtoforward-agent.service

echo "Installed Avtoforward Agent for Ubuntu."
echo "Edit $config_dir/agentsettings.json, then run: systemctl restart avtoforward-agent"
