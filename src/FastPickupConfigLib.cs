using ConfigLib;
using ImGuiNET;
using Vintagestory.API.Common;

namespace FastPickup;

// Registers FastPickup's settings with ConfigLib so they appear in the
// in-game mod settings panel alongside other mods.
// ConfigLib is an optional dependency — the mod works fine without it.
public class FastPickupConfigLib : ModSystem
{
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartPre(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled("configlib")) return;

        var configLib = api.ModLoader.GetModSystem<ConfigLibModSystem>();
        if (configLib == null) return;

        configLib.RegisterCustomConfig(Mod.Info.ModID, (id, buttons) =>
            {
                FastPickupConfig cfg = FastPickupModSystem.Config;

                if (buttons.Save)    api.StoreModConfig(cfg, "FastPickup.json");
                if (buttons.Restore) cfg = api.LoadModConfig<FastPickupConfig>("FastPickup.json") ?? new();
                if (buttons.Defaults) cfg = new FastPickupConfig();

                DrawConfig(cfg, id);

                FastPickupModSystem.Config = cfg;
            });
    }

    private static void DrawConfig(FastPickupConfig cfg, string id)
    {
        bool enabled = cfg.Enabled;
        if (ImGui.Checkbox($"Enabled##{id}", ref enabled)) cfg.Enabled = enabled;

        ImGui.Spacing();

        float radius = cfg.FreshDropRadiusBlocks;
        if (ImGui.SliderFloat($"Pickup radius (blocks)##{id}", ref radius, 1f, 40f))
            cfg.FreshDropRadiusBlocks = radius;

        int delay = cfg.PickupDelayMs;
        if (ImGui.SliderInt($"Pickup delay (ms)##{id}", ref delay, 0, 4000))
            cfg.PickupDelayMs = delay;

        int boostDuration = cfg.RangeBoostDurationMs;
        if (ImGui.SliderInt($"Range boost duration (ms)##{id}", ref boostDuration, 200, 10000))
            cfg.RangeBoostDurationMs = boostDuration;

        ImGui.Spacing();

        bool debug = cfg.DebugLogging;
        if (ImGui.Checkbox($"Debug logging##{id}", ref debug)) cfg.DebugLogging = debug;
    }
}
