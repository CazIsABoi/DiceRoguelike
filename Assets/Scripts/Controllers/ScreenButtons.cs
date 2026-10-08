using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ScreenButtons : MonoBehaviour
{
    [System.Serializable]
    public class ScreenButton
    {
        public RectTransform rect;   // the button area on the hidden canvas
        public Image image;          // optional: gets brighter on hover
        public UnityEvent onClick;
    }

    [SerializeField] private Camera cam;                 // the main camera, not the screen's render camera
    [SerializeField] private Collider screenCollider;    // MeshCollider on the screen mesh
    [SerializeField] private RectTransform canvasRect;   // root RectTransform of the hidden canvas
    [SerializeField] private InputActionReference click; // LMB
    [SerializeField] private InputActionReference mousePos;
    [SerializeField] private ScreenButton[] buttons;

    [Header("Hover")]
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.6f);
    [SerializeField] private Color hoverColor = Color.white;

    private void OnEnable()
    {
        click.action.Enable();
        mousePos.action.Enable();
    }

    private void Update()
    {
        if (click.action.WasPressedThisFrame())
        {
            Vector2 m = mousePos.action.ReadValue<Vector2>();
            bool didHit = Physics.Raycast(cam.ScreenPointToRay(m), out RaycastHit h);
            Debug.Log($"[Screen] mouse {m}  screen {Screen.width}x{Screen.height}  " +
                      $"hit {(didHit ? h.collider.name : "nothing")}  uv {(didHit ? h.textureCoord : Vector2.zero)}");
        }

        bool onScreen = TryGetCanvasPoint(out Vector3 world);

        foreach (ScreenButton b in buttons)
        {
            bool over = onScreen && b.rect.gameObject.activeInHierarchy && Contains(b.rect, world);
            if (b.image != null) b.image.color = over ? hoverColor : normalColor;
            if (over && click.action.WasPressedThisFrame()) b.onClick.Invoke();
        }
    }

    // One raycast per frame: where on the hidden canvas is the mouse?
    private bool TryGetCanvasPoint(out Vector3 world)
    {
        world = default;
        Ray ray = cam.ScreenPointToRay(mousePos.action.ReadValue<Vector2>());
        if (!Physics.Raycast(ray, out RaycastHit hit) || hit.collider != screenCollider) return false;

        Vector2 uv = hit.textureCoord;
        Vector2 size = canvasRect.rect.size;
        Vector2 local = new Vector2((uv.x - canvasRect.pivot.x) * size.x,
                                    (uv.y - canvasRect.pivot.y) * size.y);
        world = canvasRect.TransformPoint(local);
        return true;
    }

    private bool Contains(RectTransform rect, Vector3 world)
    {
        Vector2 inRect = rect.InverseTransformPoint(world);
        return rect.rect.Contains(inRect);
    }
}