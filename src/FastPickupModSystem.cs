using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FastPickup;

public class FastPickupModSystem : ModSystem
{
    private struct BreakWindow
    {
        public Vec3d Center;
        public string OwnerUid;
        public long StartMs;
        public long ExpireMs;
    }

    private Harmony? _harmony;
    private static ICoreServerAPI? _sapi;
    private static long _tickId;

    private static int _freshDropWindowMs;
    private static int _forceAgeMs;
    private static float _scanRadius;
    private static int _pickupDelayMs;
    private static bool _debug;

    private static readonly List<BreakWindow> _windows = new();

    internal static FastPickupConfig Config { get; set; } = new();

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        _sapi = sapi;

        Config = sapi.LoadModConfig<FastPickupConfig>("FastPickup.json") ?? new FastPickupConfig();
        sapi.StoreModConfig(Config, "FastPickup.json");

        if (!Config.Enabled) return;

        _freshDropWindowMs = 1200;
        _forceAgeMs = 1500;
        _scanRadius = Math.Clamp(Config.FreshDropRadiusBlocks, 0.9f, 40f);
        _pickupDelayMs = Math.Clamp(Config.PickupDelayMs, 0, 4000);
        _debug = Config.DebugLogging;

        // Confirm that the collection behavior exists on players so we can diagnose
        // silent failures in TryCollect.
        sapi.Event.PlayerNowPlaying += sp => {
            var b = sp.Entity?.GetBehavior<EntityBehaviorCollectEntities>();
            sapi.Logger.Event("[FastPickup] CollectEntities behavior for {0}: {1}",
                sp.PlayerName, b != null ? "FOUND" : "NULL – TryCollect will always return false!");
        };

        _harmony = new Harmony(Mod.Info.ModID);
        PatchOnBlockBroken(_harmony, sapi);

