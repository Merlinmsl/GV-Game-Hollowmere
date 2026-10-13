using Hollowmere.AI.Senses;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hollowmere.AI.Debugging
{
    /// <summary>TEMPORARY test keys for the noise system. Lives on the debug player.</summary>
    public sealed class DebugNoiseKeys : MonoBehaviour
    {
        string lastResult = "";

        void OnEnable() => NoiseSystem.Emitted += OnEmitted;
        void OnDisable() => NoiseSystem.Emitted -= OnEmitted;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.pKey.wasPressedThisFrame) DropProbe();
            if (keyboard.gKey.wasPressedThisFrame)
                NoiseSystem.Emit(transform.position, Loudness.Gunshot, NoiseKind.Gunshot, gameObject);
            if (keyboard.hKey.wasPressedThisFrame)
                NoiseSystem.Emit(transform.position, Loudness.Door, NoiseKind.Door, gameObject);
        }

        void DropProbe()
        {
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            probe.name = "NoiseProbe " + (NoiseSystem.ListenerCount + 1);
            Destroy(probe.GetComponent<Collider>());
            probe.transform.position = transform.position + Vector3.up * 0.5f;
            probe.transform.localScale = Vector3.one * 0.4f;
            probe.AddComponent<NoiseProbe>();
        }

        void OnEmitted(Noise noise, int heardBy)
        {
            lastResult = noise.Kind + " (" + noise.Loudness + " m) heard by " + heardBy + " of " + NoiseSystem.ListenerCount + " listeners";
        }

        void OnGUI()
        {
            GUI.Label(new Rect(12, 64, 900, 40), "NOISE TEST   P drop a listener | G gunshot | H door noise\n" + lastResult);
        }
    }
}







