using System.Collections.Generic;
using Hollowmere.AI.Senses;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollowmere.AI.Biscuit
{
    /// <summary>
    /// Draws Biscuit's thinking into the world while the F2 panel is open, so it shows in the Game
    /// view and in recordings:
    /// - its nose range (blue ring), and each sniff spreading out to how far it can smell;
    /// - each bark spreading out to 25 m, the real distance the dead can hear it from;
    /// - the route it is taking (yellow);
    /// - where it believes each enemy is: red for the dead, pale blue for ghosts, purple for the
    ///   creature; brighter and taller the surer it is;
    /// - every page and ammunition spot in the loaded zones (grey), and the ones it has smelt
    ///   (green page, yellow ammunition).
    /// Uses a pool of LineRenderers, made the first time the panel opens; nothing while it is closed.
    /// </summary>
    [RequireComponent(typeof(BiscuitAgent), typeof(BiscuitDebugView))]
    public sealed class BiscuitWorldOverlay : MonoBehaviour
    {
        const int CircleSegments = 48;
        const float Lift = 0.08f;          // just above the ground, so lines do not flicker into it
        const float BarkRingTime = 1f;
        const float PathRefresh = 0.2f;

        static readonly Color NoseColour = new Color(0.45f, 0.8f, 1f, 0.45f);
        static readonly Color SniffColour = new Color(0.45f, 0.85f, 1f, 1f);
        static readonly Color BarkColour = new Color(1f, 0.55f, 0.1f, 1f);
        static readonly Color PathColour = new Color(1f, 0.85f, 0.2f, 0.9f);
        static readonly Color DeadColour = new Color(1f, 0.2f, 0.2f, 1f);
        static readonly Color GhostColour = new Color(0.6f, 0.9f, 1f, 1f);
        static readonly Color CreatureColour = new Color(0.8f, 0.3f, 1f, 1f);
        static readonly Color PageColour = new Color(0.3f, 1f, 0.4f, 1f);
        static readonly Color AmmoColour = new Color(1f, 0.9f, 0.3f, 1f);
        static readonly Color PointColour = new Color(0.75f, 0.75f, 0.75f, 0.35f);

        readonly List<LineRenderer> pool = new List<LineRenderer>(48);
        readonly Vector3[] corners = new Vector3[32];
        BiscuitAgent biscuit;
        BiscuitDebugView panel;
        Material lineMaterial;
        bool noMaterial;
        int used;
        int cornerCount;
        float nextPathTime;
        float sniffStart = float.NegativeInfinity;
        float barkStart = float.NegativeInfinity;
        Vector3 barkAt;

        void Awake()
        {
            biscuit = GetComponent<BiscuitAgent>();
            panel = GetComponent<BiscuitDebugView>();
        }

        void OnEnable()
        {
            biscuit.OnSniff += StartSniffRing;
            biscuit.OnBark += StartBarkRing;
        }

        void OnDisable()
        {
            biscuit.OnSniff -= StartSniffRing;
            biscuit.OnBark -= StartBarkRing;
        }

        void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
        }

        void StartSniffRing() => sniffStart = Time.time;

        void StartBarkRing()
        {
            barkStart = Time.time;
            barkAt = transform.position;
        }

        void LateUpdate()
        {
            used = 0;
            if (panel.ShowPanel && biscuit.Brain != null && !noMaterial) Draw(biscuit.Brain);
            for (int i = used; i < pool.Count; i++)
                if (pool[i].enabled) pool[i].enabled = false;
        }

        void Draw(BiscuitBrain brain)
        {
            BiscuitTuning tuning = biscuit.Tuning;
            Vector3 here = transform.position;
            float now = Time.time;

            Circle(here, tuning.noseRange, NoseColour, 0.03f);

            float sniff = (now - sniffStart) / tuning.sniffDuration;
            if (sniff < 1f)
            {
                float reach = Mathf.Max(tuning.sniffRange, tuning.pointSniffRange);
                Circle(here, reach * sniff, Fade(SniffColour, 1f - sniff), 0.06f);
            }

            float bark = (now - barkStart) / BarkRingTime;
            if (bark < 1f)
            {
                Circle(barkAt, Loudness.Bark * bark, Fade(BarkColour, 1f - bark), 0.1f);
                Circle(barkAt, Loudness.Bark, Fade(BarkColour, 0.4f * (1f - bark)), 0.04f);
            }

            if (now >= nextPathTime)
            {
                cornerCount = biscuit.GetPathCorners(corners);
                nextPathTime = now + PathRefresh;
            }
            if (biscuit.HasPath && cornerCount > 1) Polyline(corners, cornerCount, PathColour, 0.05f);

            float brainNow = brain.Perception.Now;
            BiscuitMemory memory = brain.Memory;
            for (int i = 0; i < memory.Count; i++)
            {
                EnemyBelief belief = memory[i];
                float confidence = Mathf.Clamp01(memory.ConfidenceOf(belief, brainNow));
                Color colour = Fade(ColourOf(belief.Kind), 0.25f + 0.75f * confidence);
                Circle(belief.Position, 0.4f + 0.6f * confidence, colour, 0.05f);
                Stake(belief.Position, 2.5f * confidence, colour);
            }

            IReadOnlyList<PointRegistry.Point> points = PointRegistry.All;
            for (int i = 0; i < points.Count; i++) Circle(points[i].Position, 0.35f, PointColour, 0.03f);

            if (brain.TryGetKnownPoint(PointKind.Evidence, out _, out Vector3 page))
            {
                Circle(page, 0.8f, PageColour, 0.06f);
                Stake(page, 2f, PageColour);
            }
            if (brain.TryGetKnownPoint(PointKind.Ammo, out _, out Vector3 ammo))
            {
                Circle(ammo, 0.8f, AmmoColour, 0.06f);
                Stake(ammo, 2f, AmmoColour);
            }
        }

        void Circle(Vector3 centre, float radius, Color colour, float width)
        {
            if (radius < 0.05f) return;
            LineRenderer line = Next(colour, width);
            if (line == null) return;

            line.loop = true;
            line.positionCount = CircleSegments;
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = i * (Mathf.PI * 2f / CircleSegments);
                line.SetPosition(i, new Vector3(centre.x + Mathf.Cos(angle) * radius, centre.y + Lift,
                                                centre.z + Mathf.Sin(angle) * radius));
            }
        }

        void Stake(Vector3 at, float height, Color colour)
        {
            LineRenderer line = Next(colour, 0.05f);
            if (line == null) return;

            line.loop = false;
            line.positionCount = 2;
            line.SetPosition(0, at + Vector3.up * Lift);
            line.SetPosition(1, at + Vector3.up * (Lift + height));
        }

        void Polyline(Vector3[] points, int count, Color colour, float width)
        {
            LineRenderer line = Next(colour, width);
            if (line == null) return;

            line.loop = false;
            line.positionCount = count;
            for (int i = 0; i < count; i++) line.SetPosition(i, points[i] + Vector3.up * Lift);
        }

        LineRenderer Next(Color colour, float width)
        {
            if (used == pool.Count)
            {
                LineRenderer made = MakeLine();
                if (made == null) return null;
                pool.Add(made);
            }

            LineRenderer line = pool[used++];
            line.enabled = true;
            line.startColor = colour;
            line.endColor = colour;
            line.startWidth = width;
            line.endWidth = width;
            return line;
        }

        LineRenderer MakeLine()
        {
            if (lineMaterial == null)
            {
                // Unlit and coloured per vertex, so each line can have its own colour and fade.
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    noMaterial = true;
                    Debug.LogWarning("BiscuitWorldOverlay: shader Sprites/Default not found; world overlay is off.", this);
                    return null;
                }
                lineMaterial = new Material(shader);
            }

            var holder = new GameObject("Biscuit F2 line") { hideFlags = HideFlags.DontSave };
            holder.transform.SetParent(transform, false);
            var line = holder.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial;
            line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 2;
            line.alignment = LineAlignment.View;
            return line;
        }

        static Color ColourOf(EnemyKind kind) =>
            kind == EnemyKind.Ghost ? GhostColour : kind == EnemyKind.Creature ? CreatureColour : DeadColour;

        static Color Fade(Color colour, float alpha) => new Color(colour.r, colour.g, colour.b, colour.a * alpha);
    }
}
