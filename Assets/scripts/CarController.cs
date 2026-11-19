using UnityEngine;

/// <summary>
/// Conduite d'une voiture avec WheelCollider (Unity 6), clavier et manette.
/// Clavier : WASD ou flèches, Espace = frein renforcé, R = redresser le pickup.
/// Manette : stick gauche, gâchettes RT/LT, bouton A/Croix = frein renforcé.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("WheelColliders")]
    public WheelCollider frontLeft;
    public WheelCollider frontRight;
    public WheelCollider rearLeft;
    public WheelCollider rearRight;

    [Header("Roues du modèle 3D (l'objet qui contient le pneu et la jante)")]
    public Transform frontLeftVisual;
    public Transform frontRightVisual;
    public Transform rearLeftVisual;
    public Transform rearRightVisual;
    [Tooltip("Monte (+) ou descend (-) les roues visibles, en mètres, si besoin")]
    public float wheelVisualHeightOffset = 0f;

    [Header("Moteur et freins")]
    [Tooltip("Couple moteur TOTAL, réparti sur les roues motrices")]
    public float motorTorque = 7000f;
    public float brakeTorque = 3000f;
    [Tooltip("Couple de frein appliqué aux quatre roues par Espace, pour éviter le tête-à-queue du frein à main.")]
    public float handbrakeTorque = 8000f;
    public float engineBrakeTorque = 150f;
    public bool allWheelDrive = true;
    public float maxSpeedKmh = 140f;
    public float maxReverseSpeedKmh = 30f;

    [Header("Direction")]
    public float maxSteerAngle = 32f;
    public float minSteerAngleAtTopSpeed = 8f;
    [Tooltip("Vitesse de réaction de la direction. Une valeur plus basse rend le pickup plus lourd.")]
    public float steeringResponse = 3.5f;

    [Header("Stabilité")]
    public Vector3 centerOfMass = new Vector3(0f, 0.28f, 0f);
    public float downforce = 27f;
    [Tooltip("Réduit l'inclinaison de la carrosserie dans les virages. Mettre 0 pour désactiver.")]
    public float frontAntiRoll = 1850f;
    public float rearAntiRoll = 1450f;
    [Tooltip("Freine le déplacement latéral de la caisse pour empêcher les longs drifts.")]
    [Range(0f, 5f)] public float lateralVelocityDamping = 2.4f;

    Rigidbody rb;
    float throttleInput;
    float steerInput;
    float steerCommand;
    bool handbrakeInput;

    WheelCollider[] cols;
    Transform[] visuals;
    Quaternion[] rotOffsets;
    Vector3[] stockWheelPositions;
    float damageFactor = 1f;

    public float SpeedKmh => rb.linearVelocity.magnitude * 3.6f;
    public bool IsHandbraking => handbrakeInput;
    public float ThrottleInput => throttleInput;

    /// <summary>Réduit progressivement les performances quand le pickup est endommagé.</summary>
    public void SetDamageCondition(float condition01)
    {
        // Les dégâts restent sensibles, mais n'imposent plus une grosse perte de vitesse
        // après quelques secousses sur la piste.
        damageFactor = Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(condition01));
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = centerOfMass;

        cols = new[] { frontLeft, frontRight, rearLeft, rearRight };
        visuals = new[] { frontLeftVisual, frontRightVisual, rearLeftVisual, rearRightVisual };
        rotOffsets = new Quaternion[4];
        stockWheelPositions = new Vector3[4];

        for (int i = 0; i < 4; i++)
        {
            rotOffsets[i] = Quaternion.identity;
            if (cols[i] != null) stockWheelPositions[i] = cols[i].transform.localPosition;
            if (visuals[i] == null || cols[i] == null) continue;

            // Les roues visibles ne doivent pas avoir de collision
            foreach (var c in visuals[i].GetComponentsInChildren<Collider>())
                c.enabled = false;

            // Orientation de la roue du modèle par rapport au WheelCollider
            rotOffsets[i] = Quaternion.Inverse(cols[i].transform.rotation) * visuals[i].rotation;
        }
    }

    /// <summary>Abaisse légèrement les roues et la caisse quand elles s'enfoncent dans le sable.</summary>
    public void SetSandSinkDepth(float depth)
    {
        if (cols == null || stockWheelPositions == null) return;
        depth = Mathf.Clamp(depth, 0f, 0.25f);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null) continue;
            Vector3 position = stockWheelPositions[i];
            position.y -= depth;
            cols[i].transform.localPosition = position;
        }
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        var gamepad = UnityEngine.InputSystem.Gamepad.current;
        bool gamepadActive = gamepad != null &&
            (gamepad.leftStick.ReadValue().sqrMagnitude > 0.01f
             || gamepad.rightTrigger.isPressed || gamepad.leftTrigger.isPressed
             || gamepad.buttonSouth.isPressed || gamepad.buttonEast.wasPressedThisFrame);
        if (gamepadActive)
        {
            throttleInput = gamepad.rightTrigger.ReadValue() - gamepad.leftTrigger.ReadValue();
            steerCommand = gamepad.leftStick.ReadValue().x;
            handbrakeInput = gamepad.buttonSouth.isPressed;
            if (gamepad.buttonEast.wasPressedThisFrame) ResetCar();
        }
        else if (kb != null)
        {
            throttleInput = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                          - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
            steerCommand = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                         - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            handbrakeInput = kb.spaceKey.isPressed;
            if (kb.rKey.wasPressedThisFrame) ResetCar();
        }
        else
        {
            throttleInput = 0f;
            steerCommand = 0f;
            handbrakeInput = false;
        }
