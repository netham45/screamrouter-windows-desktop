# ------------------------------------------------------------------------
# 1. Load minimal .NET assemblies (NO WPF)
# ------------------------------------------------------------------------
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Net.Http

# Input and output paths
$svgPath    = "Properties\Resources\logo.svg"
$outputPath = "Properties\Resources\app.ico"

# WebView2 assemblies
$webView2DllPath     = "packages\Microsoft.Web.WebView2.1.0.3124.44\lib\net462\Microsoft.Web.WebView2.WinForms.dll"
$webView2CoreDllPath = "packages\Microsoft.Web.WebView2.1.0.3124.44\lib\net462\Microsoft.Web.WebView2.Core.dll"

Add-Type -Path $webView2DllPath
Add-Type -Path $webView2CoreDllPath

# Ensure output directory exists
$outputDir = [System.IO.Path]::GetDirectoryName($outputPath)
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# ------------------------------------------------------------------------
# 2. Create a 256x256 form with WebView2 to render the SVG
# ------------------------------------------------------------------------
$form = New-Object System.Windows.Forms.Form
$form.Width = 256
$form.Height = 256
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$form.ShowInTaskbar = $false

$webView = New-Object Microsoft.Web.WebView2.WinForms.WebView2
$webView.Dock = [System.Windows.Forms.DockStyle]::Fill
$form.Controls.Add($webView)

