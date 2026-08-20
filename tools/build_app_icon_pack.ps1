<#!
.SYNOPSIS
Builds the multi-resolution Windows application and tray icon files.

.DESCRIPTION
Each ICO frame is a PNG generated directly from its canonical artwork using
high-quality bicubic resampling. This avoids Windows having to scale a single
256px frame for taskbar, title-bar, Start menu, and high-DPI shell surfaces.
#>

[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$assetsDirectory = Join-Path $RepositoryRoot 'src\ZTMZ.PacenoteTool.WpfGUI\Assets'

function Get-SourceBitmap {
    param([Parameter(Mandatory)][string]$Path)

    if ([IO.Path]::GetExtension($Path) -ieq '.ico') {
        $icon = [Drawing.Icon]::new($Path)
        try {
            return $icon.ToBitmap()
        }
        finally {
            $icon.Dispose()
        }
    }

    return [Drawing.Bitmap]::FromFile($Path)
}

function Resize-ToPng {
    param(
        [Parameter(Mandatory)][Drawing.Image]$Source,
        [Parameter(Mandatory)][int]$Size
    )

    $target = [Drawing.Bitmap]::new(
        $Size,
        $Size,
        [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($target)
    $attributes = [Drawing.Imaging.ImageAttributes]::new()
    try {
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $graphics.DrawImage(
            $Source,
            [Drawing.Rectangle]::new(0, 0, $Size, $Size),
            0,
            0,
            $Source.Width,
            $Source.Height,
            [Drawing.GraphicsUnit]::Pixel,
            $attributes)

        $stream = [IO.MemoryStream]::new()
        try {
            $target.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            # Keep the byte array as one pipeline object. PowerShell otherwise
            # enumerates it and BinaryWriter receives an object array instead.
            Write-Output -NoEnumerate $stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $attributes.Dispose()
        $graphics.Dispose()
        $target.Dispose()
    }
}

function Write-MultiResolutionIco {
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $source = Get-SourceBitmap $SourcePath
    try {
        $frames = foreach ($size in $sizes) {
            [PSCustomObject]@{
                Size = $size
                Data = Resize-ToPng -Source $source -Size $size
            }
        }

        $stream = [IO.File]::Open($DestinationPath, [IO.FileMode]::Create, [IO.FileAccess]::Write)
        $writer = [IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([UInt16]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]$frames.Count)

            $offset = 6 + (16 * $frames.Count)
            foreach ($frame in $frames) {
                $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
                $writer.Write([byte]$dimension)
                $writer.Write([byte]$dimension)
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([UInt16]1)
                $writer.Write([UInt16]32)
                $writer.Write([UInt32]$frame.Data.Length)
                $writer.Write([UInt32]$offset)
                $offset += $frame.Data.Length
            }

            foreach ($frame in $frames) {
                $writer.Write([byte[]]$frame.Data)
            }
        }
        finally {
            $writer.Dispose()
            $stream.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }

    $frameList = $sizes -join ', '
    Write-Output "Generated $DestinationPath ($frameList px)"
}

Write-MultiResolutionIco `
    -SourcePath (Join-Path $assetsDirectory 'app.png') `
    -DestinationPath (Join-Path $assetsDirectory 'app.ico')
Write-MultiResolutionIco `
    -SourcePath (Join-Path $assetsDirectory 'app_tray.png') `
    -DestinationPath (Join-Path $assetsDirectory 'app_tray.ico')
