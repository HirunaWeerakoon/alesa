using UnityEngine;

[RequireComponent(typeof(InteractablePickup))]
public class RevolverPickupVisual : MonoBehaviour
{
    public GameObject modelPrefab;
    public Material modelMaterial;
    public Vector3 modelAngles = Vector3.zero;
    public float modelLength = 0.34f;

    private void Awake()
    {
        if (modelPrefab == null) return;
        var placeholder = GetComponent<MeshRenderer>();
        if (placeholder != null) placeholder.enabled = false;
        RevolverModel.Create(modelPrefab, transform, modelAngles, modelLength, modelMaterial, false);
    }
}
