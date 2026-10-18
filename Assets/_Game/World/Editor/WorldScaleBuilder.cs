using System.Collections.Generic;
using KryticalNova.World;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Writes the world's measure (<see cref="WorldScaleSettings"/>): the table of kinds, each with the real
    /// measurement it is rooted in. A kind given no range of its own may be between the settings' shortfall and
    /// allowance of its real size; the grown and the grand have ranges of their own, because a tree or a statue is
    /// not one size. The same table is written out in words in `_Docs/Design/16_world_scale.md`: change both.
    /// </summary>
    public static class WorldScaleBuilder
    {
        public const string AssetPath = "Assets/_Game/World/Data/Assets/WorldScale.asset";

        private static ScaleKind K(string id, ScaleAxis axis, float real, string root, float min = 0f, float max = 0f, float maxFactor = 0f) =>
            new ScaleKind { id = id, axis = axis, real = real, min = min, max = max, root = root, maxFactor = maxFactor };

        private const ScaleAxis H = ScaleAxis.Height, L = ScaleAxis.Length, A = ScaleAxis.Largest;

        private static readonly ScaleKind[] Kinds =
        {
            // ---- sat on, slept in
            K("chair", H, 0.95f, "A dining chair is 0.85 to 1.0 m to the top of its back, its seat at 0.45 m."),
            K("stool", H, 0.6f, "A stool's seat is 0.45 m; a bar stool's 0.75 m.", 0.4f, 0.9f),
            K("armchair", H, 1.1f, "An armchair or an office chair is 0.9 to 1.2 m.", 0.85f, 1.5f),
            K("throne", H, 1.9f, "A seat of state stands over the person in it: a head higher than they are, and no more than one and a half times a person.", 1.5f, 2.7f),
            K("bench", L, 1.8f, "A bench for three is 1.5 to 1.8 m long.", 1.2f, 3.2f),
            K("sofa", L, 2.2f, "A three-seat sofa is 2.0 to 2.3 m long.", 1.6f, 3.1f),
            K("bed", L, 2.0f, "A bed is 1.9 to 2.0 m long; a made bed with its frame 2.2 m.", 1.9f, 2.6f),
            K("bunk", L, 2.0f, "A berth is 1.9 to 2.0 m long; a rack of them is as long as it is wide.", 1.9f, 3.6f),
            // ---- stood at, put things on
            K("table", H, 0.75f, "A dining table or a desk is 0.74 to 0.76 m high.", 0.65f, 0.95f),
            K("lowtable", H, 0.45f, "A coffee table is 0.4 to 0.5 m high.", 0.3f, 0.6f),
            K("counter", H, 1.0f, "A kitchen counter is 0.9 m; a bar 1.05 to 1.1 m.", 0.85f, 1.3f),
            K("tallcase", H, 2.0f, "A wardrobe, bookcase or locker is 1.8 to 2.2 m; library shelving goes a storey high.", 1.5f, 3.0f),
            K("lowcase", H, 0.85f, "A dresser or a sideboard is 0.75 to 0.95 m high.", 0.6f, 1.3f),
            K("chest", L, 0.9f, "A sea chest is 0.8 to 1.0 m long.", 0.5f, 1.4f),
            K("crate", A, 1.0f, "A shipping crate is 0.6 to 1.2 m on a side.", 0.5f, 1.5f),
            K("barrel", H, 0.95f, "A wine barrel (a barrique) stands 0.95 m.", 0.6f, 1.25f),
            // ---- lit
            K("candle", H, 0.25f, "A candle in its stick is 0.15 to 0.4 m.", 0.08f, 0.7f),
            K("tablelamp", H, 0.5f, "A lamp or lantern carried in a hand is 0.3 to 0.6 m.", 0.2f, 1.0f),
            K("floorlamp", H, 1.6f, "A standing lamp or candelabra is 1.4 to 1.8 m: about head height.", 0.9f, 2.2f),
            K("lamppost", H, 4.0f, "A street lamp for walkers is 3 to 5 m.", 2.5f, 5.5f),
            K("chandelier", A, 1.2f, "A hall chandelier is 0.9 to 1.5 m across.", 0.7f, 2.4f),
            K("brazier", H, 1.0f, "A brazier burns at waist to chest height: 0.8 to 1.2 m.", 0.6f, 1.6f),
            K("hearth", H, 1.6f, "A fireplace's mantel is 1.2 to 1.5 m; a hall's hearth is taller than a person.", 1.0f, 2.8f),
            // ---- machines
            K("screen", A, 1.0f, "A monitor is 0.5 to 0.7 m across; a wall screen 1.2 to 1.9 m.", 0.25f, 2.0f),
            K("console", H, 1.2f, "A standing console is worked at with the hands: 1.0 to 1.4 m.", 0.7f, 1.9f),
            K("machine", H, 1.9f, "A vending machine or a server rack is 1.8 to 2.0 m.", 1.2f, 2.8f),
            // ---- looked at
            K("statue", H, 2.4f, "A statue is life size (1.8 m) to heroic, twice life size, on its plinth.", 1.2f, 4.2f),
            K("monument", H, 5.4f, "A monument is three persons high or more.", 4.0f, 9.0f),
            K("fountain", L, 3.0f, "A courtyard fountain's basin is 2 to 4 m across.", 1.2f, 5.5f),
            K("well", L, 1.8f, "A well's mouth is 1.2 to 1.8 m across.", 1.2f, 3.6f),
            K("rug", L, 2.5f, "A rug is 1.5 to 3 m; a hall runner up to 9 m.", 0.8f, 9.5f),
            K("picture", A, 0.9f, "A painting, a poster or a mounted shield is 0.4 to 1.5 m.", 0.25f, 2.6f),
            K("banner", H, 2.2f, "A hall banner hangs 1.5 to 4 m.", 1.0f, 4.5f),
            K("small", A, 0.3f, "What is held in a hand: a cup, a book, a skull.", 0.04f, 0.9f),
            K("decor", A, 1.5f, "A piece of standing decor is between a chair and a wardrobe in size.", 0.4f, 3.6f),
            K("shelter", H, 4.0f, "A gazebo or an arch is walked under: 3 to 6 m.", 2.5f, 10f),
            // ---- grown
            K("potplant", H, 0.9f, "A plant in a pot is knee to chest high.", 0.3f, 2.2f),
            K("planter", L, 1.5f, "A planter box is 0.6 to 3 m long.", 0.5f, 5.2f),
            K("flowers", H, 0.5f, "Flowers stand ankle to knee high.", 0.15f, 1.3f),
            K("bush", H, 1.2f, "A garden shrub is 0.6 to 2 m.", 0.5f, 2.8f),
            K("hedge", H, 1.3f, "A clipped hedge is waist to shoulder high.", 0.8f, 2.2f),
            K("smalltree", H, 5.0f, "A garden tree (a maple, a cherry) is 4 to 7 m.", 3.0f, 8.0f),
            K("tree", H, 9.0f, "A grown tree is 8 to 12 m, with a crown no wider than it is tall.", 6.0f, 14.0f),
            K("greattree", H, 16.0f, "A great tree is 15 to 25 m: the tallest thing on a Lot.", 12.0f, 25.0f),
            K("rock", A, 1.5f, "A garden boulder is 0.5 to 3 m.", 0.4f, 4.0f),
            // ---- out in the world (18 Oct 2026, Zone Build Kits): grown things at their real height, the built and
            // the driven at their real size. A field tree is not a garden tree: the Lot kinds above stay for housing.
            K("birch", H, 18f, "A grown birch is 15 to 25 m.", 14f, 22f),
            K("conifer", H, 28f, "A grown pine or spruce is 25 to 35 m; a mountain pine a little less.", 20f, 35f),
            K("broadleaf", H, 20f, "A grown oak, maple, ash or willow is 15 to 25 m.", 15f, 25f),
            K("fruittree", H, 5f, "An orchard apple or pear is kept at 4 to 6 m.", 3.5f, 7f),
            K("blossom", H, 8f, "A cherry, a Japanese maple or a ginkgo in a garden is 6 to 10 m.", 6f, 11f),
            K("palm", H, 15f, "A palm is 10 to 20 m.", 10f, 20f),
            K("bamboo", H, 12f, "A grove bamboo cane is 10 to 15 m.", 9f, 16f),
            K("deadtree", H, 11f, "A dead or burned tree keeps most of its height: 8 to 15 m.", 7f, 16f),
            K("swamptree", H, 12f, "A mangrove or swamp tree is 8 to 15 m.", 8f, 16f),
            K("oldgrowth", H, 40f, "An old-growth forest giant is 30 to 50 m (the Sylwood's ordinary trees).", 30f, 50f, 5f),
            K("colossaltree", H, 75f, "A tree a whole zone steers by: 60 to 90 m, ten persons and more (the Great Tree, the Ancestor Grove).", 60f, 90f, 8f),
            K("fern", H, 1f, "A fern is knee to chest high; a tree fern up to 3.5 m.", 0.4f, 3.5f),
            K("tallgrass", H, 1f, "Meadow grass gone to seed is knee to waist high.", 0.5f, 1.6f),
            K("crop", H, 1.8f, "Corn stands 1.5 to 2.5 m; wheat and rice lower.", 0.8f, 2.6f),
            K("boulder", A, 3f, "A field boulder is 1.5 to 6 m.", 1.5f, 6f),
            K("outcrop", A, 12f, "A rock outcrop or tor is 6 to 25 m.", 6f, 25f, 4f),
            K("standingstone", H, 3.5f, "A standing stone or runestone is 2.5 to 5 m: heroic.", 2.5f, 5.5f),
            K("crystal", H, 3f, "A crystal cluster in the open is 1 to 6 m.", 1f, 6f),
            K("house", H, 8f, "A house of one or two storeys is 6 to 11 m to its ridge.", 6f, 11f),
            K("hall", H, 14f, "A longhall or barn is 10 to 18 m to its ridge.", 10f, 18f),
            K("tower", H, 25f, "A watchtower or silo is 18 to 50 m.", 18f, 50f, 4f),
            K("citywall", H, 25f, "A city wall is 20 to 30 m (New Meridian's is 30).", 18f, 32f, 4f),
            K("fence", H, 1.2f, "A field fence or a barricade is waist to chest high.", 0.8f, 2.2f),
            K("pylon", H, 30f, "A power pylon is 25 to 40 m.", 22f, 42f, 4f),
            K("car", L, 4.5f, "A car is 4 to 5 m long.", 3.8f, 5.6f),
            K("truck", L, 9f, "A truck, a food trailer or an armoured carrier is 6 to 12 m long.", 6f, 12f),
            K("bus", L, 12f, "A bus or a train car is 12 to 20 m long.", 10f, 20f),
            K("dropship", L, 30f, "A dropship is 25 to 40 m long.", 22f, 40f, 4f),
            K("longship", L, 25f, "A longship is 20 to 30 m long; a skiff 6 to 10.", 5f, 30f),
            K("mech", H, 12f, "A war-frame stands 10 to 16 m: monumental.", 9f, 16f, 4f),
        };

        [MenuItem("Krytical Nova/World/Build World Scale")]
        public static WorldScaleSettings Build()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldScaleSettings>(AssetPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<WorldScaleSettings>();
                WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(AssetPath).Replace('\\', '/'));
                AssetDatabase.CreateAsset(settings, AssetPath);
            }
            var kinds = new List<ScaleKind>(Kinds.Length);
            foreach (ScaleKind row in Kinds)
            {
                ScaleKind kind = row;
                if (kind.min <= 0f) kind.min = kind.real * settings.shortfall;
                if (kind.max <= 0f) kind.max = kind.real * settings.allowance;
                kinds.Add(kind);
            }
            settings.kinds = kinds;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[World] World scale written: {kinds.Count} kinds, a person is {settings.personHeight} m. {AssetPath}");
            return settings;
        }

        /// <summary>The world's measure, built when it is not there yet.</summary>
        public static WorldScaleSettings Load()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldScaleSettings>(AssetPath);
            return settings != null && settings.kinds.Count > 0 ? settings : Build();
        }
    }
}
