using System.Diagnostics;
using UnityEngine;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// SANDBOX ONLY, until GhostBody (Phase 6) plans its own routes: moves a ghost to the cell the
    /// position search chose (GhostMapDebugView.BestCell), or to a target transform, by time-sliced A*,
    /// smoothing and the route follower. Draws the raw cell route (orange) and the smoothed legs (magenta)
    /// and reports the cost of each slice, so the planner can be watched and timed in Play mode.
    /// </summary>
    public sealed class GhostRouteProbe : MonoBehaviour
    {
        [SerializeField] GhostHouseMap house;
        [Tooltip("Where the goal comes from when no target is set: its position search.")]
        [SerializeField] GhostMapDebugView view;
        [SerializeField] GhostRouteFollower follower;
        [Tooltip("Optional fixed goal. Empty: follow the position search's best cell.")]
        [SerializeField] Transform target;

        [SerializeField] RouteCosts costs = RouteCosts.Default;
        [Tooltip("Cells A* may expand per frame: the time slice.")]
        [SerializeField] int nodeBudgetPerFrame = 150;
        [Tooltip("Cells one search may expand in total before it settles for a partial route.")]
        [SerializeField] int maxNodes = 4000;
        [Tooltip("Re-plan this often even when the goal is the same, as the map (light, watched) changes.")]
        [SerializeField] float replanInterval = 1f;
        [SerializeField] bool showRoute = true;

        readonly Stopwatch watch = new Stopwatch();
        readonly int[] route = new int[512];
        readonly Vector3[] waypoints = new Vector3[GhostRouteFollower.MaxWaypoints];
        InfluenceMap map;
        AStarSearch astar;
        PathSmoother smoother;
        LineRenderer rawLine, smoothLine;
        int plannedGoal = -1;
        float nextPlan;
        float searchMs, worstSliceMs, smoothMs;
        int routeLength, waypointCount, lastExpanded, lastSlices;
        RouteStatus lastStatus;

        void OnEnable()
        {
            if (view != null) view.KeepSearching = true;
        }

        void OnDisable()
        {
            if (view != null) view.KeepSearching = false;
        }

        void Update()
        {
            if (house == null || !house.IsBaked || follower == null) return;
            if (map != house.Runtime || astar == null)
            {
                map = house.Runtime;
                astar = new AStarSearch(map);
                smoother = new PathSmoother(map);
            }

            if (astar.Status == RouteStatus.Searching) Slice();
            else PlanIfNeeded();
        }

        void PlanIfNeeded()
        {
            GhostGrid grid = map.Grid;
            int goal = target != null ? grid.ClampedCellAt(target.position + Vector3.up * 0.1f)
                                      : view != null ? view.BestCell : -1;
            if (goal < 0) return;
            if (goal == plannedGoal && Time.time < nextPlan) return;

            int start = grid.ClampedCellAt(follower.transform.position + Vector3.up * 0.1f);
            plannedGoal = goal;
            nextPlan = Time.time + replanInterval;
            searchMs = worstSliceMs = 0f;
            astar.Begin(start, goal, costs, Time.time, maxNodes);
            Slice();
        }

        void Slice()
        {
            watch.Restart();
            RouteStatus status = astar.Step(nodeBudgetPerFrame);
            watch.Stop();
            float ms = (float)watch.Elapsed.TotalMilliseconds;
            searchMs += ms;
            worstSliceMs = Mathf.Max(worstSliceMs, ms);
            if (status == RouteStatus.Searching) return;

            lastStatus = status;
            lastExpanded = astar.NodesExpanded;
            lastSlices = astar.Slices;
            if (status != RouteStatus.Found && status != RouteStatus.Partial) return;

            watch.Restart();
            routeLength = astar.CopyRoute(route);
            waypointCount = smoother.Smooth(route, routeLength, costs, Time.time, waypoints);
            watch.Stop();
            smoothMs = (float)watch.Elapsed.TotalMilliseconds;
            follower.SetRoute(waypoints, waypointCount);
            DrawRoute();
        }

        void DrawRoute()
        {
            if (!showRoute) return;
            if (rawLine == null)
            {
                rawLine = MakeLine("A* cells", new Color(1f, 0.55f, 0.1f), 0.06f);
                smoothLine = MakeLine("Smoothed", new Color(1f, 0.1f, 1f), 0.12f);
            }
            GhostGrid grid = map.Grid;
            rawLine.positionCount = routeLength;
            for (int i = 0; i < routeLength; i++) rawLine.SetPosition(i, grid.CellCentre(route[i]) + Vector3.down * 0.9f);
            smoothLine.positionCount = waypointCount;
            for (int i = 0; i < waypointCount; i++) smoothLine.SetPosition(i, waypoints[i] + Vector3.down * 0.85f);
        }

        LineRenderer MakeLine(string label, Color colour, float width)
        {
            var go = new GameObject(label) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
            line.startColor = line.endColor = colour;
            line.startWidth = line.endWidth = width;
            line.useWorldSpace = true;
            line.positionCount = 0;
            return line;
        }

        void OnGUI()
        {
            if (astar == null) return;
            string text = "GHOST ROUTE (A*)   " + lastStatus + "   cost " + astar.RouteCost.ToString("F1") + "\n" +
                          lastExpanded + " cells expanded in " + lastSlices + " slices of <= " + nodeBudgetPerFrame +
                          "   total " + searchMs.ToString("F3") + " ms, worst slice " + worstSliceMs.ToString("F3") + " ms\n" +
                          routeLength + " route cells -> " + waypointCount + " waypoints (smoothing " + smoothMs.ToString("F3") + " ms)" +
                          (follower.Arrived ? "   arrived" : "");
            GUI.Box(new Rect(10f, Screen.height - 70f, 560f, 60f), GUIContent.none);
            GUI.Label(new Rect(18f, Screen.height - 66f, 550f, 56f), text);
        }

        void OnDestroy()
        {
            if (rawLine != null) Destroy(rawLine.sharedMaterial);
            if (smoothLine != null) Destroy(smoothLine.sharedMaterial);
        }
    }
}
