using System.Collections.Generic;
using UnityEngine;
using Alesa.Core;
using Alesa.Core.Graph;
using Alesa.Runtime;
using Alesa.Runtime.World;

namespace Alesa.Runtime
{
    /// <summary>
    /// Gives Alesa something to say during the final chase: which way to turn at junctions on the way to the
    /// exit door, that you are going the wrong way, that the route is blocked by the boss, and that you have
    /// reached the exit. It works on the hospital's maze graph, so it needs no extra setup in the scene.
    /// </summary>
    /// <remarks>
    /// This only decides WHEN a line is warranted. How often she actually speaks is rationed by the speaker's
    /// dialogue budget, so most junctions pass in silence.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class AlesaMazeGuide : MonoBehaviour
    {
        [Tooltip("Left empty, found on this object.")]
        public AlesaVoiceSpeaker speaker;

        [Header("Junction calls")]
        [Tooltip("A junction is called out only when it is between these distances ahead along the route (m).")]
        public Vector2 callDistance = new Vector2(3f, 10f);
        [Tooltip("Turns smaller than this (degrees) are 'keep going straight'.")]
        [Min(5f)] public float straightAngle = 30f;

        [Header("Wrong way")]
        [Tooltip("Seconds of walking away from the route before she tells you.")]
        [Min(0.5f)] public float wrongWaySeconds = 3f;

        [Header("Blocked by the boss")]
        [Tooltip("The boss within this distance (m) of the route ahead counts as blocking it.")]
        [Min(1f)] public float blockedRadius = 7f;

        [Header("The exit")]
        [Tooltip("Within this distance (m) of the exit door she says you made it.")]
        [Min(1f)] public float arriveRadius = 14f;

        [Header("Debug (read-only)")]
        [Tooltip("What the guide decided on its last check. Watch it in Play mode to see why she is quiet.")]
        [SerializeField] private string status = "";

        /// <summary>What the guide decided on its last check.</summary>
        public string Status => status;

        private FinalBossEncounter _encounter;
        private FinalExitDoor _exit;
        private PossessedBoss _boss;
        private Transform _player;
        private MazeGraph _graph;
        private int _exitNode = -1;
        private readonly HashSet<int> _calledJunctions = new HashSet<int>();
        private float _nextTick, _wrongSince = -1f;
        private bool _blockedHandled, _arrivedSpoken;
        private Vector3 _lastPlayerPosition;

        private void Awake()
        {
            if (speaker == null) speaker = GetComponent<AlesaVoiceSpeaker>();
        }

        private void Update()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 0.4f;
            if (speaker == null) { status = "no speaker"; return; }
            if (!Resolve()) { status = "waiting: encounter=" + (_encounter != null) + " exit=" + (_exit != null) + " player=" + (_player != null) + " graph=" + (_graph != null) + " exitNode=" + _exitNode; return; }
            if (!_encounter.EncounterActive || _encounter.Escaped) { status = "chase not running"; return; }

            Vector3 playerPos = _player.position;
            int playerNode = _graph.NearestNode(playerPos.ToCore());
            if (playerNode < 0) { status = "player off the graph"; return; }

            // The exit: always said, once.
            if (!_arrivedSpoken && Vector3.Distance(playerPos, _exit.transform.position) <= arriveRadius)
            {
                _arrivedSpoken = true;
                status = "exit reached, spoken=" + speaker.PlayCue(VoiceCueId.DestinationReached);
                return;
            }

            var route = GraphSearch.AStar(_graph, playerNode, _exitNode);
            if (!route.Found || route.Nodes.Count < 2) { status = "no route from node " + playerNode + " to " + _exitNode; return; }
            status = "following route, " + route.Nodes.Count + " nodes to go";

            if (CheckBlocked(route.Nodes, playerNode)) return;
            if (CheckWrongWay(playerPos, route.Nodes)) return;
            CheckJunction(route.Nodes, playerPos);
        }

