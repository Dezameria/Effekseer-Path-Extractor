#requires -Version 7.0
function Start-EditorHost {
    param([Parameter(Mandatory)][ValidateSet('1.80.7','1.70e')][string]$Version, [string]$HostDirectory)
    $root = Split-Path -Parent $PSScriptRoot
    if (!$HostDirectory) { $HostDirectory = Join-Path $root "release/editor-host/$Version" }
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $info.WorkingDirectory = [IO.Path]::GetFullPath($HostDirectory)
    if ($Version -eq '1.70e') { $info.FileName = Join-Path $HostDirectory 'EffekseerAI.Host.exe' }
    else {
        $info.FileName = Join-Path $root '.tools/dotnet/dotnet.exe'
        if (!(Test-Path -LiteralPath $info.FileName)) { $info.FileName = (Get-Command dotnet -ErrorAction Stop).Source }
        $info.ArgumentList.Add((Join-Path $HostDirectory 'EffekseerAI.Host.dll'))
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    [void]$process.Start()
    # Drain stderr concurrently so native loader diagnostics cannot deadlock stdout.
    $process | Add-Member -NotePropertyName ErrorRead -NotePropertyValue $process.StandardError.ReadToEndAsync()
    return $process
}
function Send-EditorRequest {
    param([Parameter(Mandatory)]$HostProcess, [Parameter(Mandatory)][hashtable]$Request)
    $HostProcess.StandardInput.WriteLine(($Request | ConvertTo-Json -Depth 30 -Compress))
    $HostProcess.StandardInput.Flush()
    $read = $HostProcess.StandardOutput.ReadLineAsync()
    if (!$read.Wait(30000)) { $HostProcess.Kill(); throw 'Editor host timed out; its session was discarded.' }
    $line = $read.Result
    if (!$line) { throw 'Editor host exited without a response.' }
    return $line | ConvertFrom-Json -Depth 30
}
function Stop-EditorHost {
    param([Parameter(Mandatory)]$HostProcess)
    if (!$HostProcess.HasExited) {
        $HostProcess.StandardInput.Close()
        if (!$HostProcess.WaitForExit(3000)) { $HostProcess.Kill(); $HostProcess.WaitForExit() }
    }
    $HostProcess.Dispose()
}
Export-ModuleMember -Function Start-EditorHost, Send-EditorRequest, Stop-EditorHost
