using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FastPickup;

// Temporarily extends the auto-pickup radius around a player.
// Activated by FastPickupModSystem after every block break; runs its own
// server tick to sweep up fresh drops within the boosted radius.
internal static class PickupRangeBoost
{
    private class BoostState
    {
        public float Radius;
        public long ExpireMs;
        public long FreshSinceMs;
        public long FreshUntilMs;
    }

    private const int TickMs = 100;
    private const int MaxEntitiesPerTick = 64;

    private static ICoreServerAPI? _sapi;
    private static long _tickId;

    private static readonly Dictionary<string, BoostState> _boosts = new();

    public static void Init(ICoreServerAPI sapi, FastPickupConfig config)
    {
        _sapi = sapi;
    }

    // Activate a pickup-radius boost for a player after a block break.
    public static void Activate(IPlayer player, float radiusBlocks, int durationMs, long freshSinceMs, long freshUntilMs)
    {
        if (_sapi == null || player is not IServerPlayer sp) return;

        float radius = Math.Clamp(radiusBlocks, 0.5f, 40f);
        long now = _sapi.World.ElapsedMilliseconds;

        _boosts[sp.PlayerUID] = new BoostState
        {
            Radius = radius,
            ExpireMs = now + Math.Max(200, durationMs),
            FreshSinceMs = freshSinceMs,
            FreshUntilMs = Math.Max(freshSinceMs, freshUntilMs)
        };

        if (_tickId == 0)
            _tickId = _sapi.World.RegisterGameTickListener(ServerTick, TickMs);
    }

    public static void Clear()
    {
        _boosts.Clear();
        if (_sapi != null && _tickId != 0)
            _sapi.World.UnregisterGameTickListener(_tickId);
        _tickId = 0;
    }

    private static void ServerTick(float dt)
    {
        if (_sapi == null) { StopTick(); return; }

        long now = _sapi.World.ElapsedMilliseconds;
        PickupTracker.Cull(now);

        int activePlayers = 0;
        foreach (var kvp in new List<KeyValuePair<string, BoostState>>(_boosts))
        {
            string uid = kvp.Key;
            BoostState boost = kvp.Value;

            if (now > boost.ExpireMs || now > boost.FreshUntilMs)
            {
                _boosts.Remove(uid);
                continue;
            }

            var player = _sapi.World.PlayerByUid(uid) as IServerPlayer;
            if (player?.Entity == null) continue;

            // Respect the player's "only collect when sneaking" mode.
            if (player.ItemCollectMode == 1)
            {
                if (!((EntityAgent)player.Entity).Controls.Sneak) continue;
            }

            activePlayers++;

            Entity[] nearby;
            try { nearby = _sapi.World.GetEntitiesAround(player.Entity.Pos.XYZ, boost.Radius, boost.Radius); }
            catch { continue; }

            if (nearby == null || nearby.Length == 0) continue;

            int collected = 0;
            for (int i = 0; i < nearby.Length && collected < MaxEntitiesPerTick; i++)
            {
                if (nearby[i] is not EntityItem item) continue;
                if (!item.Alive || item.Itemstack == null || item.Itemstack.StackSize <= 0) continue;

                long spawnedMs = item.itemSpawnedMilliseconds;
                if (spawnedMs < boost.FreshSinceMs || spawnedMs > boost.FreshUntilMs) continue;

                if (PickupTracker.WasJustProcessed(item.EntityId, now)) continue;

                if (PickupHelper.TryCollect(player, item))
                {
                    PickupTracker.MarkProcessed(item.EntityId, now);
                    collected++;
                }
            }
        }

        if (activePlayers == 0 && _boosts.Count == 0)
            StopTick();
    }

    private static void StopTick()
    {
        if (_sapi != null && _tickId != 0)
            _sapi.World.UnregisterGameTickListener(_tickId);
        _tickId = 0;
    }
}