        PickupRangeBoost.Init(sapi, Config);
    }

    public override void Dispose()
    {
        try { _harmony?.UnpatchAll(Mod.Info.ModID); } catch { }

        if (_sapi != null && _tickId != 0)
        {
            _sapi.World.UnregisterGameTickListener(_tickId);
            _tickId = 0;
        }
        _windows.Clear();
        PickupRangeBoost.Clear();
    }

    // Postfix on Block.OnBlockBroken and all overrides.
    // Registers a short "break window" so drops from this block are swept up automatically.
    private static void AfterOnBlockBroken_Postfix(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier)
    {
        if (world?.Side != EnumAppSide.Server || byPlayer == null || _sapi == null)
            return;

        long now = world.ElapsedMilliseconds;
        // Items drop during OnBlockBroken (before this Postfix fires), so backdate
        // the window start slightly to ensure those drops aren't rejected as too old.
        long freshSince = now - 200;
        Vec3d center = pos.ToVec3d().Add(0.5, 0.5, 0.5);
        int windowMs = Math.Max(_freshDropWindowMs, _pickupDelayMs + 250);

        _windows.Add(new BreakWindow
        {
            Center = center,
            OwnerUid = byPlayer.PlayerUID,
            StartMs = freshSince,
            ExpireMs = now + windowMs
        });

        PickupRangeBoost.Activate(byPlayer, _scanRadius, Math.Clamp(Config.RangeBoostDurationMs, 200, 10000), freshSince, now + windowMs);

        if (_tickId == 0)
            _tickId = _sapi.World.RegisterGameTickListener(ServerTick, 50);
    }

    private static void ServerTick(float dt)
    {
        if (_sapi == null) { StopTick(); return; }

        PickupTracker.Cull(_sapi.World.ElapsedMilliseconds);

        long now = _sapi.World.ElapsedMilliseconds;

        // Remove expired windows.
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            if (now > _windows[i].ExpireMs)
                _windows.RemoveAt(i);
        }

        if (_windows.Count == 0) { StopTick(); return; }

        foreach (var w in _windows)
        {
            var player = _sapi.World.PlayerByUid(w.OwnerUid) as IServerPlayer;
            if (player?.Entity == null) continue;

            // Respect the player's "only collect when sneaking" setting.
            if (player.ItemCollectMode == 1)
            {
                if (!((EntityAgent)player.Entity).Controls.Sneak) continue;
            }

            Entity[] nearby;
            try { nearby = _sapi.World.GetEntitiesAround(w.Center, _scanRadius, _scanRadius); }
            catch { continue; }

            if (nearby == null || nearby.Length == 0) continue;

            if (_debug)
                _sapi.Logger.Event("[FastPickup] MainTick: {0} entities near break center for {1}", nearby.Length, player.PlayerName);

            foreach (var entity in nearby)
            {
                if (entity is not EntityItem item) continue;
                if (!item.Alive || item.Itemstack == null || item.Itemstack.StackSize <= 0) continue;

                long spawnedMs = item.itemSpawnedMilliseconds;
                if (spawnedMs < w.StartMs) continue; // not from this break
                if (_pickupDelayMs > 0 && now - spawnedMs < _pickupDelayMs) continue;

                double dx = player.Entity.Pos.X - item.Pos.X;
                double dy = player.Entity.Pos.Y - item.Pos.Y;
                double dz = player.Entity.Pos.Z - item.Pos.Z;
                double distSq = dx * dx + dy * dy + dz * dz;
                if (distSq > _scanRadius * _scanRadius)
                {
                    if (_debug)
                        _sapi.Logger.Event("[FastPickup] MainTick: {0} at dist {1:F1} – fails player-distance check (radius {2})",
                            item.Itemstack?.GetName() ?? "item", Math.Sqrt(distSq), _scanRadius);
                    continue;
                }

                if (PickupTracker.WasJustProcessed(item.EntityId, now)) continue;

                // Force-age the item so the vanilla "too fresh to collect" gate doesn't block us.
                item.itemSpawnedMilliseconds = now - _forceAgeMs;

                bool collected = PickupHelper.TryCollect(player, item);
                if (_debug)
                    _sapi.Logger.Event("[FastPickup] MainTick: TryCollect({0}) = {1}", item.Itemstack?.GetName() ?? "item", collected);
                if (collected)
                    PickupTracker.MarkProcessed(item.EntityId, now);
            }
        }
    }

    private static void StopTick()
    {
        if (_sapi != null && _tickId != 0)
            _sapi.World.UnregisterGameTickListener(_tickId);
        _tickId = 0;
    }

    private static void PatchOnBlockBroken(Harmony h, ICoreServerAPI sapi)
    {
        var sig = new[] { typeof(IWorldAccessor), typeof(BlockPos), typeof(IPlayer), typeof(float) };
        var postfix = new HarmonyMethod(typeof(FastPickupModSystem), nameof(AfterOnBlockBroken_Postfix));
        var patched = new HashSet<MethodBase>();

        // Base Block.OnBlockBroken.
        TryPatch(h, typeof(Block), sig, postfix, patched, sapi, "Block");

        // BlockReeds has its own override.
        var reedsType = AccessTools.TypeByName("Vintagestory.GameContent.BlockReeds");
        if (reedsType != null) TryPatch(h, reedsType, sig, postfix, patched, sapi, "BlockReeds");

        // Walk every loaded assembly for other Block overrides.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }

            foreach (var t in types)
            {
                if (t == null || t.IsAbstract || !typeof(Block).IsAssignableFrom(t)) continue;
                MethodInfo? method;
                try { method = t.GetMethod("OnBlockBroken", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, sig, null); }
                catch { continue; }
                if (method == null || method.GetBaseDefinition().DeclaringType != typeof(Block)) continue;
                TryPatch(h, t, sig, postfix, patched, sapi, t.FullName ?? t.Name);
            }
        }
    }

    private static void TryPatch(Harmony h, Type type, Type[] sig, HarmonyMethod postfix, HashSet<MethodBase> patched, ICoreServerAPI sapi, string label)
    {
        try
        {
            var m = AccessTools.Method(type, "OnBlockBroken", sig);
            if (m == null || patched.Contains(m)) return;
            h.Patch(m, postfix: postfix);
            patched.Add(m);
            sapi.Logger.Event("[FastPickup] Patched OnBlockBroken: {0}", label);
        }
        catch (Exception ex)
        {
            sapi.Logger.Warning("[FastPickup] Could not patch {0}: {1}", label, ex.Message);
        }
    }
}
