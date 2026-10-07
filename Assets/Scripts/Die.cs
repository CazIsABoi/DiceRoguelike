using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Die : MonoBehaviour
{
    [SerializeField] private Transform[] sockets;   // Socket0..Socket5 in order
    [SerializeField] private int faceCount = 6;   // 4, 6, 8, 10, 12, 20 per prefab
    [SerializeField] private float faceScale = 1f;
    [SerializeField] private float faceDepth = 0.3f;
    [SerializeField] private DieDefinition testDefinition; // for testing without a spawner
    [SerializeField] private FaceDefinition blankFace; // For empty sockets
    private FaceDefinition[] currentFaces;
    private FaceView[] spawnedFaces;

    [Header("Face Swap")]
    [SerializeField] private float flyForce = 2f;
    [SerializeField] private float spin = 5f;
    [SerializeField] private float timeToSwitch = 0.5f;
    [SerializeField] private float slamDistance = 0.5f;   // how far out the new face starts
    [Header("Look")]
    [SerializeField] private bool usePips = true;      // D6: on, D8 and up: off
    [SerializeField] private FaceView numeralFace;     // every prefab gets one, sized for its faces


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
        spawnedFaces[index] = CreateFaceView(index, face);
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
        FaceView newFace = CreateFaceView(index, face);
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
    private FaceView CreateFaceView(int index, FaceDefinition face)
    {
        if (usePips && face.faceView != null) return Instantiate(face.faceView, sockets[index]);

        FaceView view = Instantiate(numeralFace, sockets[index]);
        view.SetLabel(LabelFor(face));
        return view;
    }

    private string LabelFor(FaceDefinition face)
    {
        if (face.type == FaceType.Operator) return GameController.OpSymbol(face.op);
        string text = face.number.ToString();
        if (face.number == 6 || face.number == 9) text += ".";   // so a tumbling 6 and 9 can be told apart
        return text;
    }

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

#if UNITY_EDITOR
    [ContextMenu("Generate Sockets From Mesh")]
    private void GenerateSockets()
    {
        MeshFilter mf = GetComponentInChildren<MeshFilter>();
        Vector3[] v = mf.sharedMesh.vertices;
        int[] tris = mf.sharedMesh.triangles;

        List<Vector3> normals = new List<Vector3>();   // one per flat face found
        List<Vector3> centers = new List<Vector3>();   // area-weighted sum of triangle centers
        List<float> areas = new List<float>();

        // 1. Sort every triangle into a "face": triangles pointing the same way belong together
        for (int t = 0; t < tris.Length; t += 3)
        {
            Vector3 a = v[tris[t]], b = v[tris[t + 1]], c = v[tris[t + 2]];
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float area = cross.magnitude * 0.5f;
            if (area < 0.000001f) continue;               // skip degenerate (zero-size) triangles

            Vector3 center = (a + b + c) / 3f;
            Vector3 n = cross.normalized;
            if (Vector3.Dot(n, center) < 0f) n = -n;      // dice are convex: outward = away from the middle

            int found = -1;
            for (int i = 0; i < normals.Count; i++)
            {
                if (Vector3.Dot(normals[i], n) > 0.99f) { found = i; break; }
            }

            if (found >= 0)
            {
                centers[found] += center * area;
                areas[found] += area;
            }
            else
            {
                normals.Add(n);
                centers.Add(center * area);
                areas.Add(area);
            }
        }

        // 2. Remove old sockets, so running it twice doesn't create duplicates
        if (sockets != null)
        {
            foreach (Transform old in sockets)
            {
                if (old != null) DestroyImmediate(old.gameObject);
            }
        }

        // 3. Keep the 'faceCount' biggest faces. Bevels are always smaller, so they get cut
        List<int> order = new List<int>();
        for (int i = 0; i < normals.Count; i++) order.Add(i);
        order.Sort((x, y) => areas[y].CompareTo(areas[x]));   // biggest first
        if (order.Count > faceCount) order.RemoveRange(faceCount, order.Count - faceCount);

        List<Transform> newSockets = new List<Transform>();
        foreach (int i in order)
        {
            GameObject s = new GameObject("Socket" + newSockets.Count);
            s.transform.SetParent(transform);
            s.transform.position = mf.transform.TransformPoint(centers[i] / areas[i]);
            s.transform.rotation = Quaternion.FromToRotation(Vector3.up, mf.transform.TransformDirection(normals[i]));
            s.transform.localScale = new Vector3(faceScale, faceDepth, faceScale);
            newSockets.Add(s.transform);
        }

        sockets = newSockets.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"Generated {sockets.Length} sockets");
    }
#endif
}