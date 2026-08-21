using System;
using System.Collections.Generic;
using GameOverlay.Drawing;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.Mathematics.Interop;

using Image = GameOverlay.Drawing.Image;

namespace ZTMZ.PacenoteTool.UI.Windows;

/// <summary>
/// Bounds occupied by retained static image artwork in a native dashboard.
/// </summary>
public readonly record struct NativeStaticImageLayerBounds(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;

    public float Height => Bottom - Top;

    public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>
/// Lightweight graphics surface used to find the final bounds of an image-only
/// static layer before allocating its compatible Direct2D render target.
/// </summary>
public sealed class NativeStaticImageBoundsCapture
{
    private bool _hasBounds;
    private float _left;
    private float _top;
    private float _right;
    private float _bottom;

    public NativeStaticImageBoundsCapture(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public float FPS { get; set; }

    public void DrawImage(Image image, float x, float y, float opacity = 1.0f)
    {
        if (image == null)
        {
            return;
        }

        DrawImage(image, x, y, x + image.Width, y + image.Height, opacity);
    }

    public void DrawImage(
        Image image,
        float left,
        float top,
        float right,
        float bottom,
        float opacity = 1.0f,
        bool linearScale = true)
    {
        if (image == null || opacity <= 0)
        {
            return;
        }

        Include(left, top, right, bottom);
    }

    public void Include(float left, float top, float right, float bottom)
    {
        if (!float.IsFinite(left)
            || !float.IsFinite(top)
            || !float.IsFinite(right)
            || !float.IsFinite(bottom))
        {
            return;
        }

        var clippedLeft = Math.Max(0, Math.Min(left, right));
        var clippedTop = Math.Max(0, Math.Min(top, bottom));
        var clippedRight = Math.Min(Width, Math.Max(left, right));
        var clippedBottom = Math.Min(Height, Math.Max(top, bottom));
        if (clippedRight <= clippedLeft || clippedBottom <= clippedTop)
        {
            return;
        }

        if (!_hasBounds)
        {
            _left = clippedLeft;
            _top = clippedTop;
            _right = clippedRight;
            _bottom = clippedBottom;
            _hasBounds = true;
            return;
        }

        _left = Math.Min(_left, clippedLeft);
        _top = Math.Min(_top, clippedTop);
        _right = Math.Max(_right, clippedRight);
        _bottom = Math.Max(_bottom, clippedBottom);
    }

    public bool TryGetBounds(out NativeStaticImageLayerBounds bounds)
    {
        bounds = _hasBounds
            ? new NativeStaticImageLayerBounds(_left, _top, _right, _bottom)
            : default;
        return bounds.IsValid;
    }
}

/// <summary>
/// Retains image-only static dashboard artwork in a compatible Direct2D bitmap.
/// The bitmap is composited by the existing overlay window, so this does not
/// introduce a second native window or change dashboard z-order.
/// </summary>
public sealed class NativeStaticImageLayer : IDisposable
{
    // 64 MiB of BGRA pixels keeps a malformed dashboard from allocating an
    // unbounded offscreen surface while still allowing full 4K artwork.
    private const long MaximumCachedPixelCount = 16_777_216;

    private BitmapRenderTarget _renderTarget;
    private Bitmap _bitmap;
    private NativeStaticImageLayerBounds _bounds;

    public bool IsReady => _renderTarget != null && _bitmap != null && _bounds.IsValid;

    public bool TryBuild(Graphics graphics, Dashboard dashboard, Action<object> renderStaticLayer)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(renderStaticLayer);

        Dispose();

        var boundsCapture = new NativeStaticImageBoundsCapture(graphics.Width, graphics.Height)
        {
            FPS = graphics.FPS
        };
        renderStaticLayer(boundsCapture);
        if (!boundsCapture.TryGetBounds(out var bounds)
            || (long)Math.Ceiling(bounds.Width) * (long)Math.Ceiling(bounds.Height) > MaximumCachedPixelCount)
        {
            return false;
        }

        BitmapRenderTarget target = null;
        NativeStaticImageLayerRenderer renderer = null;
        var drawingStarted = false;
        var built = false;
        try
        {
            target = new BitmapRenderTarget(
                graphics.GetRenderTarget(),
                CompatibleRenderTargetOptions.None,
                new Size2F(bounds.Width, bounds.Height));
            renderer = new NativeStaticImageLayerRenderer(
                target,
                bounds,
                graphics.Width,
                graphics.Height,
                dashboard)
            {
                FPS = graphics.FPS
            };

            target.BeginDraw();
            drawingStarted = true;
            target.Clear(null);
            renderStaticLayer(renderer);
            var endDrawResult = target.TryEndDraw(out _, out _);
            drawingStarted = false;
            if (endDrawResult.Failure || renderer.HasFailure || !renderer.HasDrawnImage)
            {
                return false;
            }

            _bitmap = target.Bitmap;
            _renderTarget = target;
            _bounds = bounds;
            target = null;
            built = true;
            return true;
        }
        finally
        {
            if (drawingStarted)
            {
                target?.TryEndDraw(out _, out _);
            }

            renderer?.Dispose();
            if (!built)
            {
                target?.Dispose();
            }
        }
    }

    public bool TryDraw(Graphics graphics)
    {
        if (!IsReady)
        {
            return false;
        }

        try
        {
            var destination = new RawRectangleF(_bounds.Left, _bounds.Top, _bounds.Right, _bounds.Bottom);
            var source = new RawRectangleF(0, 0, _bitmap.Size.Width, _bitmap.Size.Height);
            graphics.GetRenderTarget().DrawBitmap(
                _bitmap,
                destination,
                1.0f,
                BitmapInterpolationMode.Linear,
                source);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _renderTarget?.Dispose();
        _renderTarget = null;
        _bounds = default;
    }
}

public sealed class NativeStaticImageLayerRenderer : IDisposable
{
    private readonly BitmapRenderTarget _target;
    private readonly NativeStaticImageLayerBounds _bounds;
    private readonly int _overlayWidth;
    private readonly int _overlayHeight;
    private readonly Dictionary<Image, DashboardImageResourceSource> _sources = new();
    private readonly Dictionary<Image, Image> _loadedImages = new();

    public NativeStaticImageLayerRenderer(
        BitmapRenderTarget target,
        NativeStaticImageLayerBounds bounds,
        int overlayWidth,
        int overlayHeight,
        Dashboard dashboard)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _bounds = bounds;
        _overlayWidth = overlayWidth;
        _overlayHeight = overlayHeight;
        if (dashboard.ImageResources == null || dashboard.ImageResourceSources == null)
        {
            HasFailure = true;
            return;
        }

        foreach (var resource in dashboard.ImageResources)
        {
            if (resource.Value is Image image
                && dashboard.ImageResourceSources.TryGetValue(resource.Key, out var source))
            {
                _sources[image] = source;
            }
        }
    }

    // Static Lua layout must observe the same surface dimensions during the
    // capture and compatible-target passes. Coordinates are translated only
    // when they are submitted to the cropped bitmap.
    public int Width => _overlayWidth;

    public int Height => _overlayHeight;

    public float FPS { get; set; }

    public bool HasDrawnImage { get; private set; }

    public bool HasFailure { get; private set; }

    public void DrawImage(Image image, float x, float y, float opacity = 1.0f)
    {
        if (image == null)
        {
            HasFailure = true;
            return;
        }

        DrawImage(image, x, y, x + image.Width, y + image.Height, opacity);
    }

    public void DrawImage(
        Image image,
        float left,
        float top,
        float right,
        float bottom,
        float opacity = 1.0f,
        bool linearScale = true)
    {
        if (image == null || opacity <= 0)
        {
            return;
        }

        try
        {
            var sourceImage = GetImage(image);
            if (sourceImage == null)
            {
                HasFailure = true;
                return;
            }

            _target.DrawBitmap(
                sourceImage.Bitmap,
                new RawRectangleF(
                    left - _bounds.Left,
                    top - _bounds.Top,
                    right - _bounds.Left,
                    bottom - _bounds.Top),
                opacity,
                linearScale ? BitmapInterpolationMode.Linear : BitmapInterpolationMode.NearestNeighbor,
                new RawRectangleF(0, 0, sourceImage.Bitmap.Size.Width, sourceImage.Bitmap.Size.Height));
            HasDrawnImage = true;
        }
        catch
        {
            HasFailure = true;
        }
    }

    public void Dispose()
    {
        foreach (var image in _loadedImages.Values)
        {
            image.Dispose();
        }

        _loadedImages.Clear();
    }

    private Image GetImage(Image sourceImage)
    {
        if (_loadedImages.TryGetValue(sourceImage, out var cachedImage))
        {
            return cachedImage;
        }

        if (!_sources.TryGetValue(sourceImage, out var source) || string.IsNullOrWhiteSpace(source.Path))
        {
            return null;
        }

        Image image;
        if (Guid.TryParse(source.FormatGuid, out var formatGuid))
        {
            image = new Image(_target, source.Path, new[] { formatGuid });
        }
        else
        {
            image = new Image(_target, source.Path);
        }

        _loadedImages[sourceImage] = image;
        return image;
    }
}
