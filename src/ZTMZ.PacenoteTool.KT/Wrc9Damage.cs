using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool.KT;

internal static class Wrc9Damage
{
    internal static CarDamageSnapshot? Read(Func<ulong, int, byte[]> read, ulong image, ulong participant, ulong vehicle) =>
        KtMechanicalDamage.Read(read, image, participant, vehicle, 0xc78, 0x1401488, "wrc9");

    internal static CarDamageSnapshot Create(IReadOnlyList<KtMechanicalDamagePart> parts) =>
        KtMechanicalDamage.Create(parts, "wrc9");
}

internal sealed class Wrc9DamageEvents
{
    private readonly KtMechanicalDamageEvents events = new();
    internal IReadOnlyList<CarDamageEvent> Update(Wrc9Snapshot? sample, GameState state, bool newAttempt) =>
        events.Update(sample?.Telemetry.CarDamage, sample?.Identity, sample?.RawMilliseconds / 1000f ?? 0, state, newAttempt);
}
