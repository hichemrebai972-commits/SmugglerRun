using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

namespace SmugglerRun.Track
{
    /// <summary>
    /// Pont suspendu dont le tablier s'effondre derrière le camion.
    /// Quand le camion (tag "Player") entre sur le pont, un "front d'effondrement" part derrière lui et avance
    /// à collapseSpeedKmh : les planches qu'il dépasse tombent, celles juste devant tremblent (avertissement visuel).
    /// Si le camion roule moins vite que le front, il le rattrape et tombe dans le ravin.
    /// Le tablier est construit au lancement du jeu (Play) et à chaque appel de ResetHazard().
    /// Axes : l'axe Z local pointe dans le sens de la piste, le dessus du tablier est à Y = 0 (le TrackGenerator s'en charge).
    /// </summary>
    [DisallowMultipleComponent]
    public class BridgeHazard : MonoBehaviour
    {
        [Serializable] public class StringEvent : UnityEvent<string> { }

        class Item
        {
            public Transform t;
            public Vector3 basePos;
            public Quaternion baseRot;
            public float z;
            public float mass;
        }

        [Header("Pont")]
        public float lengthM = 120f;
        public float widthM = 4f;
        public float plankLengthM = 0.5f;
        public float plankGapM = 0.02f;
        public float plankThicknessM = 0.14f;
        [Tooltip("Courbure légère du tablier suspendu.")]
        public float sagAmplitudeM = 0.48f;
        [Tooltip("Différence de hauteur entre le début et la fin du tablier.")]
        public float endHeightOffsetM;
        [Tooltip("Le tablier dépasse de chaque côté pour bien rejoindre la route.")]
        public float deckOverlapM = 1f;
        public bool buildRails = true;
        public float railSpacingM = 12f;
        [Tooltip("Optionnel. Vide = matériau par défaut du pipeline de rendu.")]
        public Material plankMaterial;
        public Material railMaterial;

        [Header("Approche")]
        [Min(0f)] public float warningDistanceM = 50f;
        public string approachMessage = "PONT FRAGILE • ACCÉLÈRE";

        [Header("Effondrement")]
        [Tooltip("Vitesse du front d'effondrement : le joueur doit rouler plus vite pour traverser.")]
        public float collapseSpeedKmh = 45f;
        [Tooltip("Avance du joueur au moment où il entre sur le pont (le front démarre à cette distance derrière lui).")]
        public float headStartM = 20f;
        [Tooltip("Les planches situées à cette distance devant le front tremblent.")]
        public float crackZoneM = 6f;
        public float shakeAmplitudeM = 0.03f;
        public float debrisLifetimeSeconds = 6f;

        [Header("Joueur")]
        public Transform player;
        public string playerTag = "Player";
        [Tooltip("Hauteur locale sous laquelle le camion est considéré comme tombé.")]
        public float fallThresholdY = -1.5f;

        [Header("Alerte et sons")]
        public string collapseMessage = "LE PONT CÈDE • ACCÉLÈRE !";
        public AudioClip creakClip;
        [Range(0f, 1f)] public float creakVolume = 1f;

        [Header("Événements")]
        [Tooltip("Alerte envoyée à l'entrée de la zone, avant le pont.")]
        public StringEvent onApproach = new StringEvent();
        [Tooltip("Reçoit le message au début de l'effondrement : HUD, secousse de caméra, musique...")]
        public StringEvent onCollapseStart = new StringEvent();
        [Tooltip("Déclenché quand le camion tombe du pont : écran d'échec 'chute mortelle'.")]
        public UnityEvent onPlayerFell = new UnityEvent();

        [NonSerialized] Func<float, Vector3> _deckPositionAtProgress;
        [NonSerialized] Func<float, Quaternion> _deckRotationAtProgress;
        [NonSerialized] public Transform progressFrame;
        [NonSerialized] public float progressStartZ;
        readonly List<Item> _items = new List<Item>();
        BoxCollider _trigger;
        bool _started;
        bool _collapsing;
        bool _fellFired;
        bool _warned;
        Transform _activePlayer;
        float _frontZ;
        int _next;

        /// <summary>Appelé par le TrackGenerator pour régler la largeur et la longueur du pont.</summary>
        public void Configure(float width, float length)
        {
            widthM = width;
            lengthM = length;
        }

        public void SetDeckPath(Func<float, Vector3> positionAtProgress,
            Func<float, Quaternion> rotationAtProgress)
        {
            _deckPositionAtProgress = positionAtProgress;
            _deckRotationAtProgress = rotationAtProgress;
        }

        void Start()
        {
            BuildTrigger();
            // ResetPursuit may already have built the deck earlier in this frame.
            if (_items.Count == 0) ResetHazard();
        }

