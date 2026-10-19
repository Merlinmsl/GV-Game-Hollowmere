using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Hollowmere.AI.Scheduling
{
    /// <summary>Anything that thinks. The scheduler calls Think a few times a second, not every frame.</summary>
    public interface IAiThinker
    {
        /// <summary>Seconds between thoughts, for example 0.2 for five times a second.</summary>
        float ThinkInterval { get; }

        /// <summary>Decide what to do. deltaTime is the real time since this thinker's last thought.</summary>
        void Think(float deltaTime);
    }

    /// <summary>
    /// Runs every agent's thinking, spread across frames so they never all think at once.
    /// - Each thinker starts at a random point in its interval, so twenty agents at 5 Hz become
    ///   about two thoughts per frame instead of twenty every fifth frame.
    /// - At most MaxThinksPerFrame thoughts run in one frame; the rest wait for the next frame.
    /// - The time spent is measured, so the AI's share of the frame can be shown and defended.
    /// Movement and animation still run every frame in each agent's own Update.
    /// </summary>
    public static class AiScheduler
    {
        public const int MaxThinksPerFrame = 6;

        sealed class Entry
        {
            public IAiThinker Thinker;
            public float LastThink;
            public float NextThink;
        }

        static readonly List<Entry> entries = new List<Entry>();
        static readonly Stopwatch watch = new Stopwatch();
        static int cursor;
        static Runner runner;

        public static int Count => entries.Count;
        public static int LastFrameThinks { get; private set; }
        public static float LastFrameMilliseconds { get; private set; }
        public static float PeakMilliseconds { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            entries.Clear();
            cursor = 0;
            runner = null;
            LastFrameThinks = 0;
            LastFrameMilliseconds = 0f;
            PeakMilliseconds = 0f;
        }

        public static void Register(IAiThinker thinker)
        {
            if (!Application.isPlaying) return;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Thinker == thinker) return;

            EnsureRunner();
            float now = Time.time;
            entries.Add(new Entry
            {
                Thinker = thinker,
                LastThink = now,
                NextThink = now + Random.value * thinker.ThinkInterval   // spread the start
            });
        }

        public static void Unregister(IAiThinker thinker)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Thinker != thinker) continue;
                entries.RemoveAt(i);
                if (i < cursor) cursor--;
                return;
            }
        }

        static void Tick()
        {
            watch.Restart();
            float now = Time.time;
            int thinks = 0;
            int count = entries.Count;

            // Round robin from where the last frame stopped, so nobody is starved by the per-frame cap.
            for (int visited = 0; visited < count && thinks < MaxThinksPerFrame; visited++)
            {
                if (entries.Count == 0) break;
                if (cursor >= entries.Count) cursor = 0;
                Entry entry = entries[cursor++];
                if (now < entry.NextThink) continue;

                float deltaTime = now - entry.LastThink;
                entry.LastThink = now;
                entry.NextThink = now + entry.Thinker.ThinkInterval;
                entry.Thinker.Think(deltaTime);
                thinks++;
            }

            watch.Stop();
            LastFrameThinks = thinks;
            LastFrameMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            if (LastFrameMilliseconds > PeakMilliseconds) PeakMilliseconds = LastFrameMilliseconds;
        }

        static void EnsureRunner()
        {
            if (runner != null) return;
            var host = new GameObject("AiScheduler");
            Object.DontDestroyOnLoad(host);
            runner = host.AddComponent<Runner>();
        }

        /// <summary>The one Update that drives all AI thinking.</summary>
        sealed class Runner : MonoBehaviour
        {
            void Update() => Tick();
        }
    }
}
