using TMPro;
using UnityEngine;

// One die in the settings menu's "YOUR DICE" list: a label and a grid of its faces.
// The prefab: a Vertical Layout Group with a TMP label and a child with a Grid Layout Group (the Faces).
public class DieShowcaseRow : MonoBehaviour
{
    public TMP_Text label;
    public Transform faces;
}
