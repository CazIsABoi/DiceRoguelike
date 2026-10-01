using UnityEngine;

public class DiceSlot : MonoBehaviour
{
    [SerializeField] private FaceType acceptableFace;

    public FaceType AcceptableFace => acceptableFace;
    public Die CurrentDie { get; private set; }

    public bool IsEmpty => CurrentDie == null;

    public bool CanAccept(Die die)
    {

        if (die.GetTopFace().type == acceptableFace && IsEmpty) return true;
        else return false;
    }

    public void Place(Die die)
    {
        CurrentDie = die;
    }

    public void Clear()
    {
        CurrentDie = null;
    }
}

