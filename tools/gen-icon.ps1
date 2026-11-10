# Buat icon.ico (logo petir AllSense) — multi-ukuran, PNG di dalam ICO.
param([string]$Out = "icon.ico")

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $f = $size / 32.0
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # latar persegi bulat gelap
    $r = [int](8 * $f)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 16, 18, 26))
    $g.FillPath($bg, $path)

    # petir aksen (koordinat sama dgn tray, diskalakan)
    $pts = @(
        (New-Object System.Drawing.Point([int](18*$f), [int](3*$f))),
        (New-Object System.Drawing.Point([int](8*$f), [int](18*$f))),
        (New-Object System.Drawing.Point([int](15*$f), [int](18*$f))),
        (New-Object System.Drawing.Point([int](14*$f), [int](29*$f))),
        (New-Object System.Drawing.Point([int](24*$f), [int](14*$f))),
        (New-Object System.Drawing.Point([int](17*$f), [int](14*$f)))
    )
    $bolt = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 67, 86))
    $g.FillPolygon($bolt, $pts)

    $g.Dispose()
    return $bmp
}

# Render semua ukuran jadi PNG in-memory
$sizes = @(16, 24, 32, 48, 64, 256)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,$ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
}

# Tulis wadah ICO
$fs = [System.IO.File]::Create((Join-Path (Get-Location) $Out))
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]0)   # warna
    $bw.Write([byte]0)   # reserved
    $bw.Write([uint16]1) # planes
    $bw.Write([uint16]32) # bitcount
    $bw.Write([uint32]$pngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close(); $fs.Close()

Write-Host "OK: $Out ($((Get-Item (Join-Path (Get-Location) $Out)).Length) bytes)"
