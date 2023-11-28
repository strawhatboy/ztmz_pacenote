
using System;
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
        Image = new BitmapImage(new Uri(game.ImageUri));
        Image.Freeze(); // must freeze for different thread access.
    }
}


