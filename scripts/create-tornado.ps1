#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new output folder.' }
$textures = Join-Path $output 'Textures'
[void](New-Item -ItemType Directory -Path $textures)
# Deterministic procedural sprite masks; no external asset library required.
Add-Type -AssemblyName System.Drawing.Common
foreach ($kind in @('cloud','wind','spark')) {
    $bitmap = [Drawing.Bitmap]::new(256,256)
    try {
        for ($y=0; $y -lt 256; $y++) { for ($x=0; $x -lt 256; $x++) {
            $u = ($x-127.5)/127.5; $v = ($y-127.5)/127.5
            $r2 = $u*$u+$v*$v
            $mask = switch ($kind) {
                'cloud' {
                    $noise = 0.60 + 0.16*[Math]::Sin(9*$u+3*[Math]::Sin(5*$v)) + 0.12*[Math]::Cos(13*$v+5*$u) + 0.07*[Math]::Sin(27*$u-19*$v)
                    [Math]::Pow([Math]::Max(0,1-$r2),1.8)*$noise
                }
                'wind' {
                    $curve = $v - 0.28*[Math]::Sin($u*2.6)
                    [Math]::Exp(-$curve*$curve*400)*[Math]::Pow([Math]::Max(0,1-$u*$u),1.5)
                }
                'spark' { [Math]::Exp(-$r2*9)*[Math]::Max(0,1-$r2) }
            }
            $alpha = [int][Math]::Clamp(255*$mask,0,255)
            $bitmap.SetPixel($x,$y,[Drawing.Color]::FromArgb($alpha,255,255,255))
        } }
        $bitmap.Save((Join-Path $textures ($kind+'.png')),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}
Import-Module (Join-Path $PSScriptRoot 'editor-host-client.psm1') -Force
$p = Start-EditorHost -Version '1.80.7'
function Checked($r) { if (!$r.ok) { throw ($r.error.code + ': ' + $r.error.message) }; $r.result }
try {
    $state = Checked (Send-EditorRequest $p @{method='new';durationFrames=480})
    $state = Checked (Send-EditorRequest $p @{method='apply';sessionId=$state.sessionId;revision=$state.revision;operations=@(@{kind='node.setRendered';nodeId=$state.nodes[1].id;value=$false})})
    $state = Checked (Send-EditorRequest $p @{method='apply';sessionId=$state.sessionId;revision=$state.revision;operations=@(@{kind='recipe.tornado';nodeId=$state.nodes[0].id;textureDirectory=$textures})})
    $count = $state.nodes.Count
    $state = Checked (Send-EditorRequest $p @{method='undo';sessionId=$state.sessionId;revision=$state.revision})
    if ($state.nodes.Count -ne 2) { throw 'Recipe Undo failed.' }
    $state = Checked (Send-EditorRequest $p @{method='redo';sessionId=$state.sessionId;revision=$state.revision})
    if ($state.nodes.Count -ne $count) { throw 'Recipe Redo failed.' }
    $result = Checked (Send-EditorRequest $p @{method='saveAs';sessionId=$state.sessionId;revision=$state.revision;path=(Join-Path $output 'Raging-Tornado.efkefc')})
    $result | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $output 'creation-report.json') -Encoding utf8
    Write-Output "Created: $($result.path) ($count nodes; Undo/Redo and official reload verified)"
} finally { Stop-EditorHost $p }
