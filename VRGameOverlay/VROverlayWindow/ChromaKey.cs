using D2D = SharpDX.Direct2D1;
namespace VRGameOverlay.VROverlayWindow
{
    /// <summary>
    /// C# implementation of https://docs.microsoft.com/en-us/windows/win32/direct2d/chromakey-effect
    /// </summary>
    public class ChromaKey : D2D.Effect
    {
        public ChromaKey(D2D.DeviceContext context)
            : base(context, ChromaKey)
        {
        }

        public enum Properties
        {
            COLOR = 0,
            TOLERANCE = 1,
            INVERT_ALPHA = 2,
            FEATHER = 3,
        }

        /// <summary>
        /// The D2D1_CHROMAKEY_PROP_INVERT_ALPHA property is a boolean value indicating whether the alpha values should be inverted. The default value if False.
        /// </summary>
        public bool InvertAlpha
        {
            get
            {
                return GetBoolValue((int)Properties.INVERT_ALPHA);
            }
            set
            {
                SetValue((int)Properties.INVERT_ALPHA, value);
            }
        }

        /// <summary>
        /// The D2D1_CHROMAKEY_PROP_FEATHER property is a boolean value whether the edges of the output should be softened in the alpha channel.
        /// When set to False, the alpha output by the effect is 1-bit: either fully opaque or fully transparent.Setting to True results in a softening of edges in the alpha channel of the Chroma Key output.
        /// The default value is False.
        /// </summary>
        public bool Feather
        {
            get
            {
                return GetBoolValue((int)Properties.FEATHER);
            }
            set
            {
                SetValue((int)Properties.FEATHER, value);
            }
        }

        /// <summary>
        /// The D2D1_CHROMAKEY_PROP_TOLERANCE property is a float value indicating the tolerance for matching the color specified in the D2D1_CHROMAKEY_PROP_COLOR property.
        /// The allowed range is 0.0 to 1.0. The default value is 0.1.
        /// </summary>
        public float Tolerance
        {
            get
            {
                return GetFloatValue((int)Properties.TOLERANCE);
            }
            set
            {
                SetValue((int)Properties.TOLERANCE, value);
            }
        }

        /// <summary>
        /// The D2D1_CHROMAKEY_PROP_COLOR property is a value indicating the color that should be converted to alpha. The default color is black.
        /// </summary>
        public SharpDX.Mathematics.Interop.RawColor3 Color
        {
            get
            {
                return GetColor3Value((int)Properties.COLOR);
            }
            set
            {
                SetValue((int)Properties.COLOR, value);
            }
        }
    }
}
