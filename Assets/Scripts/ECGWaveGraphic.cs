using UnityEngine;
using UnityEngine.UI;

/// <summary>Small scrolling ECG-style UI mesh. All mesh data uses VertexHelper's reusable buffers.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ECGWaveGraphic : MaskableGraphic
{
    [SerializeField, Range(24, 160)] private int samples = 80;
    [SerializeField, Min(0.5f)] private float thickness = 1.5f;
    private float beatsPerMinute = 65f, phase;
    private bool erratic;
    [SerializeField, Range(10, 60)] private int refreshRate = 30;
    private float meshElapsed;

    /// <summary>Sets the visual heart rate and tint; zero BPM draws a flatline.</summary>
    public void SetStatus(float bpm, Color tint, bool irregular)
    {
        beatsPerMinute = Mathf.Max(0f, bpm); color = tint; erratic = irregular;
        SetVerticesDirty();
    }

    private void Update()
    {
        if (Time.deltaTime <= 0f || beatsPerMinute <= 0f) return;
        phase = Mathf.Repeat(phase + Time.deltaTime * beatsPerMinute / 60f, 1f);
        meshElapsed += Time.deltaTime;
        if (meshElapsed < 1f / refreshRate) return;
        meshElapsed = 0f;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = GetPixelAdjustedRect();
        int count = Mathf.Clamp(samples, 24, 160);
        Vector2 previous = Point(0f, rect);
        for (int i = 1; i <= count; i++)
        {
            Vector2 next = Point((float)i / count, rect);
            Vector2 offset = new Vector2(-(next - previous).y, (next - previous).x).normalized * thickness * 0.5f;
            int start = helper.currentVertCount;
            AddVertex(helper, previous - offset); AddVertex(helper, previous + offset);
            AddVertex(helper, next + offset); AddVertex(helper, next - offset);
            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
            previous = next;
        }
    }

    private Vector2 Point(float x, Rect rect)
    {
        float t = Mathf.Repeat(x * 2f + phase, 1f);
        float wave = beatsPerMinute <= 0f ? 0f :
            0.1f * Peak(t, 0.2f, 0.07f) - 0.2f * Peak(t, 0.38f, 0.035f) +
            Peak(t, 0.43f, 0.035f) - 0.35f * Peak(t, 0.49f, 0.04f) + 0.2f * Peak(t, 0.7f, 0.12f);
        if (erratic && beatsPerMinute > 0f) wave += Mathf.Sin((x * 18f + phase) * 12f) * 0.08f;
        return new Vector2(rect.xMin + x * rect.width, rect.center.y + wave * rect.height * 0.65f);
    }
    private static float Peak(float t, float center, float width) { return Mathf.Max(0f, 1f - Mathf.Abs(t - center) / width); }
    private void AddVertex(VertexHelper helper, Vector2 position)
    {
        var vertex = UIVertex.simpleVert;
        vertex.position = position; vertex.color = color;
        helper.AddVert(vertex);
    }
}
