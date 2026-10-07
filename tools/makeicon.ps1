# REQ-001: トレイアイコン (.ico) を生成する
# PNG エントリを持つ ICO を組み立てる (Vista 以降は ICO 内 PNG を許容)。16px と 32px を1枚に収める。
# 用法: powershell -NoProfile -File tools/makeicon.ps1
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path (Get-Location) 'assets'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-PngBytes {
    param([int]$Size, [string]$Text, [int]$R, [int]$G, [int]$B)

    $back = [System.Drawing.Color]::FromArgb($R, $G, $B)

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gfx.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $gfx.Clear([System.Drawing.Color]::Transparent)

    # 角丸の背景
    $radius = [Math]::Max(2, [int]($Size / 6))
    $inset = 1
    $w = $Size - 2 * $inset
    $h = $Size - 2 * $inset

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($inset, $inset, $radius, $radius, 180, 90)
    $path.AddArc($inset + $w - $radius, $inset, $radius, $radius, 270, 90)
    $path.AddArc($inset + $w - $radius, $inset + $h - $radius, $radius, $radius, 0, 90)
    $path.AddArc($inset, $inset + $h - $radius, $radius, $radius, 90, 90)
    $path.CloseFigure()

    $brush = [System.Drawing.SolidBrush]::new($back)
    $gfx.FillPath($brush, $path)

    $fontSize = 8
    if ($Size -ge 32) { $fontSize = 11 }

    $font = [System.Drawing.Font]::new('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold)

    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center

    $rectF = [System.Drawing.RectangleF]::new(0, 0, $Size, $Size)
    $gfx.DrawString($Text, $font, [System.Drawing.Brushes]::White, $rectF, $format)

    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)

    $gfx.Dispose()
    $bmp.Dispose()
    $brush.Dispose()
    $font.Dispose()

    return ,$ms.ToArray()
}

function New-IcoFile {
    param([string]$Path, [array]$Entries)

    # ICONDIR: reserved(2)=0, type(2)=1, count(2)
    # ICONDIRENTRY: w(1) h(1) palette(1) reserved(1) planes(2) bitcount(2) size(4) offset(4)
    $bytes = [System.Collections.Generic.List[byte]]::new()
    $headerSize = 6 + 16 * $Entries.Count

    $bytes.AddRange([byte[]](0, 0, 1, 0, $Entries.Count, 0))

    $offset = $headerSize
    foreach ($entry in $Entries) {
        $size = [int]$entry.Bytes.Length
        $w = 0
        $h = 0
        if ($entry.Size -lt 256) { $w = [int]$entry.Size; $h = [int]$entry.Size }

        $bytes.AddRange([byte[]]($w, $h, 0, 0, 1, 0, 32, 0))
        $bytes.AddRange([BitConverter]::GetBytes($size))
        $bytes.AddRange([BitConverter]::GetBytes($offset))
        $offset += $size
    }

    foreach ($entry in $Entries) {
        $bytes.AddRange([byte[]]$entry.Bytes)
    }

    [System.IO.File]::WriteAllBytes($Path, $bytes.ToArray())
    Write-Output "written: $Path ($($bytes.Count) bytes)"
}

$idle = @(
    @{ Size = 16; Bytes = (New-PngBytes -Size 16 -Text 's' -R 37 -G 99 -B 235) },
    @{ Size = 32; Bytes = (New-PngBytes -Size 32 -Text 'sCam' -R 37 -G 99 -B 235) }
)

$recording = @(
    @{ Size = 16; Bytes = (New-PngBytes -Size 16 -Text 's' -R 220 -G 38 -B 38) },
    @{ Size = 32; Bytes = (New-PngBytes -Size 32 -Text 'sCam' -R 220 -G 38 -B 38) }
)

New-IcoFile -Path (Join-Path $outDir 'scam.ico') -Entries $idle
New-IcoFile -Path (Join-Path $outDir 'scam_recording.ico') -Entries $recording
