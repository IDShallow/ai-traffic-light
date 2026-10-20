param(
  [Parameter(Mandatory=$true)][string]$Url,
  [string]$Version = "",
  [string]$Notes = "",
  [string]$Setup = "",
  [string]$Out = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($Setup -eq "") { $Setup = Join-Path $root "dist\ClaudeTrafficLightSetup.exe" }
if ($Out -eq "")   { $Out   = Join-Path $root "dist\latest.json" }
if (-not (Test-Path $Setup)) { throw "setup not found: $Setup" }
if ($Version -eq "") {
  $Version = (Get-Item $Setup).VersionInfo.FileVersion
}
$hash = (Get-FileHash -Algorithm SHA256 -Path $Setup).Hash.ToLower()
$obj = [ordered]@{
  version   = $Version
  url       = $Url
  sha256    = $hash
  notes     = $Notes
  mandatory = $false
}
$obj | ConvertTo-Json | Set-Content -Encoding UTF8 -Path $Out
Write-Host "wrote $Out"
Write-Host "version=$Version"
Write-Host "sha256=$hash"
