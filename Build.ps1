param([string]$DotNet = 'dotnet', [switch]$WidgetOnly)

$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'dist'
$null = New-Item -ItemType Directory -Force -Path $output
$widgetPackage = Join-Path $output 'SZU-Dorm-Electricity.dbxwidget'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryPackage = "$widgetPackage.tmp"
$stream = [IO.File]::Open($temporaryPackage, [IO.FileMode]::Create)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($name in 'manifest.json', 'logic.js') {
            $file = Join-Path $PSScriptRoot "widget\$name"
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, $name)
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
Move-Item -LiteralPath $temporaryPackage -Destination $widgetPackage -Force

if ($WidgetOnly) { return }

$sdk = (Get-Command $DotNet -CommandType Application).Source
$sdkDirectory = Split-Path -Parent $sdk
$serviceOutput = Join-Path $output 'service'
& $sdk publish (Join-Path $PSScriptRoot 'service\DormElectricityBridge.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -o $serviceOutput
if ($LASTEXITCODE -ne 0) { throw "本机服务构建失败，退出代码 $LASTEXITCODE。" }

$portable = Join-Path $output 'bundle'
$null = New-Item -ItemType Directory -Force -Path $portable
Copy-Item -LiteralPath $widgetPackage, "$serviceOutput\DormElectricityBridge.exe", `
    "$PSScriptRoot\Install-Bridge.ps1", "$PSScriptRoot\StartBridge.vbs", `
    "$PSScriptRoot\README.md", "$PSScriptRoot\LICENSE" -Destination $portable -Force
Copy-Item -LiteralPath "$sdkDirectory\LICENSE.txt" -Destination "$portable\DotNet-LICENSE.txt" -Force
Copy-Item -LiteralPath "$sdkDirectory\ThirdPartyNotices.txt" -Destination "$portable\DotNet-ThirdPartyNotices.txt" -Force

$package = Join-Path $output 'SZU-Dorm-Electricity-win-x64.zip'
$files = @('SZU-Dorm-Electricity.dbxwidget', 'DormElectricityBridge.exe', 'Install-Bridge.ps1',
    'StartBridge.vbs', 'README.md', 'LICENSE', 'DotNet-LICENSE.txt', 'DotNet-ThirdPartyNotices.txt')
Compress-Archive -LiteralPath ($files | ForEach-Object { Join-Path $portable $_ }) `
    -DestinationPath $package -Force
$hashes = foreach ($file in $package, $widgetPackage) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant(), (Split-Path -Leaf $file)
}
Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Value $hashes -Encoding ascii
