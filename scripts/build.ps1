param([switch]$Publish, [string]$ArtifactRoot = 'release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
Push-Location $projectRoot
try {
    & $sdk build 'EffekseerResourceManager.sln' -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $sdk test 'tests\ResourceManager.Tests\ResourceManager.Tests.csproj' -c Release --no-build --nologo --logger 'trx;LogFileName=validation.trx' --results-directory 'artifacts\tests'
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($Publish) {
        & $sdk publish 'src\ResourceManager.App\ResourceManager.App.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o (Join-Path $ArtifactRoot 'app') --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
        & $sdk publish 'src\ResourceManager.Inspector\ResourceManager.Inspector.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o (Join-Path $ArtifactRoot 'inspector') --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Inspector publish failed.' }
        Copy-Item -LiteralPath 'docs\licenses\Effekseer-MIT.txt' -Destination (Join-Path $ArtifactRoot 'Effekseer-MIT.txt')
        Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $ArtifactRoot 'LICENSE.txt')
    }
}
finally { Pop-Location }
