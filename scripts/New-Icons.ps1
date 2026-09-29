$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot '../src/Assets'
New-Item -ItemType Directory -Force $destination | Out-Null
foreach ($disabled in @($false, $true)) {
    $images = @()
    foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = 'AntiAlias'
        $color = if ($disabled) { [System.Drawing.Color]::FromArgb(255,148,163,184) } else { [System.Drawing.Color]::FromArgb(255,59,130,246) }
        $pen = [System.Drawing.Pen]::new($color, [single]($size * 0.11))
        $graphics.DrawEllipse($pen, [single]($size * .14), [single]($size * .14), [single]($size * .72), [single]($size * .72))
        $brush = [System.Drawing.SolidBrush]::new($color)
        $graphics.FillEllipse($brush, [single]($size * .38), [single]($size * .38), [single]($size * .24), [single]($size * .24))
        if ($disabled) { $graphics.DrawLine($pen, [single]($size * .2), [single]($size * .8), [single]($size * .8), [single]($size * .2)) }
        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $images += ,($stream.ToArray())
        $stream.Dispose(); $pen.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
    $name = if ($disabled) { 'ClickShow-disabled.ico' } else { 'ClickShow.ico' }
    $file = [System.IO.File]::Create((Join-Path $destination $name))
    $writer = [System.IO.BinaryWriter]::new($file)
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    $sizes = @(16,20,24,32,40,48,64,128,256)
    for ($i = 0; $i -lt $images.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([uint16]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
    $writer.Dispose()
}
