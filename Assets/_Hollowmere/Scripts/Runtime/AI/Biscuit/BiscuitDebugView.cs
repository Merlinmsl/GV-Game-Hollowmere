using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Shows what Biscuit is thinking. F2 toggles the panel: the facts, every goal's priority, every
    /// action's cost, the current plan and what planning cost. A label over Biscuit shows its goal
    /// and action, and each belief is labelled where Biscuit thinks the enemy is. The line at the
    /// bottom ("Biscuit growls...") always shows, standing in for sound until there is some.
    /// The panel builds strings while it is open; it is a debug tool and costs nothing when closed.
    /// BiscuitWorldOverlay draws the matching rings and routes into the world.
    /// </summary>
    [RequireComponent(typeof(BiscuitAgent))]
    public sealed class BiscuitDebugView : MonoBehaviour
    {
        const float PanelWidth = 440f;

        [Tooltip("Show the thinking panel from the start. F2 toggles it.")]
        [SerializeField] bool showPanel;

        readonly StringBuilder text = new StringBuilder(2048);
        BiscuitAgent biscuit;
        Camera view;
        string callout = "";
        float calloutUntil;
        GUIStyle panelStyle;
        GUIStyle calloutStyle;
        GUIStyle labelStyle;
        GUIStyle headStyle;

        public bool ShowPanel => showPanel;

        void Awake() => biscuit = GetComponent<BiscuitAgent>();

        void OnEnable()
        {
            biscuit.OnBark += ShowBark;
            biscuit.OnGrowl += ShowGrowl;
            biscuit.OnWhine += ShowWhine;
            biscuit.OnFetch += ShowFetch;
            biscuit.OnDeliver += ShowDelivery;
            biscuit.OnEvidenceShown += ShowEvidence;
        }

        void OnDisable()
        {
            biscuit.OnBark -= ShowBark;
            biscuit.OnGrowl -= ShowGrowl;
            biscuit.OnWhine -= ShowWhine;
            biscuit.OnFetch -= ShowFetch;
            biscuit.OnDeliver -= ShowDelivery;
            biscuit.OnEvidenceShown -= ShowEvidence;
        }

        void ShowBark() => Callout("Biscuit barks!");
        void ShowGrowl() => Callout("Biscuit growls...");
        void ShowWhine() => Callout("Biscuit whines and backs away. A ghost?");
        void ShowFetch() => Callout("Biscuit picks up a magazine");
        void ShowDelivery() => Callout("Biscuit drops a magazine at your feet");
        void ShowEvidence(string found) => Callout("Biscuit found something: " + found);

        void Callout(string message)
        {
            callout = message;
            calloutUntil = Time.time + 2.5f;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame) showPanel = !showPanel;
        }

        void OnGUI()
        {
            MakeStyles();

            if (Time.time < calloutUntil)
                GUI.Label(new Rect(0f, Screen.height - 90f, Screen.width, 40f), callout, calloutStyle);

            BiscuitBrain brain = biscuit.Brain;
            if (!showPanel || brain == null) return;

            DrawPanel(brain);
            DrawLabels(brain);
        }

        void DrawPanel(BiscuitBrain brain)
        {
            float now = brain.Perception.Now;
            text.Clear();
            text.Append("BISCUIT   goal-oriented action planning (A*)   F2 to hide\n\n");

            text.Append("Goal   ").Append(brain.CurrentGoal != null ? brain.CurrentGoal.Name : "none (idle)").Append('\n');
            text.Append("Plan   ");
            if (brain.PlanLength == 0) text.Append('-');
            for (int i = 0; i < brain.PlanLength; i++)
            {
                if (i > 0) text.Append("  >  ");
                bool active = i == brain.PlanStep && brain.CurrentAction != null;
                text.Append(active ? "[" : "").Append(brain.Plan[i].Name).Append(active ? "]" : "");
            }
            text.Append("\n       cost ").Append(brain.PlanCost.ToString("F1"))
                .Append("   searched ").Append(brain.PlanNodesExpanded).Append(" states   ")
                .Append(brain.PlanMilliseconds.ToString("F3")).Append(" ms\n\n");

            text.Append("Facts\n");
            Fact[] facts = WorldState.AllFacts;
            for (int i = 0; i < facts.Length; i += 2)
            {
                AppendFact(brain, facts[i]);
                if (i + 1 < facts.Length)
                {
                    text.Append("      ");
                    AppendFact(brain, facts[i + 1]);
                }
                text.Append('\n');
            }

            text.Append("\nGoals (priority)\n");
            for (int i = 0; i < brain.Goals.Length; i++)
            {
                BiscuitGoal goal = brain.Goals[i];
                text.Append(goal == brain.CurrentGoal ? "  > " : "    ")
                    .Append(goal.Name).Append("  ").Append(goal.Priority.ToString("F2"))
                    .Append(goal.Wants.IsMetBy(brain.State) ? "  (met)" : "").Append('\n');
            }

            text.Append("\nActions (cost to the planner)\n");
            for (int i = 0; i < brain.Actions.Length; i += 2)
            {
                AppendAction(brain, brain.Actions[i]);
                if (i + 1 < brain.Actions.Length)
                {
                    text.Append("      ");
                    AppendAction(brain, brain.Actions[i + 1]);
                }
                text.Append('\n');
            }

            text.Append("\nMemory   ").Append(brain.Memory.Count).Append(" enemies, strongest ")
                .Append(brain.Memory.StrongestConfidence(now).ToString("F2"));
            text.Append("\nNearby   ").Append(brain.Perception.PagesLeft).Append(" pages, ")
                .Append(brain.Perception.AmmoSpotsLeft).Append(" ammo spots");
            AppendKnown(brain, PointKind.Evidence, "page");
            AppendKnown(brain, PointKind.Ammo, "ammo");
            if (brain.Carrying) text.Append("\n         carrying ammo");
            if (brain.LastFailed != null)
                text.Append("\nLast failure   ").Append(brain.LastFailed.Name)
                    .Append(", ").Append((now - brain.LastFailedTime).ToString("F0")).Append(" s ago");

            var content = new GUIContent(text.ToString());
            float height = Mathf.Min(panelStyle.CalcHeight(content, PanelWidth), Screen.height - 20f);
            GUI.Box(new Rect(Screen.width - PanelWidth - 10f, 10f, PanelWidth, height), content, panelStyle);
        }

        void AppendFact(BiscuitBrain brain, Fact fact) =>
            text.Append(brain.Has(fact) ? "[x] " : "[  ] ").Append(fact);

        void AppendAction(BiscuitBrain brain, BiscuitAction action)
        {
            text.Append("  ").Append(action.Name).Append(' ');
            if (brain.IsBanned(action)) text.Append("(failed)");
            else if (!action.IsUsable(brain)) text.Append("(-)");
            else text.Append(action.Cost(brain).ToString("F1"));
        }

        void AppendKnown(BiscuitBrain brain, PointKind kind, string label)
        {
            if (!brain.TryGetKnownPoint(kind, out _, out Vector3 at)) return;
            text.Append("\n         smelt a ").Append(label).Append(' ')
                .Append(Vector3.Distance(brain.Perception.Position, at).ToString("F0")).Append(" m away");
        }

        void DrawLabels(BiscuitBrain brain)
        {
            if (view == null) view = Camera.main;
            if (view == null) return;

            // Goal and action over Biscuit's head.
            Vector3 head = view.WorldToScreenPoint(transform.position + Vector3.up * 1.2f);
            if (head.z > 0f)
            {
                string doing = (brain.CurrentGoal != null ? brain.CurrentGoal.Name : "idle") +
                               (brain.CurrentAction != null ? "  >  " + brain.CurrentAction.Name : "");
                GUI.Label(new Rect(head.x - 120f, Screen.height - head.y - 12f, 240f, 24f), doing, headStyle);
            }

            float now = brain.Perception.Now;
            for (int i = 0; i < brain.Memory.Count; i++)
            {
                EnemyBelief belief = brain.Memory[i];
                Vector3 screen = view.WorldToScreenPoint(belief.Position + Vector3.up * 2.6f);
                if (screen.z <= 0f) continue;   // behind the camera

                string label = belief.Kind.ToString().ToLowerInvariant() + (belief.Smelt ? " (smelt) " : " (heard) ") +
                               brain.Memory.ConfidenceOf(belief, now).ToString("F2");
                GUI.Label(new Rect(screen.x - 70f, Screen.height - screen.y - 10f, 140f, 20f), label, labelStyle);
            }
        }

        // Scene view only: the same picture for when the Game view is not visible.
        void OnDrawGizmos()
        {
            if (biscuit == null || biscuit.Brain == null) return;
            BiscuitBrain brain = biscuit.Brain;

            Gizmos.color = new Color(1f, 0.85f, 0.2f);
            if (biscuit.HasPath) Gizmos.DrawLine(transform.position, biscuit.Destination);

            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, biscuit.Tuning.noseRange);

            float now = brain.Perception.Now;
            for (int i = 0; i < brain.Memory.Count; i++)
            {
                EnemyBelief belief = brain.Memory[i];
                Gizmos.color = new Color(1f, 0.2f, 0.2f, Mathf.Clamp01(brain.Memory.ConfidenceOf(belief, now)));
                Gizmos.DrawSphere(belief.Position + Vector3.up, 0.4f);
            }
        }

        void MakeStyles()
        {
            if (panelStyle != null) return;

            panelStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12, wordWrap = true };
            panelStyle.normal.textColor = Color.white;
            panelStyle.padding = new RectOffset(10, 10, 10, 10);

            calloutStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
            calloutStyle.normal.textColor = new Color(1f, 0.9f, 0.6f);

            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            labelStyle.normal.textColor = new Color(1f, 0.55f, 0.55f);

            headStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };
            headStyle.normal.textColor = new Color(1f, 0.9f, 0.4f);
        }
    }
}
