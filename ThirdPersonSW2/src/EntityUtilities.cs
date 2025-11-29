using System;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Services;

namespace ThirdPersonSW2;

public static class EntityUtilities
{
    public static bool BlockCamera { get; set; } = true;
    
    public static class DebugLogger
    {
        public static void Log(
            string tag,
            string message,
            IPlayer? player = null,
            object? data = null
        )
        {
            string steamId = player != null ? player.SteamID.ToString() : "Unknown";
            string fullMessage = $"[{DateTime.Now:HH:mm:ss}] [{tag}] [Player: {steamId}] {message}";
            if (data != null)
                fullMessage += $" | Data: {data}";

            Console.WriteLine(fullMessage);
        }
    }

    public static void UpdateCamera(
        this CBaseEntity camera,
        IPlayer player,
        float desiredDistance,
        float verticalOffset,
        ITraceManager traceManager
    )
    {
        if (player.IsNullOrInvalid() || !camera.IsValid)
            return;

        var pawn = player.Pawn;
        if (pawn == null) return;

        Vector cameraPos = player.CalculateSafeCameraPosition(desiredDistance, verticalOffset, traceManager);  
        QAngle cameraAngle = pawn.V_angle;

        camera.Teleport(cameraPos, cameraAngle, Vector.Zero);
    }

    public static void UpdateCameraSmooth(
        this CBaseEntity camera,
        IPlayer player,
        float desiredDistance,
        float verticalOffset,
        ITraceManager traceManager
    )
    {
        if (player.IsNullOrInvalid() || !camera.IsValid)
            return;

        var pawn = player.Pawn;
        if (pawn == null || pawn.AbsOrigin == null)
            return;

        Vector targetPos = player.CalculateSafeCameraPosition(desiredDistance, verticalOffset, traceManager);  
        QAngle targetAngle = pawn.V_angle;

        Vector currentPos = camera.AbsOrigin ?? Vector.Zero;

        float lerpFactor = 0.3f;

        Vector smoothedPos = currentPos.Lerp(targetPos, lerpFactor);

        camera.Teleport(smoothedPos, targetAngle, Vector.Zero);
    }

    public static Vector CalculatePositionInFront(
        this IPlayer player,
        float offSetXY,
        float offSetZ = 0
    )
    {
        var pawn = player.Pawn;
        if (pawn?.AbsOrigin == null || pawn?.V_angle == null)
            return Vector.Zero;

        float yawAngleRadians = (float)(pawn.V_angle.Yaw * Math.PI / 180.0);
        float offsetX = offSetXY * (float)Math.Cos(yawAngleRadians);
        float offsetY = offSetXY * (float)Math.Sin(yawAngleRadians);

        Vector pawnOrigin = pawn.AbsOrigin!.Value;
        if (pawnOrigin == Vector.Zero || pawn.V_angle == QAngle.Zero)
            return Vector.Zero;

        return new Vector(
            pawnOrigin.X + offsetX,
            pawnOrigin.Y + offsetY,
            pawnOrigin.Z + offSetZ
        );
    }

    public static bool IsInfrontOfPlayer(this IPlayer player1, IPlayer player2)
    {
        if (player1.IsNullOrInvalid() || player2.IsNullOrInvalid())
            return false;

        var player1Pawn = player1.Pawn;
        var player2Pawn = player2.Pawn;
        
        if (player1Pawn == null || player2Pawn == null)
            return false;

        var yawAngleRadians = (float)(player1Pawn.V_angle.Y * Math.PI / 180.0);

        Vector player1Direction = new(MathF.Cos(yawAngleRadians), MathF.Sin(yawAngleRadians), 0);

        if (player1Pawn.AbsOrigin == null || player2Pawn.AbsOrigin == null)
            return false;

        Vector player1ToPlayer2 = player2Pawn.AbsOrigin.Value - player1Pawn.AbsOrigin.Value;

        float dotProduct = player1ToPlayer2.Dot(player1Direction);

        return dotProduct < 0;
    }

    public static float Dot(this Vector vector1, Vector vector2)
    {
        return vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z;
    }

    public static Vector CalculateSafeCameraPosition(
        this IPlayer player,
        float desiredDistance,
        float verticalOffset = 70f,
        ITraceManager? traceManager = null
    )
    {
        if (player.IsNullOrInvalid() || player.Pawn?.AbsOrigin == null)
            return Vector.Zero;

        var pawn = player.Pawn;
        Vector pawnPos = pawn.AbsOrigin.Value;

        float yawRadians = pawn.V_angle.Y * (float)Math.PI / 180f;
        var backwardDir = new Vector(-MathF.Cos(yawRadians), -MathF.Sin(yawRadians), 0);
        var eyePos = pawnPos + new Vector(0, 0, verticalOffset);
        var targetCamPos = eyePos + backwardDir * desiredDistance;

        Vector finalPos = targetCamPos;

        if (traceManager != null && BlockCamera)
                {
                    var trace = traceManager.GetGameTraceByEyePositionAlternative(
                        player,
                        targetCamPos,
                        (ulong)(MaskTrace.Solid | MaskTrace.Player)
                    );

                    if (trace.DidHit())
                    {
                        Vector hitVec = trace.EndPos;
                        float distanceToWall = (hitVec - eyePos).Length();
                        float clampedDistance = Math.Clamp(distanceToWall - 10f, 10f, desiredDistance);
                        finalPos = eyePos + backwardDir * clampedDistance;
                    }
                }

        return finalPos;
    }

    public static Vector Lerp(this Vector from, Vector to, float t)
    {
        return new Vector(
            from.X + (to.X - from.X) * t,
            from.Y + (to.Y - from.Y) * t,
            from.Z + (to.Z - from.Z) * t
        );
    }

    public static float Length(this Vector vec)
    {
        return (float)Math.Sqrt(vec.X * vec.X + vec.Y * vec.Y + vec.Z * vec.Z);
    }

    public static bool IsNullOrInvalid(this IPlayer? player)
    {
        return player == null || !player.IsValid || player.Pawn?.IsValid != true;
    }

    public static Vector Normalized(this Vector vec)
    {
        float length = vec.Length();
        if (length == 0) return Vector.Zero;
        
        return new Vector(vec.X / length, vec.Y / length, vec.Z / length);
    }
}