using System.Collections;
using UnityEngine;

public class Die : MonoBehaviour
{
    [SerializeField] private Transform[] sockets;   // Socket0..Socket5 in order
    [SerializeField] private DieDefinition testDefinition; // for testing without a spawner
    [SerializeField] private FaceDefinition blankFace; // For empty sockets
    private FaceDefinition[] currentFaces;
    private FaceView[] spawnedFaces;

    [Header("Face Swap")]
    [SerializeField] private float flyForce = 2f;
    [SerializeField] private float spin = 5f;
    [SerializeField] private float timeToSwitch = 0.5f;
    [SerializeField] private float slamDistance = 0.5f;   // how far out the new face starts

    // ---------- Setup ----------

    public void Initialize(DieDefinition definition)
    {
        currentFaces = new FaceDefinition[sockets.Length];
        spawnedFaces = new FaceView[sockets.Length];
        for (int i = 0; i < sockets.Length; i++)
        {
            if (i < definition.faceDefinitions.Length && definition.faceDefinitions[i] != null)
            {
                SetFace(i, definition.faceDefinitions[i]);
            }
            else
            {
                SetFace(i, blankFace);
            }
        }
    }

    // ---------- Faces ----------

    // Instant, no animation. Used by Initialize (and anything else that just needs the data)
    public void SetFace(int index, FaceDefinition face)
    {
        if (spawnedFaces[index] != null) Destroy(spawnedFaces[index].gameObject);
        spawnedFaces[index] = Instantiate(face.faceView, sockets[index]);
        currentFaces[index] = face;
    }

    // Animated swap, only used by the reward
    public IEnumerator SwapFace(int index, FaceDefinition face)
    {
        // 1. Old face pops off and falls away
        FaceView oldFace = spawnedFaces[index];
        if (oldFace != null)
        {
            oldFace.transform.SetParent(null);
            Rigidbody rb = oldFace.gameObject.AddComponent<Rigidbody>();
            Vector3 dir = (sockets[index].up + Vector3.up).normalized;
            rb.AddForce(dir * flyForce, ForceMode.Impulse);
            rb.AddTorque(Random.onUnitSphere * spin, ForceMode.Impulse);
            Destroy(oldFace.gameObject, 2f);
        }

        // 2. Update the data right away, so it's correct even if the animation gets interrupted
        FaceView newFace = Instantiate(face.faceView, sockets[index]);
        currentFaces[index] = face;
        spawnedFaces[index] = newFace;

        // 3. Slam it in, in socket space, so it follows the die
        Vector3 target = newFace.transform.localPosition;
        Vector3 start = target + Vector3.up * slamDistance;   // local up = outward from the die
        newFace.transform.localPosition = start;

        float elapsed = 0f;
        while (elapsed < timeToSwitch)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / timeToSwitch;
            newFace.transform.localPosition = Vector3.Lerp(start, target, t * t);   // speeds up = slam
            yield return null;
        }
        newFace.transform.localPosition = target;

        // 4. Impact: particles, Shake, Punch, thud sound go here
    }

    public FaceDefinition[] GetCurrentFaces() { return currentFaces; }

    // ---------- Orientation ----------

    // Index of the socket pointing most along 'direction'
    public int GetSocketFacing(Vector3 direction)
    {
        int bestIndex = 0;
        float bestDot = -Mathf.Infinity;

        for (int i = 0; i < sockets.Length; i++)
        {
            float dot = Vector3.Dot(sockets[i].up, direction);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    public Transform GetTopSocket() => sockets[GetSocketFacing(Vector3.up)];

    public FaceDefinition GetTopFace() => currentFaces[GetSocketFacing(Vector3.up)];

    public bool IsFlat(float threshold)
    {
        Transform top = GetTopSocket();
        return Vector3.Dot(top.up, Vector3.up) >= threshold;
    }

    public void LogTopFace()
    {
        FaceDefinition topFace = GetTopFace();
        print(topFace.number);
        print(topFace.op);
    }

    public FaceView GetFaceView(int index) { return spawnedFaces[index]; }
}