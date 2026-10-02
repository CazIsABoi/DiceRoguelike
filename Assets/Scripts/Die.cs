using UnityEngine;

public class Die : MonoBehaviour
{
    [SerializeField] private Transform[] sockets;   // Socket0..Socket5 in order
    [SerializeField] private DieDefinition testDefinition; // for testing without a spawner
    [SerializeField] private FaceDefinition blankFace; // For empty sockets
    private FaceDefinition[] currentFaces;
    private FaceView[] spawnedFaces;


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

    public void SetFace(int index, FaceDefinition face)
    {
        if (spawnedFaces[index] != null)
        {
            Destroy(spawnedFaces[index].gameObject);
        }
        FaceView newFace = Instantiate(face.faceView, sockets[index]); 
        currentFaces[index] = face;
        spawnedFaces[index] = newFace;
    }

    public FaceDefinition GetTopFace()
    {
        Vector3 worldDir = Vector3.up;
        int bestIndex = 0;
        float bestDot = -Mathf.Infinity;

        for (int i = 0; i < sockets.Length; i++)
        {
            Vector3 worldAxis = sockets[i].up;
            float dot = Vector3.Dot(worldAxis, worldDir);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestIndex = i;
            }
        }
        return currentFaces[bestIndex];
    }

    public void LogTopFace()
    {
        FaceDefinition topFace = GetTopFace();

        print(topFace.number);
        print(topFace.op);
    }
}
