using UnityEngine;

// Invisible walls around a felt area, so a hard throw bounces back like on a craps table instead of flying off
// (and getting popped back to the spawn point, which players read as "the game pulled my die back").
//
// Put it on an empty object at the centre of a felt bowl, then set Radius and Arc until the yellow gizmo
// sits on the edge of the felt. The walls are built when you press Play.
// They go on the Ignore Raycast layer, so your clicks go straight through them to the dice.
public class TableRails : MonoBehaviour
{
    [SerializeField] private float radius = 3f;
    [SerializeField, Range(10f, 360f)] private float arcDegrees = 180f;   // 180 = a half circle, 360 = a full ring
    [SerializeField] private float facing = 0f;                           // which way the middle of the arc points, in degrees (0 = this object's blue arrow)
    [SerializeField] private bool closeFlatSide = false;                  // a wall along the straight edge of the arc too
    [SerializeField, Range(4, 64)] private int segments = 20;
    [SerializeField] private float height = 2.5f;                         // taller than Lift Height, so a held die can't be dragged off the felt either
    [SerializeField] private float thickness = 0.3f;
    [SerializeField] private PhysicsMaterial material;                    // "Rail": a bit bouncy, low friction

    private const int IgnoreRaycastLayer = 2;

    private void Start()
    {
        GameObject root = new GameObject("Rails");
        root.layer = IgnoreRaycastLayer;
        root.transform.SetParent(transform, false);

        float start = facing - arcDegrees / 2f;
        float step = arcDegrees / segments;
        for (int i = 0; i < segments; i++)
        {
            AddWall(root.transform, Point(start + step * i), Point(start + step * (i + 1)));
        }
        if (closeFlatSide && arcDegrees < 360f)
        {
            AddWall(root.transform, Point(start + arcDegrees), Point(start));
        }
    }

    // A point on the wall's centre line, in local space
    private Vector3 Point(float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r)) * (radius + thickness / 2f);
    }

    private void AddWall(Transform parent, Vector3 a, Vector3 b)
    {
        GameObject wall = new GameObject("Rail", typeof(BoxCollider));
        wall.layer = IgnoreRaycastLayer;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = (a + b) / 2f + Vector3.up * (height / 2f);
        wall.transform.localRotation = Quaternion.LookRotation(b - a, Vector3.up);

        BoxCollider box = wall.GetComponent<BoxCollider>();
        box.size = new Vector3(thickness, height, Vector3.Distance(a, b) + thickness);   // the overlap closes the gaps between pieces
        box.sharedMaterial = material;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.matrix = transform.localToWorldMatrix;
        float start = facing - arcDegrees / 2f;
        float step = arcDegrees / segments;
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = Point(start + step * i);
            Vector3 b = Point(start + step * (i + 1));
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(a + Vector3.up * height, b + Vector3.up * height);
            Gizmos.DrawLine(a, a + Vector3.up * height);
        }
        if (closeFlatSide && arcDegrees < 360f) Gizmos.DrawLine(Point(start + arcDegrees), Point(start));
    }
}