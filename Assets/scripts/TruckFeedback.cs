using UnityEngine;

/// <summary>Ajoute poussière derrière le pickup et des caisses qui bougent dans la benne.</summary>
[RequireComponent(typeof(Rigidbody))]
public class TruckFeedback : MonoBehaviour
{
    [Header("Poussière")]
    public float dustSpeedThreshold = 3f;
    public float maxDustPerSecond = 92f;
    [Range(0f, 0.15f)] public float slipDustBoost = 0.08f;

    [Header("Cargaison")]
    public Vector3 cargoCenter = new Vector3(0f, 1.35f, -1.55f);
    public float cargoSway = 0.014f;

    Rigidbody body;
    ParticleSystem dust;
    readonly ParticleSystem[] gravelSprays = new ParticleSystem[2];
    Mesh gravelMesh;
    Vector3 previousVelocity;
    readonly Transform[] crates = new Transform[3];
    readonly Vector3[] crateHome = new Vector3[3];
    readonly Vector3[] crateVelocity = new Vector3[3];
    readonly Quaternion[] crateRotation = new Quaternion[3];
    readonly Vector3[] impactRotation = new Vector3[3];
    readonly GameObject[,] damageMarks = new GameObject[3, 2];
    Material boxMaterial;
    Material strapMaterial;
    Material damageMaterial;
    Material dustMaterial;
    Material gravelMaterial;
    float cargoCondition = 1f;
    float surfaceDustFactor = 0.12f;
    float currentDustIntensity;
    float impactUntil;
    float lastGravelBurstTime;

    public bool DustCloudActive => currentDustIntensity > 0.45f;

    public void SetRoadSurfaceDust(float amount)
    {
        surfaceDustFactor = Mathf.Clamp(amount, 0f, 2f);
    }

    void Start()
    {
        body = GetComponent<Rigidbody>();
        previousVelocity = body.linearVelocity;
        CreateDust();
        CreateCargo();
        SetCargoCondition(cargoCondition);
    }

    public void SetCargoCondition(float condition)
    {
        cargoCondition = Mathf.Clamp01(condition);
        if (boxMaterial == null) return;
        float damage = 1f - cargoCondition;
        boxMaterial.color = Color.Lerp(new Color(0.48f, 0.29f, 0.14f), new Color(0.19f, 0.17f, 0.15f), damage);
        strapMaterial.color = Color.Lerp(new Color(0.12f, 0.09f, 0.06f), new Color(0.08f, 0.06f, 0.05f), damage);
        for (int i = 0; i < crates.Length; i++)
        {
            if (damageMarks[i, 0] != null) damageMarks[i, 0].SetActive(damage > 0.18f + i * 0.06f);
            if (damageMarks[i, 1] != null) damageMarks[i, 1].SetActive(damage > 0.42f + i * 0.06f);
        }
    }

    /// <summary>Fait tanguer les caisses au choc pour rendre les dégâts perceptibles.</summary>
    public void NotifyCargoImpact(float severity)
    {
        float strength = Mathf.Clamp(severity, 0f, 1f);
        if (strength <= 0f) return;
        impactUntil = Time.time + 0.42f;
        for (int i = 0; i < crates.Length; i++)
        {
            float variation = 0.75f + i * 0.14f;
            crateVelocity[i] += new Vector3(
                Random.Range(-0.9f, 0.9f), Random.Range(0.25f, 0.8f),
                Random.Range(-0.9f, 0.9f)) * strength * variation;
            impactRotation[i] = new Vector3(
                Random.Range(-18f, 18f), Random.Range(-12f, 12f),
                Random.Range(-22f, 22f)) * strength * variation;
        }
    }

