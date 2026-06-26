using UnityEngine;

public class BoidsUIController : MonoBehaviour
{
    [SerializeField] private BoidsManager boidsManager;
    [SerializeField] private Transform targetObject;
    private float positionStep = 1f;
    private int maxFishCount = 250;

    private Vector3 objectPosition;
    private string posXInput;
    private string posZInput;

    private void Start()
    {
        if (boidsManager == null)
        {
            boidsManager = BoidsManager.instance;
        }

        if (targetObject != null)
        {
            objectPosition = targetObject.position;
            posXInput = objectPosition.x.ToString("F2");
            posZInput = objectPosition.z.ToString("F2");
        }
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(10, 10, 350, 450), "Boid Controls");

        GUILayout.BeginArea(new Rect(20, 35, 330, 420));

        GUILayout.Label("Speed Settings", GUI.skin.box);
        GUILayout.Label($"Min Speed: {boidsManager.minSpeed:F2}");
        boidsManager.minSpeed = GUILayout.HorizontalSlider(boidsManager.minSpeed, 0f, 5f);

        GUILayout.Label($"Max Speed: {boidsManager.maxSpeed:F2}");
        boidsManager.maxSpeed = GUILayout.HorizontalSlider(boidsManager.maxSpeed, 0f, 5f);

        GUILayout.Space(10);

        GUILayout.Label("Behavior Settings", GUI.skin.box);
        GUILayout.Label($"Neighbour Distance: {boidsManager.neighbourDist:F2}");
        boidsManager.neighbourDist = GUILayout.HorizontalSlider(boidsManager.neighbourDist, 0f, 10f);

        GUILayout.Label($"Rotation Speed: {boidsManager.rotationSpeed:F2}");
        boidsManager.rotationSpeed = GUILayout.HorizontalSlider(boidsManager.rotationSpeed, 1f, 5f);

        GUILayout.Space(10);

        GUILayout.Label("Position Settings", GUI.skin.box);
        if (targetObject != null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Position X:", GUILayout.Width(80));
            posXInput = GUILayout.TextField(posXInput, GUILayout.Width(60));
            if (GUILayout.Button("-", GUILayout.Width(30)))
            {
                objectPosition.x -= positionStep;
                posXInput = objectPosition.x.ToString("F2");
            }
            if (GUILayout.Button("+", GUILayout.Width(30)))
            {
                objectPosition.x += positionStep;
                posXInput = objectPosition.x.ToString("F2");
            }
            GUILayout.EndHorizontal();

            if (float.TryParse(posXInput, out float newX))
            {
                objectPosition.x = newX;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Position Z:", GUILayout.Width(80));
            posZInput = GUILayout.TextField(posZInput, GUILayout.Width(60));
            if (GUILayout.Button("-", GUILayout.Width(30)))
            {
                objectPosition.z -= positionStep;
                posZInput = objectPosition.z.ToString("F2");
            }
            if (GUILayout.Button("+", GUILayout.Width(30)))
            {
                objectPosition.z += positionStep;
                posZInput = objectPosition.z.ToString("F2");
            }
            GUILayout.EndHorizontal();

            if (float.TryParse(posZInput, out float newZ))
            {
                objectPosition.z = newZ;
            }

            targetObject.position = objectPosition;
        }
        else
        {
            GUILayout.Label("No target object assigned");
        }

        GUILayout.Space(10);

        GUILayout.Label("Fish Count Control", GUI.skin.box);
        GUILayout.Label($"Current Fish: {boidsManager.allFish.Length} / {maxFishCount}");
        
        int currentFishCount = boidsManager.allFish.Length;
        int newFishCount = (int)GUILayout.HorizontalSlider(currentFishCount, 0, maxFishCount);
        
        if (newFishCount != currentFishCount)
        {
            if (newFishCount > currentFishCount)
            {
                int difference = newFishCount - currentFishCount;
                for (int i = 0; i < difference; i++)
                {
                    boidsManager.AddFish();
                }
            }
            else if (newFishCount < currentFishCount)
            {
                int difference = currentFishCount - newFishCount;
                for (int i = 0; i < difference; i++)
                {
                    boidsManager.RemoveFish();
                }
            }
        }

        GUILayout.EndArea();
    }

}