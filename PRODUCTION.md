# Автофорвад Agent Production Notes

## Runtime paths

- Install binaries: `C:\Program Files\Avtoforward\Agent`
- Persistent config: `%ProgramData%\Avtoforward\Agent\agentsettings.json`
- Logs: `%ProgramData%\Avtoforward\Agent\logs\agent.log`

## Build and install

1. Publish runtime:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\publish-production.ps1`
2. Install locally:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\install-agent.ps1 -StartAfterInstall`

## MSI

`build-msi.ps1` is included as a production packaging entrypoint, but this environment does not currently have WiX Toolset installed. Once WiX is present, the MSI wrapper can be added around the published runtime in `artifacts\publish\win-x64`.
