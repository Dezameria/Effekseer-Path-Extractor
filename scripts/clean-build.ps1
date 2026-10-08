$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projects = @('src\ResourceManager.App', 'src\ResourceManager.Core', 'src\ResourceManager.Infrastructure', 'src\ResourceManager.Inspector', 'tests\ResourceManager.Tests')
$archiveRoot = Join-Path $projectRoot ('artifacts\archive\build-cache-' + [guid]::NewGuid().ToString('N'))
$moved = 0
foreach ($project in $projects) {
    foreach ($folder in @('bin', 'obj')) {
        $target = [System.IO.Path]::GetFullPath((Join-Path $projectRoot (Join-Path $project $folder)))
        if (-not $target.StartsWith($projectRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup target is outside the project.' }
        if (-not (Test-Path -LiteralPath $target)) { continue }
        $items = @((Get-Item -LiteralPath $target -Force)) + @(Get-ChildItem -LiteralPath $target -Recurse -Force)
        if ($items | Where-Object { $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint }) { throw "Linked path found; cleanup skipped: $target" }
        $files = @($items | Where-Object { -not $_.PSIsContainer })
        $destination = [System.IO.Path]::GetFullPath((Join-Path $archiveRoot (Join-Path $project $folder)))
        if (-not $destination.StartsWith($archiveRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Archive target is outside the archive.' }
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Move-Item -LiteralPath $target -Destination $destination
        $moved += $files.Count
    }
}
Write-Output "Archived $moved generated build files. Source, assets and releases kept. Archive: $archiveRoot"
