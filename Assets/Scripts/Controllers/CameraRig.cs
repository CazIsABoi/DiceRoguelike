using System.Collections;
using UnityEngine;

public class CameraRig : MonoBehaviour
{
    [SerializeField] private float moveTime = 0.6f;

    public IEnumerator MoveTo(Transform view)
    {
        float elapsed = 0f;
        Vector3 startPos = transform.localPosition;
        Quaternion currentRotation = transform.localRotation;

        while (elapsed < moveTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / moveTime;
            t = Mathf.SmoothStep(0f, 1f, t);
            transform.localPosition = Vector3.Lerp(startPos, view.position, t);
            transform.localRotation = Quaternion.Slerp(currentRotation, view.rotation, t);
            yield return null;
        }
        transform.localPosition = view.position;
        transform.localRotation = view.rotation;
        yield break;
    }
}