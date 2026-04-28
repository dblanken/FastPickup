using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FastPickup;

// Shared collection logic and an anti-double-collect tracker used by both
// FastPickupModSystem and PickupRangeBoost.
internal static class PickupHelper
{
    // Attempt to collect an EntityItem for the given server player by delegating to
    // the player's EntityBehaviorCollectEntities (the same path vanilla uses).
    // Returns true if the item was collected.
    public static bool TryCollect(IServerPlayer player, EntityItem item)
    {
        if (player.Entity == null || !item.Alive) return false;

        var collector = player.Entity.GetBehavior<EntityBehaviorCollectEntities>();
        if (collector == null) return false;

        try { return collector.OnFoundCollectible(item); }
        catch { return false; }
    }
}

// Tracks recently-processed entity IDs to avoid double-collecting a single drop
// in the same tick window.
internal static class PickupTracker
{
    private const int TtlMs = 1500;
    private static readonly Dictionary<long, long> _processedUntil = new();

    public static bool WasJustProcessed(long entityId, long nowMs)
        => _processedUntil.TryGetValue(entityId, out long until) && nowMs < until;

    public static void MarkProcessed(long entityId, long nowMs)
        => _processedUntil[entityId] = nowMs + TtlMs;

    public static void Cull(long nowMs)
    {
        // Remove a small batch of stale entries each call to avoid O(n) scans on large worlds.
        const int MaxScan = 64;
        int scanned = 0;
        foreach (var key in new List<long>(_processedUntil.Keys))
        {
            if (++scanned > MaxScan) break;
            if (nowMs >= _processedUntil[key]) _processedUntil.Remove(key);
        }
    }

    public static void Clear() => _processedUntil.Clear();
}
