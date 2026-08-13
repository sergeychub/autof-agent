# Avtoforward Agent for Ubuntu

Headless Ubuntu implementation of the Autof workstation agent. It uses the existing Autof API contract without backend changes:

- `POST /workstation-agent/register` for first registration;
- Socket.IO namespace `/workstation-agent` with the existing device ID and API key handshake;
- `agent:heartbeat`, `printer:test`, `printer:job`, `pos:terminal:purchase`, and `pos:terminal:cancel` events;
- the same result events as the Windows agent.

## Supported features

- ESC/POS receipt jobs: `text`, `raw-base64`, and structured `document` payloads;
- TSPL label jobs, including text, barcode, QR, boxes, bars, bitmap text, and base64 bitmaps;
- CUPS raw queues, direct Linux character devices such as `/dev/usb/lp0`, and TCP port 9100 printers;
- PrivatBank POS terminal purchases and cancellation over its TCP JSON protocol;
- foreground execution and `systemd` operation.
- signed automatic binary updates from the `main` channel through the Autof API.

Image and bitmap-text blocks are rasterized through ImageMagick. Install the `imagemagick` package if those payloads are used. CUPS printing requires `cups-client`.

Automatic updates are enabled by default. Each push to `main` publishes a signed `linux-x64` release to the Autof API. A root-owned `systemd` timer checks every five minutes, verifies the RSA-PSS manifest signature plus archive size and SHA-256, preserves the previous binary, installs the new one, and restarts the unprivileged agent service. Set `autoUpdateEnabled` to `false` to opt out.

## Local build

The target framework is .NET 10, matching the Windows agent:

```bash
dotnet restore WorkstationAgent.Ubuntu/WorkstationAgent.Ubuntu.csproj
dotnet build WorkstationAgent.Ubuntu/WorkstationAgent.Ubuntu.csproj -c Release
```

Create a self-contained `linux-x64` package:

```bash
./scripts/publish-ubuntu.sh
```

## Configuration

Copy `agentsettings.example.json` to `/etc/avtoforward-agent/agentsettings.json` and set at least:

- `agentName`;
- `apiBaseUrl`;
- `registrationToken` (the API's `WORKSTATION_AGENT_REGISTRATION_TOKEN`);
- the enabled printer transport and its queue/device/network address.

The registration response is written with mode `0600` to `/var/lib/avtoforward-agent/state.json`. It contains `deviceId`, `apiKey`, the registered name, and Socket.IO URL. `registrationToken` stays in the root-owned configuration file and is not copied to state.

Printer transports:

- `cups`: set `printerName` to the CUPS queue and ensure it accepts raw jobs;
- `device`: set an absolute `devicePath`, normally `/dev/usb/lp0`;
- `tcp`: set `host` and usually port `9100`.

Useful checks before enabling the service:

```bash
WorkstationAgent.Ubuntu --validate --config ./agentsettings.json --state ./state.json
WorkstationAgent.Ubuntu --print-test receipt --config ./agentsettings.json --state ./state.json
WorkstationAgent.Ubuntu --print-test label --config ./agentsettings.json --state ./state.json
WorkstationAgent.Ubuntu --pos-test --config ./agentsettings.json --state ./state.json
```

## Ubuntu installation

After publishing, run as root:

```bash
sudo ./scripts/install-ubuntu-agent.sh ./artifacts/publish/linux-x64
```

Then edit the installed config and restart:

```bash
sudo editor /etc/avtoforward-agent/agentsettings.json
sudo systemctl restart avtoforward-agent
sudo journalctl -u avtoforward-agent -f
systemctl list-timers avtoforward-agent-update.timer
journalctl -u avtoforward-agent-update.service
```

The installer creates an unprivileged `avtoforward-agent` system user and adds it to the `lp` group. Direct USB mode can still require a printer-specific udev rule granting that group write access.

## Peripheral-free compatibility checks

`WorkstationAgent.Ubuntu.Tests` verifies printer control without sending a job to physical hardware:

- the device transport writes an unchanged byte stream to a Linux device-like file;
- the TCP transport sends an unchanged stream to an emulated raw port 9100 printer;
- a CUPS `lp` shim verifies the destination, raw mode, title, standard-input marker, payload, and error handling;
- API-shaped `raw-base64`, structured ESC/POS, and TSPL jobs are deserialized and converted to their expected command streams;
- ESC/POS bitmap data uses set bits for printed dots, while TSPL bitmap data uses the Xprinter-compatible inverse polarity;
- the updater rejects invalid signatures and installs an authenticated archive with a rollback copy;
- print result JSON uses the field names consumed by `WorkstationAgentGateway`.

Run the suite with:

```bash
dotnet test WorkstationAgent.Ubuntu.Tests/WorkstationAgent.Ubuntu.Tests.csproj -c Release
```

These checks cover software and transport compatibility. A final hardware smoke test is still required for USB permissions, the exact printer firmware dialect, media calibration, cutter behavior, and print density.
