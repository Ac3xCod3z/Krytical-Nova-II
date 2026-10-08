using System.Reflection;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The comic book the menus are pages of. It is a MegaBook of one leaf: <see cref="MenuTransition"/> puts the
    /// picture of the screen being left on the leaf's face and turns it, and the next screen is what lies under it.
    /// The book is drawn by its own camera into a picture with nothing round the page, which the transition lays
    /// over the screen; it only exists while a page is turning.
    ///
    /// MegaBook's scripts are in the project's default assembly, which this one cannot refer to, so the builder is
    /// held as a plain behaviour and its two control values (the page asked for and the page shown) are set by name.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuBook : MonoBehaviour
    {
        private static readonly int PictureScaleId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");

        [Tooltip("The MegaBook Builder of the book (a vendor script).")]
        [SerializeField] private MonoBehaviour book;
        [SerializeField] private Camera bookCamera;
        [SerializeField] private MenuBookSettings settings;
        [Tooltip("The faction skins: the back of a page takes the accent of the one the menus wear.")]
        [SerializeField] private UiSkin[] skins;

        private FieldInfo askedPage, shownPage;
        private RenderTexture target;
        private MeshRenderer leaf;
        private Material[] paper; // front, back, edge: this leaf's own copies

        public MenuBookSettings Settings => settings;
        /// <summary>The picture of the turning page, see-through everywhere else.</summary>
        public Texture Target => target;
        /// <summary>True once the book has made its leaf and it carries the picture.</summary>
        public bool Ready { get; private set; }

        private void Awake()
        {
            if (settings != null) transform.position = settings.stage;
            if (book != null)
            {
                const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;
                askedPage = book.GetType().GetField("page", Public);
                shownPage = book.GetType().GetField("Flip", Public);
            }
            if (bookCamera != null) bookCamera.enabled = false;
        }

        private void OnDestroy()
        {
            if (bookCamera != null) bookCamera.targetTexture = null;
            if (target != null)
            {
                target.Release();
                Destroy(target);
            }
            if (paper != null)
                foreach (Material each in paper)
                    if (each != null) Destroy(each);
        }

        /// <summary>
        /// Puts <paramref name="picture"/> on the leaf, flat, and starts drawing it. False until MegaBook has made
        /// the leaf (its first frame): ask again on the next one. Turning back is the same turn seen in a mirror,
        /// which <see cref="MenuTransition"/> does when it shows <see cref="Target"/>; the picture is mirrored here
        /// so that it reads the right way round there.
        /// </summary>
        public bool Prepare(Texture picture, bool forward)
        {
            if (Ready) return true;
            if (book == null || bookCamera == null || settings == null || askedPage == null || shownPage == null) return false;
            if (leaf == null) leaf = book.GetComponentInChildren<MeshRenderer>();
            if (leaf == null) return false;

            paper = leaf.materials;
            if (paper.Length < 2) return false;
            Material face = paper[0], back = paper[1];
            Bounds page = leaf.bounds;
            float width = Mathf.Max(0.01f, page.size.x), height = Mathf.Max(0.01f, page.size.z);
            face.mainTexture = picture;
            face.SetVector(PictureScaleId, forward ? new Vector4(1f, 1f, 0f, 0f) : new Vector4(-1f, 1f, 1f, 0f));
            back.SetColor(TintId, Tint());
            back.SetFloat(AspectId, width / height);

            // Straight above the page, at the height where it fills the view exactly: flat, it is the screen it shows.
            float distance = height * 0.5f / Mathf.Tan(settings.fieldOfView * 0.5f * Mathf.Deg2Rad);
            bookCamera.fieldOfView = settings.fieldOfView;
            bookCamera.aspect = width / height;
            bookCamera.nearClipPlane = Mathf.Min(0.02f, distance * 0.01f);
            bookCamera.farClipPlane = distance + width * 2f;
            bookCamera.transform.SetPositionAndRotation(new Vector3(page.center.x, page.max.y + distance, page.center.z), Quaternion.Euler(90f, 0f, 0f));

            target = new RenderTexture(Mathf.Max(16, Screen.width), Mathf.Max(16, Screen.height), 24, RenderTextureFormat.ARGB32)
            {
                name = "Menu Book Page",
                antiAliasing = 4,
            };
            bookCamera.targetTexture = target;
            bookCamera.enabled = true;
            SetTurn(0f);
            Ready = true;
            return true;
        }

        /// <summary>How far the page has turned: 0 lies flat over the screen, 1 has gone over.</summary>
        public void SetTurn(float turned)
        {
            if (askedPage == null || shownPage == null) return;
            object value = Mathf.Clamp01(turned);
            // Both, so that MegaBook shows this very value instead of easing towards it at its own pace.
            askedPage.SetValue(book, value);
            shownPage.SetValue(book, value);
        }

        private Color Tint()
        {
            if (settings.tintWithTheSkin && skins != null)
            {
                FactionId wearing = MenuSkin.Current;
                foreach (UiSkin skin in skins)
                    if (skin != null && skin.faction == wearing) return skin.menuAccent;
            }
            return settings.backTint;
        }
    }
}
