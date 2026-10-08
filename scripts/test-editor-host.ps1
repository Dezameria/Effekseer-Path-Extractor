#requires -Version 7.0
param([Parameter(Mandatory)][ValidateSet('1.80.7','1.70e')][string]$Version, [string]$HostDirectory, [string]$SamplePath, [string]$MismatchedSamplePath)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'editor-host-client.psm1') -Force
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root ('artifacts/editor-host-tests/' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $out)
$process = Start-EditorHost -Version $Version -HostDirectory $HostDirectory
$checks = [Collections.Generic.List[string]]::new()
function Assert($condition, [string]$label) { if (!$condition) { throw "FAILED: $label" }; $checks.Add($label) }
function Request([hashtable]$request) { Send-EditorRequest $process $request }
function Change([string]$method, $state, [hashtable]$extra = @{}) {
    $request = @{method = $method; sessionId = $state.sessionId; revision = $state.revision}
    foreach ($key in $extra.Keys) { $request[$key] = $extra[$key] }
    Request $request
}
try {
    $cap = Request @{method='capabilities'}
    Assert ($cap.ok -and $cap.result.version -eq $Version -and !$cap.result.liveEditor) 'matching capabilities'
    $state = (Request @{method='new'}).result
    Assert ($state.nodes.Count -eq 2) 'native default scene'
    $baseline = Join-Path $out 'baseline.efkefc'
    $reply = Change 'saveAs' $state @{path=$baseline}
    Assert $reply.ok 'default project roundtrip'
    $state = $reply.result.snapshot
    $baselineHash = (Get-FileHash -LiteralPath $baseline).Hash
    $node = $state.nodes[1].id
    $before = $state
    $reply = Change 'select' $state @{nodeId=$node}
    Assert ($reply.ok -and $reply.result.selectedNodeId -eq $node) 'selection'
    $state = $reply.result
    $reply = Change 'apply' $before @{operations=@(@{kind='node.rename';nodeId=$node;name='stale'})}
    Assert (!$reply.ok -and $reply.error.code -eq 'StaleRevision') 'stale revision rejected'
    $reply = Change 'apply' $state @{operations=@(@{kind='node.rename';nodeId=$node;name='should not apply'},@{kind='unknown';nodeId=$node})}
    Assert (!$reply.ok -and (Request @{method='snapshot'}).result.nodes[1].name -eq 'Node') 'whole plan validation'
    $reply = Change 'apply' $state @{operations=@(@{kind='color.setFixed';nodeId=$node;rgba=@(160,50,255,180)},@{kind='scale.setFixed';nodeId=$node;xyz=@(0.5,0.75,1.25)},@{kind='node.rename';nodeId=$node;name='สีม่วง'})}
    Assert $reply.ok ('apply color/scale/name: ' + $reply.error.message)
    $state = $reply.result
    Assert (($state.nodes[1].color -join ',') -eq '160,50,255,180' -and ($state.nodes[1].scale -join ',') -eq '0.5,0.75,1.25') 'typed values changed'
    $state = (Change 'undo' $state).result
    Assert ($state.nodes[1].name -eq 'Node' -and ($state.nodes[1].color -join ',') -eq '255,255,255,255' -and ($state.nodes[1].scale -join ',') -eq '1,1,1') 'one undo restores whole plan'
    $state = (Change 'redo' $state).result
    Assert ($state.nodes[1].name -eq 'สีม่วง') 'redo and Unicode'
    $state = (Change 'apply' $state @{operations=@(@{kind='node.create';nodeId=$node;name='child'})}).result
    Assert ($state.nodes.Count -eq 3 -and $state.nodes[2].parentId -eq $node) 'native node creation'
    $state = (Change 'select' $state @{nodeId=$state.nodes[2].id}).result
    $state = (Change 'undo' $state).result
    Assert ($state.nodes.Count -eq 2) 'undo node creation'
    Assert ($null -eq $state.selectedNodeId) 'undo clears removed selection'
    $reply = Change 'apply' $state @{operations=@(@{kind='color.setFixed';nodeId=$node;rgba=@(300,0,0,255)})}
    Assert (!$reply.ok -and $reply.error.code -eq 'InvalidColor') 'invalid color rejected'
    $reply = Change 'apply' $state @{operations=@(@{kind='scale.setFixed';nodeId=$state.nodes[0].id;xyz=@(1,1,1)})}
    Assert (!$reply.ok -and $reply.error.code -eq 'UnsupportedBinding') 'root transform rejected'
    $target = Join-Path $out 'สีม่วง.efkefc'
    $reply = Change 'saveAs' $state @{path=$target}
    Assert $reply.ok ('official save/reload: ' + $reply.error.message)
    $state = $reply.result.snapshot
    Assert ((Test-Path -LiteralPath $target) -and $state.nodes[1].name -eq 'สีม่วง' -and $reply.result.undoHistoryReset) 'published only after roundtrip'
    $bytes = [IO.File]::ReadAllBytes($target)
    $chunks = @(); $position = 8
    while ($position -lt $bytes.Length) { $chunks += [Text.Encoding]::ASCII.GetString($bytes,$position,4); $position += 8 + [BitConverter]::ToInt32($bytes,$position+4) }
    Assert (($chunks -contains 'EDIT') -and ($chunks -contains 'BIN_') -and ($chunks -contains 'INFO')) 'official editor/runtime/resource chunks'
    function RuntimeHash([string]$path) {
        $data = [IO.File]::ReadAllBytes($path); $offset = 8
        while ($offset -lt $data.Length) {
            $length = [BitConverter]::ToInt32($data,$offset+4)
            if ([Text.Encoding]::ASCII.GetString($data,$offset,4) -eq 'BIN_') {
                $sha = [Security.Cryptography.SHA256]::Create()
                try { return [Convert]::ToBase64String($sha.ComputeHash($data,$offset+8,$length)) } finally { $sha.Dispose() }
            }
            $offset += 8 + $length
        }
        throw 'Runtime chunk missing'
    }
    Assert ((RuntimeHash $target) -ne (RuntimeHash $baseline)) 'runtime binary rebuilt for edits'
    Assert ((Get-FileHash -LiteralPath $baseline).Hash -eq $baselineHash) 'input file remains unchanged'
    $savedHash = (Get-FileHash -LiteralPath $target).Hash
    $reply = Change 'saveAs' $state @{path=$target}
    Assert (!$reply.ok -and $reply.error.code -eq 'OutputExists' -and (Get-FileHash -LiteralPath $target).Hash -eq $savedHash) 'overwrite rejected'
    $reply = Change 'apply' $before @{operations=@(@{kind='node.rename';nodeId=$node;name='stale'})}
    Assert (!$reply.ok -and $reply.error.code -eq 'StaleRevision') 'old session rejected after reload'
    [IO.File]::AppendAllText($target, 'changed externally')
    $reply = Change 'saveAs' $state @{path=(Join-Path $out 'conflict.efkefc')}
    Assert (!$reply.ok -and $reply.error.code -eq 'SourceChanged') 'source hash conflict'
    if ($MismatchedSamplePath) {
        $reply = Change 'open' $state @{path=[IO.Path]::GetFullPath($MismatchedSamplePath)}
        Assert (!$reply.ok -and $reply.error.code -eq 'VersionMismatch') 'cross-version input rejected'
    }
    if ($SamplePath) {
        $sampleHash = (Get-FileHash -LiteralPath $SamplePath).Hash
        $reply = Change 'open' $state @{path=[IO.Path]::GetFullPath($SamplePath)}
        Assert $reply.ok ('sample opened: ' + $reply.error.message)
        $state = $reply.result
        $materialNode = $state.nodes | Where-Object material -EQ 'File' | Select-Object -First 1
        if ($materialNode) {
            $reply = Change 'apply' $state @{operations=@(@{kind='color.setFixed';nodeId=$materialNode.id;rgba=@(255,0,0,255)})}
            Assert (!$reply.ok -and $reply.error.code -eq 'UnsupportedBinding') 'custom material color override rejected'
        }
        $reply = Change 'saveAs' $state @{path=(Join-Path $out 'sample-copy.efkefc')}
        Assert $reply.ok ('sample roundtrip: ' + $reply.error.message)
        Assert ((Get-FileHash -LiteralPath $SamplePath).Hash -eq $sampleHash) 'sample original untouched'
    }
    @{version=$Version; passed=$checks.Count; checks=$checks; output=$out} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $out 'result.json')
    Write-Output "PASS $Version : $($checks.Count) checks; $out"
} finally { Stop-EditorHost $process }
