using UnityEngine;

public class ScreenFader : MonoBehaviour
{
    public static ScreenFader instance { get; private set; }

    private void Start()
    {
        if (instance != null) Destroy(gameObject);
        else instance = this;
    }

    public void FadeIn(float duration)
    {
        
    }

}
