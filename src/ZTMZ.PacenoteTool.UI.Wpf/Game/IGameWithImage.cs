
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base.Game;
namespace ZTMZ.PacenoteTool.UI.Windows.Game;


public class GameWithImage
{
    public IGame Game {get;}

    public BitmapImage Image {get;}

    public GameWithImage(IGame game)
    {
        Game = game;

        var imageUri = string.IsNullOrWhiteSpace(game.ImageUri)
            ? "pack://application:,,,/ZTMZ.PacenoteTool.UI.Wpf;component/pacenote.png"
            : game.ImageUri;
        Image = new BitmapImage(new Uri(imageUri));
        Image.Freeze(); // must freeze for different thread access.
    }
}

