using UnityEngine;

// Resolve imported furniture bounds once, after pickup model creation in Awake.
public class PickupCabinetPlacement : MonoBehaviour
{
    public Transform cabinet;
    public string shelfName = "SheetRack";
    public Vector3 surfaceOffset;
    public Vector3 restingAngles = new Vector3(0, 0, 90);
    public float surfaceGap = 0.003f;

    private void Start()
    {
        if (cabinet == null) return;
        Renderer surface = null;
        foreach (var renderer in cabinet.GetComponentsInChildren<Renderer>())
            if (renderer.name == shelfName) { surface = renderer; break; }
        if (surface == null) { Debug.LogWarning("Pickup cabinet shelf not found: " + shelfName, this); return; }
        transform.rotation = Quaternion.Euler(restingAngles);
        var top = surface.bounds;
        transform.position = new Vector3(top.center.x + surfaceOffset.x, top.max.y + surfaceGap, top.center.z + surfaceOffset.z);
        float bottom = float.PositiveInfinity;
        foreach (var renderer in GetComponentsInChildren<Renderer>())
            if (renderer.enabled) bottom = Mathf.Min(bottom, renderer.bounds.min.y);
        if (!float.IsPositiveInfinity(bottom)) transform.position += Vector3.up * (top.max.y + surfaceGap - bottom);
    }
}