        void BuildTrigger()
        {
            if (_trigger != null) Destroy(_trigger);
            _trigger = gameObject.AddComponent<BoxCollider>();
            _trigger.isTrigger = true;
            // Volume qui englobe le pont et l'espace dessous (Y de -7 à +3), 3 m avant et après
            _trigger.center = new Vector3(0f, -2f,
                (lengthM - warningDistanceM + 6f) * 0.5f);
            _trigger.size = new Vector3(widthM + 16f, 10f,
                lengthM + warningDistanceM + 6f);
        }

        /// <summary>Reconstruit le tablier et réarme l'effondrement (à appeler au redémarrage d'une tentative).</summary>
        [ContextMenu("Réarmer le pont")]
        public void ResetHazard()
        {
            _started = false;
            _collapsing = false;
            _fellFired = false;
            _warned = false;
            _activePlayer = player;
            _frontZ = -headStartM;
            _next = 0;
            BuildDeck();
        }

        // ------------------------------------------------------------------
        // Construction du tablier
        // ------------------------------------------------------------------
        void BuildDeck()
        {
            var old = transform.Find("Deck");
            if (old != null)
            {
                // Une planche déjà tombée n'est plus enfant de Deck : il faut aussi la supprimer.
                for (int i = 0; i < _items.Count; i++)
                {
                    Transform item = _items[i].t;
                    if (item == null || item.parent == old) continue;
                    if (Application.isPlaying) Destroy(item.gameObject);
                    else DestroyImmediate(item.gameObject);
                }
                old.gameObject.SetActive(false);
                old.name = "Deck_old";
                if (Application.isPlaying) Destroy(old.gameObject);
                else DestroyImmediate(old.gameObject);
            }

            _items.Clear();
            var deck = new GameObject("Deck").transform;
            deck.SetParent(transform, false);

            float total = lengthM + 2f * deckOverlapM;
            float pitch = Mathf.Max(0.1f, plankLengthM + plankGapM);
            int count = Mathf.CeilToInt(total / pitch);

            for (int i = 0; i < count; i++)
            {
                float z = -deckOverlapM + i * pitch + plankLengthM * 0.5f;
                float progress = Mathf.Clamp(z + deckOverlapM, 0f, lengthM);
                float t = Mathf.Clamp01(progress / Mathf.Max(1f, lengthM));
                float sag = sagAmplitudeM * Mathf.Sin(Mathf.PI * t);
                Transform plank = MakeCube("Plank", deck,
                    new Vector3(0f, -plankThicknessM * 0.5f + endHeightOffsetM * t - sag, z),
                    new Vector3(widthM, plankThicknessM, plankLengthM), true, plankMaterial);
                if (_deckPositionAtProgress != null)
                {
                    plank.position = _deckPositionAtProgress(progress);
                    plank.rotation = _deckRotationAtProgress != null
                        ? _deckRotationAtProgress(progress) : transform.rotation;
                }
                _items.Add(new Item
                {
                    t = plank, basePos = plank.localPosition, baseRot = plank.localRotation,
                    z = progress, mass = 25f
                });
            }

            if (buildRails)
            {
                float spacing = Mathf.Max(3f, railSpacingM);
                float endZ = lengthM + deckOverlapM;
                for (float zPost = -deckOverlapM; zPost <= endZ + 0.01f; zPost += spacing)
                {
                    for (int s = 0; s < 2; s++)
                    {
                        float x = (s == 0 ? -1f : 1f) * (widthM * 0.5f + 0.1f);

                        Transform post = MakeCube("Post", deck, new Vector3(x, 0.55f, zPost),
                            new Vector3(0.1f, 1.1f, 0.1f), false, railMaterial);
                        _items.Add(new Item
                        {
                            t = post, basePos = post.localPosition, baseRot = post.localRotation,
                            z = zPost, mass = 8f
                        });

                        float segLen = Mathf.Min(spacing, endZ - zPost);
                        if (segLen > 0.2f)
                        {
                            float zc = zPost + segLen * 0.5f;
                            Transform rope = MakeCube("Rope", deck, new Vector3(x, 1.0f, zc),
                                new Vector3(0.05f, 0.05f, segLen), false, railMaterial);
                            _items.Add(new Item
                            {
                                t = rope, basePos = rope.localPosition, baseRot = rope.localRotation,
                                z = zc, mass = 5f
                            });
                        }
                    }
                }
            }

            _items.Sort((a, b) => a.z.CompareTo(b.z));
        }

        Transform MakeCube(string objName, Transform parent, Vector3 localPos, Vector3 size, bool keepCollider, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = objName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;

            if (!keepCollider)
            {
                Collider c = go.GetComponent<Collider>();
                if (Application.isPlaying) Destroy(c);
                else DestroyImmediate(c);
            }

            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        // ------------------------------------------------------------------
        // Détection du joueur
        // ------------------------------------------------------------------
        bool IsPlayer(Collider other, Rigidbody rb)
        {
            if (player != null)
            {
                Transform candidate = rb != null ? rb.transform : other.transform;
                return candidate == player || candidate.IsChildOf(player)
                    || other.transform == player || other.transform.IsChildOf(player);
            }
            return other.CompareTag(playerTag) || (rb != null && rb.CompareTag(playerTag));
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other, other.attachedRigidbody)) return;
            _activePlayer = player != null ? player
                : other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform;
            if (!_warned)
            {
                _warned = true;
                onApproach?.Invoke(approachMessage);
            }
            TryStartCollapseAtDeckEntry();
        }

