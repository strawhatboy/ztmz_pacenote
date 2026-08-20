
using System;
using System.Windows.Controls;
using ZTMZ.PacenoteTool.Base.Game;
namespace ZTMZ.PacenoteTool.UI.Windows.Game;

public abstract class IGameConfigSettingsPane: UserControl
{
    public abstract void InitializeWithGame(IGame game);
    public virtual void RefreshFromConfiguration()
    {
    }
    public Action RestartNeeded;
}
