using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The comic transition between menu scenes: Ben-Day dots sweep across and merge into solid ink, the next scene
    /// loads behind them, and its own transition lets the dots shrink away. One per front-end scene, on its own
    /// top-most canvas.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuTransition : MonoBehaviour
    {
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int DirectionId = Shader.PropertyToID("_Direction");
        // A scene's first frames are long. Without a cap they would swallow the whole animation.
        private const float MaxStep = 1f / 30f;

        [Tooltip("Full-screen image using the BenDayWipe material.")]
        [SerializeField] private Image cover;
        [SerializeField, Min(0.05f)] private float duration = 0.45f;
        [SerializeField] private Vector2 direction = new Vector2(1f, 1f);

        private Material material;
        private Coroutine running;
        private float progress = 1f;
        private bool opening, closing;

        /// <summary>The transition of the scene that is currently open.</summary>
        public static MenuTransition Current { get; private set; }
        public bool IsBusy => opening || closing;

        private void Awake()
        {
            Current = this;
            if (cover == null) return;
            material = new Material(cover.material); // never animate the shared asset
            cover.material = material;
            SetProgress(1f);
            SetCovering(true);
        }

        private void Start()
        {
            if (cover != null) running = StartCoroutine(Open());
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (material != null) Destroy(material);
        }

        public void LoadScene(string scene) => Close(() => SceneManager.LoadScene(scene));

        /// <summary>
        /// Cover the screen, then run <paramref name="then"/> (load a scene, activate a preloaded one).
        /// With <paramref name="carryIntoNextScene"/> the transition survives the scene change and uncovers the next
        /// scene itself: for scenes that have no transition of their own (the world).
        /// </summary>
        public void Close(Action then, bool carryIntoNextScene = false)
        {
            if (closing) return;
            bool interrupted = opening;
            closing = true;
            opening = false;
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(CloseRoutine(then, carryIntoNextScene, interrupted));
        }

        private IEnumerator Open()
        {
            opening = true;
            // The previous scene closed towards one corner; open away from it so the sweep reads as one motion.
            material.SetVector(DirectionId, -direction);
            yield return null; // the new scene draws once under the ink
            yield return Animate(0f);
            SetCovering(false);
            opening = false;
            running = null;
        }

        private IEnumerator CloseRoutine(Action then, bool carryIntoNextScene, bool interruptedOpening)
        {
            if (cover != null)
            {
                SetCovering(true);
                // Closing over a half-finished opening keeps its direction: the dots simply grow back.
                if (!interruptedOpening) material.SetVector(DirectionId, direction);
                yield return Animate(1f);
            }
            if (!carryIntoNextScene || cover == null)
            {
                then?.Invoke();
                yield break;
            }

            var from = SceneManager.GetActiveScene();
            transform.SetParent(null, false);
            DontDestroyOnLoad(gameObject);
            then?.Invoke();
            while (SceneManager.GetActiveScene() == from) yield return null;
            // The next scene's first frames (spawning, the camera snapping into place) stay under the ink.
            yield return null;
            yield return null;
            closing = false;
            opening = true;
            material.SetVector(DirectionId, -direction);
            yield return Animate(0f);
            Destroy(gameObject);
        }

        private IEnumerator Animate(float to)
        {
            float from = progress;
            float time = duration * Mathf.Abs(to - from);
            float elapsed = 0f;
            while (elapsed < time)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxStep);
                SetProgress(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / time)));
                yield return null;
            }
            SetProgress(to);
        }

        private void SetProgress(float value)
        {
            progress = value;
            material.SetFloat(ProgressId, value);
        }

        private void SetCovering(bool value)
        {
            cover.enabled = value;
            cover.raycastTarget = value; // swallow clicks while the scene changes
        }
    }
}
