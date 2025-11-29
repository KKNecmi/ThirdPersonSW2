using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Services;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;


namespace ThirdPersonSW2;

public static class TraceExtensions
{
    public static CGameTrace? GetGameTraceByEyePosition(
        this ITraceManager self,
        IPlayer player,
        Vector destination,
        ulong mask
    )
    {
        var pawn = player.PlayerPawn;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin is null)
            return null;

        var eyePos = pawn.EyePosition;
        if (eyePos == null)
            return null;

        pawn.EyeAngles.ToDirectionVectors(out var forward, out _, out _);
        
        var startPos = new Vector(eyePos.Value.X, eyePos.Value.Y, eyePos.Value.Z);
        var endPos = startPos + forward * 8192;
        
        var trace = new CGameTrace();
        self.SimpleTrace(
            startPos,
            endPos,
            RayType_t.RAY_TYPE_LINE,
            RnQueryObjectSet.Static | RnQueryObjectSet.Dynamic,
            MaskTrace.Solid | MaskTrace.Player,
            MaskTrace.Empty,
            MaskTrace.Empty,
            CollisionGroup.Player,
            ref trace,
            null
        );

        if (trace.Fraction < 1.0f)
        {
            trace.EndPos.Z += 10f;
            return trace;
        }
            
        return null;
    }

    public static CGameTrace GetGameTraceByEyePositionAlternative(
        this ITraceManager self,
        IPlayer player,
        Vector destination,
        ulong mask)
    {
        var pawn = player.Pawn;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin is null)
            return new CGameTrace();
        
        var start = pawn.AbsOrigin!.Value;
        start.Z += 64f;

        var trace = new CGameTrace();
        var ray = new Ray_t();
        var filter = new CTraceFilter();

        self.TraceShape(start, destination, ray, filter, ref trace);
        
        return trace;
    }

    public static bool DidHit(this CGameTrace trace)
    {
        return trace.Fraction < 1.0f;
    }
}