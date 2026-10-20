using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hollowmere.AI.Ghost
{
    /// <summary>
    /// DEBUG: what the ghosts cost, frame by frame, read from their profiler markers (GhostProfiling) with
    /// ProfilerRecorder, so it measures exactly what the Unity Profiler shows, without the Profiler window.
    /// Keeps the last 600 frames: average, 99th percentile, worst frame (with its stage breakdown) and how
    /// many frames went over the budget. F4 shows or hides it; "Log report" prints it.
    /// </summary>
    public sealed class GhostProfilerHud : MonoBehaviour
    {
        const int History = 600;
        const int Think = 0, Sense = 1, WalkField = 2, Position = 3, WallSearch = 4, Route = 5, Stages = 6;

        [Tooltip("Milliseconds all ghosts together may take in one frame.")]
        [SerializeField] float budgetMs = 0.6f;
        [SerializeField] bool show = true;

        readonly ProfilerRecorder[] recorders = new ProfilerRecorder[Stages];
        readonly float[] totals = new float[History];
        readonly float[] sorted = new float[History];
        readonly float[] worstStages = new float[Stages];
        readonly float[] stageSums = new float[Stages];
        int count, next, overBudget, worstFrame;
        long framesSeen;
        float worst;
        GUIStyle style;

        public float BudgetMs => budgetMs;

        void OnEnable()
        {
            for (int i = 0; i < Stages; i++)
                recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, GhostProfiling.Names[i]);
        }

        void OnDisable()
        {
            for (int i = 0; i < Stages; i++) recorders[i].Dispose();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f4Key.wasPressedThisFrame) show = !show;
        }

        // LastValue is the finished previous frame, in nanoseconds.
        void LateUpdate()
        {
            float think = Ms(Think), route = Ms(Route);
            float total = think + route;                       // the other stages run inside Think
            totals[next] = total;
            next = (next + 1) % History;
            if (count < History) count++;
            framesSeen++;
            for (int i = 0; i < Stages; i++) stageSums[i] += Ms(i);
            if (total > budgetMs) overBudget++;
            if (total > worst)
            {
                worst = total;
                worstFrame = Time.frameCount - 1;
                for (int i = 0; i < Stages; i++) worstStages[i] = Ms(i);
            }
        }

        float Ms(int stage) => recorders[stage].Valid ? recorders[stage].LastValue * 1e-6f : 0f;

        [ContextMenu("Reset stats")]
        public void ResetStats()
        {
            count = next = overBudget = 0;
            framesSeen = 0;
            worst = 0f;
            for (int i = 0; i < Stages; i++) stageSums[i] = worstStages[i] = 0f;
        }

        [ContextMenu("Log report")]
        void LogReport() => Debug.Log("[GhostProfilerHud]\n" + Report(), this);

        /// <summary>The numbers as text: for the overlay, the console and the viva.</summary>
        public string Report()
        {
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                sorted[i] = totals[i];
                sum += totals[i];
            }
            System.Array.Sort(sorted, 0, count);
            float avg = count > 0 ? sum / count : 0f;
            float p99 = count > 0 ? sorted[Mathf.Min(count - 1, (int)(count * 0.99f))] : 0f;
            float last = count > 0 ? sorted[count - 1] : 0f;
            double n = System.Math.Max(1, framesSeen);

            return "GHOSTS " + GhostBody.Active.Count + "   budget " + budgetMs.ToString("F2") + " ms/frame   (F4)\n" +
                   "last " + count + " frames: avg " + avg.ToString("F3") + "  p99 " + p99.ToString("F3") + "  max " + last.ToString("F3") + " ms\n" +
                   "since reset (" + framesSeen + " frames): worst " + worst.ToString("F3") + " ms at frame " + worstFrame +
                   ", over budget " + overBudget + " (" + (100.0 * overBudget / n).ToString("F2") + "%)\n" +
                   "worst frame: think " + worstStages[Think].ToString("F3") + " (sense " + worstStages[Sense].ToString("F3") +
                   ", walk " + worstStages[WalkField].ToString("F3") + ", position " + worstStages[Position].ToString("F3") +
                   ", wall " + worstStages[WallSearch].ToString("F3") + ")  route " + worstStages[Route].ToString("F3") + "\n" +
                   "avg per frame: think " + (stageSums[Think] / n).ToString("F4") + " (sense " + (stageSums[Sense] / n).ToString("F4") +
                   ", walk " + (stageSums[WalkField] / n).ToString("F4") + ", position " + (stageSums[Position] / n).ToString("F4") +
                   ", wall " + (stageSums[WallSearch] / n).ToString("F4") + ")  route " + (stageSums[Route] / n).ToString("F4");
        }

        void OnGUI()
        {
            if (!show) return;
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, wordWrap = false };
            GUI.Box(new Rect(10f, Screen.height - 110f, 760f, 100f), Report(), style);
        }
    }
}
