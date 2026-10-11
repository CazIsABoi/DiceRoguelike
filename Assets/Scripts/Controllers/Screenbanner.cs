using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A big full-screen banner ("TARGET TABLE", "ACT 2") that slowly flashes, with CRT scanlines rolling over it.
//
// Setup:
//  1. On a Screen Space - Overlay canvas, make an empty child "Table Banner" and stretch it over the whole screen
//     (anchor preset: stretch/stretch, all offsets 0). Add this component.
//  2. Give it two TMP texts as children: a big Title in the middle and a smaller Subtitle under it. Drag them in.
//  3. That's it: the dark band and the scanlines are built by the code, and it starts hidden.
public class ScreenBanner : MonoBehaviour
{
    [ContextMenu("Test banner")]
    private void TestBanner() => StartCoroutine(Play("LOW TABLE", "CLOSEST TO 0 WINS", Color.cyan, 3f));
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text subtitle;          // optional

    [Header("Look")]
    [SerializeField] private float bandHeight = 260f;    // the dark strip behind the text (canvas pixels)
    [SerializeField, Range(0f, 1f)] private float bandDarkness = 0.55f;
    [SerializeField] private int lineSpacing = 4;        // screen pixels per scanline (one dark, the rest clear)
    [SerializeField, Range(0f, 1f)] private float lineDarkness = 0.45f;
    [SerializeField] private float lineScrollSpeed = 0.6f;   // how fast the lines roll down

    [Header("Flashing")]
    [SerializeField] private float fadeTime = 0.25f;
    [SerializeField] private float pulseSpeed = 2.2f;    // slow flashes per second (roughly)
    [SerializeField, Range(0f, 1f)] private float pulseLow = 0.45f;   // how dim it gets between flashes
    [SerializeField] private float jitter = 1.5f;        // a little sideways CRT wobble on the text, in pixels

    private CanvasGroup group;
    private RectTransform band;
    private RawImage lines;
    private Vector2 titleHome, subtitleHome;
    private Coroutine playing;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        BuildBand();
        BuildScanlines();
        if (title != null) titleHome = title.rectTransform.anchoredPosition;
        if (subtitle != null) subtitleHome = subtitle.rectTransform.anchoredPosition;
    }

    // Shows the banner for 'duration' seconds (fade in, slow flashing, fade out). Yield on it to wait.
    public IEnumerator Play(string titleText, string subtitleText, Color color, float duration)
    {
        if (playing != null) StopCoroutine(playing);
        playing = null;

        if (title != null) { title.text = titleText; title.color = color; }
        if (subtitle != null)
        {
            subtitle.text = subtitleText ?? "";
            subtitle.color = Color.Lerp(color, Color.white, 0.35f);
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;

            float fadeIn = Mathf.Clamp01(t / fadeTime);
            float fadeOut = Mathf.Clamp01((duration - t) / fadeTime);
            float pulse = Mathf.Lerp(pulseLow, 1f, (Mathf.Cos(t * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f);
            group.alpha = Mathf.Min(fadeIn, fadeOut) * pulse;

            if (lines != null)
            {
                Rect r = lines.uvRect;
                r.y += lineScrollSpeed * Time.deltaTime;
                lines.uvRect = r;
            }

            // Every so often the picture "slips" a pixel or two, like an old screen
            if (jitter > 0f && Random.value < 0.08f)
            {
                Vector2 slip = new Vector2(Random.Range(-jitter, jitter), 0f);
                if (title != null) title.rectTransform.anchoredPosition = titleHome + slip;
                if (subtitle != null) subtitle.rectTransform.anchoredPosition = subtitleHome + slip;
            }
            yield return null;
        }

        group.alpha = 0f;
        if (title != null) title.rectTransform.anchoredPosition = titleHome;
        if (subtitle != null) subtitle.rectTransform.anchoredPosition = subtitleHome;
    }

    // A dark strip across the middle of the screen, behind the text
    private void BuildBand()
    {
        GameObject go = new GameObject("Band", typeof(RectTransform), typeof(Image));
        band = (RectTransform)go.transform;
        band.SetParent(transform, false);
        band.SetAsFirstSibling();   // behind the texts
        band.anchorMin = new Vector2(0f, 0.5f);
        band.anchorMax = new Vector2(1f, 0.5f);
        band.sizeDelta = new Vector2(0f, bandHeight);
        band.anchoredPosition = Vector2.zero;

        Image image = go.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, bandDarkness);
        image.raycastTarget = false;
    }

    // Horizontal lines over the band and the text: a tiny repeating texture, one dark row and the rest clear
    private void BuildScanlines()
    {
        int rows = Mathf.Max(2, lineSpacing);
        Texture2D tex = new Texture2D(1, rows, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;
        for (int y = 0; y < rows; y++)
        {
            tex.SetPixel(0, y, y == 0 ? new Color(0f, 0f, 0f, lineDarkness) : Color.clear);
        }
        tex.Apply();

        GameObject go = new GameObject("Scanlines", typeof(RectTransform), typeof(RawImage));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.SetAsLastSibling();      // over the texts
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(0f, bandHeight);
        rt.anchoredPosition = Vector2.zero;

        lines = go.GetComponent<RawImage>();
        lines.texture = tex;
        lines.raycastTarget = false;
        lines.uvRect = new Rect(0f, 0f, 1f, bandHeight / rows);   // one texture repeat every 'rows' pixels
    }
}