        private bool Resolve()
        {
            if (_encounter == null) _encounter = FindFirstObjectByType<FinalBossEncounter>();
            if (_exit == null) _exit = FindFirstObjectByType<FinalExitDoor>();
            if (_boss == null) _boss = FindFirstObjectByType<PossessedBoss>();
            if (_player == null && _encounter != null && _encounter.player != null) _player = _encounter.player.transform;
            var bootstrap = AIBootstrap.Instance;
            if (_graph == null && bootstrap != null && bootstrap.IsInitialized) _graph = bootstrap.Graph;
            if (_graph != null && _exitNode < 0 && _exit != null) _exitNode = _graph.NearestNode(_exit.transform.position.ToCore());
            return _encounter != null && _exit != null && _player != null && _graph != null && _exitNode >= 0;
        }

        // The next junction on the way, called once, as left, right or straight on.
        private void CheckJunction(IReadOnlyList<int> nodes, Vector3 playerPos)
        {
            float travelled = 0f;
            Vector3 previous = playerPos;
            for (int i = 1; i < nodes.Count - 1; i++)
            {
                Vector3 here = _graph[nodes[i]].Position.ToUnity();
                travelled += Vector3.Distance(previous, here);
                previous = here;
                if (travelled > callDistance.y) return;
                if (travelled < callDistance.x || _graph[nodes[i]].Neighbours.Count <= 2) continue;
                if (!_calledJunctions.Add(nodes[i])) continue;

                Vector3 incoming = here - _graph[nodes[i - 1]].Position.ToUnity();
                Vector3 outgoing = _graph[nodes[i + 1]].Position.ToUnity() - here;
                incoming.y = outgoing.y = 0f;
                if (incoming.sqrMagnitude < 0.01f || outgoing.sqrMagnitude < 0.01f) return;

                float angle = Vector3.SignedAngle(incoming, outgoing, Vector3.up); // positive = clockwise = right
                VoiceCueId cue = Mathf.Abs(angle) < straightAngle ? VoiceCueId.ContinueStraight
                               : angle > 0f ? VoiceCueId.TurnRight : VoiceCueId.TurnLeft;
                status = "junction " + nodes[i] + ": " + cue + " spoken=" + speaker.PlayCue(cue);
                return;
            }
        }

        // Walking steadily away from the next node on the route.
        private bool CheckWrongWay(Vector3 playerPos, IReadOnlyList<int> nodes)
        {
            Vector3 moved = playerPos - _lastPlayerPosition;
            _lastPlayerPosition = playerPos;
            moved.y = 0f;
            Vector3 toNext = _graph[nodes[1]].Position.ToUnity() - playerPos;
            toNext.y = 0f;
            bool away = moved.magnitude > 0.48f && toNext.sqrMagnitude > 1f && Vector3.Dot(moved.normalized, toNext.normalized) < -0.3f;
            if (!away) { _wrongSince = -1f; return false; }
            if (_wrongSince < 0f) _wrongSince = Time.time;
            if (Time.time - _wrongSince < wrongWaySeconds) return false;
            _wrongSince = Time.time + 20f; // don't re-trigger straight away; the budget also limits repeats
            return speaker.PlayCue(VoiceCueId.UTurn);
        }

        // The boss standing on the route ahead: say it is blocked, then whether there is another way round.
        private bool CheckBlocked(IReadOnlyList<int> nodes, int playerNode)
        {
            bool blocked = false;
            if (_boss != null && _boss.IsPresent)
            {
                Vector3 bossPos = _boss.transform.position;
                for (int i = 1; i < Mathf.Min(nodes.Count, 7) && !blocked; i++)
                    blocked = Vector3.Distance(_graph[nodes[i]].Position.ToUnity(), bossPos) < blockedRadius;
            }
            if (!blocked) { _blockedHandled = false; return false; }
            if (_blockedHandled) return false;
            _blockedHandled = true;

            speaker.PlayCue(VoiceCueId.RouteBlocked);

            Vector3 boss = _boss.transform.position;
            EdgeCost avoid = (edge, from) =>
            {
                float cost = edge.Length;
                if (Vector3.Distance(_graph[edge.Other(from)].Position.ToUnity(), boss) < blockedRadius) cost += 500f;
                return cost;
            };
            var around = GraphSearch.AStar(_graph, playerNode, _exitNode, avoid);
            speaker.PlayCue(around.Found && around.Cost < 500f ? VoiceCueId.RouteRerouted : VoiceCueId.NoRouteAvailable);
            return true;
        }
    }
}
