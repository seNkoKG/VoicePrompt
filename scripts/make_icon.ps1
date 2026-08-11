Add-Type -AssemblyName System.Drawing

$dir = Join-Path (Split-Path -Parent $PSScriptRoot) "assets"
$png = Join-Path $dir "logo.png"
$ico = Join-Path $dir "voiceprompt.ico"
if (-not (Test-Path -LiteralPath $png)) { throw "Logo source not found: $png" }

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
