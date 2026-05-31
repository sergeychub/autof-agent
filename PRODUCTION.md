# Avtoforward Agent Production Notes

## Runtime paths

- Install binaries: `C:\Program Files\Avtoforward\Agent`
- Persistent config: `%ProgramData%\Avtoforward\Agent\agentsettings.json`
- Logs: `%ProgramData%\Avtoforward\Agent\logs\agent.log`
- Update state, manifests, downloads, staging, backups: `%ProgramData%\Avtoforward\Agent\updates`

## Build and install

1. Publish runtime:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\publish-production.ps1`
2. Install locally, from an elevated shell:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\install-agent.ps1 -StartAfterInstall`

The installer preserves `%ProgramData%\Avtoforward\Agent\agentsettings.json`, refreshes the install directory, configures Windows startup, and registers the elevated `AvtoforwardAgentUpdater` scheduled task.

## Auto-update setup

1. Generate update signing keys and embed the public key:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\new-update-signing-key.ps1 -UpdatePublicKey`
2. Add GitHub Actions secrets:
   - `UPDATE_PUBLISH_URL`: backend `POST /admin/workstation-agent/releases` URL.
   - `UPDATE_PUBLISH_TOKEN`: bearer token accepted by that backend endpoint.
   - `UPDATE_MANIFEST_PRIVATE_KEY`: contents of `artifacts\update-signing\update-manifest-private-key.pem`.
3. Implement or configure backend endpoints:
   - `POST /admin/workstation-agent/releases`
   - `GET /workstation-agent/update/latest?channel=main&runtime=win-x64&currentVersion=...`
   - `GET /workstation-agent/update/download/{releaseId}`
   - `POST /workstation-agent/update/report`
4. Bootstrap existing machines once with the updated installer. Later pushes to `main` publish signed update artifacts automatically.

## MSI

`build-msi.ps1` is included as a production packaging entrypoint, but this environment does not currently have WiX Toolset installed. Once WiX is present, the MSI wrapper can be added around the published runtime in `artifacts\publish\win-x64`.
