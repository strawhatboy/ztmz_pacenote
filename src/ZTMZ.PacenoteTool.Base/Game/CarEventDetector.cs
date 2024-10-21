namespace ZTMZ.PacenoteTool.Base.Game;

using System;

public enum CollisionSeverity
{
    None = -1,
    Slight = 0,
    Medium = 1,
    Severe = 2
}

public class CarEventDetector
{
    private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

    public static CollisionSeverity DetectCollision(GameData lastGameData, GameData currentGameData)
    {
        // if currentGameData's LapTime is less than or equal to lastGameData's LapTime, then it's not a collision.
        // if currentGameData's Speed is less than 1.0f, then it's not a collision, it's a vehicle reset.
        if (currentGameData.LapTime <= lastGameData.LapTime || 
        currentGameData.Speed < 5.0f && lastGameData.LapDistance > currentGameData.LapDistance)
        {
            return CollisionSeverity.None;
        }

        // speed difference devided by time difference is the acceleration
        var acceleration = (lastGameData.Speed - currentGameData.Speed) / 3.6 / (currentGameData.LapTime - lastGameData.LapTime);

        if (acceleration >= Config.Instance.CollisionSpeedChangeThreshold_Severe)
        {
            logger.Trace($"Acceleration: {acceleration}, Speed: {currentGameData.Speed}, LapTime: {currentGameData.LapTime}, LapDistance: {currentGameData.LapDistance}, lastSpeed: {lastGameData.Speed}, lastLapTime: {lastGameData.LapTime}, lastLapDistance: {lastGameData.LapDistance}");
            return CollisionSeverity.Severe;
        }
        else if (acceleration >= Config.Instance.CollisionSpeedChangeThreshold_Medium)
        {
            logger.Trace($"Acceleration: {acceleration}, Speed: {currentGameData.Speed}, LapTime: {currentGameData.LapTime}, LapDistance: {currentGameData.LapDistance}, lastSpeed: {lastGameData.Speed}, lastLapTime: {lastGameData.LapTime}, lastLapDistance: {lastGameData.LapDistance}");
            return CollisionSeverity.Medium;
        }
        else if (acceleration >= Config.Instance.CollisionSpeedChangeThreshold_Slight)
        {
            logger.Trace($"Acceleration: {acceleration}, Speed: {currentGameData.Speed}, LapTime: {currentGameData.LapTime}, LapDistance: {currentGameData.LapDistance}, lastSpeed: {lastGameData.Speed}, lastLapTime: {lastGameData.LapTime}, lastLapDistance: {lastGameData.LapDistance}");
            return CollisionSeverity.Slight;
        }
        else
        {
            return CollisionSeverity.None;
        }
    }

    public static bool IsCarReset(GameData lastGameData, GameData currentGameData)
    {
        // if currentGameData's Speed is less than 1.0f, and the lapdistance decreases, then it's not a collision, it's a vehicle reset.
        // there is one exception, when the car runs backward and got a collision.
        if (currentGameData.LapTime > lastGameData.LapTime && 
            currentGameData.Speed < 5.0f && 
            currentGameData.Speed < lastGameData.Speed &&
            lastGameData.Speed - currentGameData.Speed > 5.0f &&
            lastGameData.LapDistance > currentGameData.LapDistance) // 5 km/h
        {
            return true;
        }
        return false;
    }
}
