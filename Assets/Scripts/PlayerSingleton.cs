using UnityEngine;

public class PlayerSingleton : MonoBehaviour
{
    public static PlayerSingleton Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Make sure the tag is correct so triggers and binders work.
        if (!CompareTag("Player"))
            gameObject.tag = "Player";

        DontDestroyOnLoad(gameObject);
    }
}
