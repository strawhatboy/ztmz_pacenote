using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ZTMZ.PacenoteTool.Base;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

public class ZTMZPacenoteTool {
    
    private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
    private IGame _currentGame;
    private List<IGame> _games = new();
        

    // init the tool, load settings, etc.
    public void Init() {

    }

    private void loadGames()
    {
        this._games.Clear();
        foreach (var file in Directory.EnumerateFiles(Constants.PATH_GAMES, "*.dll")) 
        {
            var assembly = Assembly.LoadFrom(System.IO.Path.GetFullPath(file));
            if (assembly.GetName().Name.Equals("ZTMZ.PacenoteTool.Base")) 
            {
                continue;
            }
            var games = assembly.GetTypes().Where(t => typeof(IGame).IsAssignableFrom(t)).Select(i => (IGame)Activator.CreateInstance(i));
            this._games.AddRange(games);
        }
        this._games.Sort((g1, g2) => g1.Order.CompareTo(g2.Order));
        this._games.ForEach(g => g.GameConfigurations = Config.Instance.LoadGameConfig(g));
        _logger.Info($"{this._games.Count} games loaded.");
    }

    

}


