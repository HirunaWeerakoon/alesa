using UnityEngine;

public static class RevolverModel
{
    public static GameObject Create(GameObject prefab, Transform parent, Vector3 angles, float length, Material material, bool firstPerson)
    {
        var model = Object.Instantiate(prefab, parent, false);
        model.name = "Revolver model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(angles);
        model.transform.localScale = Vector3.one;
        foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return model;
        // Measure in parent space to avoid dependence on spawn rotation or position.
        var bounds = new Bounds();
        bool first = true;
        foreach (var renderer in renderers)
        {
            var local = renderer.localBounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var point = parent.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
            if (material != null)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
            if (firstPerson) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        float scale = longest > 0.0001f ? length / longest : 1f;
        model.transform.localScale = Vector3.one * scale;
        model.transform.localPosition = -bounds.center * scale;
        return model;
    }
}
