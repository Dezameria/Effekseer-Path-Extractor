#requires -Version 7.0
param(
    [Parameter(Mandatory)][ValidateSet('1.80.7', '1.70e')][string]$Version,
    [Parameter(Mandatory)][string]$EditorDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$editor = (Resolve-Path -LiteralPath $EditorDirectory).Path
foreach ($candidate in @($editor, (Join-Path $editor 'bin'), (Join-Path $editor 'Tool'), (Join-Path $editor 'Tool/bin'))) {
    if (Test-Path -LiteralPath (Join-Path $candidate 'EffekseerCore.dll')) { $editor = $candidate; break }
}
if (!(Test-Path -LiteralPath (Join-Path $editor 'Viewer.dll'))) { throw 'Select the matching Effekseer installation folder.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root "release/editor-host/$Version" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output.StartsWith($editor.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or $output -eq $editor) { throw 'Build output must be outside the editor installation.' }
$sdk = Join-Path $root '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
& $sdk build (Join-Path $root 'src/EffekseerAI.Host/EffekseerAI.Host.csproj') -c Release "-p:EffekseerVersion=$Version" "-p:EffekseerDirectory=$editor" -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Editor host build failed.' }
Import-Module (Join-Path $PSScriptRoot 'editor-host-client.psm1') -Force
$hostProcess = Start-EditorHost -Version $Version -HostDirectory $output
try {
    $capabilities = Send-EditorRequest $hostProcess @{ method = 'capabilities' }
    if (!$capabilities.ok -or $capabilities.result.version -ne $Version) { throw 'Installed Core does not match the selected version.' }
} finally { Stop-EditorHost $hostProcess }
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $output 'LICENSE.txt')
$license = @((Join-Path $editor 'LICENSE_TOOL'), (Join-Path $editor '../LICENSE_TOOL'), (Join-Path $editor '../../LICENSE_TOOL')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($license) { Copy-Item -LiteralPath $license -Destination (Join-Path $output 'LICENSE_TOOL') }
@{ version = $Version; source = $editor; builtUtc = [DateTime]::UtcNow.ToString('o');
    coreSha256 = (Get-FileHash -LiteralPath (Join-Path $editor 'EffekseerCore.dll')).Hash;
    capabilities = $capabilities.result } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'profile.json') -Encoding utf8
Write-Output "Editor host ready: $output"
