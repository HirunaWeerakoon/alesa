using Alesa.Runtime;
using Alesa.Runtime.World;
using UnityEngine;

namespace Alesa.Agents.BlindMinion
{
    /// <summary>
    /// Draws what the minion "hears" in the Scene view, and in the Game view when Gizmos is on:
    ///   • spheres on graph nodes sized by how loud the last noise was there (the sound field)
    ///   • yellow line along the route it is retracing toward the sound
    ///   • orange circle: how unsure it is about where the noise came from
    ///   • state colour and perceived loudness above its head
    /// </summary>
    [RequireComponent(typeof(BlindMinionAgent))]
    public sealed class BlindMinionDebugView : MonoBehaviour
    {
        [SerializeField] private AIBootstrap ai;
        [SerializeField] private bool showSoundField = true;

        private BlindMinionAgent _agent;

        private void Awake() => _agent = GetComponent<BlindMinionAgent>();

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || _agent == null || _agent.Brain == null || ai == null || ai.Context == null) return;
            var brain = _agent.Brain;
            var graph = ai.Context.Graph;
            var head = transform.position + Vector3.up * 1.4f;

            Gizmos.color = ColourFor(brain.State);
            Gizmos.DrawSphere(head, 0.25f);

            if (showSoundField && brain.LastSound != null)
            {
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
                foreach (var node in graph.Nodes)
                {
                    float loudness = brain.LastSound.LoudnessAtNode(node.Id);
                    if (loudness <= 0f) continue;
                    Gizmos.DrawWireSphere(node.Position.ToUnity() + Vector3.up * 0.1f, Mathf.Clamp(loudness / 10f, 0.15f, 3f));
                }
            }

            Gizmos.color = Color.yellow;
            var from = transform.position;
            for (int i = brain.RouteIndex; i < brain.Route.Count; i++)
            {
                var to = graph[brain.Route[i]].Position.ToUnity();
                Gizmos.DrawLine(from, to);
                from = to;
            }
            if (brain.HasFinalTarget && brain.State != MinionState.Dormant)
            {
                Gizmos.DrawLine(from, brain.FinalTarget.ToUnity());
                Gizmos.DrawWireCube(brain.FinalTarget.ToUnity(), Vector3.one * 0.4f);
                if (brain.HearingErrorRadius > 0f) DrawCircle(brain.FinalTarget.ToUnity(), brain.HearingErrorRadius, new Color(1f, 0.55f, 0f));
            }

#if UNITY_EDITOR
            string heard = float.IsNegativeInfinity(brain.LastPerceivedLoudness) ? "-" : brain.LastPerceivedLoudness.ToString("0.0");
            UnityEditor.Handles.Label(head + Vector3.up * 0.4f, $"{brain.State}  heard {heard}  alert {brain.Alertness:P0}");
#endif
        }

        private static void DrawCircle(Vector3 centre, float radius, Color colour)
        {
            Gizmos.color = colour;
            const int segments = 32;
            var previous = centre + new Vector3(radius, 0.05f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var next = centre + new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        public static Color ColourFor(MinionState state)
        {
            switch (state)
            {
                case MinionState.Alerted: return new Color(1f, 0.6f, 0f);
                case MinionState.Investigating: return Color.yellow;
                case MinionState.Hunting: return Color.red;
                case MinionState.Searching: return new Color(0.6f, 0.4f, 1f);
                case MinionState.Attacking: return Color.magenta;
                default: return Color.gray;
            }
        }
    }
}
