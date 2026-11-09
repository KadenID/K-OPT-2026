#requires -Version 7.4
[CmdletBinding()]
param([ValidateSet('win-x64', 'osx-arm64', 'osx-x64')][string]$RuntimeIdentifier = 'win-x64')
$ErrorActionPreference = 'Stop'
$boardRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $boardRoot
$project = Join-Path $boardRoot 'src/Kopt2026.App/Kopt2026.App.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
$bundleVersion = $version.Split('-')[0]
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$packageName = "K-OPT_2026-$RuntimeIdentifier-$version-$stamp"
$packageRoot = Join-Path $boardRoot "dist/releases/$packageName"
$isMacTarget = $RuntimeIdentifier.StartsWith('osx-')
$appRoot = Join-Path $packageRoot 'K-OPT 2026.app'
$binaryRoot = if ($isMacTarget) { Join-Path $appRoot 'Contents/MacOS' } else { $packageRoot }
$resources = Join-Path $appRoot 'Contents/Resources'
if (Test-Path -LiteralPath $packageRoot) { throw "Output already exists: $packageRoot" }
$null = New-Item -ItemType Directory -Path $binaryRoot -Force
if ($isMacTarget) { $null = New-Item -ItemType Directory -Path $resources -Force }
$singleFile = if ($isMacTarget) { 'false' } else { 'true' }
& dotnet publish $project -c Release -r $RuntimeIdentifier --self-contained true -o $binaryRoot `
    -p:PublishSingleFile=$singleFile -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:DebugSymbols=false -p:DebugType=None `
    -p:NuGetLockFilePath=obj/packages.$RuntimeIdentifier.lock.json
if ($LASTEXITCODE -ne 0) { throw 'Common application publish failed.' }
$runtimeConfigPath = Join-Path $boardRoot "src/Kopt2026.App/bin/Release/net10.0/$RuntimeIdentifier/Kopt2026.App.runtimeconfig.json"
if ($isMacTarget) {
    foreach ($required in @('Kopt2026.App', 'highs.dylib', 'libAvaloniaNative.dylib', 'libSkiaSharp.dylib', 'libHarfBuzzSharp.dylib', 'libhostfxr.dylib')) {
        if (-not (Test-Path -LiteralPath (Join-Path $binaryRoot $required))) { throw "Missing bundle dependency: $required" }
    }
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>Kopt2026.App</string>
  <key>CFBundleIdentifier</key><string>org.kopt2026.app</string>
  <key>CFBundleName</key><string>K-OPT 2026</string>
  <key>CFBundleDisplayName</key><string>K-OPT 2026</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleVersion</key><string>$bundleVersion</string>
  <key>CFBundleShortVersionString</key><string>$bundleVersion</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
"@
    Set-Content -LiteralPath (Join-Path $appRoot 'Contents/Info.plist') -Value $plist -Encoding utf8
} elseif (-not (Test-Path -LiteralPath (Join-Path $binaryRoot 'Kopt2026.App.exe'))) { throw 'Missing Windows executable.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'kanban-board-docs/public/legal') -Destination (Join-Path $packageRoot 'legal') -Recurse
Copy-Item -LiteralPath (Join-Path $boardRoot 'samples') -Destination (Join-Path $packageRoot 'samples') -Recurse
$manualSource = Join-Path $repoRoot 'kanban-board-docs/public/manual'
$userGuides = @(Get-ChildItem -LiteralPath $manualSource -File -Filter '01_K-OPT_2026_간단사용설명서_*.md')
if ($userGuides.Count -ne 1) { throw 'Exactly one current user guide is required.' }
$manualRoot = Join-Path $packageRoot 'manual'
$null = New-Item -ItemType Directory -Path $manualRoot -Force
Get-ChildItem -LiteralPath $manualSource -File | Where-Object { $_.Extension -in @('.md', '.html') } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $manualRoot }
if ($isMacTarget) { Copy-Item -LiteralPath $manualRoot -Destination (Join-Path $resources 'manual') -Recurse }
$userGuideLink = 'manual/' + $userGuides[0].Name

