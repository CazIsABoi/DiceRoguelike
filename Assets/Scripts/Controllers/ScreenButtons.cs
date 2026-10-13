using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ScreenButtons : MonoBehaviour
{
    [System.Serializable]
    public class ScreenButton
    {
        public RectTransform rect;   // the button area on the hidden canvas
        public Image image;          // optional: gets brighter on hover
        public UnityEvent onClick;
        public UnityEvent onHoverEnter;   // optional, e.g. MainMenu.Describe(Easy)
        public UnityEvent onHoverExit;    // optional, e.g. MainMenu.Describe(None)
        [HideInInspector] public bool hovered;
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

    [Header("Clicks")]
    [SerializeField] private float clickCooldown = 0.3f;   // seconds after a click (or a panel change) when clicks are ignored

    private float clicksAllowedAt;

    private void OnEnable()
    {
        click.action.Enable();
        mousePos.action.Enable();
        clicksAllowedAt = Time.unscaledTime + clickCooldown;   // no click lands the moment the menu appears
    }

    private void Update()
    {
        bool onScreen = TryGetCanvasPoint(out Vector3 world);
        bool clicked = click.action.WasPressedThisFrame() && Time.unscaledTime >= clicksAllowedAt;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) { onScreen = false; clicked = false; }
        ScreenButton toClick = null;

        foreach (ScreenButton b in buttons)
        {
            bool over = onScreen && b.rect.gameObject.activeInHierarchy && Contains(b.rect, world);
            if (b.image != null) b.image.color = over ? hoverColor : normalColor;

            if (over != b.hovered)
            {
                b.hovered = over;
                if (over) b.onHoverEnter?.Invoke(); else b.onHoverExit?.Invoke();
            }
            if (over && clicked && toClick == null) toClick = b;   // only the first button under the mouse
        }

        // Clicked after the loop, so a button that opens another panel can't also click a button on that panel
        if (toClick != null)
        {
            clicksAllowedAt = Time.unscaledTime + clickCooldown;
            toClick.onClick.Invoke();
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