#else
        throttleInput = Input.GetAxis("Vertical");
        steerCommand = Input.GetAxis("Horizontal");
        handbrakeInput = Input.GetKey(KeyCode.Space);
        if (Input.GetKeyDown(KeyCode.R)) ResetCar();
#endif
        UpdateWheelVisuals();
    }

    void FixedUpdate()
    {
        steerInput = Mathf.MoveTowards(steerInput, steerCommand, steeringResponse * Time.fixedDeltaTime);
        ApplyDrive();
        ApplySteering();
        ApplyLateralStability();
        ApplyAntiRoll(frontLeft, frontRight, frontAntiRoll);
        ApplyAntiRoll(rearLeft, rearRight, rearAntiRoll);
        ApplyDownforce();
    }

    void ApplyLateralStability()
    {
        if (lateralVelocityDamping <= 0f) return;
        int groundedWheels = 0;
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null && cols[i].GetGroundHit(out _)) groundedWheels++;
        if (groundedWheels < 2) return;

        float lateralSpeed = Vector3.Dot(rb.linearVelocity, transform.right);
        rb.AddForce(-transform.right * lateralSpeed * lateralVelocityDamping,
            ForceMode.Acceleration);
    }

    void ApplyAntiRoll(WheelCollider left, WheelCollider right, float stiffness)
    {
        if (left == null || right == null || stiffness <= 0f) return;

        bool groundedLeft = left.GetGroundHit(out WheelHit hitLeft);
        bool groundedRight = right.GetGroundHit(out WheelHit hitRight);
        float travelLeft = groundedLeft ? GetSuspensionTravel(left, hitLeft) : 1f;
        float travelRight = groundedRight ? GetSuspensionTravel(right, hitRight) : 1f;
        float force = (travelLeft - travelRight) * stiffness;

        if (groundedLeft)
            rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
        if (groundedRight)
            rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
    }

    static float GetSuspensionTravel(WheelCollider wheel, WheelHit hit)
    {
        if (wheel.suspensionDistance <= 0f) return 0f;
        float localY = wheel.transform.InverseTransformPoint(hit.point).y;
        return Mathf.Clamp01((-localY - wheel.radius) / wheel.suspensionDistance);
    }

    void ApplyDrive()
    {
        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);
        float speedKmh = SpeedKmh;
        float motor = 0f;
        float brake = 0f;

        if (throttleInput > 0.01f)
        {
            if (forwardSpeed < -1f) brake = brakeTorque;
            else if (speedKmh < maxSpeedKmh * damageFactor) motor = throttleInput * motorTorque * damageFactor;
        }
        else if (throttleInput < -0.01f)
        {
            if (forwardSpeed > 1f) brake = brakeTorque * -throttleInput;
            else if (speedKmh < maxReverseSpeedKmh * damageFactor) motor = throttleInput * motorTorque * 0.5f * damageFactor;
        }
        else
        {
            brake = engineBrakeTorque;
        }

        float perWheel = allWheelDrive ? motor / 4f : motor / 2f;
        rearLeft.motorTorque = perWheel;
        rearRight.motorTorque = perWheel;
        frontLeft.motorTorque = allWheelDrive ? perWheel : 0f;
        frontRight.motorTorque = allWheelDrive ? perWheel : 0f;

        float allWheelBrake = handbrakeInput
            ? Mathf.Max(brake, Mathf.Min(handbrakeTorque, brakeTorque))
            : brake;
        frontLeft.brakeTorque = allWheelBrake;
        frontRight.brakeTorque = allWheelBrake;
        rearLeft.brakeTorque = allWheelBrake;
        rearRight.brakeTorque = allWheelBrake;
    }

    void ApplySteering()
    {
        float t = Mathf.Clamp01(SpeedKmh / maxSpeedKmh);
        float maxAngle = Mathf.Lerp(maxSteerAngle, minSteerAngleAtTopSpeed, t) * Mathf.Lerp(0.78f, 1f, damageFactor);
        float angle = steerInput * maxAngle;
        frontLeft.steerAngle = angle;
        frontRight.steerAngle = angle;
    }

    void ApplyDownforce()
    {
        rb.AddForce(-transform.up * downforce * rb.linearVelocity.magnitude);
    }

    void UpdateWheelVisuals()
    {
        for (int i = 0; i < 4; i++)
        {
            if (visuals[i] == null || cols[i] == null) continue;
            cols[i].GetWorldPose(out Vector3 pos, out Quaternion rot);
            visuals[i].SetPositionAndRotation(
                pos + transform.up * wheelVisualHeightOffset,
                rot * rotOffsets[i]);
        }
    }

    void ResetCar()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position += Vector3.up * 1.5f;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
    }

    /// <summary>
    /// Pratique : place les 4 WheelColliders exactement au centre des roues du modèle 3D.
    /// Utilisation : menu ⋮ du composant Car Controller > "Placer les WheelColliders sur les roues".
    /// </summary>
    [ContextMenu("Placer les WheelColliders sur les roues")]
    void SnapCollidersToWheels()
    {
        var cs = new[] { frontLeft, frontRight, rearLeft, rearRight };
        var vs = new[] { frontLeftVisual, frontRightVisual, rearLeftVisual, rearRightVisual };
        for (int i = 0; i < 4; i++)
        {
            if (cs[i] == null || vs[i] == null) continue;
            cs[i].transform.position = vs[i].position;
            cs[i].transform.rotation = transform.rotation;
        }
    }
}
