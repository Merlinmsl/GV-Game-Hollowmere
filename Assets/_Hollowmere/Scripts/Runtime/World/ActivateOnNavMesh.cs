using UnityEngine;
using UnityEngine.AI;

namespace Hollowmere.World
{
    /// <summary>
    /// Keeps a NavMesh character switched off until the NavMesh under it has streamed in, then
    /// switches it on. Characters that live in the World scene (Biscuit) start before the zone
    /// holding their ground has loaded; an agent enabled with no NavMesh under it fails to place
    /// itself and stays stuck. Put this on an always-active object and point it at the character,
    /// which is saved switched off.
    /// </summary>
    public sealed class ActivateOnNavMesh : MonoBehaviour
    {
        [Tooltip("The character to switch on. Save it switched off.")]
        [SerializeField] GameObject target;

        [Tooltip("How far from the character the NavMesh may be.")]
        [SerializeField] float searchRadius = 3f;

        NavMeshQueryFilter filter;

        void Start()
        {
            if (target == null) { enabled = false; return; }
            var agent = target.GetComponent<NavMeshAgent>();
            filter = new NavMeshQueryFilter { agentTypeID = agent != null ? agent.agentTypeID : 0, areaMask = NavMesh.AllAreas };
        }

        void Update()
        {
            if (!NavMesh.SamplePosition(target.transform.position, out NavMeshHit hit, searchRadius, filter)) return;
            target.transform.position = hit.position;
            target.SetActive(true);
            enabled = false;
        }
    }
}
