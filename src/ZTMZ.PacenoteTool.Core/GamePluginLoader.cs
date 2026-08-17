using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.Core;

public static class GamePluginLoader
{
    public static IReadOnlyList<IGame> Load(IEnumerable<string> assemblyPaths, NLog.Logger logger)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        ArgumentNullException.ThrowIfNull(logger);

        var games = new List<IGame>();
        foreach (var assemblyPath in assemblyPaths)
        {
            try
            {
                var assembly = Assembly.LoadFrom(assemblyPath);
                if (string.Equals(
                        assembly.GetName().Name,
                        "ZTMZ.PacenoteTool.Base",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var type in assembly.GetLoadableTypes().Where(IsConcreteGameType))
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is IGame game)
                        {
                            games.Add(game);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Failed to create game provider {0} from {1}; continuing.", type.FullName, assemblyPath);
                    }
                }
            }
            catch (BadImageFormatException)
            {
                logger.Debug("Skipping non-managed game library {0}.", assemblyPath);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to load game provider library {0}; continuing.", assemblyPath);
            }
        }

        return games;
    }

    private static bool IsConcreteGameType(Type type)
    {
        return !type.IsAbstract &&
               !type.IsInterface &&
               !type.ContainsGenericParameters &&
               typeof(IGame).IsAssignableFrom(type);
    }
}
