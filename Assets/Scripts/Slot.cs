using UnityEngine;

public class DiceSlot : MonoBehaviour
{
    [SerializeField] private FaceType acceptableFace;
    [SerializeField] private Transform outline;
    private MeshRenderer renderer;
    private Color startingColor;

    public FaceType AcceptableFace => acceptableFace;
    public Die CurrentDie { get; private set; }

    public bool IsEmpty => CurrentDie == null;

    private void Awake()
    {
        renderer = GetComponentInChildren<MeshRenderer>();
        startingColor = renderer.material.color;
    }

    public bool CanAccept(Die die)
    {

        if (die.GetTopFace().type == acceptableFace && IsEmpty) return true;
        else return false;
    }

    public void Place(Die die)
    {
        CurrentDie = die;
        renderer.material.color = Color.white;
    }

    public void Clear()
    {
        CurrentDie = null;
        renderer.material.color = startingColor;
    }

    public void Setup(FaceType type)
    {
        acceptableFace = type;
        if (type == FaceType.Operator)
        {
            outline.Rotate(transform.up, 45f, Space.World);
        }
    }
}

