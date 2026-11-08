param(
    [Parameter(Mandatory = $true)]
    [int]$TargetProcessId,

    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory,

    [string]$ReleaseTag = 'staged-release'
)

$workspacePath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\')
$sourcePath = (Resolve-Path -LiteralPath $SourceDirectory).Path.TrimEnd('\')
$destinationPath = (Resolve-Path -LiteralPath $DestinationDirectory).Path.TrimEnd('\')
$allowedPrefix = $workspacePath + '\'

if (-not $sourcePath.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not $destinationPath.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and destination must stay inside the K-OPT 2026 workspace.'
}

Wait-Process -Id $TargetProcessId -ErrorAction SilentlyContinue

foreach ($item in Get-ChildItem -LiteralPath $sourcePath -Force) {
    Copy-Item -LiteralPath $item.FullName -Destination $destinationPath -Recurse -Force
}

$markerPath = Join-Path $destinationPath '.staged-release-applied.txt'
$markerText = "$ReleaseTag`r`n적용 시각: $([DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss zzz'))"
[IO.File]::WriteAllText($markerPath, $markerText, [Text.UTF8Encoding]::new($false))
