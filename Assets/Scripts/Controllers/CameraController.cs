using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [SerializeField] private InputActionReference enemyLookInput;
    [SerializeField] private InputActionReference ItemLookInput;
    [SerializeField] private float lookDegrees = 15f;
    [SerializeField] private float timeToLook = .5f;
    public bool LookEnabled = true;
    private Quaternion restRotation;
    private Quaternion lookRotation;
    private Quaternion itemRotation;
    private Camera cam;
    private Coroutine rotationRoutine;

    private void Awake()
    {
        cam = Camera.main;
        restRotation = cam.transform.localRotation;
        lookRotation = restRotation * Quaternion.Euler(-lookDegrees, 0f, 0f);
        itemRotation = restRotation * Quaternion.Euler(lookDegrees, 0f, 0f);
    }

    private void OnEnable()
    {
        enemyLookInput.action.started += LookAtEnemy;
        enemyLookInput.action.canceled += StopLookAtEnemy;

        ItemLookInput.action.started += LookAtItems;
        ItemLookInput.action.canceled+= StopLookAtItems;
    }

    private void OnDisable()
    {
        enemyLookInput.action.started -= LookAtEnemy;
        enemyLookInput.action.canceled -= StopLookAtEnemy;

        ItemLookInput.action.started -= LookAtItems;
        ItemLookInput.action.canceled -= StopLookAtItems;
    }

    private void LookAtEnemy(InputAction.CallbackContext context)
    {
        PlayRotation(timeToLook, lookRotation);
    }
    private void StopLookAtEnemy(InputAction.CallbackContext context)
    {
        PlayRotation(timeToLook, restRotation);
    }

    private void StopLookAtItems(InputAction.CallbackContext context)
    {
        PlayRotation(timeToLook, restRotation);
    }

    private void LookAtItems(InputAction.CallbackContext context)
    {
        PlayRotation(timeToLook, itemRotation);
    }

    private void PlayRotation(float time, Quaternion target)
    {
        if (rotationRoutine != null) StopCoroutine(rotationRoutine);
        rotationRoutine = StartCoroutine(RotateCamera(time, target));
    }
    private IEnumerator RotateCamera(float time, Quaternion to)
    {
        if (!LookEnabled) yield break;
        float elapsed = 0f;
        Quaternion currentRotation = cam.transform.localRotation;

        float angleLeft = Quaternion.Angle(currentRotation, to);
        float scaledTime = time * (angleLeft / lookDegrees);
        while (elapsed < scaledTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / scaledTime;
            t = Mathf.SmoothStep(0f, 1f, t);
            cam.transform.localRotation = Quaternion.Slerp(currentRotation, to, t);
            yield return null;
        }
        cam.transform.localRotation = to;
    }
}
