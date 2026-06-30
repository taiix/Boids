using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Put this on the shark (or anything the fish should flee). While enabled it registers
    /// itself so every <see cref="BoidsManager"/> flock steers away from it within its flee
    /// radius — the school parts/splits around it and rejoins once it passes. A fast/dashing
    /// shark still outruns the gradual escape, so it can catch fish.
    /// </summary>
    public class Predator : MonoBehaviour
    {
        void OnEnable()
        {
            if (!BoidsManager.Predators.Contains(transform))
                BoidsManager.Predators.Add(transform);
        }

        void OnDisable() => BoidsManager.Predators.Remove(transform);
    }
}
