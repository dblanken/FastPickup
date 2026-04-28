# Fast Pickup

**Vintage Story 1.22** — After breaking a block, nearby fresh drops are
auto-collected without needing to walk over them. Also briefly extends your
pickup radius when a block breaks.

Two mechanisms work together:

1. **Break-window sweep** — When you break a block, a short window opens
   (≈1.2 s). Any `EntityItem` that spawned from that break and is within
   the configured radius is automatically collected for you.
2. **Range boost** — After a block break, your effective pickup radius is
   temporarily extended (≈1.5 s) around your player position as well,
   catching drops that landed slightly further away.

Port of the _FastPickupPlus_ + _PickupRangeBoost_ features from
[HandyTweaks](https://mods.vintagestory.at/handytweaks) by Interzoner,
updated for VS 1.22.

---

## Requirements

| | |
|---|---|
| Vintage Story | 1.22.x |
| .NET SDK | 10.0 |
| `VINTAGE_STORY` env var | `/opt/vintagestory` (or wherever VS is installed) |

---

## Build

```bash
export VINTAGE_STORY=/opt/vintagestory
bash build.sh
# or with dotnet directly:
dotnet build -c Release
```

The release zip is written to `Releases/FastPickup_v<version>.zip`.

### Install automatically

```bash
bash build.sh --install
# copies the zip to ~/.config/VintagestoryData/Mods/
```

---

## Configuration

On first launch VS writes `VintagestoryData/ModConfig/FastPickup.json`:

```json
{
  "Enabled": true,
  "FreshDropRadiusBlocks": 4.0,
  "PickupDelayMs": 150,
  "RangeBoostDurationMs": 1500
}
```

| Key | Default | Description |
|---|---|---|
| `Enabled` | `true` | Master switch. |
| `FreshDropRadiusBlocks` | `4.0` | Radius (in blocks) around the break point and around the player to scan for fresh drops. Clamped to `[0.9, 40]`. |
| `PickupDelayMs` | `150` | Minimum milliseconds after spawning before a drop can be auto-collected. Clamped to `[0, 4000]`. |
| `RangeBoostDurationMs` | `1500` | How long the range boost lasts after each block break. Clamped to `[200, 10000]`. |

---

## Notes

- The mod respects the vanilla "only collect when sneaking" player preference
  (`ItemCollectMode`). If a player has that set, auto-collection only triggers
  while they are sneaking.
- If a third-party mod introduces a new `Block` subclass that overrides
  `OnBlockBroken` *after* this mod loads, that override won't be patched.
  This is an inherent limitation of startup-time Harmony patching.

---

## Credits

Original feature by **Interzoner** (HandyTweaks). Ported to VS 1.22 by dblanken.
