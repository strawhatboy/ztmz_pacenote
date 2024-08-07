namespace ZTMZ.PacenoteTool.Base.Game;

using System;

public enum CollisionSeverity
{
    None = -1,
    Slight = 0,
    Medium = 1,
    Severe = 2
}

public class CollisionDetector
{

    public static CollisionSeverity DetectCollision(GameData lastGameData, GameData currentGameData)
    {
        // if currentGameData's LapTime is less than or equal to lastGameData's LapTime, then it's not a collision
        if (currentGameData.LapTime <= lastGameData.LapTime)
        {
            return CollisionSeverity.None;
        }

        // speed difference devided by time difference is the acceleration
        var acceleration = (currentGameData.Speed - lastGameData.Speed) / (currentGameData.LapTime - lastGameData.LapTime);

        if (acceleration >= Config.Instance.CollisionSpeedChangeThreshold_Severe)
        {
            return CollisionSeverity.Severe;
        }
        else if (acceleration >= Config.Instance.CollisionSpeedChangeThreshold_Medium)
        {
            return CollisionSeverity.Medium;
        }
        else if (acceleration >= Config.Instance.CollisionSpeedChangeThreshold_Slight)
        {
            return CollisionSeverity.Slight;
        }
        else
        {
            return CollisionSeverity.None;
        }
    }
}
