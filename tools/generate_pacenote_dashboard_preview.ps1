param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Add-Type -AssemblyName System.Drawing

$iconRoot = Join-Path $RepositoryRoot "src\ZTMZ.PacenoteTool.WebDashboard\pacenote-icons\v1\icons"
$outputPath = Join-Path $RepositoryRoot "src\ZTMZ.PacenoteTool.WebDashboard\dashboards\pacenote_roadbook\preview.png"

function New-RoundedRectanglePath {
    param([System.Drawing.RectangleF]$Rectangle, [float]$Radius)

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $arc = [System.Drawing.RectangleF]::new($Rectangle.X, $Rectangle.Y, $diameter, $diameter)
    $path.AddArc($arc, 180, 90)
    $arc.X = $Rectangle.Right - $diameter
    $path.AddArc($arc, 270, 90)
    $arc.Y = $Rectangle.Bottom - $diameter
    $path.AddArc($arc, 0, 90)
    $arc.X = $Rectangle.X
    $path.AddArc($arc, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-ImageWithOpacity {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Image]$Image,
        [System.Drawing.RectangleF]$Destination,
        [float]$Opacity
    )

    $matrix = [System.Drawing.Imaging.ColorMatrix]::new()
    $matrix.Matrix33 = $Opacity
    $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
    $attributes.SetColorMatrix($matrix)
    $Graphics.DrawImage(
        $Image,
        [System.Drawing.Rectangle]::Round($Destination),
        0,
        0,
        $Image.Width,
        $Image.Height,
        [System.Drawing.GraphicsUnit]::Pixel,
        $attributes)
    $attributes.Dispose()
}

function Draw-FadedImage {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Image]$Image,
        [System.Drawing.RectangleF]$Destination
    )

    $slices = 12
    for ($slice = 0; $slice -lt $slices; $slice++) {
        $sourceTop = [int][Math]::Floor($Image.Height * $slice / $slices)
        $sourceBottom = [int][Math]::Ceiling($Image.Height * ($slice + 1) / $slices)
        $destinationTop = $Destination.Top + $Destination.Height * $slice / $slices
        $destinationBottom = $Destination.Top + $Destination.Height * ($slice + 1) / $slices
        $opacity = 0.06 + (0.62 - 0.06) * (($slice + 0.5) / $slices)

        $matrix = [System.Drawing.Imaging.ColorMatrix]::new()
        $matrix.Matrix33 = [float]$opacity
        $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
        $attributes.SetColorMatrix($matrix)
        $Graphics.DrawImage(
            $Image,
            [System.Drawing.Rectangle]::Round([System.Drawing.RectangleF]::new(
                $Destination.Left,
                $destinationTop,
                $Destination.Width,
                $destinationBottom - $destinationTop)),
            0,
            $sourceTop,
            $Image.Width,
            $sourceBottom - $sourceTop,
            [System.Drawing.GraphicsUnit]::Pixel,
            $attributes)
        $attributes.Dispose()
    }
}

function Draw-CenteredLabel {
    param(
        [System.Drawing.Graphics]$Graphics,
        [string]$Text,
        [float]$CenterX,
        [float]$Top,
        [System.Drawing.Brush]$Brush,
        [System.Drawing.Font]$Font
    )

    $size = $Graphics.MeasureString($Text, $Font)
    $Graphics.DrawString($Text, $Font, $Brush, $CenterX - $size.Width / 2, $Top)
}

$bitmap = [System.Drawing.Bitmap]::new(656, 328, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

$backdrop = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(0, 0),
    [System.Drawing.PointF]::new(656, 328),
    [System.Drawing.Color]::FromArgb(255, 25, 31, 35),
    [System.Drawing.Color]::FromArgb(255, 10, 14, 17))
$graphics.FillRectangle($backdrop, 0, 0, 656, 328)
$backdrop.Dispose()

$dashboardRectangle = [System.Drawing.RectangleF]::new(28, 64, 600, 200)
$dashboardPath = New-RoundedRectanglePath -Rectangle $dashboardRectangle -Radius 10
$dashboardBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(170, 8, 13, 16))
$graphics.FillPath($dashboardBrush, $dashboardPath)
$dashboardBrush.Dispose()
$dashboardPath.Dispose()

$font = [System.Drawing.Font]::new("Arial", 13, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$currentBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$historyBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(0, 72),
    [System.Drawing.PointF]::new(0, 156),
    [System.Drawing.Color]::FromArgb(15, 235, 239, 241),
    [System.Drawing.Color]::FromArgb(158, 235, 239, 241))

$historyTokens = @(
    @{ Name = "over_crest"; Label = "Over crest" },
    @{ Name = "4_right"; Label = "4 right" },
    @{ Name = "dont_cut"; Label = "Don't cut" }
)
$currentTokens = @(
    @{ Name = "into"; Label = "Into" },
    @{ Name = "6_left"; Label = "6 left" },
    @{ Name = "dont_cut"; Label = "Don't cut" }
)
$centers = @(238, 328, 418)

for ($index = 0; $index -lt $historyTokens.Count; $index++) {
    $token = $historyTokens[$index]
    $image = [System.Drawing.Image]::FromFile((Join-Path $iconRoot ($token.Name + ".png")))
    try {
        Draw-FadedImage -Graphics $graphics -Image $image -Destination ([System.Drawing.RectangleF]::new($centers[$index] - 28, 72, 56, 56))
    }
    finally {
        $image.Dispose()
    }
    Draw-CenteredLabel -Graphics $graphics -Text $token.Label -CenterX $centers[$index] -Top 133 -Brush $historyBrush -Font $font
}

for ($index = 0; $index -lt $currentTokens.Count; $index++) {
    $token = $currentTokens[$index]
    $image = [System.Drawing.Image]::FromFile((Join-Path $iconRoot ($token.Name + ".png")))
    try {
        Draw-ImageWithOpacity -Graphics $graphics -Image $image -Destination ([System.Drawing.RectangleF]::new($centers[$index] - 35, 164, 70, 70)) -Opacity 1
    }
    finally {
        $image.Dispose()
    }
    Draw-CenteredLabel -Graphics $graphics -Text $token.Label -CenterX $centers[$index] -Top 238 -Brush $currentBrush -Font $font
}

$historyBrush.Dispose()
$currentBrush.Dispose()
$font.Dispose()
$graphics.Dispose()
$bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

Write-Output "Generated $outputPath"
