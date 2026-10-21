using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollowmere.World
{
    /// <summary>
    /// Marks something the map should show: an item to collect or a zone exit.
    /// Placed on the gameplay markers by Hollowmere > World > Add Map Markers To Items.
    ///
    /// When the player picks the item up, the pickup code calls Collect(). The item then disappears
    /// from the map and stays gone, even after its zone unloads and loads again (zone scenes reload
    /// fresh, so the list of collected items is kept here, not in the zone).
    /// </summary>
    public sealed class MapMarker : MonoBehaviour
    {
        public enum Kind { Weapon, Ammo, Key, Evidence, IceCanister, Battery, Throwable, Exit }

        [SerializeField] Kind kind;
        [Tooltip("Name shown when the mouse is over the icon. Left empty, it is made from the object's name.")]
        [SerializeField] string label;

        static readonly List<MapMarker> active = new List<MapMarker>();
        static readonly HashSet<string> collected = new HashSet<string>();

        /// <summary>Raised when any item is collected, so an open map can redraw.</summary>
        public static event Action<MapMarker> Collected;

        public static IReadOnlyList<MapMarker> Active => active;

        public Kind Type => kind;
        public string Label => string.IsNullOrEmpty(label) ? MakeLabel(name) : label;

        /// <summary>Unique across zones: the zone scene and the marker's name.</summary>
        public string Id => gameObject.scene.name + "/" + name;

        /// <summary>Exits are always shown; items until they are collected.</summary>
        public bool ShowOnMap => kind == Kind.Exit || !collected.Contains(Id);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            active.Clear();
            collected.Clear();
            Collected = null;
        }

        void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        void OnDisable() => active.Remove(this);

        /// <summary>Call this when the player picks the item up.</summary>
        public void Collect()
        {
            if (kind == Kind.Exit || !collected.Add(Id)) return;
            Collected?.Invoke(this);
        }

        public static bool IsCollected(string id) => collected.Contains(id);

        public void Setup(Kind newKind, string newLabel)
        {
            kind = newKind;
            label = newLabel;
        }

        /// <summary>Guesses the kind from the marker's name, e.g. Pickup_RevolverAmmo_Car is Ammo.</summary>
        public static bool TryKindFromName(string objectName, out Kind result)
        {
            result = Kind.Ammo;
            string n = objectName.ToLowerInvariant();
            if (n.StartsWith("zoneexit_")) result = Kind.Exit;
            else if (n.StartsWith("key_")) result = Kind.Key;
            else if (n.StartsWith("evidence_") || n.StartsWith("clue_")) result = Kind.Evidence;
            else if (!n.StartsWith("pickup_")) return false;
            else if (n.Contains("ammo") || n.Contains("magazine")) result = Kind.Ammo;
            else if (n.Contains("icecanister")) result = Kind.IceCanister;
            else if (n.Contains("battery")) result = Kind.Battery;
            else if (n.Contains("bottle")) result = Kind.Throwable;
            else result = Kind.Weapon;
            return true;
        }

        /// <summary>"Pickup_RevolverAmmo_Car" becomes "Revolver ammo (car)".</summary>
        public static string MakeLabel(string objectName)
        {
            string[] parts = objectName.Split('_');
            int start = parts.Length > 1 ? 1 : 0;
            string main = SplitWords(parts[start]);
            if (parts.Length > start + 1)
            {
                string rest = string.Join(" ", parts, start + 1, parts.Length - start - 1);
                bool isNumber = int.TryParse(rest, out _);
                if (!isNumber) main += " (" + SplitWords(rest).ToLowerInvariant() + ")";
            }
            return main;
        }

        static string SplitWords(string s)
        {
            var b = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) b.Append(' ');
                b.Append(i == 0 ? s[i] : (i > 0 && char.IsUpper(s[i]) ? char.ToLowerInvariant(s[i]) : s[i]));
            }
            return b.ToString();
        }
    }
}
