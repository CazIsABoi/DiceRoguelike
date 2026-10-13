using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

// Plays a looping, muted video on a UI RawImage (the throw-style cards).
// Desktop builds: drag the clip into Clip.
// WebGL builds can't play clips stored inside the build, so the file must be in Assets/StreamingAssets
// and its name goes in Streaming File (e.g. "throw_drag.mp4"). Fill in both and it works everywhere.
[RequireComponent(typeof(RawImage))]
public class VideoPreview : MonoBehaviour
{
    [SerializeField] private VideoClip clip;
    [SerializeField] private string streamingFile = "";       // e.g. "throw_drag.mp4" in Assets/StreamingAssets
    [SerializeField] private Vector2Int size = new Vector2Int(640, 360);

    private VideoPlayer player;
    private RenderTexture texture;

    private void Awake()
    {
        texture = new RenderTexture(size.x, size.y, 0);
        GetComponent<RawImage>().texture = texture;

        player = gameObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.isLooping = true;
        player.audioOutputMode = VideoAudioOutputMode.None;    // previews are silent
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = texture;
        player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;

        bool useUrl = Application.platform == RuntimePlatform.WebGLPlayer || clip == null;
        if (useUrl && !string.IsNullOrEmpty(streamingFile))
        {
            player.source = VideoSource.Url;

            // FIX: WebGL requires forward slashes for URLs. 
            // We use simple string combination to keep it web-safe.
            player.url = Application.streamingAssetsPath + "/" + streamingFile;
        }
        else
        {
            player.source = VideoSource.VideoClip;
            player.clip = clip;
        }
    }

    private void OnEnable() { if (player != null) player.Play(); }    // plays while the picker is open
    private void OnDisable() { if (player != null) player.Stop(); }

    private void OnDestroy() { if (texture != null) texture.Release(); }
}
