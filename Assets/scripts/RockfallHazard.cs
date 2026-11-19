using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SmugglerRun.Track
{
    /// <summary>
    /// Éboulement : quand le camion (tag "Player") passe à armDistanceM avant ce point,
    /// 1) le message d'alerte est envoyé (onWarn) et un grondement est joué,
    /// 2) des petits cailloux tombent pendant l'avertissement,
    /// 3) les gros rochers tombent et touchent le sol environ landMarginSeconds avant l'arrivée du camion.
    /// Le moment de la chute dépend de la vitesse du camion : l'éboulement est donc toujours lisible et jamais "injuste".
    /// Axes : l'axe Z local de cet objet doit pointer dans le sens de la piste (le TrackGenerator s'en charge).
    /// </summary>
    [DisallowMultipleComponent]
    public class RockfallHazard : MonoBehaviour
    {
        [Serializable] public class StringEvent : UnityEvent<string> { }
        [Serializable] public class FloatEvent : UnityEvent<float> { }

        [Header("Zone")]
        public float roadWidthM = 5f;
        [Tooltip("Distance avant l'éboulement où la séquence se déclenche.")]
        public float armDistanceM = 60f;
        [Tooltip("Distance totale de la boîte de détection, pour armer assez tôt à grande vitesse.")]
        public float triggerDistanceM = 140f;
        public Transform player;
        public string playerTag = "Player";
        [Tooltip("Référence locale utilisée si le générateur déclenche le danger par progression.")]
        public Transform progressFrame;
        [NonSerialized] public float progressTargetZ;
        [Tooltip("Désactive le collider si le générateur de route déclenche le danger par progression.")]
        public bool useTrigger = true;

        [Header("Alerte")]
        public string warningMessage = "ÉBOULEMENT • ATTENTION";
        public AudioClip rumbleClip;
        [Range(0f, 1f)] public float rumbleVolume = 1f;

        [Header("Chute des rochers")]
        [Tooltip("Prefab de rocher d'environ 1 m de diamètre. Vide = cubes gris provisoires.")]
        public GameObject rockPrefab;
        public float spawnHeightM = 8f;
        [Tooltip("Les gros rochers touchent le sol cette durée avant l'arrivée du camion.")]
        public float landMarginSeconds = 0.8f;
        [Tooltip("Délai minimum entre l'alerte et la chute des gros rochers.")]
        public float minBigDelaySeconds = 0.5f;
        [Tooltip("Après ce délai, les cailloux cessent de tomber si le camion est très lent.")]
        public float maxPebbleWarningSeconds = 8f;
        [Tooltip("Vitesse minimale supposée du camion pour calculer le moment de la chute (m/s).")]
        public float minSpeedMps = 8f;
        [Tooltip("Même graine = même éboulement à chaque essai (plus juste pour le joueur).")]
        public int seed = 7;

        [Header("Rochers")]
        public int bigRockCount = 2;
        public Vector2 bigSizeM = new Vector2(1.2f, 1.8f);
        public int mediumRockCount = 3;
        public Vector2 mediumSizeM = new Vector2(0.4f, 0.8f);
        public int pebblesPerBatch = 2;
        public Vector2 pebbleSizeM = new Vector2(0.12f, 0.25f);
        public float rockDensity = 2400f;
        public float rockLifetimeSeconds = 45f;

        [Header("Impact sur le camion")]
        [Tooltip("Vitesse relative minimale (m/s) pour compter un impact.")]
        public float minImpactSpeed = 2f;

        [Header("Événements")]
        [Tooltip("Reçoit le message d'alerte : branche-le à ton HUD.")]
        public StringEvent onWarn = new StringEvent();
        [Tooltip("Déclenché quand les gros rochers tombent : secousse de caméra, son...")]
        public UnityEvent onRocksFall = new UnityEvent();
        [Tooltip("Reçoit la vitesse d'impact (m/s) quand un rocher touche le camion : dégâts, cargaison...")]
        public FloatEvent onPlayerHit = new FloatEvent();

        BoxCollider _trigger;
        System.Random _rng;
        System.Random _pebbleRng;
        bool _armed;
        Coroutine _sequence;
        readonly List<GameObject> _rocks = new List<GameObject>();

        /// <summary>Appelé par le TrackGenerator pour régler la largeur de route et la distance d'alerte.</summary>
        public void Configure(float roadWidth, float armDistance, string message)
        {
            Configure(roadWidth, armDistance, message, player);
        }

        public void Configure(float roadWidth, float armDistance, string message, Transform playerTarget)
        {
            roadWidthM = roadWidth;
            armDistanceM = armDistance;
            if (playerTarget != null) player = playerTarget;
            if (!string.IsNullOrEmpty(message)) warningMessage = message;
        }

        void Start()
        {
            if (useTrigger) BuildTrigger();
            ResetHazard();
        }

        void BuildTrigger()
        {
            if (!useTrigger) return;
            if (_trigger != null) Destroy(_trigger);
            _trigger = gameObject.AddComponent<BoxCollider>();
            _trigger.isTrigger = true;
            // La détection commence loin, puis la coroutine attend la distance
            // nécessaire selon la vitesse réelle avant d'afficher l'alerte.
            float detectionLength = Mathf.Max(armDistanceM, triggerDistanceM);
            _trigger.center = new Vector3(0f, 3f, -detectionLength * 0.5f);
            _trigger.size = new Vector3(roadWidthM + 4f, 6f, detectionLength);
        }

        /// <summary>Réarme l'éboulement et supprime les rochers (à appeler au redémarrage d'une tentative).</summary>
        [ContextMenu("Réarmer l'éboulement")]
        public void ResetHazard()
        {
            if (_sequence != null) StopCoroutine(_sequence);
            _sequence = null;
            foreach (var rock in _rocks)
                if (rock != null)
                {
                    rock.SetActive(false);
                    Destroy(rock);
                }
            _rocks.Clear();
            _rng = new System.Random(seed);
            _pebbleRng = new System.Random(seed ^ 0x5F3759DF);
            _armed = true;
        }

        /// <summary>Appelé par un générateur de route qui suit la progression réelle du pickup.</summary>
        public void CheckApproach(Rigidbody playerBody, Transform playerTransform, float forwardDistance)
        {
            if (!_armed || playerTransform == null || forwardDistance < 0f
                || forwardDistance > Mathf.Max(armDistanceM, triggerDistanceM)) return;

            float speed = 0f;
            if (playerBody != null)
            {
                Vector3 velocity = GetVelocity(playerBody);
                speed = progressFrame != null
                    ? progressFrame.InverseTransformDirection(velocity).z
                    : velocity.magnitude;
            }
            if (speed <= 0.5f) return;
            float fallTime = Mathf.Sqrt(2f * Mathf.Max(0.5f, spawnHeightM) / 9.81f);
            float neededLeadSeconds = fallTime + landMarginSeconds + minBigDelaySeconds;
            float warningDistance = Mathf.Max(armDistanceM,
                Mathf.Max(minSpeedMps, speed) * neededLeadSeconds + 1f);
            if (forwardDistance > warningDistance) return;

            _armed = false;
            StartSequence(playerBody, playerTransform);
        }

        void OnTriggerEnter(Collider other)
        {
            if (!_armed || !useTrigger) return;

            Rigidbody rb = other.attachedRigidbody;
            if (!IsPlayerCollider(other, rb)) return;

            _armed = false;
            Transform playerTransform = player != null
                ? player : rb != null ? rb.transform : other.transform;
            _sequence = StartCoroutine(WaitUntilWarningDistance(rb, playerTransform));
        }

        bool IsPlayerCollider(Collider other, Rigidbody rb)
        {
            if (player != null)
            {
                Transform candidate = rb != null ? rb.transform : other.transform;
                return candidate == player || candidate.IsChildOf(player)
                    || other.transform == player || other.transform.IsChildOf(player);
            }
            return other.CompareTag(playerTag) || (rb != null && rb.CompareTag(playerTag));
        }

        IEnumerator WaitUntilWarningDistance(Rigidbody rb, Transform playerTransform)
        {
            float outerDistance = Mathf.Max(armDistanceM, triggerDistanceM);
            float fallTime = Mathf.Sqrt(2f * Mathf.Max(0.5f, spawnHeightM) / 9.81f);
            float neededLeadSeconds = fallTime + landMarginSeconds + minBigDelaySeconds;

            while (playerTransform != null)
            {
                Vector3 playerPosition = rb != null ? rb.position : playerTransform.position;
                float distance = Vector3.Dot(transform.position - playerPosition, transform.forward);
                float speed = rb != null ? GetSpeed(rb) : 0f;

                // Un arrêt ou une marche arrière ne déclenche pas les rochers.
                if (speed > 0.5f && distance > 0f)
                {
                    float speedAlongTrack = Vector3.Dot(
                        rb != null ? GetVelocity(rb) : playerTransform.forward * speed,
                        transform.forward);
                    if (speedAlongTrack > 0.25f)
                    {
                        float leadSpeed = Mathf.Max(minSpeedMps, speedAlongTrack);
                        float warningDistance = Mathf.Max(armDistanceM,
                            leadSpeed * neededLeadSeconds + 1f);
                        if (distance <= warningDistance)
                        {
                            StartSequence(rb, playerTransform);
                            yield break;
                        }
                    }
                }

                if (distance > outerDistance + 6f)
                {
                    _armed = true;
                    _sequence = null;
                    yield break;
                }
                yield return null;
            }

            _armed = true;
            _sequence = null;
        }

        void StartSequence(Rigidbody rb, Transform playerTransform)
        {
            onWarn?.Invoke(warningMessage);
            if (rumbleClip != null)
                AudioSource.PlayClipAtPoint(rumbleClip, transform.position, rumbleVolume);
            _sequence = StartCoroutine(Sequence(rb, playerTransform));
        }

        static Vector3 GetVelocity(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        static float GetSpeed(Rigidbody rb) => GetVelocity(rb).magnitude;

        IEnumerator Sequence(Rigidbody rb, Transform playerTransform)
        {
            float fallTime = Mathf.Sqrt(2f * Mathf.Max(0.5f, spawnHeightM) / 9.81f);
            float elapsed = 0f;
            float nextPebbleTime = 0f;
            float outerDistance = Mathf.Max(armDistanceM, triggerDistanceM);

            // Maintient le danger lisible si le joueur ralentit après l'alerte :
            // les gros rochers ne partent qu'à la bonne distance selon sa vitesse courante.
            while (playerTransform != null)
            {
                Vector3 playerPosition = rb != null ? rb.position : playerTransform.position;
                float distance = progressFrame != null
                    ? progressTargetZ - progressFrame.InverseTransformPoint(playerPosition).z
                    : Vector3.Dot(transform.position - playerPosition, transform.forward);
                Vector3 velocity = rb != null ? GetVelocity(rb) : playerTransform.forward * 0.5f;
                float speedAlongTrack = progressFrame != null
                    ? progressFrame.InverseTransformDirection(velocity).z
                    : Vector3.Dot(velocity, transform.forward);

                if (distance > outerDistance + 6f)
                {
                    _armed = true;
                    _sequence = null;
                    yield break;
                }

                if (elapsed < maxPebbleWarningSeconds && elapsed >= nextPebbleTime)
                {
                    SpawnPebbles();
                    nextPebbleTime = elapsed + 0.3f;
                }

                float fallLeadDistance = Mathf.Max(0f, speedAlongTrack)
                    * (fallTime + landMarginSeconds);
                if (elapsed >= minBigDelaySeconds
                    && (distance <= fallLeadDistance || distance <= 0f))
                    break;

                yield return new WaitForSeconds(0.05f);
                elapsed += 0.05f;
            }

            // Chute des gros rochers
            onRocksFall?.Invoke();
            SpawnBigAndMedium();
            _sequence = null;
        }

        // ------------------------------------------------------------------
        // Génération des rochers
        // ------------------------------------------------------------------
        float Range(float a, float b)
        {
            return a + (float)_rng.NextDouble() * (b - a);
        }

        float PebbleRange(float a, float b)
        {
            return a + (float)_pebbleRng.NextDouble() * (b - a);
        }

        Vector3 DropPoint(float lateral, float forward, float height)
        {
            return transform.position + transform.right * lateral + transform.forward * forward + Vector3.up * height;
        }

        void SpawnPebbles()
        {
            for (int i = 0; i < pebblesPerBatch; i++)
            {
                float side = _pebbleRng.Next(2) == 0 ? -1f : 1f;
                float lateral = side * (roadWidthM * 0.5f + 0.5f);
                float forward = PebbleRange(-4f, 4f);
                var pebble = SpawnRock(DropPoint(lateral, forward, spawnHeightM * 0.8f),
                                       PebbleRange(pebbleSizeM.x, pebbleSizeM.y), PrimitiveType.Sphere);
                var rb = pebble.GetComponent<Rigidbody>();
                rb.AddForce(-side * transform.right * rb.mass * 1.5f, ForceMode.Impulse);
            }
        }

        void SpawnBigAndMedium()
        {
            // Gros rochers : un de chaque côté, décalés pour former un slalom
            float side = _rng.Next(2) == 0 ? -1f : 1f;
            for (int i = 0; i < bigRockCount; i++)
            {
                float forward = (i - (bigRockCount - 1) * 0.5f) * 10f;
                float lateral = side * roadWidthM * 0.3f;
                SpawnRock(DropPoint(lateral, forward, spawnHeightM),
                          Range(bigSizeM.x, bigSizeM.y), PrimitiveType.Cube);
                side = -side;
            }

            // Rochers moyens : des bosses franchissables (qui abîment la cargaison)
            float half = Mathf.Max(0.5f, roadWidthM * 0.5f - 0.4f);
            for (int i = 0; i < mediumRockCount; i++)
            {
                SpawnRock(DropPoint(Range(-half, half), Range(-8f, 8f), spawnHeightM),
                          Range(mediumSizeM.x, mediumSizeM.y), PrimitiveType.Cube);
            }
        }

        GameObject SpawnRock(Vector3 position, float diameter, PrimitiveType placeholderShape)
        {
            Quaternion rotation = Quaternion.Euler(Range(0f, 360f), Range(0f, 360f), Range(0f, 360f));
            GameObject rock = rockPrefab != null
                ? Instantiate(rockPrefab, position, rotation)
                : GameObject.CreatePrimitive(placeholderShape);

            rock.name = "Rock";
            rock.transform.SetPositionAndRotation(position, rotation);
            rock.transform.localScale = new Vector3(diameter * Range(0.85f, 1.15f),
                                                    diameter * Range(0.7f, 1f),
                                                    diameter * Range(0.85f, 1.15f));

            Collider[] colliders = rock.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0)
                colliders = new Collider[] { rock.AddComponent<BoxCollider>() };
            foreach (Collider collider in colliders)
            {
                collider.enabled = true;
                collider.isTrigger = false;
                if (collider is MeshCollider meshCollider) meshCollider.convex = true;
            }

            Rigidbody rb = rock.GetComponent<Rigidbody>();
            if (rb == null) rb = rock.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.mass = Mathf.Max(1f, diameter * diameter * diameter * rockDensity * 0.6f);
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.AddTorque(new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f)) * rb.mass * 0.1f,
                         ForceMode.Impulse);

            FallingRock fr = rock.GetComponent<FallingRock>();
            if (fr == null) fr = rock.AddComponent<FallingRock>();
            fr.owner = this;

            _rocks.Add(rock);
            Destroy(rock, rockLifetimeSeconds);
            return rock;
        }

        /// <summary>Appelé par FallingRock quand un rocher entre en collision.</summary>
        public void NotifyCollision(FallingRock rock, Collision collision)
        {
            if (rock.hasHitPlayer) return;

            Rigidbody rb = collision.rigidbody;
            bool isPlayer;
            if (player != null)
            {
                Transform candidate = rb != null ? rb.transform : collision.collider.transform;
                isPlayer = candidate == player || candidate.IsChildOf(player)
                    || collision.collider.transform == player
                    || collision.collider.transform.IsChildOf(player);
            }
            else
            {
                isPlayer = collision.collider.CompareTag(playerTag)
                    || (rb != null && rb.CompareTag(playerTag));
            }
            if (!isPlayer) return;

            float impact = collision.relativeVelocity.magnitude;
            if (impact < minImpactSpeed) return;

            rock.hasHitPlayer = true;
            onPlayerHit?.Invoke(impact);
        }

        // ------------------------------------------------------------------
        // Aperçu dans la scène
        // ------------------------------------------------------------------
        void OnDrawGizmosSelected()
        {
            var old = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.color = new Color(1f, 0.6f, 0f, 1f);
            float detectionLength = Mathf.Max(armDistanceM, triggerDistanceM);
            Gizmos.DrawWireCube(new Vector3(0f, 3f, -detectionLength * 0.5f),
                                new Vector3(roadWidthM + 4f, 6f, detectionLength));

            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(new Vector3(0f, spawnHeightM * 0.5f, 0f),
                                new Vector3(roadWidthM, spawnHeightM, 20f));

            Gizmos.matrix = old;
        }
    }
}