# Copy WebView2Loader.dll locally
Copy-Item -Path "packages\Microsoft.Web.WebView2.1.0.3124.44\runtimes\win-x64\native\WebView2Loader.dll" `
           -Destination "WebView2Loader.dll" -Force

$form.Show()
$env:PATH = "$env:PATH;$PWD\packages\Microsoft.Web.WebView2.1.0.3124.44\runtimes\win-x64\native"

$userDataFolder = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(),"WebView2UserData")
if (-not (Test-Path $userDataFolder)) {
    New-Item -ItemType Directory -Path $userDataFolder -Force | Out-Null
}

$initCompleted = $false
$webView.add_CoreWebView2InitializationCompleted({ $script:initCompleted = $true })

$envTask = [Microsoft.Web.WebView2.Core.CoreWebView2Environment]::CreateAsync($null, $userDataFolder)
$envTask.Wait()
$environment = $envTask.Result
$initTask = $webView.EnsureCoreWebView2Async($environment)

while (-not $initCompleted) { [System.Windows.Forms.Application]::DoEvents() }

if ($webView.CoreWebView2 -eq $null) {
    throw "WebView2 failed to initialize."
}

# ------------------------------------------------------------------------
# 3. Navigate to the SVG and capture as PNG
# ------------------------------------------------------------------------
$absoluteSvgPath = [System.IO.Path]::GetFullPath($svgPath)
Write-Host "Navigating to: file:///$absoluteSvgPath"
$webView.CoreWebView2.Navigate("file:///$absoluteSvgPath")

$navDone = $false
$webView.add_NavigationCompleted({ 
    $script:navDone = $true
    Write-Host "Navigation Completed."
})

while (-not $navDone) { [System.Windows.Forms.Application]::DoEvents() }
Start-Sleep -Seconds 1  # give it time to render

Write-Host "Capturing preview..."
$ms = New-Object System.IO.MemoryStream
$captureTask = $webView.CoreWebView2.CapturePreviewAsync(0, $ms)

$timeout = [DateTime]::Now.AddSeconds(5)
while (-not $captureTask.IsCompleted -and [DateTime]::Now -lt $timeout) {
    [System.Windows.Forms.Application]::DoEvents()
}
if (-not $captureTask.IsCompleted) { throw "Capture timed out." }
if ($captureTask.IsFaulted)       { throw $captureTask.Exception }

# Save that PNG so we can reload in GDI+
$ms.Position = 0
[System.IO.File]::WriteAllBytes("test.png", $ms.ToArray())
Write-Host "Preview captured as test.png"

$form.Close()

# ------------------------------------------------------------------------
# 4. Load PNG as a 32bpp Bitmap, force white -> transparent
# ------------------------------------------------------------------------
$bitmap = [System.Drawing.Image]::FromFile("test.png")
Write-Host "Captured PixelFormat: $($bitmap.PixelFormat)"

# Make a 32bpp ARGB version
$bmp32 = New-Object System.Drawing.Bitmap($bitmap.Width, $bitmap.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp32)
$g.DrawImage($bitmap, 0, 0)
$g.Dispose()
$bitmap.Dispose()

# **Here's the important bit**: remove pure white by making it transparent
$bmp32.MakeTransparent([System.Drawing.Color]::White)

# ------------------------------------------------------------------------
# 5. Build an uncompressed 32-bit icon from $bmp32
# ------------------------------------------------------------------------
Function Convert-BitmapToIconBytes($bmp) {
    # We only handle 256x256 in this example
    $width  = $bmp.Width
    $height = $bmp.Height
    if ($width -ne 256 -or $height -ne 256) {
        throw "This script expects a 256x256 bitmap. Got ${width}x${height}."
    }

    $iconData = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($iconData)

    # ICONDIR (6 bytes)
    $bw.Write([byte]0) # reserved
    $bw.Write([byte]0)
    $bw.Write([byte]1) # type=1 (icon)
    $bw.Write([byte]0)
    $bw.Write([byte]1) # count=1
    $bw.Write([byte]0)

    # ICONDIRENTRY (16 bytes)
    #  0: width=0 => 256
    #  1: height=0 => 256
    #  2: colorcount=0
    #  3: reserved=0
    #  4-5: planes=1
    #  6-7: bitcount=32
    #  8-11: size of bitmap bits (including BITMAPINFOHEADER + pixels + mask)
    #  12-15: offset to those bits = 22
    $bw.Write([byte]0) # width=256
    $bw.Write([byte]0) # height=256
    $bw.Write([byte]0) # color count
    $bw.Write([byte]0) # reserved

    # planes=1
    $bw.Write([byte[]][BitConverter]::GetBytes([int16]1))
    # bitcount=32
    $bw.Write([byte[]][BitConverter]::GetBytes([int16]32))

    # We'll build the DIB in a separate stream
    $dibStream = New-Object System.IO.MemoryStream
    $dibWriter = New-Object System.IO.BinaryWriter($dibStream)

    # BITMAPINFOHEADER (40 bytes)
    #   biSize=40
    #   biWidth=256
    #   biHeight=256*2 (color + mask)
    #   biPlanes=1
    #   biBitCount=32
    #   biCompression=0 (BI_RGB)
    #   rest=0
    $dibWriter.Write([System.BitConverter]::GetBytes([int]40))
    $dibWriter.Write([System.BitConverter]::GetBytes([int]256))
    $dibWriter.Write([System.BitConverter]::GetBytes([int](256*2)))
    $dibWriter.Write([System.BitConverter]::GetBytes([int16]1))
    $dibWriter.Write([System.BitConverter]::GetBytes([int16]32))
    $dibWriter.Write([System.BitConverter]::GetBytes([int]0)) 
    $dibWriter.Write([System.BitConverter]::GetBytes([int]0))
    $dibWriter.Write([System.BitConverter]::GetBytes([int]0))
    $dibWriter.Write([System.BitConverter]::GetBytes([int]0))
    $dibWriter.Write([System.BitConverter]::GetBytes([int]0))
    $dibWriter.Write([System.BitConverter]::GetBytes([int]0))

    # Lock bits so we can read them row by row, bottom-up
    $rect = New-Object System.Drawing.Rectangle(0,0,256,256)
    $bmpData = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = [math]::Abs($bmpData.Stride)
    [byte[]] $rowBuf = New-Object byte[]($stride)

    for ($row = 0; $row -lt 256; $row++) {
        $srcOffset = $bmpData.Scan0.ToInt64() + $stride * (255 - $row)
        [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]$srcOffset, $rowBuf, 0, $stride)
        $dibWriter.Write($rowBuf)
    }
    $bmp.UnlockBits($bmpData)

    # The 1-bit AND mask is 256x256 => 8192 bytes
    [byte[]] $maskBytes = New-Object byte[](8192) # all zeros => no masked-out area
    $dibWriter.Write($maskBytes)

    $dibWriter.Flush()
    $dib = $dibStream.ToArray()
    $dibStream.Close()

    # Size of the DIB
    $sizeOfDib = $dib.Length

    # Write dwBytesInRes
    $bw.Write([byte[]][BitConverter]::GetBytes([int]$sizeOfDib))
    # dwImageOffset = 22
    $bw.Write([byte[]][BitConverter]::GetBytes([int]22))

    # Now append the DIB
    $bw.Write($dib)
    $bw.Flush()

    return $iconData.ToArray()
}

Write-Host "Converting to uncompressed 32-bit .ico (white => transparent)..."
$icoBytes = Convert-BitmapToIconBytes $bmp32
[System.IO.File]::WriteAllBytes($outputPath, $icoBytes)

Write-Host "Saved icon to: $outputPath"

# Cleanup
$bmp32.Dispose()
$ms.Dispose()

Write-Host "All done. White pixels are now transparent in your 32-bit icon!"
