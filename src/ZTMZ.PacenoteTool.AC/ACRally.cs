using System;
using System.Collections.Generic;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.AC;

public class ACRally : IGame
{
    public string WindowTitle => "Assetto Corsa Rally";

    public string Name => "Assetto Corsa Rally";

    public string Description { get; private set; } = "";

    public string ImageUri { get; } = "pack://application:,,,/ZTMZ.PacenoteTool.AC;component/acr.jpg";

    public string Executable => "acr";

    public IGamePacenoteReader GamePacenoteReader => throw new NotImplementedException();

    public IGameDataReader GameDataReader => new ACRGameDataReader();

    public IGamePrerequisiteChecker GamePrerequisiteChecker => throw new NotImplementedException();

    public Dictionary<string, IGameConfig> GameConfigurations { set; get; } = new Dictionary<string, IGameConfig>();

    public Dictionary<string, IGameConfig> DefaultGameConfigurations { get; } = new Dictionary<string, IGameConfig>();

    public bool IsRunning { get; set; }
    public bool IsInitialized { get; set; }

    public int Order => 4000;


    public event Action<int, string> onCustomMessage;

    public void OnCustomMessage(int level, string message)
    {
        onCustomMessage?.Invoke(level, message);
    }

    public ACRally()
    {
        this.Description = I18NLoader.Instance["game.acr.description"];
        this.GameConfigurations = DefaultGameConfigurations;
    }
}
