using UnityEngine;

// Pushes the camera in on the monitor while a big number counts (CountShow sets Target), then lets go.
// It only nudges the field of view on top of whatever CameraController does, and notices when something
// else (a kick, a focus move) has set the field of view this frame.
[RequireComponent(typeof(Camera))]
public class CountZoom : MonoBehaviour
{
    [SerializeField] private float zoomIn = 0.35f;    // seconds to get most of the way in
    [SerializeField] private float zoomOut = 0.2f;    // seconds to snap back out

    public float Target { get; set; }                 // degrees of field of view to take off. CountShow sets this

    private Camera cam;
    private float applied;                            // how much we took off last frame
    private float written;                            // what we set the field of view to last frame

    private void Awake()
    {
        cam = GetComponent<Camera>();
        written = cam.fieldOfView;
    }

    private void LateUpdate()
    {
        // The field of view without our zoom. If someone else wrote it since last frame, theirs is the base.
        float baseFov = Mathf.Approximately(cam.fieldOfView, written) ? written + applied : cam.fieldOfView;

        float time = Target > applied ? zoomIn : zoomOut;
        applied = Mathf.Lerp(applied, Target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 3f / Mathf.Max(0.01f, time)));

        cam.fieldOfView = baseFov - applied;
        written = cam.fieldOfView;
    }
}