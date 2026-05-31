param(
    [string]$OutputDirectory = "",
    [switch]$UpdatePublicKey
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "artifacts\update-signing"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$privateKeyPath = Join-Path $OutputDirectory "update-manifest-private-key.pem"
$publicKeyPath = Join-Path $OutputDirectory "update-manifest-public-key.pem"

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("wa-update-keygen-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
try {
    $csproj = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
'@

    $program = @'
using System.Security.Cryptography;
using System.Text;

static string ToPem(string label, byte[] data)
{
    var base64 = Convert.ToBase64String(data);
    var builder = new StringBuilder();
    builder.AppendLine($"-----BEGIN {label}-----");
    for (var index = 0; index < base64.Length; index += 64)
    {
        builder.AppendLine(base64.Substring(index, Math.Min(64, base64.Length - index)));
    }

    builder.AppendLine($"-----END {label}-----");
    return builder.ToString();
}

if (args.Length != 1)
{
    throw new InvalidOperationException("Output directory argument is required.");
}

Directory.CreateDirectory(args[0]);
using var rsa = RSA.Create(3072);
File.WriteAllText(
    Path.Combine(args[0], "update-manifest-private-key.pem"),
    ToPem("PRIVATE KEY", rsa.ExportPkcs8PrivateKey()),
    Encoding.ASCII);
File.WriteAllText(
    Path.Combine(args[0], "update-manifest-public-key.pem"),
    ToPem("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo()),
    Encoding.ASCII);
'@

    Set-Content -Path (Join-Path $tempDir "KeyGen.csproj") -Value $csproj -Encoding UTF8
    Set-Content -Path (Join-Path $tempDir "Program.cs") -Value $program -Encoding UTF8
    dotnet run --project $tempDir -- $OutputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet key generation failed with exit code $LASTEXITCODE"
    }
}
finally {
    if (Test-Path -LiteralPath $tempDir) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force
    }
}

$publicKeyPem = Get-Content -Path $publicKeyPath -Raw

if ($UpdatePublicKey) {
    $trustPath = Join-Path $root "WorkstationAgent.Update\UpdateTrust.cs"
    $source = Get-Content -Path $trustPath -Raw
    $escapedPublicKey = $publicKeyPem.Trim()
    $source = [System.Text.RegularExpressions.Regex]::Replace(
        $source,
        'public const string ManifestPublicKeyPem = """[\s\S]*?""";',
        "public const string ManifestPublicKeyPem = `"`"`"`r`n$escapedPublicKey`r`n`"`"`";")
    Set-Content -Path $trustPath -Value $source -Encoding UTF8
}

Write-Host "Private key: $privateKeyPath"
Write-Host "Public key: $publicKeyPath"
if ($UpdatePublicKey) {
    Write-Host "Updated WorkstationAgent.Update\UpdateTrust.cs with the public key."
}
