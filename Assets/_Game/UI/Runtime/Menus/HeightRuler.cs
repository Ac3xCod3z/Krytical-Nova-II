using TMPro;
using UnityEngine;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The height ruler of the character creator (owner, 24 Oct 2026): while the Height slider is changed, a metre ruler
    /// stands beside the character with a tree of a known height behind it for scale, a mark at the top of the head with
    /// the character's height, and a note under the ruler saying what it is for. Built in code on the showroom's stage;
    /// <see cref="CharacterShowroom"/> shows and hides it.
    /// </summary>
    public sealed class HeightRuler : MonoBehaviour
    {
        private const float Tall = 2.5f;              // metres of ruler
        // Beside the character on the side the options panel leaves open (the doll stands right of centre), at the
        // pedestal's rim; ticks and the head mark reach in towards the character, the numbers stand outside.
        private const float RulerX = -0.95f;
        private static readonly Color Ink = new Color(0.93f, 0.95f, 1f, 1f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.32f, 1f);

        private Transform character;
        private Transform marker;
        private TextMeshPro markerLabel;
        private Material ink, gold;
        private float shownHeight = -1f;

        /// <summary>Builds the ruler under <paramref name="stage"/>; <paramref name="tree"/> is scaled to <paramref name="treeMetres"/>.</summary>
        public static HeightRuler Build(Transform stage, Material material, TMP_FontAsset font, GameObject tree, float treeMetres)
        {
            var root = new GameObject("Height Ruler");
            root.transform.SetParent(stage, false);
            var ruler = root.AddComponent<HeightRuler>();
            ruler.Make(material, font, tree, treeMetres);
            root.SetActive(false);
            return ruler;
        }

        /// <summary>The character whose head the mark follows.</summary>
        public void Follow(Transform target) => character = target;

        private void Make(Material material, TMP_FontAsset font, GameObject tree, float treeMetres)
        {
            ink = material != null ? new Material(material) : null;
            gold = material != null ? new Material(material) : null;
            if (ink != null) Tint(ink, Ink);
            if (gold != null) Tint(gold, Gold);

            // The ruler: a bar, a tick every 10 cm (longer and named every 50 cm).
            Bar(new Vector3(RulerX, Tall * 0.5f, 0f), new Vector3(0.03f, Tall, 0.03f), ink);
            for (int cm = 0; cm <= Tall * 100f + 0.5f; cm += 10)
            {
                float y = cm / 100f;
                bool half = cm % 50 == 0;
                float length = half ? 0.16f : 0.07f;
                Bar(new Vector3(RulerX + length * 0.5f, y, 0f), new Vector3(length, half ? 0.012f : 0.007f, 0.02f), ink);
                if (half && cm > 0) Text($"{y:0.0} m", new Vector3(RulerX - 0.08f, y, 0f), 0.9f, Ink, font, TextAlignmentOptions.Right);
            }

            // The mark at the top of the head, from the character to the ruler, with the height beside it.
            marker = new GameObject("Head Mark").transform;
            marker.SetParent(transform, false);
            Bar(new Vector3(RulerX * 0.5f, 0f, 0f), new Vector3(Mathf.Abs(RulerX), 0.012f, 0.012f), gold, marker);
            markerLabel = Text("", new Vector3(RulerX + 0.08f, 0.1f, -0.02f), 1.15f, Gold, font, TextAlignmentOptions.Left, marker);

            // The note under the ruler.
            Text("For scale: the ruler is in metres; the tree behind is " + treeMetres.ToString("0") + " m tall.",
                new Vector3(RulerX - 0.35f, 0.06f, -0.45f), 0.62f, Ink, font, TextAlignmentOptions.Center);

            // A tree from the world, behind and to the side, at its real height.
            if (tree != null)
            {
                GameObject copy = Instantiate(tree, transform);
                copy.name = "Reference Tree";
                copy.transform.localPosition = new Vector3(0.8f, 0f, 2.6f);
                copy.transform.localRotation = Quaternion.identity;
                foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Destroy(c);
                Bounds bounds = default;
                bool any = false;
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                if (any && bounds.size.y > 0.01f) copy.transform.localScale *= treeMetres / bounds.size.y;
            }
        }

        private const float MeasureEvery = 0.15f;
        private Mesh baked;
        private float nextMeasure;

        private void LateUpdate()
        {
            if (character == null || marker == null || Time.unscaledTime < nextMeasure) return;
            nextMeasure = Time.unscaledTime + MeasureEvery;
            // The top of the head: the highest vertex of the character's posed meshes above the stage (renderer bounds
            // of a skinned mesh are padded, so the mesh itself is measured).
            if (baked == null) baked = new Mesh();
            float top = 0f;
            var vertices = new System.Collections.Generic.List<Vector3>(4096);
            foreach (SkinnedMeshRenderer skinned in character.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!skinned.enabled || !skinned.gameObject.activeInHierarchy || skinned.sharedMesh == null) continue;
                skinned.BakeMesh(baked, true);
                baked.GetVertices(vertices);
                Matrix4x4 toWorld = skinned.transform.localToWorldMatrix;
                foreach (Vector3 v in vertices) top = Mathf.Max(top, toWorld.MultiplyPoint3x4(v).y);
            }
            float height = Mathf.Max(0f, transform.InverseTransformPoint(new Vector3(transform.position.x, top, transform.position.z)).y);
            marker.localPosition = new Vector3(0f, height, 0f);
            if (Mathf.Abs(height - shownHeight) < 0.005f) return;
            shownHeight = height;
            markerLabel.text = $"{height:0.00} m";
        }

        private void Bar(Vector3 at, Vector3 size, Material material, Transform parent = null)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(parent != null ? parent : transform, false);
            bar.transform.localPosition = at;
            bar.transform.localScale = size;
            var renderer = bar.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private TextMeshPro Text(string text, Vector3 at, float size, Color colour, TMP_FontAsset font, TextAlignmentOptions alignment, Transform parent = null)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.transform.localPosition = at;
            var label = go.AddComponent<TextMeshPro>();
            if (font != null) label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = colour;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.sizeDelta = new Vector2(alignment == TextAlignmentOptions.Center ? 3.2f : 1.2f, 0.3f);
            label.rectTransform.pivot = alignment == TextAlignmentOptions.Center ? new Vector2(0.5f, 0.5f)
                : alignment == TextAlignmentOptions.Right ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
            return label;
        }

        private static void Tint(Material material, Color colour)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
        }

        private void OnDestroy()
        {
            if (baked != null) Destroy(baked);
            if (ink != null) Destroy(ink);
            if (gold != null) Destroy(gold);
        }
    }
}
