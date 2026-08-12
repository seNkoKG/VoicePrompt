Add-Type -AssemblyName System.Drawing

$dir = Join-Path (Split-Path -Parent $PSScriptRoot) "assets"
$png = Join-Path $dir "logo.png"
$ico = Join-Path $dir "voiceprompt.ico"

# Build the master mark from geometry so the taskbar, title bar, tray, app, and
# website all share the same crisp VoicePrompt waveform at every icon size.
$master = [System.Drawing.Bitmap]::new(1024, 1024)
$canvas = [System.Drawing.Graphics]::FromImage($master)
try {
    $canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $canvas.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $canvas.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $canvas.Clear([System.Drawing.Color]::Transparent)

    $tile = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $tile.AddArc(32, 32, 288, 288, 180, 90)
    $tile.AddArc(704, 32, 288, 288, 270, 90)
    $tile.AddArc(704, 704, 288, 288, 0, 90)
    $tile.AddArc(32, 704, 288, 288, 90, 90)
    $tile.CloseFigure()
    $surface = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.Rectangle]::new(32, 32, 960, 960),
        [System.Drawing.Color]::FromArgb(43, 48, 53),
        [System.Drawing.Color]::FromArgb(13, 16, 18),
        55
    )
    $canvas.FillPath($surface, $tile)
    $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(70, 79, 86), 24)
    $canvas.DrawPath($outline, $tile)

    [System.Drawing.PointF[]]$points = @(
        [System.Drawing.PointF]::new(176, 520),
        [System.Drawing.PointF]::new(280, 520),
        [System.Drawing.PointF]::new(326, 388),
        [System.Drawing.PointF]::new(392, 680),
        [System.Drawing.PointF]::new(452, 220),
        [System.Drawing.PointF]::new(520, 804),
        [System.Drawing.PointF]::new(590, 392),
        [System.Drawing.PointF]::new(652, 674),
        [System.Drawing.PointF]::new(702, 520),
        [System.Drawing.PointF]::new(848, 520)
    )
    $signalBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.Point]::new(176, 0),
        [System.Drawing.Point]::new(848, 0),
        [System.Drawing.Color]::FromArgb(245, 247, 244),
        [System.Drawing.Color]::FromArgb(245, 247, 244)
    )
    $blend = [System.Drawing.Drawing2D.ColorBlend]::new()
    $blend.Colors = [System.Drawing.Color[]]@(
        [System.Drawing.Color]::FromArgb(245, 247, 244),
        [System.Drawing.Color]::FromArgb(205, 224, 211),
        [System.Drawing.Color]::FromArgb(245, 247, 244)
    )
    $blend.Positions = [single[]]@(0, 0.5, 1)
    $signalBrush.InterpolationColors = $blend
    $signal = [System.Drawing.Pen]::new($signalBrush, 52)
    $signal.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $signal.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $signal.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $canvas.DrawLines($signal, $points)
    $master.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    if ($signal) { $signal.Dispose() }
    if ($signalBrush) { $signalBrush.Dispose() }
    if ($outline) { $outline.Dispose() }
    if ($surface) { $surface.Dispose() }
    if ($tile) { $tile.Dispose() }
    $canvas.Dispose()
    $master.Dispose()
}

$source = [System.Drawing.Image]::FromFile($png)
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($source, 0, 0, $size, $size)
        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $images.Add($stream.ToArray())
        $stream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
} finally {
    $source.Dispose()
}

$file = [System.IO.FileStream]::new($ico, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([Byte]$dimension)
        $writer.Write([Byte]$dimension)
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$images[$i].Length)
        $writer.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image) }
} finally {
    $writer.Dispose()
    $file.Dispose()
}
Write-Output "saved $ico ($($sizes.Count) sizes)"

$shell = New-Object -ComObject WScript.Shell
$startup = [Environment]::GetFolderPath("Startup")
$desktop = [Environment]::GetFolderPath("Desktop")
@($desktop, $startup) | Select-Object -Unique | ForEach-Object {
    Get-ChildItem -LiteralPath $_ -Filter "*.lnk" | ForEach-Object {
        $shortcut = $shell.CreateShortcut($_.FullName)
        $isVoiceTyping = $_.BaseName -match "(?i)voice typing" -or
            $shortcut.TargetPath -like "*VoicePromptTray.exe" -or
            $shortcut.Arguments -like "*run_daemon.pyw*"
        if ($isVoiceTyping) {
            $shortcut.IconLocation = "$ico,0"
            $shortcut.Save()
            Write-Output "icon set on $($_.Name)"
        }
    }
}

$refresh = Join-Path $env:SystemRoot "System32\ie4uinit.exe"
if (Test-Path -LiteralPath $refresh) { & $refresh -show }
