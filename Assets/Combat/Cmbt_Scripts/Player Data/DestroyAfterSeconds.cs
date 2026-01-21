using UnityEngine;

public class DestroyAfterSeconds : MonoBehaviour
{
    [SerializeField] private float lifetime = 0.2f;

    private void OnEnable()
    {
        Destroy(gameObject, lifetime);
    }
}
