using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.RBR
{
    public class RBR : IGame
    {
        public string WindowTitle => "";

        public string Name => "Richard Burns Rally (NGP6)";

        public string Description => "The most classic rally simulation game";

        public BitmapImage Image { set => throw new NotImplementedException(); }

        public string Executable => throw new NotImplementedException();

        public IGamePacenoteReader GamePacenoteReader => throw new NotImplementedException();

        public IGameDataReader GameDataReader => throw new NotImplementedException();

        BitmapImage IGame.Image => throw new NotImplementedException();

        Dictionary<string, IGameConfig> IGame.GameConfigurations => new();
    }
}
