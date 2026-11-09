# Regenerates the PNG exports and the multi-size Windows icon from the SVG sources.
# Requires Microsoft Edge (used headless as the SVG renderer).
#   pwsh ./assets/build-icons.ps1
$ErrorActionPreference = 'Stop'
$assets = $PSScriptRoot
$out = Join-Path $assets 'png'
$icoPath = Join-Path $assets '..\src\AgHouse.App\Assets\aghouse.ico'
New-Item -ItemType Directory -Force $out, (Split-Path $icoPath) | Out-Null

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge not found.' }
$profileDir = Join-Path $env:TEMP 'aghouse-svgrender'

function Render([string]$svg, [int]$size, [string]$png) {
    $html = Join-Path $env:TEMP "aghouse-render-$size.html"
    $body = Get-Content $svg -Raw
    $doc = "<!doctype html><html><head><style>html,body{margin:0;background:transparent;overflow:hidden}svg{display:block;width:${size}px;height:${size}px}</style></head><body>$body</body></html>"
    [IO.File]::WriteAllText($html, $doc)
    # Edge reports progress on stderr; don't let Windows PowerShell treat that as a failure.
    $edgeArgs = @('--headless=new', '--disable-gpu', '--hide-scrollbars', "--user-data-dir=$profileDir",
        '--default-background-color=00000000', '--force-device-scale-factor=1', "--window-size=$size,$size",
        "--screenshot=$png", "file:///$($html -replace '\\','/')")
    Start-Process -FilePath $edge -ArgumentList $edgeArgs -NoNewWindow -Wait -RedirectStandardError (Join-Path $env:TEMP 'aghouse-edge.err')
    Remove-Item $html -ErrorAction SilentlyContinue
    if (-not (Test-Path $png)) { throw "Failed to render $svg at $size" }
}

# Small sizes use the simplified mark; larger ones the full logo.
$frames = @()
foreach ($size in 16, 20, 24, 32, 40, 48, 64, 128, 256) {
    $svg = if ($size -le 32) { Join-Path $assets 'logo-small.svg' } else { Join-Path $assets 'logo.svg' }
    $png = Join-Path $out "aghouse-$size.png"
    Render $svg $size $png
    $frames += [pscustomobject]@{ Size = $size; Bytes = [IO.File]::ReadAllBytes($png) }
}

# ICO with PNG-compressed frames (supported since Windows Vista).
$ms = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$f.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Flush()
[IO.File]::WriteAllBytes((Resolve-Path (Split-Path $icoPath)).Path + '\aghouse.ico', $ms.ToArray())
"Wrote $icoPath ($($frames.Count) sizes) and PNGs in $out"
