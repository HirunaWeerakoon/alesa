using UnityEngine;

// Keep hospital illumination, but budget the expensive realtime local-light shadows.
[DisallowMultipleComponent]
public class PlayerLightingBudget : MonoBehaviour
{
    [Min(0)] public int nearbyShadowLights = 4;
    [Min(1f)] public float shadowRadius = 28f;
    [Min(0.1f)] public float refreshSeconds = 0.3f;
    public int maximumCustomShadowResolution = 1024;
    private Light[] lights;
    private LightShadows[] shadows;
    private int[] resolutions;
    private bool[] chosen;
    private bool[] managed;
    private float nextRefresh;

    private void Start()
    {
        lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        shadows = new LightShadows[lights.Length];
        resolutions = new int[lights.Length];
        chosen = new bool[lights.Length];
        managed = new bool[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            shadows[i] = lights[i].shadows; resolutions[i] = lights[i].shadowCustomResolution;
            managed[i] = shadows[i] != LightShadows.None && lights[i].type != LightType.Directional && lights[i].GetComponent<Flashlight>() == null;
        }
        Refresh();
    }

    private bool Managed(int i)
    {
        var light = lights[i];
        return light != null && managed[i];
    }

    private void Update()
    {
        if (lights == null || Time.timeScale <= 0 || Time.unscaledTime < nextRefresh) return;
        Refresh();
    }

    private void Refresh()
    {
        nextRefresh = Time.unscaledTime + refreshSeconds;
        System.Array.Clear(chosen, 0, chosen.Length);
        float limit = shadowRadius * shadowRadius;
        for (int slot = 0; slot < nearbyShadowLights; slot++)
        {
            int best = -1; float score = limit;
            for (int i = 0; i < lights.Length; i++)
            {
                if (chosen[i] || !Managed(i) || !lights[i].isActiveAndEnabled) continue;
                float distance = (lights[i].transform.position - transform.position).sqrMagnitude;
                if (distance >= score) continue;
                score = distance; best = i;
            }
            if (best < 0) break;
            chosen[best] = true;
        }
        for (int i = 0; i < lights.Length; i++)
        {
            if (!Managed(i)) continue;
            var desired = chosen[i] ? shadows[i] : LightShadows.None;
            if (lights[i].shadows != desired) lights[i].shadows = desired;
            if (resolutions[i] > maximumCustomShadowResolution)
                lights[i].shadowCustomResolution = maximumCustomShadowResolution;
        }
    }

    private void OnDisable()
    {
        if (lights == null) return;
        for (int i = 0; i < lights.Length; i++)
            if (lights[i] != null) { lights[i].shadows = shadows[i]; lights[i].shadowCustomResolution = resolutions[i]; }
    }
}
