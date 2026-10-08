using TMPro;
using UnityEngine;

public class VersionLabel : MonoBehaviour
{
    private void Start()
    {
        GetComponent<TMP_Text>().text = "v" + Application.version + " demo";
    }
}