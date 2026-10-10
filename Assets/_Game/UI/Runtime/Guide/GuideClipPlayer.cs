using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace KryticalNova.UI.Guide
{
    /// <summary>
    /// Plays a Guide topic's clip on the page: one video player, made when the first clip is shown, drawing into one
    /// small texture that is the background of the page's screen. A clip loops without sound and only while the
    /// Guide is being read; <see cref="Stop"/> ends it and gives back the player and the texture. Until a clip's
    /// first frame is ready, and for a topic that has none, the topic's still is shown.
    /// </summary>
    public sealed class GuideClipPlayer
    {
        private const int RepaintMilliseconds = 33;

        private GameObject host;
        private VideoPlayer player;
        private RenderTexture target;
        private VisualElement screen;
        private IVisualElementScheduledItem repaint;
        private Rect part = new Rect(0f, 0f, 1f, 1f);

        public bool IsPlaying => player != null && player.isPlaying;

        /// <param name="focus">The part of the picture to show, as shares of it; no size shows all of it.</param>
        public void Show(VisualElement onScreen, VideoClip clip, Texture2D still, Rect focus = default)
        {
            Stop();
            screen = onScreen;
            if (screen == null) return;
            part = focus.width > 0.01f && focus.height > 0.01f ? focus : new Rect(0f, 0f, 1f, 1f);
            Frame();
            screen.RegisterCallback<GeometryChangedEvent>(OnScreenResized);
            screen.style.backgroundImage = still != null ? new StyleBackground(still) : new StyleBackground(StyleKeyword.None);
            screen.EnableInClassList("empty", clip == null && still == null);
            if (clip == null) return;

            host = new GameObject("Guide Clip");
            Object.DontDestroyOnLoad(host);
            player = host.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.skipOnDrop = true;
            player.waitForFirstFrame = true;
            player.audioOutputMode = VideoAudioOutputMode.None;
            // The Guide is read with the game going on behind it, or paused: the clip keeps its own time.
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.renderMode = VideoRenderMode.RenderTexture;
            target = new RenderTexture((int)clip.width, (int)clip.height, 0) { name = "Guide Clip" };
            player.targetTexture = target;
            player.source = VideoSource.VideoClip;
            player.clip = clip;
            player.prepareCompleted += OnPrepared;
            player.Prepare();
        }

        private void OnScreenResized(GeometryChangedEvent evt) => Frame();

        /// <summary>
        /// Shows the whole picture, or the part of it the topic names: the picture is drawn larger than the screen
        /// and moved so that part fills it. The page gives the screen that part's shape, so nothing is stretched.
        /// </summary>
        private void Frame()
        {
            if (screen == null) return;
            float width = screen.resolvedStyle.width, height = screen.resolvedStyle.height;
            bool whole = part.width >= 0.999f && part.height >= 0.999f;
            if (whole || float.IsNaN(width) || width < 1f || float.IsNaN(height) || height < 1f)
            {
                screen.style.backgroundSize = StyleKeyword.Null;
                screen.style.backgroundPositionX = StyleKeyword.Null;
                screen.style.backgroundPositionY = StyleKeyword.Null;
                return;
            }
            float pictureWidth = width / part.width, pictureHeight = height / part.height;
            screen.style.backgroundSize = new BackgroundSize(new Length(pictureWidth), new Length(pictureHeight));
            screen.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(-part.x * pictureWidth));
            screen.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(-part.y * pictureHeight));
        }

        private void OnPrepared(VideoPlayer prepared)
        {
            if (prepared != player || screen == null) return;
            player.Play();
            screen.style.backgroundImage = Background.FromRenderTexture(target);
            // The texture's pixels change under the panel: the screen is asked to draw again as the clip runs.
            repaint = screen.schedule.Execute(() => screen.MarkDirtyRepaint()).Every(RepaintMilliseconds);
        }

        public void Stop()
        {
            repaint?.Pause();
            repaint = null;
            screen?.UnregisterCallback<GeometryChangedEvent>(OnScreenResized);
            if (screen != null && target != null) screen.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            if (player != null)
            {
                player.prepareCompleted -= OnPrepared;
                player.Stop();
                player.targetTexture = null;
            }
            if (host != null) Object.Destroy(host);
            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
            }
            host = null;
            player = null;
            target = null;
        }
    }
}
