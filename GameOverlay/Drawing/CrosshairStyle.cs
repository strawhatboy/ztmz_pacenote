﻿using System;

namespace GameOverlay.Drawing
{
    /// <summary>
    ///     Offers different built-in styles for crosshairs
    /// </summary>
    public enum CrosshairStyle
    {
        /// <summary>
        ///     Draws a single dot
        /// </summary>
        Dot,

        /// <summary>
        ///     Draws a +
        /// </summary>
        Plus,

        /// <summary>
        ///     Draws a cross
        /// </summary>
        Cross,

        /// <summary>
        ///     Draws a + with a gap in the middle
        /// </summary>
        Gap,

        /// <summary>
        ///     Draws diagonal lines
        /// </summary>
        Diagonal
    }
}