    void LateUpdate()
    {
        if (body == null) return;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 acceleration = (body.linearVelocity - previousVelocity) / dt;
        previousVelocity = body.linearVelocity;
        Vector3 localAcceleration = transform.InverseTransformDirection(acceleration);

        if (dust != null)
        {
            ParticleSystem.EmissionModule emission = dust.emission;
            float speed = body.linearVelocity.magnitude;
            bool moving = speed > dustSpeedThreshold;
            float lateralSlip = Mathf.Abs(transform.InverseTransformDirection(body.linearVelocity).x);
            float intensity = moving
                ? Mathf.Clamp01(speed / 26f + lateralSlip * slipDustBoost) * surfaceDustFactor
                : 0f;
            currentDustIntensity = intensity;
            emission.rateOverTime = moving ? maxDustPerSecond * intensity : 0f;
        }

        float speedNow = body.linearVelocity.magnitude;
        if (surfaceDustFactor > 0.55f && speedNow > 5f
            && acceleration.magnitude > 8f && Time.time - lastGravelBurstTime > 0.22f)
        {
            lastGravelBurstTime = Time.time;
            for (int i = 0; i < gravelSprays.Length; i++)
                if (gravelSprays[i] != null) gravelSprays[i].Emit(Random.Range(3, 6));
        }

        for (int i = 0; i < crates.Length; i++)
        {
            if (crates[i] == null) continue;
            float weight = 0.75f + i * 0.12f;
            Vector3 shift = new Vector3(-localAcceleration.x, 0f, -localAcceleration.z)
                          * cargoSway * weight;
            shift.x = Mathf.Clamp(shift.x, -0.14f, 0.14f);
            shift.z = Mathf.Clamp(shift.z, -0.18f, 0.18f);
            Vector3 destination = crateHome[i] + shift;
            if (Time.time < impactUntil)
                destination += new Vector3(impactRotation[i].z, 0f, impactRotation[i].x) * 0.0045f;
            crates[i].localPosition = Vector3.SmoothDamp(
                crates[i].localPosition, destination, ref crateVelocity[i], 0.18f);

            float pitch = Mathf.Clamp(localAcceleration.x * 0.12f, -5f, 5f);
            float roll = Mathf.Clamp(-localAcceleration.z * 0.10f, -5f, 5f);
            Quaternion targetRotation = crateRotation[i] * Quaternion.Euler(pitch, 0f, roll);
            if (Time.time < impactUntil)
                targetRotation *= Quaternion.Euler(impactRotation[i] * Mathf.Clamp01((impactUntil - Time.time) / 0.42f));
            crates[i].localRotation = Quaternion.Slerp(crates[i].localRotation, targetRotation,
                1f - Mathf.Exp(-5f * dt));
        }
    }

    void CreateDust()
    {
        GameObject emitter = new GameObject("Rear wheel dust");
        emitter.transform.SetParent(transform, false);
        emitter.transform.localPosition = new Vector3(0f, 0.15f, -2.45f);
        emitter.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        dust = emitter.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = dust.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 4.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.0f, 2.6f);
        main.startColor = new Color(0.59f, 0.47f, 0.33f, 0.58f);
        main.maxParticles = 1000;

        ParticleSystem.EmissionModule emission = dust.emission;
        emission.rateOverTime = 0f;
        ParticleSystem.ShapeModule shape = dust.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 38f;
        shape.radius = 1.05f;

        ParticleSystem.LimitVelocityOverLifetimeModule limit = dust.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.25f;

        ParticleSystemRenderer renderer = dust.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        dustMaterial = SoftParticleMaterial.Create();
        if (dustMaterial != null) renderer.material = dustMaterial;

        gravelMaterial = MakeMaterial(new Color(0.40f, 0.35f, 0.28f, 1f));
        gravelMaterial.SetFloat("_Metallic", 0f);
        gravelMaterial.SetFloat("_Smoothness", 0.02f);
        GameObject pebbleTemplate = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        MeshFilter pebbleFilter = pebbleTemplate.GetComponent<MeshFilter>();
        if (pebbleFilter != null && pebbleFilter.sharedMesh != null)
            gravelMesh = Instantiate(pebbleFilter.sharedMesh);
        Destroy(pebbleTemplate);

