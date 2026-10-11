using UnityEngine;

// Trauma-based screen shake (guide section 6). The idea is from Squirrel Eiserloh's GDC talk
// "Math for Game Programmers: Juicing Your Cameras With Math". Worth the 20 minutes.
//
// Put this on its OWN object between the rig and the camera:  Rig (CameraRig) > Shaker (this) > Main Camera.
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }   // anything can call CameraShake.Instance.AddTrauma(0.3f)

    [SerializeField] private float maxOffset = 0.3f;    // how far it moves at full trauma (world units; your dice are 2 wide)
    [SerializeField] private float maxAngle = 2.5f;     // how far it tilts at full trauma (degrees)
    [SerializeField] private float frequency = 20f;     // how fast the noise moves
    [SerializeField] private float recovery = 1.5f;     // trauma lost per second

    private float trauma;
    private float seed;

    private void Awake()
    {
        Instance = this;
        seed = Random.value * 100f;   // a different shake pattern every time you play
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;   // don't leave a dead object behind when the scene reloads
    }

    public void AddTrauma(float amount)
    {
        trauma = Mathf.Clamp01(trauma + amount);
    }

    private void LateUpdate()
    {
        if (trauma <= 0f)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            return;
        }

        float shake = trauma * trauma;   // the curve: 0.5 trauma only shakes at 0.25, 1 shakes at 1
        float time = Time.unscaledTime * frequency;

        float x = Noise(0, time);
        float y = Noise(1, time);
        float roll = Noise(2, time);

        transform.localPosition = new Vector3(x, y, 0f) * maxOffset * shake;
        transform.localRotation = Quaternion.Euler(0f, 0f, roll * maxAngle * shake);

        trauma = Mathf.Max(0f, trauma - recovery * Time.unscaledDeltaTime);
    }

    // Smooth noise from -1 to 1. Each axis reads its own row of the noise (seed + n), so they move independently.
    private float Noise(int n, float time) => Mathf.PerlinNoise(seed + n, time) * 2f - 1f;

    [ContextMenu("Test big shake")]
    private void TestBig() => AddTrauma(1f);

    [ContextMenu("Test small shake")]
    private void TestSmall() => AddTrauma(0.3f);
}