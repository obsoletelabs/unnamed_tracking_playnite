param(
    [Parameter(Mandatory = $true)]
    [string]$ExtensionDirectory,
    [string]$ExpectedVersion = ""
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $ExtensionDirectory -PathType Container)) {
    throw "Extension directory does not exist: $ExtensionDirectory"
}
$manifestPath = Join-Path $ExtensionDirectory "extension.yaml"
if (-not (Test-Path $manifestPath -PathType Leaf)) { throw "extension.yaml was not produced." }
$manifest = Get-Content $manifestPath -Raw
$fields = @{}
foreach ($name in @("Id", "Name", "Author", "Version", "Module", "Type")) {
    $fieldMatches = [regex]::Matches($manifest, "(?m)^${name}:[ \t]*(\S[^\r\n]*)\r?$")
    if ($fieldMatches.Count -ne 1) { throw "Manifest must contain exactly one non-empty '$name'." }
    $fields[$name] = $fieldMatches[0].Groups[1].Value.Trim()
}
if ($fields.Id -ne 'UnnamedTrackingPlaynite') { throw "The stable extension ID changed." }
if ($fields.Type -ne 'GenericPlugin') { throw "Manifest Type must remain GenericPlugin." }
if ($fields.Module -ne 'UnnamedTrackingPlaynite.dll') { throw "Unexpected manifest module." }
if ($fields.Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must contain major.minor.patch." }
$modulePath = Join-Path $ExtensionDirectory $fields.Module
if (-not (Test-Path $modulePath -PathType Leaf)) { throw "Manifest module was not produced." }

$privatePlayniteAssemblies = @(
    "Playnite.dll", "Playnite.Common.dll", "Playnite.DesktopApp.dll",
    "Playnite.FullscreenApp.dll", "PlayniteSDK.dll", "Playnite.SDK.dll"
)
foreach ($file in Get-ChildItem $ExtensionDirectory -Recurse -File) {
    if ($file.Name -in $privatePlayniteAssemblies) { throw "Extension output must not package Playnite dependency '$($file.Name)'." }
}
$project = [xml](Get-Content (Join-Path $PSScriptRoot '../src/UnnamedTrackingPlaynite/UnnamedTrackingPlaynite.csproj') -Raw)
$properties = $project.Project.PropertyGroup
if (-not $ExpectedVersion) { $ExpectedVersion = $properties.Version }
if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Expected version must contain major.minor.patch." }
if ($fields.Version -cne $ExpectedVersion) { throw "Manifest does not match expected version '$ExpectedVersion'." }
$expectedAssemblyVersion = "$ExpectedVersion.0"
$assembly = [System.Reflection.AssemblyName]::GetAssemblyName((Resolve-Path $modulePath))
if ($assembly.Name -ne 'UnnamedTrackingPlaynite' -or $assembly.Version.ToString() -ne $expectedAssemblyVersion) {
    throw "Built assembly identity/version does not agree with the manifest and project."
}
$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path $modulePath))
if ($fileVersion.FileVersion -ne $expectedAssemblyVersion) { throw "Built file version does not match the expected version." }
Write-Host "Validated extension $($fields.Id) $($fields.Version)"
