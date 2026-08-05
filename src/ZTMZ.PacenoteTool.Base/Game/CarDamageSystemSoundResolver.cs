using System.Collections.Generic;

namespace ZTMZ.PacenoteTool.Base.Game;

public readonly record struct CarDamageSystemSound(
    string Token,
    string CooldownKey,
    bool RequiresDamageSoundEnabled);

public static class CarDamageSystemSoundResolver
{
    public static bool TryResolve(CarDamageEvent damageEvent, out CarDamageSystemSound systemSound)
    {
        systemSound = default;
        if (damageEvent?.Parameters is null)
        {
            return false;
        }

        switch (damageEvent.DamageType)
        {
            case CarDamage.Collision:
                if (!TryGetCollisionSeverity(damageEvent.Parameters, out var collisionSeverity))
                {
                    return false;
                }

                systemSound = new CarDamageSystemSound(
                    Constants.SYSTEM_COLLISION[collisionSeverity],
                    "collision",
                    RequiresDamageSoundEnabled: true);
                return true;

            case CarDamage.Wheel:
                if (!TryGetInteger(damageEvent.Parameters, CarDamageConstants.WHEELINDEX, out var wheelIndex) ||
                    wheelIndex < 0 ||
                    wheelIndex >= Constants.SYSTEM_PUNCTURE.Count)
                {
                    return false;
                }

                systemSound = new CarDamageSystemSound(
                    Constants.SYSTEM_PUNCTURE[wheelIndex],
                    $"wheel:{wheelIndex}",
                    RequiresDamageSoundEnabled: false);
                return true;

            case CarDamage.Radiator:
                systemSound = new CarDamageSystemSound(
                    Constants.SYSTEM_RADIATOR_DAMAGE,
                    "mechanical:radiator",
                    RequiresDamageSoundEnabled: true);
                return true;

            case CarDamage.WaterPump:
                systemSound = new CarDamageSystemSound(
                    Constants.SYSTEM_WATER_PUMP_DAMAGE,
                    "mechanical:water-pump",
                    RequiresDamageSoundEnabled: true);
                return true;

            case CarDamage.Engine:
                systemSound = new CarDamageSystemSound(
                    Constants.SYSTEM_ENGINE_DAMAGE,
                    "mechanical:engine",
                    RequiresDamageSoundEnabled: true);
                return true;

            default:
                return false;
        }
    }

    private static bool TryGetCollisionSeverity(
        IReadOnlyDictionary<string, object> parameters,
        out int severity)
    {
        if (!TryGetInteger(parameters, CarDamageConstants.SEVERITY, out severity))
        {
            return false;
        }

        return severity >= 0 && severity < Constants.SYSTEM_COLLISION.Count;
    }

    private static bool TryGetInteger(
        IReadOnlyDictionary<string, object> parameters,
        string key,
        out int value)
    {
        value = 0;
        if (!parameters.TryGetValue(key, out var rawValue))
        {
            return false;
        }

        switch (rawValue)
        {
            case int integerValue:
                value = integerValue;
                return true;
            case CollisionSeverity collisionSeverity:
                value = (int)collisionSeverity;
                return true;
            default:
                return false;
        }
    }
}
