using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Services;

namespace ThirdPersonSW2;

[PluginMetadata(Id = "ThirdPersonSW2", Version = "1.0.0", Name = "ThirdPersonSW2", Author = "Necmi", Description = "SwifltyS2 ThirdPerson Plugin")]
public class ThirdPersonSW2 : BasePlugin
{
    public TPConfigModel Config { get; set; } = new TPConfigModel();
    private readonly ITraceManager _traceManager;
    private Dictionary<int, CBaseEntity> thirdPersonPool = new();
    private Dictionary<int, CBaseEntity> smoothThirdPersonPool = new();
    private Dictionary<int, Dictionary<string, int>> weapons = new();

    public ThirdPersonSW2(ISwiftlyCore core, ITraceManager traceManager) : base(core)
    {
        _traceManager = traceManager;
    }
    public static bool BlockCamera { get; private set; }
    public override void Load(bool hotReload)
    {
        Core.Event.OnTick += OnGameFrame;
        Core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
        Core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath);
        Core.GameEvent.HookPre<EventPlayerHurt>(OnPlayerHurt);

        Core.Command.RegisterCommand("sw_thirdperson", OnTPCommand);
        if (!string.IsNullOrEmpty(Config.CustomTPCommand) && Config.CustomTPCommand != "thirdperson")
        {
            Core.Command.RegisterCommand($"sw_{Config.CustomTPCommand}", OnTPCommand);
        }
        EntityUtilities.BlockCamera = Config.UseBlockCamera;
    }

    public override void Unload()
    {
        Core.Event.OnTick -= OnGameFrame;

        foreach (var slot in thirdPersonPool.Keys.ToList())
            CleanupPlayer(slot);
        foreach (var slot in smoothThirdPersonPool.Keys.ToList())
            CleanupPlayer(slot);
    }

    private void OnGameFrame()
    {
        foreach (var kvp in smoothThirdPersonPool.ToList())
        {
            var slot = kvp.Key;
            var camera = kvp.Value;
            var player = GetPlayerBySlot(slot);

            if (player == null || !player.IsValid || !camera.IsValid)
            {
                CleanupPlayer(slot);
                continue;
            }

            camera.UpdateCameraSmooth(player, Config.ThirdPersonDistance, Config.ThirdPersonHeight, _traceManager);
        }

        foreach (var kvp in thirdPersonPool.ToList())
        {
            var slot = kvp.Key;
            var camera = kvp.Value;
            var player = GetPlayerBySlot(slot);

            if (player == null || !player.IsValid || !camera.IsValid)
            {
                CleanupPlayer(slot);
                continue;
            }

            camera.UpdateCamera(player, Config.ThirdPersonDistance, Config.ThirdPersonHeight, _traceManager);
        }
    }

    [GameEventHandler(HookMode.Post)]
    private HookResult OnRoundStart(EventRoundStart @event)
    {
        foreach (var slot in thirdPersonPool.Keys.ToList())
            CleanupPlayer(slot);
        foreach (var slot in smoothThirdPersonPool.Keys.ToList())
            CleanupPlayer(slot);

        thirdPersonPool.Clear();
        smoothThirdPersonPool.Clear();
        weapons.Clear();

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    private HookResult OnPlayerDeath(EventPlayerDeath @event)
    {
        var player = @event.UserIdPlayer;
        if (player != null && player.IsValid)
        {
            CleanupPlayer(player.Slot);
        }
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Pre)]
    private HookResult OnPlayerHurt(EventPlayerHurt @event)
    {
        var victim = @event.UserIdPlayer;
        var attacker = Core.PlayerManager.GetPlayer(@event.Attacker);

        if (attacker == null || victim == null)
            return HookResult.Continue;

        if (thirdPersonPool.ContainsKey(attacker.Slot) || smoothThirdPersonPool.ContainsKey(attacker.Slot))
        {
            if (attacker.IsInfrontOfPlayer(victim))
            {
                victim.PlayerPawn?.Health += @event.DmgHealth;
                victim.PlayerPawn?.ArmorValue += @event.DmgArmor;
            }
        }

        return HookResult.Continue;
    }

    private void OnTPCommand(ICommandContext context)
    {
        var player = context.Sender;
        if (player == null || !player.IsValid || player.Pawn?.IsValid != true)
            return;

        if (Config.UseOnlyAdmin && !Core.Permission.PlayerHasPermission(player.SteamID, Config.Flag))
        {
            player.SendMessage(MessageType.Chat, " [ThirdPerson] You don't have permission to use this command.");
            return;
        }

        if (Config.UseSmooth)
        {
            SmoothThirdPerson(player);
        }
        else
        {
            DefaultThirdPerson(player);
        }
    }

    private void DefaultThirdPerson(IPlayer player)
    {
        if (!thirdPersonPool.ContainsKey(player.Slot))
        {
            var camera = CreateCameraEntity("prop_dynamic");
            if (camera == null)
            {
                player.SendMessage(MessageType.Chat, " [ThirdPerson] Failed to create camera.");
                return;
            }

            var pos = player.CalculatePositionInFront(-Config.ThirdPersonDistance, Config.ThirdPersonHeight);
            var ang = GetPlayerViewAngle(player);

            camera.Teleport(pos, ang, Vector.Zero);
            SetViewEntity(player, camera);

            player.SendMessage(MessageType.Chat, " [ThirdPerson] Third Person Activated");
            thirdPersonPool.Add(player.Slot, camera);

            if (Config.StripOnUse)
            {
                StripWeapons(player);
            }
        }
        else
        {
            SetViewEntity(player, null);
            if (thirdPersonPool.TryGetValue(player.Slot, out var cam) && cam.IsValid)
            {
                RemoveCameraEntity(cam);
            }
            thirdPersonPool.Remove(player.Slot);
            if (Config.StripOnUse)
            {
                RestoreWeapons(player);
            }

            player.SendMessage(MessageType.Chat, " [ThirdPerson] Third Person Deactivated");
        }
    }

    private void SmoothThirdPerson(IPlayer player)
    {
        if (!smoothThirdPersonPool.ContainsKey(player.Slot))
        {
            var camera = CreateCameraEntity("point_camera");
            if (camera == null)
            {
                player.SendMessage(MessageType.Chat, " [ThirdPerson] Failed to create smooth camera.");
                return;
            }

            var pos = player.CalculatePositionInFront(-Config.ThirdPersonDistance, Config.ThirdPersonHeight);
            var ang = GetPlayerViewAngle(player);

            camera.Teleport(pos, ang, Vector.Zero);
            SetViewEntity(player, camera);

            player.SendMessage(MessageType.Chat, " [ThirdPerson] Smooth Third Person Activated");
            smoothThirdPersonPool.Add(player.Slot, camera);

            if (Config.StripOnUse)
            {
                StripWeapons(player);
            }
        }
        else
        {
            SetViewEntity(player, null);
            if (smoothThirdPersonPool.TryGetValue(player.Slot, out var cam) && cam.IsValid)
            {
                RemoveCameraEntity(cam);
            }
            smoothThirdPersonPool.Remove(player.Slot); 

            if (Config.StripOnUse)
            {
                RestoreWeapons(player);
            }

            player.SendMessage(MessageType.Chat, " [ThirdPerson] Smooth Third Person Deactivated");
        }
    }

    private CBaseEntity? CreateCameraEntity(string entityName)
    {
        try
        {
            CBaseEntity? entity = null;

            if (entityName == "prop_dynamic" || entityName == "point_camera")
            {
                entity = Core.EntitySystem.CreateEntityByDesignerName<CBaseEntity>(entityName);
            }

            if (entity != null)
            {
                entity.DispatchSpawn();
                return entity;
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogError($"Failed to create camera entity {entityName}: {ex.Message}");
        }

        return null;
    }

    private void CleanupPlayer(int slot)
    {
        if (smoothThirdPersonPool.TryGetValue(slot, out var smoothCam))
        {
            if (smoothCam.IsValid)
                RemoveCameraEntity(smoothCam);
            smoothThirdPersonPool.Remove(slot);
        }

        if (thirdPersonPool.TryGetValue(slot, out var cam))
        {
            if (cam.IsValid)
                RemoveCameraEntity(cam);
            thirdPersonPool.Remove(slot);
        }

        if (weapons.ContainsKey(slot))
            weapons.Remove(slot);

        var player = GetPlayerBySlot(slot);
        if (player != null && player.IsValid)
        {
            SetViewEntity(player, null);

            if (Config.StripOnUse)
            {
                RestoreWeapons(player);
            }
        }
    }

    private void SetViewEntity(IPlayer player, CBaseEntity? entity)
    {
        if (player?.Pawn?.CameraServices == null)
            return;

        try
        {
            if (entity == null)
            {
                player.Pawn.CameraServices.ViewEntity = new CHandle<CBaseEntity>();
            }
            else
            {
                var entityHandle = Core.EntitySystem.GetRefEHandle(entity);
                player.Pawn.CameraServices.ViewEntity = entityHandle;
            }

            player.Pawn.CameraServicesUpdated();
        }
        catch (Exception ex)
        {
            Core.Logger.LogError($"Failed to set view entity: {ex.Message}");
        }
    }

    private QAngle GetPlayerViewAngle(IPlayer player)
    {
        return player?.Pawn?.V_angle ?? QAngle.Zero;
    }

    private void StripWeapons(IPlayer player)
    {
        if (player?.Pawn?.ItemServices == null)
            return;

        try
        {
            if (weapons.ContainsKey(player.Slot))
                weapons.Remove(player.Slot);

            var weaponList = new Dictionary<string, int>();

            var weaponServices = player.Pawn.WeaponServices;
            if (weaponServices != null)
            {
                foreach (var weaponHandle in weaponServices.MyWeapons)
                {
                    if (!weaponHandle.IsValid)
                        continue;

                    var weapon = weaponHandle.Value;
                    if (weapon == null)
                        continue;

                    var weaponName = weapon.DesignerName;
                    if (string.IsNullOrEmpty(weaponName))
                        continue;

                    if (weaponList.ContainsKey(weaponName))
                        weaponList[weaponName]++;
                    else
                        weaponList[weaponName] = 1;
                }
            }

            weapons[player.Slot] = weaponList;

            player.Pawn.ItemServices.RemoveItems();

            if (player.Pawn.WeaponServices != null)
            {
                player.Pawn.WeaponServices.PreventWeaponPickup = true;
            }
        }
        catch (Exception ex)
        {
            Core.Logger.LogError($"Error stripping weapons: {ex.Message}");
        }
    }

    private void RestoreWeapons(IPlayer player)
    {
        if (player?.Pawn?.ItemServices == null || !weapons.ContainsKey(player.Slot))
            return;

        try
        {
            if (player.Pawn.WeaponServices != null)
            {
                player.Pawn.WeaponServices.PreventWeaponPickup = false;
            }

            foreach (var weapon in weapons[player.Slot])
            {
                for (int i = 0; i < weapon.Value; i++)
                {
                    player.Pawn.ItemServices.GiveItem(weapon.Key);
                }
            }

            weapons.Remove(player.Slot);
        }
        catch (Exception ex)
        {
            Core.Logger.LogError($"Error restoring weapons: {ex.Message}");
        }
    }

    private IPlayer? GetPlayerBySlot(int slot)
    {
        if (slot < 0) return null;

        var players = Core.PlayerManager.GetAllPlayers();
        return players.FirstOrDefault(p => p.Slot == slot);
    }

    private void RemoveCameraEntity(CBaseEntity? entity)
    {
        if (entity == null || !entity.IsValid)
            return;

        try
        {
            entity.Despawn();
        }
        catch (Exception ex)
        {
            Core.Logger.LogError($"Failed to remove camera entity: {ex.Message}");
        }
    }
}