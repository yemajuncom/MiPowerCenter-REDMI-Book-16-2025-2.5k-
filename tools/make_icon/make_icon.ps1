$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# 与托盘图标同款：橙色(#FF6900)圆角方块 + 白色闪电
function New-IconFrame([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $scale = $w / 32.0
    $b = [int][Math]::Round(1.5 * $scale)
    $rad = [int][Math]::Round(6.0 * $scale)
    $x0 = $b; $y0 = $b; $x1 = $w - 1 - $b; $y1 = $h - 1 - $b
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Max(1, $rad)
    $path.AddArc($x0, $y0, $d, $d, 180, 90)
    $path.AddArc($x1 - $d, $y0, $d, $d, 270, 90)
    $path.AddArc($x1 - $d, $y1 - $d, $d, $d, 0, 90)
    $path.AddArc($x0, $y1 - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 105, 0))
    $g.FillPath($fill, $path)
    $fill.Dispose(); $path.Dispose()
    # 闪电：源 32x32 坐标
    $src = @(@(18, 6), @(11, 18), @(15, 18), @(13, 26), @(21, 13), @(17, 13))
    $pts = New-Object System.Collections.ArrayList
    foreach ($p in $src) {
        $x = [double]($p[0] - 2) * $scale
        $y = [double]($p[1] - 2) * $scale
        [void]$pts.Add((New-Object System.Drawing.PointF -ArgumentList $x, $y))
    }
    $bolt = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPolygon($bolt, [System.Drawing.PointF[]]@($pts.ToArray()))
    $bolt.Dispose(); $g.Dispose()
    return $bmp
}

# 用 GDI 自己编码单帧图标数据（保证结构合法），再取 data 块
function New-FrameData($bmp) {
    $hicon = $bmp.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($hicon)
    $ms = New-Object System.IO.MemoryStream
    $icon.Save($ms)
    $icon.Dispose()
    $bytes = $ms.ToArray()
    $ms.Dispose()
    # 单帧 ico：6 头 + 16 项 → dataOffset = BE? 小端 uint32 at offset 12
    $dataSize = [BitConverter]::ToUInt32($bytes, 8)
    $dataOff = [BitConverter]::ToUInt32($bytes, 12)
    $data = New-Object byte[] $dataSize
    [System.Array]::Copy($bytes, $dataOff, $data, 0, $dataSize)
    return ,$data
}

$sizes = @(16, 24, 32, 48, 64, 256)
$entries = @()   # (bytes, size)
foreach ($s in $sizes) {
    $bmp = New-IconFrame $s $s
    $d = New-FrameData $bmp
    $bmp.Dispose()
    $entries += ,@($d, $s)
}

$out = New-Object System.Collections.Generic.List[byte]
$count = $entries.Count
$out.Add([byte]0); $out.Add([byte]0)
$out.Add([byte]1); $out.Add([byte]0)
$out.Add([byte]$count); $out.Add([byte]0)
$offset = 6 + 16 * $count
foreach ($e in $entries) {
    $bw = 0
    if ($e[1] -lt 256) { $bw = $e[1] }
    $out.Add([byte]$bw); $out.Add([byte]$bw)
    $out.Add([byte]0); $out.Add([byte]0)
    foreach ($bt in [BitConverter]::GetBytes([uint16]1)) { $out.Add($bt) }
    foreach ($bt in [BitConverter]::GetBytes([uint16]32)) { $out.Add($bt) }
    foreach ($bt in [BitConverter]::GetBytes([uint32]$e[0].Length)) { $out.Add($bt) }
    foreach ($bt in [BitConverter]::GetBytes([uint32]$offset)) { $out.Add($bt) }
    $offset += $e[0].Length
}
foreach ($e in $entries) {
    foreach ($bt in $e[0]) { $out.Add($bt) }
}

$buf = $out.ToArray()
foreach ($target in $args) {
    [System.IO.File]::WriteAllBytes($target, $buf)
    Write-Host "wrote $target ($($buf.Length) bytes)"
}