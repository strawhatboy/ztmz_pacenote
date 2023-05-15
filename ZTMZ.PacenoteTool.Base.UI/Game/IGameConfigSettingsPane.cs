
using System;
using System.Windows.Controls;
using ZTMZ.PacenoteTool.Base.Game;
namespace ZTMZ.PacenoteTool.Base.UI.Game;

public abstract class IGameConfigSettingsPane: UserControl
{
    public abstract void InitializeWithGame(IGame game);
    public Action RestartNeeded;
}
