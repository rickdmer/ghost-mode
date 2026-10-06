# Renders the Ghost Mode icon (ghost on a blurple tile with an "invisible" status ring) to app.ico + app.png
Add-Type -AssemblyName System.Drawing
$out = $PSScriptRoot

function Draw-Icon([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $s = $size / 256.0
  $g.ScaleTransform($s, $s)

  # Rounded tile
  $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
  $r = 60; $tile.AddArc(0, 0, $r, $r, 180, 90); $tile.AddArc(256 - $r, 0, $r, $r, 270, 90)
  $tile.AddArc(256 - $r, 256 - $r, $r, $r, 0, 90); $tile.AddArc(0, 256 - $r, $r, $r, 90, 90); $tile.CloseFigure()
  $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 256, 256), ([System.Drawing.Color]::FromArgb(0x6A, 0x76, 0xFF)), ([System.Drawing.Color]::FromArgb(0x42, 0x4C, 0xC0))
  $g.FillPath($bg, $tile)

  # Ghost body: dome, straight sides, three scallops along the bottom
  $ghost = New-Object System.Drawing.Drawing2D.GraphicsPath
  $ghost.AddArc(58, 40, 132, 132, 180, 180)
  $ghost.AddLine(190, 106, 190, 196)
  $w = 132 / 3.0
  for ($i = 2; $i -ge 0; $i--) { $ghost.AddArc([float](58 + $i * $w), 178, [float]$w, 36, 0, 180) }
  $ghost.CloseFigure()
  $g.FillPath([System.Drawing.Brushes]::White, $ghost)

  # Eyes
  $eye = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x23, 0x27, 0x5E))
  $g.FillEllipse($eye, 88, 92, 24, 34); $g.FillEllipse($eye, 136, 92, 24, 34)

  # Status badge: cut-out + grey ring (Discord's "invisible" glyph)
  $cut = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x4A, 0x55, 0xCF))
  $g.FillEllipse($cut, 158, 158, 84, 84)
  $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x9A, 0x9E, 0xA8))), 170, 170, 60, 60)
  $g.FillEllipse($cut, 186, 186, 28, 28)

  $g.Dispose(); $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) { $b = Draw-Icon $sz; $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); if ($sz -eq 256) { $b.Save("$out\app.png") }; $b.Dispose(); , $ms.ToArray() }

# ICO container with PNG-compressed entries
$fs = [System.IO.File]::Create("$out\app.ico"); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
  $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
"wrote $out\app.ico"
