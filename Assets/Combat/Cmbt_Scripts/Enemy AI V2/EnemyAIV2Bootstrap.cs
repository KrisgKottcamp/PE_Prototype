using UnityEngine;

namespace ProjectEri.EnemyAI.V2
{
    [DisallowMultipleComponent]
    public sealed class EnemyAIV2Bootstrap : MonoBehaviour
    {
        [SerializeField] private SquadDirectorV2 director;
        [SerializeField, Min(0.1f)] private float spawnedEnemyScanInterval = 0.5f;

        private float nextScanTime;

        private void Awake()
        {
            if (director == null)
                director = GetComponent<SquadDirectorV2>();

            if (director == null)
                director = FindObjectOfType<SquadDirectorV2>(true);
        }

        private void OnEnable()
        {
            director?.DiscoverAgents();
        }

        private void Update()
        {
            if (director == null || Time.time < nextScanTime)
                return;

            nextScanTime = Time.time + Mathf.Max(0.1f, spawnedEnemyScanInterval);
            director.DiscoverAgents();
        }
    }
}
