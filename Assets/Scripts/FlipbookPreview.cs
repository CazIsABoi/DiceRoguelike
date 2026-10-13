using UnityEngine;
using UnityEngine.UI;

// Plays a list of sprites on a UI Image, on a loop: a tiny "video" that works in every build, WebGL included
// (a VideoPlayer can't play video files that are inside a WebGL build).
//
// Making the frames: record 2 to 4 seconds of each throw style (OBS is fine), then turn the clip into pictures,
// for example on ezgif.com ("Video to JPG/PNG") or with ffmpeg:
//     ffmpeg -i drag.mp4 -vf "fps=12,scale=320:-1" drag_%02d.png
// Import the pictures, set Texture Type to "Sprite (2D and UI)", select them all and drag them onto Frames.
[RequireComponent(typeof(Image))]
public class FlipbookPreview : MonoBehaviour
{
    [SerializeField] private Sprite[] frames;
    [SerializeField] private float framesPerSecond = 12f;
    [SerializeField] private float pauseAtEnd = 0.6f;    // a short hold on the last frame, so the result can be read

    private Image image;
    private float time;

    private void Awake() => image = GetComponent<Image>();

    private void OnEnable() => time = 0f;

    private void Update()
    {
        if (frames == null || frames.Length == 0) return;
        time += Time.unscaledDeltaTime;                 // unscaled: still plays if a menu pauses the game
        float loop = frames.Length / framesPerSecond + pauseAtEnd;
        float t = time % loop;
        int i = Mathf.Min(frames.Length - 1, (int)(t * framesPerSecond));
        if (image.sprite != frames[i]) image.sprite = frames[i];
    }
}
