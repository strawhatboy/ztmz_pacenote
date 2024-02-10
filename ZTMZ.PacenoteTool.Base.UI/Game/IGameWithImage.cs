
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base.Game;
namespace ZTMZ.PacenoteTool.Base.UI.Game;


public class GameWithImage
{
    public IGame Game {get;}

    public BitmapImage Image {get;}

    public GameWithImage(IGame game)
    {
        Game = game;

        // if the game is WRC, add additional text to it
        // TODO: UGLY CODE
        if (game.Name == "EA SPORTS™ WRC")
        {
            // add addtional text to the image
            Image = new BitmapImage(new Uri(game.ImageUri));
            Image.Freeze();
            var drawingVisual = new DrawingVisual();
            var drawingContext = drawingVisual.RenderOpen();
            drawingContext.DrawImage(Image, new Rect(0, 0, Image.Width, Image.Height));
            var text = new FormattedText(I18NLoader.Instance["game.wrc.description"], System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Arial"), 26, Brushes.Red);
            text.MaxTextWidth = Image.Width - 30;
            drawingContext.DrawRectangle(
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 255, 255, 255)), 
                null, 
                new Rect(0, 0, Image.Width, Image.Height));

            drawingContext.DrawText(text, new System.Windows.Point(2, 2));
            drawingContext.Close();
            var renderTargetBitmap = new RenderTargetBitmap((int)Image.Width, (int)Image.Height, 96, 96, PixelFormats.Pbgra32);
            renderTargetBitmap.Render(drawingVisual);
            Image = new BitmapImage();
            var bitmapEncoder = new PngBitmapEncoder();
            bitmapEncoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));

            using (var stream = new MemoryStream())
            {
                bitmapEncoder.Save(stream);
                stream.Seek(0, SeekOrigin.Begin);

                Image.BeginInit();
                Image.CacheOption = BitmapCacheOption.OnLoad;
                Image.StreamSource = stream;
                Image.EndInit();
            }
        } else {
            Image = new BitmapImage(new Uri(game.ImageUri));
        }

        Image.Freeze(); // must freeze for different thread access.
    }
}