$licenseRoot = Join-Path $packageRoot 'legal/nuget'
$null = New-Item -ItemType Directory -Path $licenseRoot -Force
$assets = Get-Content -LiteralPath (Join-Path $boardRoot 'src/Kopt2026.App/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$packageCache = @($assets.packageFolders.Keys)[0]
$inventory = foreach ($library in $assets.libraries.GetEnumerator()) {
    if ($library.Value.type -ne 'package') { continue }
    $cachePath = Join-Path $packageCache $library.Value.path
    $specFile = Get-ChildItem -LiteralPath $cachePath -Filter '*.nuspec' -File | Select-Object -First 1
    if (-not $specFile) { continue }
    [xml]$spec = Get-Content -LiteralPath $specFile.FullName -Raw
    $meta = $spec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
    $destination = Join-Path $licenseRoot ($library.Key.Replace('/', '-'))
    $null = New-Item -ItemType Directory -Path $destination -Force
    Copy-Item -LiteralPath $specFile.FullName -Destination $destination
    Get-ChildItem -LiteralPath $cachePath -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|THIRD.PARTY)' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destination
    }
    [ordered]@{ package = $library.Key; copyright = [string]$meta.copyright; license = [string]$meta.license; source = [string]$meta.repository.url }
}
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $packageRoot 'legal/package-inventory.json') -Encoding utf8
$mitLibrary = $assets.libraries.GetEnumerator() | Where-Object { $_.Key -like 'SkiaSharp/*' } | Select-Object -First 1
$mitSource = Get-Content -LiteralPath (Join-Path $packageCache ($mitLibrary.Value.path + '/LICENSE.txt')) -Raw
$mitTerms = $mitSource.Substring($mitSource.IndexOf('Permission is hereby granted'))
$mitNotice = [System.Text.StringBuilder]::new()
foreach ($component in $inventory | Where-Object { $_.license -eq 'MIT' }) {
    $null = $mitNotice.AppendLine($component.package).AppendLine($component.copyright).AppendLine($component.source).AppendLine()
}
$null = $mitNotice.AppendLine($mitTerms)
Set-Content -LiteralPath (Join-Path $packageRoot 'legal/MIT-COMPONENT-NOTICES.txt') -Value $mitNotice.ToString() -Encoding utf8
$runtimeVersion = (Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks[0].version
$runtimeCache = Join-Path $packageCache "microsoft.netcore.app.runtime.$RuntimeIdentifier/$runtimeVersion"
$runtimeLegal = Join-Path $packageRoot 'legal/dotnet'
$null = New-Item -ItemType Directory -Path $runtimeLegal -Force
Get-ChildItem -LiteralPath $runtimeCache -File | Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY)' } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $runtimeLegal
}
$termsPath = Join-Path $packageRoot 'legal/THIRD-PARTY-NOTICES.txt'
$terms = [System.IO.File]::ReadAllText($termsPath).Replace('.NET Runtime 10.0.3', ".NET Runtime $runtimeVersion")
[System.IO.File]::WriteAllText($termsPath, $terms, [System.Text.UTF8Encoding]::new($false))
if ($isMacTarget) { Copy-Item -LiteralPath (Join-Path $packageRoot 'legal') -Destination (Join-Path $resources 'legal') -Recurse }