        for (int i = 0; i < gravelSprays.Length; i++)
        {
            GameObject sprayObject = new GameObject("Gravel spray " + (i + 1));
            sprayObject.transform.SetParent(transform, false);
            sprayObject.transform.localPosition = new Vector3(i == 0 ? -0.95f : 0.95f, 0.18f, -1.75f);
            sprayObject.transform.localRotation = Quaternion.Euler(-12f, 180f, 0f);
            ParticleSystem spray = sprayObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule sprayMain = spray.main;
            sprayMain.loop = true;
            sprayMain.playOnAwake = false;
            sprayMain.simulationSpace = ParticleSystemSimulationSpace.World;
            sprayMain.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.15f);
            sprayMain.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5.2f);
            sprayMain.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.17f);
            sprayMain.startColor = new Color(0.55f, 0.48f, 0.37f, 0.95f);
            sprayMain.gravityModifier = 1.7f;
            sprayMain.maxParticles = 180;
            ParticleSystem.EmissionModule sprayEmission = spray.emission;
            sprayEmission.rateOverTime = 0f;
            ParticleSystem.ShapeModule sprayShape = spray.shape;
            sprayShape.shapeType = ParticleSystemShapeType.Cone;
            sprayShape.angle = 28f;
            sprayShape.radius = 0.12f;
            ParticleSystemRenderer sprayRenderer = spray.GetComponent<ParticleSystemRenderer>();
            sprayRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            sprayRenderer.mesh = gravelMesh;
            sprayRenderer.sharedMaterial = gravelMaterial;
            gravelSprays[i] = spray;
            spray.Play();
        }
    }

    void CreateCargo()
    {
        boxMaterial = MakeMaterial(new Color(0.48f, 0.29f, 0.14f));
        strapMaterial = MakeMaterial(new Color(0.12f, 0.09f, 0.06f));
        damageMaterial = MakeMaterial(new Color(0.10f, 0.075f, 0.055f));
        Vector3[] offsets =
        {
            new Vector3(-0.36f, 0f, 0f),
            new Vector3(0.32f, 0.02f, -0.12f),
            new Vector3(-0.03f, 0.48f, -0.04f)
        };
        Vector3[] sizes =
        {
            new Vector3(0.62f, 0.48f, 0.62f),
            new Vector3(0.58f, 0.44f, 0.56f),
            new Vector3(0.48f, 0.44f, 0.48f)
        };

        for (int i = 0; i < crates.Length; i++)
        {
            GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Cargo crate " + (i + 1);
            crate.transform.SetParent(transform, false);
            crateHome[i] = cargoCenter + offsets[i];
            crate.transform.localPosition = crateHome[i];
            crate.transform.localScale = sizes[i];
            crate.GetComponent<Renderer>().sharedMaterial = boxMaterial;
            Collider boxCollider = crate.GetComponent<Collider>();
            if (boxCollider != null) Destroy(boxCollider);
            crates[i] = crate.transform;
            crateRotation[i] = crate.transform.localRotation;

            for (int mark = 0; mark < 2; mark++)
            {
                GameObject crack = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crack.name = "Cargo damage mark";
                crack.transform.SetParent(crate.transform, false);
                crack.transform.localPosition = new Vector3(mark == 0 ? -0.13f : 0.12f, 0f, 0.505f);
                crack.transform.localRotation = Quaternion.Euler(0f, 0f, mark == 0 ? 31f : -38f);
                crack.transform.localScale = new Vector3(0.035f, 0.68f, 0.018f);
                crack.GetComponent<Renderer>().sharedMaterial = damageMaterial;
                Collider crackCollider = crack.GetComponent<Collider>();
                if (crackCollider != null) Destroy(crackCollider);
                damageMarks[i, mark] = crack;
                crack.SetActive(false);
            }

            GameObject strap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strap.name = "Parcel strap";
            strap.transform.SetParent(crate.transform, false);
            strap.transform.localPosition = Vector3.zero;
            strap.transform.localScale = new Vector3(0.16f, 1.015f, 1.015f);
            strap.GetComponent<Renderer>().sharedMaterial = strapMaterial;
            Collider strapCollider = strap.GetComponent<Collider>();
            if (strapCollider != null) Destroy(strapCollider);
        }
    }

    static Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    void OnDestroy()
    {
        if (boxMaterial != null) Destroy(boxMaterial);
        if (strapMaterial != null) Destroy(strapMaterial);
        if (damageMaterial != null) Destroy(damageMaterial);
        if (dustMaterial != null) Destroy(dustMaterial);
        if (gravelMaterial != null) Destroy(gravelMaterial);
        if (gravelMesh != null) Destroy(gravelMesh);
    }
}
