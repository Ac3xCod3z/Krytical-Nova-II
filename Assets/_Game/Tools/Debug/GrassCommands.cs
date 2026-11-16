using KryticalNova.Vegetation.Grass;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>Quantum Console commands for the GPU grass (local, presentation only).</summary>
    public static class GrassCommands
    {
        private const string NoGrass = "No GrassRenderer in the scene.";

        [Command("grass.debug", "Grass debug view: 0 off, 1 placement (green dense, yellow sparse, red = not allowed), 2 LOD bands, 3 biomes.")]
        private static string Debug(int mode)
        {
            GrassRenderer.DebugMode = mode;
            return $"Grass debug view {GrassRenderer.DebugMode}.";
        }

        [Command("grass.density", "Global grass density multiplier (quality), e.g. grass.density 0.6")]
        private static string Density(float scale)
        {
            GrassRenderer.DensityOverride = UnityEngine.Mathf.Clamp(scale, 0f, 2f);
            return $"Grass density x{GrassRenderer.DensityOverride:0.##}.";
        }

        [Command("grass.distance", "Grass draw distance in metres (20-150).")]
        private static string Distance(float metres)
        {
            GrassRenderer.DistanceOverride = UnityEngine.Mathf.Clamp(metres, 20f, 150f);
            return $"Grass distance {GrassRenderer.DistanceOverride:0} m.";
        }

        [Command("grass.fill", "Toggle (or set) the cheap fill tufts between the real grass clumps, to compare with and without.")]
        private static string Fill() => Fill(!GrassRenderer.FillEnabled);

        [Command("grass.fill")]
        private static string Fill(bool on)
        {
            GrassRenderer.FillEnabled = on;
            return on ? "Grass fill on." : "Grass fill off.";
        }

        [Command("grass.view", "Toggle (or set) walking only the ground the camera sees when grass is generated, to compare the cost (grass.stats).")]
        private static string View() => View(!GrassRenderer.ViewOnly);

        [Command("grass.view")]
        private static string View(bool on)
        {
            GrassRenderer.ViewOnly = on;
            return on ? "Grass is generated for what the camera sees." : "Grass is generated for the whole square round the camera.";
        }

        [Command("grass.toggle", "Turn GPU grass on or off.")]
        private static string Toggle(bool on)
        {
            var g = GrassRenderer.Instance != null ? GrassRenderer.Instance : UnityEngine.Object.FindAnyObjectByType<GrassRenderer>(UnityEngine.FindObjectsInactive.Include);
            if (g == null) return NoGrass;
            g.enabled = on;
            return on ? "Grass on." : "Grass off.";
        }

        [Command("grass.stats", "Clumps and triangles drawn per grass type for the game camera.")]
        private static string Stats()
        {
            var g = GrassRenderer.Instance;
            return g == null ? NoGrass : g.Stats();
        }

        [Command("grass.fade", "Soft fade (blades grow and shrink with distance instead of blinking): grass.fade on|off, or no word to go back to the rule set.")]
        private static string Fade() { GrassRenderer.SoftFadeOverride = -1; return "Grass soft fade: as the rule set says."; }

        [Command("grass.fade")]
        private static string Fade(bool on) { GrassRenderer.SoftFadeOverride = on ? 1 : 0; return on ? "Grass soft fade on." : "Grass soft fade off (the old hard cut)."; }

        [Command("grass.count", "Watch the grass counts before the cap (no stall): grass.count on, then grass.count to read (and restart) them, grass.count off to stop.")]
        private static string Count()
        {
            var g = GrassRenderer.Instance;
            return g == null ? NoGrass : g.CountLine(reset: true);
        }

        [Command("grass.tiles", "The ECS grass tiles: counts per look, visible, built, queued, and the main-thread and worker ms per frame (averaged since the last read).")]
        private static string Tiles() { string line = GrassRenderer.TileLine; GrassRenderer.TileLineRead = true; return line; }

        [Command("grass.tiles")]
        private static string Tiles(bool on) { GrassRenderer.TilesOverride = on ? 1 : 0; return on ? "Grass tiles on (test; not saved)." : "Grass tiles off."; }

        [Command("grass.count")]
        private static string Count(bool on) { GrassRenderer.CountWatch = on; return on ? "Grass counts watched." : "Grass counts no longer watched."; }
    }
}