$nativeChecked = $false
$signature = 'not-signed-on-this-host'
if ($IsMacOS -and $isMacTarget) {
    & chmod +x (Join-Path $binaryRoot 'Kopt2026.App')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot set executable permission.' }
    & /usr/bin/codesign --force --deep --sign - $appRoot
    if ($LASTEXITCODE -ne 0) { throw 'Ad-hoc signing failed.' }
    & /usr/bin/codesign --verify --deep --strict $appRoot
    if ($LASTEXITCODE -ne 0) { throw 'Bundle signature verification failed.' }
    $signature = 'ad-hoc; not notarized'
}
$hostOs = if ($IsMacOS) { 'osx' } elseif ($IsWindows) { 'win' } else { 'linux' }
$hostRid = $hostOs + '-' + [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
if ($hostRid -eq $RuntimeIdentifier) {
    $exe = Join-Path $binaryRoot $(if ($isMacTarget) { 'Kopt2026.App' } else { 'Kopt2026.App.exe' })
    $start = [System.Diagnostics.ProcessStartInfo]::new($exe)
    $start.ArgumentList.Add('--smoke-test')
    $start.WorkingDirectory = $binaryRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Packaged solver check timed out.' }
        $outputText = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Packaged solver check failed: $errorText" }
        $smoke = $outputText | ConvertFrom-Json
        if (-not $smoke.passed) { throw 'Packaged solver did not report success.' }
        Set-Content -LiteralPath (Join-Path $packageRoot 'smoke-test.json') -Value $outputText -Encoding utf8
        $nativeChecked = $true
    } finally { $process.Dispose() }
}
[ordered]@{ version = $version; runtime = $RuntimeIdentifier; frameworkVersion = $runtimeVersion; buildHost = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription; nativeSmokeTestExecuted = $nativeChecked; signature = $signature } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot 'validation.json') -Encoding utf8
$launch = if ($isMacTarget) { 'K-OPT 2026.app' } else { 'Kopt2026.App.exe' }
$runShortcut = if ($isMacTarget) { '`(Fn+)F5` 또는 `⌘+Enter`' } else { '`F5` 또는 `Ctrl+Enter`' }
$readme = @"
# K-OPT 2026 $version

1. 압축을 해제합니다.
2. $launch 파일을 실행합니다.
3. 파일 → 열기로 모델을 불러오거나 직접 입력한 뒤 **실행**을 누릅니다.

실행 단축키: $runShortcut

별도의 .NET 설치는 필요하지 않습니다.
[사용설명서]($userGuideLink), 예제(samples), 이용 조건과 라이선스(legal)가 함께 포함되어 있습니다.
"@
if ($isMacTarget) {
    $readme += if ($nativeChecked) { "`nMac용 미리보기입니다. 공개 배포용 Apple 서명·공증은 포함되지 않습니다.`n" }
        else { "`nMac용은 교차 빌드한 미리보기입니다. 실제 Mac 검증과 Apple 서명·공증은 완료되지 않았습니다.`n" }
}
Set-Content -LiteralPath (Join-Path $packageRoot 'README.md') -Value $readme -Encoding utf8
if ($isMacTarget) {
    # Explicit Unix modes also preserve executable permissions when built on Windows.
    Add-Type -AssemblyName System.Formats.Tar
    $archivePath = $packageRoot + '.tar.gz'
    $fileStream = [System.IO.File]::Create($archivePath)
    $gzip = [System.IO.Compression.GZipStream]::new($fileStream, [System.IO.Compression.CompressionLevel]::Optimal)
    $tar = [System.Formats.Tar.TarWriter]::new($gzip, [System.Formats.Tar.TarEntryFormat]::Pax, $false)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Sort-Object FullName) {
            $name = [System.IO.Path]::GetRelativePath($packageRoot, $file.FullName).Replace('\', '/')
            $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, $name)
            $entry.Mode = [System.IO.UnixFileMode]$(if ($file.Name -eq 'Kopt2026.App' -or $file.Extension -eq '.dylib') { 493 } else { 420 })
            $entry.DataStream = [System.IO.File]::OpenRead($file.FullName)
            try { $tar.WriteEntry($entry) } finally { $entry.DataStream.Dispose() }
        }
    } finally { $tar.Dispose(); $gzip.Dispose(); $fileStream.Dispose() }
} else {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archivePath = $packageRoot + '.zip'
    [System.IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $archivePath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($archivePath + '.sha256') -Value ($hash + '  ' + [System.IO.Path]::GetFileName($archivePath)) -Encoding utf8
Write-Output $archivePath
