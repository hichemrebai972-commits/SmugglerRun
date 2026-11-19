using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public Transform target;

    [Tooltip("Position par rapport à la voiture (x droite, y haut, z avant)")]
    public Vector3 offset = new Vector3(0f, 4.1f, -7.1f);
    public float lookHeight = 1.8f;
    [Tooltip("Vitesse à laquelle la caméra se tourne derrière la voiture")]
    public float yawSmooth = 4f;
    [Tooltip("Temps de rattrapage de la caméra : une valeur plus élevée donne une caméra plus lourde.")]
    public float followSmoothTime = 0.14f;
    [Tooltip("Élargissement léger du champ de vision à grande vitesse.")]
    public float speedFovBoost = 5f;
    public float lookAheadAtTopSpeed = 5.2f;
    [Tooltip("Anticipe la rotation de la voiture pour révéler le prochain virage.")]
    [Range(0f, 1f)] public float turnAnticipation = 0.42f;
    [Range(0f, 30f)] public float maxTurnAnticipation = 16f;
    [Tooltip("Inclinaison discrète de la caméra pendant les virages, en degrés.")]
    [Range(0f, 6f)] public float cornerTilt = 3f;

    float currentYaw;
    float originalFov;
    Vector3 cameraVelocity;
    Camera targetCamera;
    Rigidbody targetBody;
    float shakeUntil;
    float shakeDuration;
    float shakeAmplitude;

    /// <summary>Secoue très légèrement la caméra pour un avertissement de terrain ou un choc.</summary>
    public void TriggerShake(float amplitude, float duration)
    {
        if (duration <= 0f || amplitude <= 0f) return;
        shakeDuration = Mathf.Max(shakeDuration, duration);
        shakeAmplitude = Mathf.Max(shakeAmplitude, amplitude);
        shakeUntil = Mathf.Max(shakeUntil, Time.unscaledTime + duration);
    }

    void Start()
    {
        if (target != null)
        {
            currentYaw = target.eulerAngles.y;
            targetBody = target.GetComponent<Rigidbody>();
        }
        targetCamera = GetComponent<Camera>();
        if (targetCamera != null) originalFov = targetCamera.fieldOfView;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // La caméra tourne doucement derrière la voiture (axe vertical seulement)
        float predictedTurn = targetBody != null
            ? Mathf.Clamp(targetBody.angularVelocity.y * turnAnticipation, -maxTurnAnticipation, maxTurnAnticipation)
            : 0f;
        float desiredYaw = target.eulerAngles.y + predictedTurn;
        currentYaw = Mathf.LerpAngle(currentYaw, desiredYaw, yawSmooth * Time.deltaTime);
        Quaternion yawRotation = Quaternion.Euler(0f, currentYaw, 0f);

        // Distance fixe derrière la voiture : pas de retard, même à grande vitesse
        Vector3 desiredPosition = target.position + yawRotation * offset;
        float shakeRemaining = shakeUntil - Time.unscaledTime;
        if (shakeRemaining > 0f)
        {
            float fade = Mathf.Clamp01(shakeRemaining / Mathf.Max(shakeDuration, 0.01f));
            float t = Time.unscaledTime;
            Vector3 shake = new Vector3(
                Mathf.Sin(t * 31f) + Mathf.Sin(t * 47f + 0.8f),
                Mathf.Sin(t * 39f + 1.3f),
                Mathf.Sin(t * 27f + 2.1f)) * (shakeAmplitude * 0.5f * fade);
            desiredPosition += shake;
        }
        else
        {
            shakeAmplitude = 0f;
            shakeDuration = 0f;
        }
        transform.position = Vector3.SmoothDamp(
            transform.position, desiredPosition, ref cameraVelocity, followSmoothTime);

        float speed = targetBody != null ? targetBody.linearVelocity.magnitude : 0f;
        float speedFactor = Mathf.Clamp01(speed / (140f / 3.6f));
        Vector3 anticipatedForward = Quaternion.Euler(0f, predictedTurn, 0f) * target.forward;
        Vector3 lookPoint = target.position + Vector3.up * lookHeight
                          + anticipatedForward * (lookAheadAtTopSpeed * speedFactor);
        Quaternion desiredRotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
        if (targetBody != null && cornerTilt > 0f)
        {
            float turnRate = targetBody.angularVelocity.y;
            float desiredTilt = Mathf.Clamp(-turnRate * cornerTilt, -cornerTilt, cornerTilt);
            desiredRotation *= Quaternion.Euler(0f, 0f, desiredTilt);
        }
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation,
            1f - Mathf.Exp(-yawSmooth * Time.deltaTime));

        if (targetCamera != null)
            targetCamera.fieldOfView = originalFov + speedFovBoost * speedFactor;
    }
}
