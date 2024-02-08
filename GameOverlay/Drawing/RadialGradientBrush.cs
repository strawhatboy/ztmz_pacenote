using System;

using SharpDX.Direct2D1;
using SharpDX.Mathematics.Interop;

using SharpDXGradientBrush = SharpDX.Direct2D1.RadialGradientBrush;

namespace GameOverlay.Drawing
{
    /// <summary>
    /// Represents a radial gradient brush used with a Graphics surface.
    /// check https://learn.microsoft.com/en-us/windows/winui/api/microsoft.ui.xaml.media.radialgradientbrush?view=winui-2.8
    /// </summary>
    public class RadialGradientBrush : IBrush
    {
        private SharpDXGradientBrush _brush;
        private GradientStopCollection _stopCollection;

        public Brush Brush { get => _brush; set => _brush = (SharpDXGradientBrush)value; }

        public Point Center { get => _brush.Center; set => _brush.Center = value; }

        public Point GradientOriginOffset { get => _brush.GradientOriginOffset; set => _brush.GradientOriginOffset = value; }

        public float RadiusX { get => _brush.RadiusX; set => _brush.RadiusX = value; }

        public float RadiusY { get => _brush.RadiusY; set => _brush.RadiusY = value; }

        private RadialGradientBrush()
        {
        }
        public RadialGradientBrush(RenderTarget device, params Color[] colors)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (colors == null || colors.Length == 0) throw new ArgumentNullException(nameof(colors));

            float position = 0.0f;
            float stepSize = 1.0f / colors.Length;
            
            if (colors.Length > 1)
            {
                stepSize = 1.0f / (colors.Length - 1);
            }

            var gradientStops = new GradientStop[colors.Length];

            for (int i = 0; i < colors.Length; i++)
            {
                gradientStops[i] = new GradientStop()
                {
                    Color = colors[i],
                    Position = position
                };

                position += stepSize;
            }

            _stopCollection = new GradientStopCollection(device, gradientStops, Gamma.Linear, ExtendMode.Clamp);

            _brush = new SharpDXGradientBrush(device, new RadialGradientBrushProperties()
            {
                Center = new RawVector2(0, 0),
                GradientOriginOffset = new RawVector2(0, 0),
                RadiusX = 1,
                RadiusY = 1
            }, _stopCollection);
        }

        public RadialGradientBrush(Graphics graphics, params Color[] colors) : this(graphics?.GetRenderTarget(), colors)
        {
        }

        public void SetCenter(float x, float y)
        {
            Center = new Point(x, y);
        }

        public void SetGradientOriginOffset(float x, float y)
        {
            GradientOriginOffset = new Point(x, y);
        }

        public void SetRadius(float x, float y)
        {
            RadiusX = x;
            RadiusY = y;
        }

        public static implicit operator SharpDXGradientBrush(RadialGradientBrush brush) => brush._brush;

        public static implicit operator RadialGradientBrush(SharpDXGradientBrush brush) => new RadialGradientBrush
        {
            _brush = brush
        };

        public static implicit operator Brush(RadialGradientBrush brush) => brush._brush;

        public static implicit operator RadialGradientBrush(Brush brush) => new RadialGradientBrush
        {
            _brush = (SharpDXGradientBrush)brush
        };

        public override string ToString()
        {
            return OverrideHelper.ToString(
                "RadialGradientBrush", GetHashCode().ToString(),
                "Center", Center.ToString(),
                "GradientOriginOffset", GradientOriginOffset.ToString(),
                "RadiusX", RadiusX.ToString(),
                "RadiusY", RadiusY.ToString());
        }

        public override int GetHashCode()
        {
            return OverrideHelper.HashCodes(
                _brush.NativePointer.GetHashCode());
        }

        public override bool Equals(object obj)
        {
            return obj is RadialGradientBrush brush &&
                   _brush.NativePointer == brush._brush.NativePointer;
        }

        public bool Equals(RadialGradientBrush brush)
        {
            return brush != null &&
                   _brush.NativePointer == brush._brush.NativePointer;
        }

        #region IDisposable Support
        private bool disposedValue;

        /// <summary>
        /// Releases all resources used by this LinearGradientBrush.
        /// </summary>
        /// <param name="disposing">A Boolean value indicating whether this is called from the destructor.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                _brush?.Dispose();
                _stopCollection?.Dispose();

                _brush = null;
                _stopCollection = null;

                disposedValue = true;
            }
        }

        /// <summary>
        /// Releases all resources used by this LinearGradientBrush.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        /// <summary>
        /// Returns a value indicating whether two specified instances of LinearGradientBrush represent the same value.
        /// </summary>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <returns> <see langword="true" /> if <paramref name="left" /> and <paramref name="right" /> are equal; otherwise, <see langword="false" />.</returns>
        public static bool Equals(LinearGradientBrush left, LinearGradientBrush right)
        {
            return left?.Equals(right) == true;
        }
    }
}
