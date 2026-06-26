using UnityEngine;
using UnityEngine.SceneManagement;

public static class BootstrapInit
{
    private static string bootstrapSceneName = "Bootstrap";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene c = SceneManager.GetSceneAt(i);
            if (c.name == bootstrapSceneName)
                return;
        }

        SceneManager.LoadScene(bootstrapSceneName, LoadSceneMode.Additive);
    }
}

public class Bootstrap : MonoBehaviour
{
    public static Bootstrap Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
    }
}