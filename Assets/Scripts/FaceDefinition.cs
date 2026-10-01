using UnityEngine;

public enum FaceType { Number, Operator }
public enum Operator { Add, Subtract, Multiply, Divide }

[CreateAssetMenu(fileName = "NewFace", menuName = "Dice/Face")]
public class FaceDefinition : ScriptableObject
{
    public FaceType type;
    public int number;
    public Operator op;
    public FaceView faceView;
}