        /// <summary>Progression précise fournie par le générateur actif, même sur une route courbe.</summary>
        public void CheckApproach(Transform playerTransform, float distanceToBridge, float progressFromStart)
        {
            if (_started || playerTransform == null) return;
            _activePlayer = playerTransform;
            if (!_warned && distanceToBridge >= 0f && distanceToBridge <= warningDistanceM)
            {
                _warned = true;
                onApproach?.Invoke(approachMessage);
            }
            if (_warned && progressFromStart >= 0f && progressFromStart <= lengthM + 2f)
                StartCollapse(progressFromStart);
        }

        void Update()
        {
            if (!_started && _warned && _activePlayer != null)
                TryStartCollapseAtDeckEntry();
        }

        void TryStartCollapseAtDeckEntry()
        {
            if (_started || _activePlayer == null) return;
            float progress = progressFrame != null
                ? progressFrame.InverseTransformPoint(_activePlayer.position).z - progressStartZ
                : transform.InverseTransformPoint(_activePlayer.position).z;
            if (progress >= 0f && progress <= lengthM + 2f)
                StartCollapse(progress);
        }

        void OnTriggerStay(Collider other)
        {
            if (_fellFired) return;
            Rigidbody rb = other.attachedRigidbody;
            if (!IsPlayer(other, rb)) return;

            Vector3 worldPos = rb != null ? rb.position : other.transform.position;
            Vector3 local = transform.InverseTransformPoint(worldPos);
            if (local.y < fallThresholdY)
            {
                _fellFired = true;
                onPlayerFell?.Invoke();
            }
        }

        void StartCollapse(float playerLocalZ)
        {
            _started = true;
            _collapsing = true;
            _frontZ = playerLocalZ - headStartM;
            onCollapseStart?.Invoke(collapseMessage);
            if (creakClip != null)
                AudioSource.PlayClipAtPoint(creakClip, transform.TransformPoint(new Vector3(0f, 0f, lengthM * 0.5f)), creakVolume);
        }

        // ------------------------------------------------------------------
        // Effondrement
        // ------------------------------------------------------------------
        void FixedUpdate()
        {
            if (!_collapsing) return;

            _frontZ += (collapseSpeedKmh / 3.6f) * Time.fixedDeltaTime;

            // Les éléments dépassés par le front tombent
            while (_next < _items.Count && _items[_next].z <= _frontZ)
            {
                Drop(_items[_next]);
                _next++;
            }

            // Les éléments juste devant le front tremblent, de plus en plus fort
            for (int i = _next; i < _items.Count && _items[i].z <= _frontZ + crackZoneM; i++)
            {
                Item it = _items[i];
                if (it.t == null) continue;
                float intensity = 1f - Mathf.Clamp01((it.z - _frontZ) / Mathf.Max(0.1f, crackZoneM));
                float a = shakeAmplitudeM * intensity;
                it.t.localPosition = it.basePos + new Vector3(Random.Range(-a, a), Random.Range(-a, a), 0f);
            }

            if (_next >= _items.Count) _collapsing = false;
        }

        void Drop(Item it)
        {
            if (it.t == null) return;

            it.t.localPosition = it.basePos;
            it.t.localRotation = it.baseRot;
            it.t.SetParent(null, true);

            Rigidbody rb = it.t.gameObject.AddComponent<Rigidbody>();
            rb.mass = it.mass;
            rb.AddForce(new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * it.mass * 0.3f, ForceMode.Impulse);
            rb.AddTorque(new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * it.mass * 0.2f,
                         ForceMode.Impulse);

            Destroy(it.t.gameObject, debrisLifetimeSeconds);
        }

        // ------------------------------------------------------------------
        // Aperçu dans la scène
        // ------------------------------------------------------------------
        void OnDrawGizmosSelected()
        {
            var old = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(new Vector3(0f, -plankThicknessM * 0.5f, lengthM * 0.5f),
                                new Vector3(widthM, plankThicknessM, lengthM));

            Gizmos.color = new Color(1f, 0.6f, 0f, 1f);
            Gizmos.DrawWireCube(new Vector3(0f, -2f, lengthM * 0.5f),
                                new Vector3(widthM + 8f, 10f, lengthM + 6f));

            Gizmos.color = Color.red;
            Gizmos.DrawLine(new Vector3(-widthM, 0.5f, -headStartM), new Vector3(widthM, 0.5f, -headStartM));

            Gizmos.matrix = old;
        }
    }
}
