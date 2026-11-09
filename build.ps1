# 生成多尺寸图标并编译 喝水护眼提醒.exe
# 用法：
#   powershell -ExecutionPolicy Bypass -File .\build.ps1
#   powershell -ExecutionPolicy Bypass -File .\build.ps1 -OutDir "D:\somewhere"
#
# 依赖：Windows 自带的 .NET Framework 4.x（csc.exe），不需要装 Visual Studio。

param([string]$OutDir = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

Add-Type -AssemblyName System.Drawing

# ---------- 画图 ----------
function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [single]($size * 0.03)
    $d = [single]($size - 2 * $pad)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF($pad, $pad)),
        (New-Object System.Drawing.PointF(($pad + $d), ($pad + $d))),
        [System.Drawing.Color]::FromArgb(0x3D, 0x9B, 0xF5),
        [System.Drawing.Color]::FromArgb(0x1B, 0x6F, 0xD6))
    $g.FillEllipse($brush, $pad, $pad, $d, $d)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.StartFigure()
    $path.AddBezier((New-Object System.Drawing.PointF(0, -1)), (New-Object System.Drawing.PointF(0.30, -0.52)), (New-Object System.Drawing.PointF(0.62, -0.06)), (New-Object System.Drawing.PointF(0.62, 0.28)))
    $path.AddArc((New-Object System.Drawing.RectangleF(-0.62, -0.34, 1.24, 1.24)), 0, 180)
    $path.AddBezier((New-Object System.Drawing.PointF(-0.62, 0.28)), (New-Object System.Drawing.PointF(-0.62, -0.06)), (New-Object System.Drawing.PointF(-0.30, -0.52)), (New-Object System.Drawing.PointF(0, -1)))
    $path.CloseFigure()

    $k = [single]($size * 0.30)
    $g.TranslateTransform([single]($size / 2.0), [single]($size * 0.52))
    $g.ScaleTransform($k, $k)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $path)
    $g.ResetTransform()

    $g.Dispose(); $path.Dispose(); $white.Dispose(); $brush.Dispose()
    return $bmp
}

# 32bpp DIB 单帧（兼容性最好，Windows 各版本都认）
function New-IconFrame([int]$size) {
    $bmp = New-IconBitmap $size
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $buf = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
    $bmp.UnlockBits($data)
    $bmp.Dispose()

    $maskRow = [int]([math]::Floor(($size + 31) / 32) * 4)
    $maskLen = $maskRow * $size

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([UInt32]40)
    $bw.Write([Int32]$size)
    $bw.Write([Int32]($size * 2))
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]($size * $size * 4 + $maskLen))
    $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for ($y = $size - 1; $y -ge 0; $y--) { $bw.Write($buf, $y * $stride, $size * 4) }
    $bw.Write((New-Object byte[] $maskLen))
    $bw.Flush()
    $out = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return ,$out
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @{}
foreach ($s in $sizes) { $frames[$s] = New-IconFrame $s }

$icoPath = Join-Path $root 'app.ico'
$fs = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $data = $frames[$s]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$dim); $bw.Write([Byte]$dim)
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($frames[$s]) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

$check = New-Object System.Drawing.Icon($icoPath, 32, 32)
$chk = $check.ToBitmap()
if ($chk.Width -ne 32) { throw '图标校验失败' }
$chk.Dispose(); $check.Dispose()
Write-Host ("图标已生成: app.ico（{0} 种尺寸）" -f $sizes.Count)

# ---------- 编译 ----------
# 源码必须是带 BOM 的 UTF-8，否则 csc 会按 ANSI 读，中文字符串变乱码
$src = Join-Path $root 'ReminderApp.cs'
$bytes = [IO.File]::ReadAllBytes($src)
if (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) {
    $text = [IO.File]::ReadAllText($src, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($src, $text, [Text.UTF8Encoding]::new($true))
    Write-Host '源码已补充 UTF-8 BOM'
}

if (-not $OutDir) { $OutDir = $root }
elseif (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

$out = Join-Path $OutDir '喝水护眼提醒.exe'
$cscArgs = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    ('/out:' + $out),
    ('/win32icon:' + $icoPath),
    $src
)
& $csc $cscArgs 2>&1 | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { throw '编译失败（csc 返回非 0）' }

if (Test-Path $out) {
    if ($OutDir -ne $root) {
        Copy-Item (Join-Path $root 'app.ico') -Destination $OutDir -Force -ErrorAction SilentlyContinue
        Copy-Item (Join-Path $root 'ReminderApp.ini') -Destination $OutDir -Force -ErrorAction SilentlyContinue
    }
    $f = Get-Item $out
    Write-Host ("编译成功: {0}  ({1:N0} KB)" -f $f.FullName, ($f.Length / 1KB))
} else {
    throw '编译失败'
}
