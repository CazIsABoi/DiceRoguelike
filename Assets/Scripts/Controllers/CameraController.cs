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

    [Header("Focus")]
    [SerializeField] private float kickTime = 0.15f;
    [SerializeField, Range(0f, 1f)] private float kickStrength = 0.35f;   // scales every Kick in one place
    private Coroutine kickRoutine;
    private Vector3 returnPos;
    private Quaternion returnRot;
    private bool focused;
    private float restFov;


    private void Awake()
    {
        cam = Camera.main;
        restFov = cam.fieldOfView;
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

    public IEnumerator FocusOn(Transform shot, float time)
    {
        if (!focused)   // only remember where we came from the FIRST time: shot → shot keeps the original way home
        {
            returnPos = cam.transform.position;
            returnRot = cam.transform.rotation;
            focused = true;
        }
        LookEnabled = false;   // the enemy/items look keys can't yank the camera mid-zoom
        if (rotationRoutine != null) StopCoroutine(rotationRoutine);
        yield return MoveCamera(shot.position, shot.rotation, time);
    }

    public IEnumerator FocusBack(float time)
    {
        if (!focused) yield break;
        yield return MoveCamera(returnPos, returnRot, time);
        focused = false;
        LookEnabled = true;
    }

    private IEnumerator MoveCamera(Vector3 toPos, Quaternion toRot, float time)
    {
        Vector3 fromPos = cam.transform.position;
        Quaternion fromRot = cam.transform.rotation;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / time);   // slow start, slow stop
            cam.transform.SetPositionAndRotation(Vector3.Lerp(fromPos, toPos, k), Quaternion.Slerp(fromRot, toRot, k));
            yield return null;
        }
        cam.transform.SetPositionAndRotation(toPos, toRot);
    }

    // A quick zoom-in-and-back on the field of view: a camera-sized punch
    public void Kick(float degrees = 3f)
    {
        if (kickRoutine != null) StopCoroutine(kickRoutine);
        kickRoutine = StartCoroutine(FovKick(degrees));
    }

    private IEnumerator FovKick(float degrees)
    {
        degrees *= kickStrength;
        for (float t = 0f; t < kickTime; t += Time.unscaledDeltaTime)   // unscaled: the hit-stop no longer freezes it zoomed in
        {
            float k = t / kickTime;
            cam.fieldOfView = restFov - degrees * (1f - k) * (1f - k);   // snaps in, eases out
            yield return null;
        }
        cam.fieldOfView = restFov;
    }
    public void SetLook(bool on)
    {
        LookEnabled = on;
        if (!on && rotationRoutine != null) StopCoroutine(rotationRoutine);
        if (!on) cam.transform.localRotation = restRotation;   // straight back to the normal view
    }
}
