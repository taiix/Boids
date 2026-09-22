using FishGame;
using System;
using UnityEngine;

public class Debug_Clients : MonoBehaviour
{
    public GameObject fishPlayer;
    public GameObject sharkPlayer;

#if UNITY_EDITOR

    private void Start()
    {
        // A Multiplayer Play Mode test spawns networked players; a local offline fish on top of them
        // would leave an extra camera switched on in every window.
        if (DevHost.IsLocalMultiplayerTest) { enabled = false; return; }

        Instantiate(this.fishPlayer, this.gameObject.transform.position, Quaternion.identity);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.Log("Switching player role");
            var a = FindAnyObjectByType<FishPlayer>();
            
            if (a != null)
            {
                Destroy(a.gameObject);
                Debug.Log("Switching to shark player");
                Instantiate(sharkPlayer, a.transform.position, Quaternion.identity);
                return;
            }
            var b = FindAnyObjectByType<Predator>();
            if (b != null)
            {
                Destroy(b.gameObject);
                Debug.Log("Switching to fish player");
                Instantiate(fishPlayer, b.transform.position, Quaternion.identity);
                return;
            }
        }
    }
#endif
}