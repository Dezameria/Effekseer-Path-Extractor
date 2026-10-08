#requires -Version 7.0
param(
    [Parameter(Mandatory)][ValidateSet('1.80.7','1.70e')][string]$Version,
    [string]$InputPath,
    [switch]$Create,
    [switch]$Inspect,
    [string]$NodePath = '0',
    [string]$Color,
    [Nullable[float]]$Scale,
    [string]$Name,
    [string]$OutputPath,
    [string]$ExpectedSha256
)
$ErrorActionPreference = 'Stop'
if ($Create -eq [bool]$InputPath) { throw 'Specify either -Create or -InputPath.' }
if (!$Inspect -and !$OutputPath) { throw 'Specify a new -OutputPath.' }
Import-Module (Join-Path $PSScriptRoot 'editor-host-client.psm1') -Force
$process = Start-EditorHost -Version $Version
function Require-Success($reply) {
    if (!$reply.ok) { throw ($reply.error.code + ': ' + $reply.error.message) }
    return $reply.result
}
try {
    $request = if ($Create) { @{method='new'} } else { @{method='open';path=[IO.Path]::GetFullPath($InputPath)} }
    $state = Require-Success (Send-EditorRequest $process $request)
    if ($ExpectedSha256 -and $state.sourceHash -ne $ExpectedSha256) { throw 'Input changed since inspection. Inspect again.' }
    if ($Inspect) { $state | ConvertTo-Json -Depth 20; return }
    $node = $state.nodes | Where-Object path -EQ $NodePath | Select-Object -First 1
    if (!$node) { throw "Node path not found: $NodePath. Use -Inspect to list paths." }
    $operations = @()
    if ($Color) {
        if ($Color -notmatch '^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$') { throw 'Use #RRGGBB or #RRGGBBAA.' }
        $rgba = @(1,3,5 | ForEach-Object { [Convert]::ToInt32($Color.Substring($_,2),16) })
        $rgba += if ($Color.Length -eq 9) { [Convert]::ToInt32($Color.Substring(7,2),16) } else { $node.color[3] }
        $operations += @{kind='color.setFixed';nodeId=$node.id;rgba=$rgba}
    }
    if ($null -ne $Scale) { $operations += @{kind='scale.setFixed';nodeId=$node.id;xyz=@($Scale,$Scale,$Scale)} }
    if ($Name) { $operations += @{kind='node.rename';nodeId=$node.id;name=$Name} }
    if ($operations.Count) {
        $state = Require-Success (Send-EditorRequest $process @{method='apply';sessionId=$state.sessionId;revision=$state.revision;operations=$operations})
    }
    $result = Require-Success (Send-EditorRequest $process @{method='saveAs';sessionId=$state.sessionId;revision=$state.revision;path=[IO.Path]::GetFullPath($OutputPath)})
    $result | ConvertTo-Json -Depth 20
} finally { Stop-EditorHost $process }
