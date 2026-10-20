param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$BrandRoot = Join-Path $ProjectRoot 'assets\brand'
New-Item -ItemType Directory -Force -Path $BrandRoot | Out-Null

function New-DeskMcpBitmap([int]$Size, [string]$Path) {
    # 2026-10-20 Smallway Desktop MCP original S-and-connection visual; legacy asset file names retained.
    $bmp = New-Object Drawing.Bitmap $Size, $Size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([Drawing.Color]::Transparent)
    $k = $Size / 256.0
    $dark = [Drawing.Color]::FromArgb(255, 22, 26, 58)
    $indigo = [Drawing.Color]::FromArgb(255, 88, 74, 218)
    $teal = [Drawing.Color]::FromArgb(255, 43, 214, 190)
    $rect = [Drawing.RectangleF]::new(12*$k, 12*$k, 232*$k, 232*$k)
    $gradient = New-Object Drawing.Drawing2D.LinearGradientBrush $rect, $dark, $indigo, 40
    $g.FillEllipse($gradient, $rect)
    $font = New-Object Drawing.Font('Segoe UI', (155*$k), [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $textBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
    $fmt = New-Object Drawing.StringFormat
    $fmt.Alignment = [Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [Drawing.StringAlignment]::Center
    $g.DrawString('S', $font, $textBrush, [Drawing.RectangleF]::new(24*$k, 5*$k, 208*$k, 214*$k), $fmt)
    $connection = New-Object Drawing.Pen $teal, (12*$k)
    $connection.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $connection.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($connection, 55*$k, 208*$k, 201*$k, 208*$k)
    $bmp.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    $connection.Dispose(); $textBrush.Dispose(); $font.Dispose(); $fmt.Dispose(); $gradient.Dispose()
    $g.Dispose(); $bmp.Dispose()
}

$sizes = @(16,32,48,64,128,256)
$pngs = @()
foreach ($size in $sizes) {
    $p = Join-Path $BrandRoot ("deskmcp-mark-$size.png")
    New-DeskMcpBitmap $size $p
    $pngs += $p
}

$icoPath = Join-Path $BrandRoot 'DeskMCP.ico'
$fs = [IO.File]::Open($icoPath,[IO.FileMode]::Create)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$pngs.Count)
$offset = 6 + (16 * $pngs.Count)
$entries = @()
foreach ($p in $pngs) {
    $bytes = [IO.File]::ReadAllBytes($p)
    $size = [int]([IO.Path]::GetFileNameWithoutExtension($p).Split('-')[-1])
    $dim = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$bytes.Length); $bw.Write([UInt32]$offset)
    $entries += ,$bytes; $offset += $bytes.Length
}
foreach ($bytes in $entries) { $bw.Write($bytes) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Copy-Item -LiteralPath (Join-Path $BrandRoot 'deskmcp-mark-256.png') -Destination (Join-Path $ProjectRoot 'docs\images\deskmcp-mark.png') -Force
Write-Output 'BRAND_ASSETS_OK'
Write-Output ('ICON=' + $icoPath)
Write-Output ('PNG256=' + (Join-Path $BrandRoot 'deskmcp-mark-256.png'))