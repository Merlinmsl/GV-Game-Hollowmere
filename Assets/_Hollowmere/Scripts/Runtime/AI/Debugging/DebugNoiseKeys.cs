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
            if (keyboard.oKey.wasPressedThisFrame) DropSensesProbe();
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

        void DropSensesProbe()
        {
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            probe.name = "Watcher " + (NoiseSystem.ListenerCount + 1);
            Destroy(probe.GetComponent<Collider>());
            probe.transform.SetPositionAndRotation(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));

            // A small nose so you can tell which way it faces.
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(probe.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.6f, 0.5f);
            nose.transform.localScale = Vector3.one * 0.25f;

            probe.AddComponent<SensesProbe>();
        }

        void OnEmitted(Noise noise, int heardBy)
        {
            lastResult = noise.Kind + " (" + noise.Loudness + " m) heard by " + heardBy + " of " + NoiseSystem.ListenerCount + " listeners";
        }

        void OnGUI()
        {
            GUI.Label(new Rect(12, 64, 900, 40), "NOISE TEST   P drop a listener | O drop a watcher | G gunshot | H door noise\n" + lastResult);
        }
    }
}







