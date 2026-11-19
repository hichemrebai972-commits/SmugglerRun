using System;
using UnityEngine;

namespace SmugglerRun.Track
{
    /// <summary>
    /// Ajouté automatiquement à chaque rocher créé par RockfallHazard.
    /// Il signale à l'éboulement les collisions avec le camion (une seule fois par rocher).
    /// </summary>
    public class FallingRock : MonoBehaviour
    {
        [NonSerialized] public RockfallHazard owner;
        [NonSerialized] public bool hasHitPlayer;

        void OnCollisionEnter(Collision collision)
        {
            if (owner != null) owner.NotifyCollision(this, collision);
        }
    }
}
