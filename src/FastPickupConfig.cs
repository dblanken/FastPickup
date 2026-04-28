namespace FastPickup;

public class FastPickupConfig
{
    public bool Enabled { get; set; } = true;

    // Radius around the break point to scan for fresh drops.
    // Clamped to [0.9, 40] blocks.
    public float FreshDropRadiusBlocks { get; set; } = 4f;

    // How many milliseconds after spawning before a fresh drop can be auto-collected.
    // Clamped to [0, 4000] ms.
    public int PickupDelayMs { get; set; } = 150;

    // How long the range boost around the player stays active after a block break.
    // Clamped to [200, 10000] ms.
    public int RangeBoostDurationMs { get; set; } = 1500;
}
