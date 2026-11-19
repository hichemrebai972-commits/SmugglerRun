using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using SmugglerRun.Track;

/// <summary>Livraisons : protéger la cargaison, choisir une route et atteindre la sortie.</summary>
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(100)]
public class TruckGameplay : MonoBehaviour
{
    public RoughRoadTerrain route;
    public RoadsideExperience police;
    public CarController controller;
    [Range(0f, 100f)] public float cargoCondition = 100f;
    [Range(0f, 100f)] public float vehicleCondition = 100f;
    public int startingCash = 1200;
    public int deliveryValue = 1800;
    public float deliveryEndMargin = 12f;
    [Tooltip("Renforce l'adhérence latérale pour empêcher le pickup de partir en drift.")]
    [Range(1f, 2.5f)] public float lateralGripMultiplier = 2.0f;

    [Header("Ambiance de départ")]
    public bool startAtNight = true;

    [Header("Sortie des pistes de montagne")]
    [Min(60f)] public float difficultRouteEscapeSeconds = 150f;
    [Min(90f)] public float extremeRouteEscapeSeconds = 210f;

    [Header("Secousses")]
    public float joltThresholdG = 3.5f;
    public float joltMaxG = 30f;
    public float joltCargoDamage = 14f;
    Vector3 lastJoltVelocity;
    bool hasLastJoltVelocity;

    int cash;
    int engineLevel, suspensionLevel, tireLevel;
    bool delivered;
    bool night;
    enum WeatherMode { Dry, Rain, Dust }
    WeatherMode weatherMode;
    Light[] headlights;
    ParticleSystem rainWeather;
    ParticleSystem dustStorm;
    Material weatherParticleMaterial;
    TruckFeedback truckFeedback;
    float lastImpact;
    Rigidbody body;
    float savedFogDensity;
    bool savedFogEnabled;
    FogMode savedFogMode;
    float savedFogStartDistance;
    float savedFogEndDistance;
    Color savedAmbient;
    Light sun;
    float savedSunIntensity;
    Color savedSunColor;
    float savedAmbientIntensity;
    AmbientMode savedAmbientMode;
    Color savedFogColor;
    Material nightSkybox;
    float savedSkyboxExposure;
    WheelCollider[] wheels;
    JointSpring[] stockSprings;
    WheelFrictionCurve[] stockForwardFriction, stockSidewaysFriction;
    ParticleSystem engineSmoke;
    AudioSource engineSound;
    AudioClip engineClip;
    AudioLowPassFilter engineLowPass;
    AudioReverbFilter engineReverb;
    AudioSource cityAmbienceSound;
    AudioClip cityAmbienceClip;
    AudioSource roadSound;
    AudioClip roadSoundClip;
    AudioSource heartbeatSound;
    AudioClip heartbeatClip;
    AudioSource hazardSound;
    AudioClip hazardSoundClip;
    FollowCamera followCamera;
    BestRunGhost runGhost;
    Camera photoCamera;
    Canvas photoCanvas;
    Text photoHintText;
    bool photoMode;
    bool followCameraWasEnabled;
    bool previousCursorVisible;
    CursorLockMode previousCursorLock;
    float photoYaw;
    float photoPitch;
    float photoMoveSpeed = 18f;
    Material effectMaterial;
    Material smokeMaterial;
    TrailRenderer[] skidTrails;
    Canvas hudCanvas;
    GameObject garagePanel;
    GameObject minimapPanel;
    RectTransform minimapArea;
    RectTransform minimapPlayerMarker;
    readonly List<RectTransform> minimapJunctionMarkers = new List<RectTransform>();
    Text minimapPlayerArrow;
    Text minimapRouteText;
    Sprite minimapPaperSprite;
    Texture2D minimapPaperTexture;
    Text speedText, locationText, cashText, wantedText, cargoPercentText, vehiclePercentText;
    Image cargoBar, vehicleBar;
    Button engineButton, suspensionButton, tireButton, repairButton, recoveryButton;
    GameObject noticePanel;
    Text noticeText;
    float noticeUntil;
    GameObject resultsPanel;
    Text resultsTitleText;
    Text resultsBodyText;
    Button resultsActionButton;
    Text resultsActionButtonText;
    GameObject pausePanel;
    Text pauseVolumeText;
    Text pauseQualityText;
    Text pauseDisplayText;
    Text pauseSteeringText;
    Text pauseGhostText;
    Text pauseDailySeedText;
    Text pauseRecordsText;
    Button steeringButton;
    Button ghostButton;
    Button dailySeedButton;
    Button pauseFirstButton;
    GameObject mountainTimerPanel;
    Text mountainTimerText;
    Image mountainTimerBackground;
    readonly List<Image> timerWarningEdges = new List<Image>();
    bool missionFailed;
    bool mountainTimerStarted;
    bool mountainInterceptionStarted;
    float mountainTimeRemaining;
    int timedRouteTier = -1;
    float hazardWarningUntil;
    float missionStartTime;
    float lastDamageNoticeTime;
    int missionImpactCount;
    int maximumWantedDuringMission;
    float stoppedDuration;
    int lastWantedLevel;
    bool warnedCheckpoint;
    int activeExtremeCheckpointIndex = -1;
    int currentRouteTier = -1;
    int bestRouteTier;
    float currentRouteTierDuration;
    bool garageOpen;
    bool pauseOpen;
    float previousTimeScale = 1f;
    float lastTerrainDamageTime;
    float savedLinearDamping;
    float lastAppliedSuspensionCondition = -1f;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        savedLinearDamping = body.linearDamping;
        if (!controller) controller = GetComponent<CarController>();
        runGhost = GetComponent<BestRunGhost>();
        if (!runGhost) runGhost = gameObject.AddComponent<BestRunGhost>();
        if (controller)
            controller.steeringResponse = PlayerPrefs.GetFloat("SmugglerRun_SteeringResponse", controller.steeringResponse);
        if (!route) route = FindFirstObjectByType<RoughRoadTerrain>();
        if (!police) police = FindFirstObjectByType<RoadsideExperience>();
        truckFeedback = GetComponent<TruckFeedback>();
        lastWantedLevel = police ? police.WantedLevel : 0;
        cash = PlayerPrefs.GetInt("SmugglerRun_Cash", startingCash);
        engineLevel = PlayerPrefs.GetInt("SmugglerRun_Engine", 0);
        suspensionLevel = PlayerPrefs.GetInt("SmugglerRun_Suspension", 0);
        tireLevel = PlayerPrefs.GetInt("SmugglerRun_Tires", 0);
        AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat("SmugglerRun_Volume", AudioListener.volume));
        if (QualitySettings.names.Length > 0)
            QualitySettings.SetQualityLevel(Mathf.Clamp(PlayerPrefs.GetInt("SmugglerRun_Quality", QualitySettings.GetQualityLevel()),
                0, QualitySettings.names.Length - 1), true);
        Screen.fullScreen = PlayerPrefs.GetInt("SmugglerRun_Fullscreen", Screen.fullScreen ? 1 : 0) != 0;
        savedFogDensity = RenderSettings.fogDensity;
        savedFogEnabled = RenderSettings.fog;
        savedFogMode = RenderSettings.fogMode;
        savedFogStartDistance = RenderSettings.fogStartDistance;
        savedFogEndDistance = RenderSettings.fogEndDistance;
        savedFogColor = RenderSettings.fogColor;
        savedAmbient = RenderSettings.ambientLight;
        savedAmbientIntensity = RenderSettings.ambientIntensity;
        savedAmbientMode = RenderSettings.ambientMode;
        nightSkybox = RenderSettings.skybox;
        if (nightSkybox && nightSkybox.HasProperty("_Exposure"))
            savedSkyboxExposure = nightSkybox.GetFloat("_Exposure");
        sun = RenderSettings.sun;
        if (sun == null)
        {
            Light[] sceneLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (Light sceneLight in sceneLights)
                if (sceneLight.type == LightType.Directional) { sun = sceneLight; break; }
        }
        if (sun)
        {
            savedSunIntensity = sun.intensity;
            savedSunColor = sun.color;
        }
        CacheWheelSetup();
        followCamera = Camera.main ? Camera.main.GetComponent<FollowCamera>() : null;
        if (!followCamera) followCamera = FindFirstObjectByType<FollowCamera>();
        CreateHeadlights();
        CreateDamageSmoke();
        CreateEngineSound();
        CreateCityAmbience();
        CreateSurfaceAudio();
        CreateTensionAudio();
        CreateSkidMarks();
        CreateWeatherEffects();
        BuildHUD();
        ApplyUpgrades();
        ApplySurfaceGrip();
    }

    void Start()
    {
        // Laisse les scripts de décor créer d'abord les gyrophares et feux de guet.
        SetNight(startAtNight);
        missionStartTime = Time.time;
        if (runGhost)
        {
            runGhost.SetCurrentRoute(0);
            runGhost.BeginRun();
        }
        if (police) police.BeginOpeningPursuit();
    }

    void Update()
    {
        if (photoMode)
        {
            UpdatePhotoMode();
            return;
        }
        var kb = Keyboard.current;
        var gamepad = Gamepad.current;
        if (delivered)
        {
            if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                || (gamepad != null && (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame)))
                ContinueAfterDelivery();
            return;
        }
        if ((kb != null && kb.escapeKey.wasPressedThisFrame)
            || (gamepad != null && gamepad.startButton.wasPressedThisFrame))
        {
            if (pauseOpen) TogglePause(false);
            else if (garageOpen) ToggleGarage(false);
            else TogglePause(true);
            return;
        }
        if (pauseOpen) return;
        if (kb != null && kb.pKey.wasPressedThisFrame)
        {
            TogglePhotoMode(true);
            return;
        }
        if (kb != null && kb.hKey.wasPressedThisFrame) ToggleBestRunGhost();
        if (route && route.IsOnCanyonTrack(transform.position))
        {
            float extremeLocalZ = route.transform.InverseTransformPoint(transform.position).z;
            int reachedCheckpoint = route.GetExtremeCheckpointIndex(extremeLocalZ);
            if (reachedCheckpoint > activeExtremeCheckpointIndex)
            {
                activeExtremeCheckpointIndex = reachedCheckpoint;
                ShowNotice(reachedCheckpoint == 0
                    ? "POINT DE REPRISE 1  •  CRÊTE FRANCHIE"
                    : "POINT DE REPRISE 2  •  ÉPINGLE FRANCHIE", 3f);
            }
        }
        if (route && route.HasFallenFromExtremeTrack(transform.position))
        {
            FailExtremeRouteFall();
            return;
        }
        if (kb != null && kb.nKey.wasPressedThisFrame) SetNight(!night);
        if (kb != null && kb.mKey.wasPressedThisFrame) CycleWeather();
        if (kb != null && kb.gKey.wasPressedThisFrame) ToggleGarage(!garageOpen);
        if (kb != null && kb.tKey.wasPressedThisFrame) RecoverVehicle();
        float routeLocalZ = route
            ? route.transform.InverseTransformPoint(transform.position).z
            : transform.position.z;
        if (police && !warnedCheckpoint && police.IsCheckpointAhead(transform.position, 160f))
        {
            warnedCheckpoint = true;
            ShowNotice("BARRAGES SUR LA RN13  •  PRÉPARE TON PASSAGE", 3.5f);
        }
        if (route && !delivered && routeLocalZ >= route.terrainSize - deliveryEndMargin)
            CompleteDelivery();
        if (!delivered) UpdateMountainEscapeTimer();
        UpdateTensionAudio();
        if (delivered) return;
        if (vehicleCondition <= 0f)
        {
            FailMission("PICKUP HORS SERVICE");
            return;
        }
        if (cargoCondition <= 0f)
        {
            FailMission("CARGAISON PERDUE");
            return;
        }
        if (headlights != null) foreach (var light in headlights) light.enabled = night;
        UpdateDamageFeedback();
        if (truckFeedback)
        {
            truckFeedback.SetCargoCondition(cargoCondition / 100f);
            truckFeedback.SetRoadSurfaceDust(route ? route.GetDustFactor(transform.position) : 0.12f);
        }
        UpdateEngineSound();
        UpdateRouteAtmosphere();
        UpdateSurfaceAudio();
        UpdateRecoveryTimer();
        UpdateRouteRewardTracking();
        if (police) maximumWantedDuringMission = Mathf.Max(maximumWantedDuringMission, police.WantedLevel);
        if (noticePanel && noticePanel.activeSelf && Time.unscaledTime > noticeUntil)
            noticePanel.SetActive(false);
        UpdateSkidMarks();
        UpdateHUD();
    }

    void FixedUpdate()
    {
        if (route && body && Mathf.Abs(body.linearVelocity.y) > 8f)
            cargoCondition = Mathf.Max(0f, cargoCondition - Mathf.Abs(body.linearVelocity.y) * Time.fixedDeltaTime * 0.16f);
        ApplyJoltDamage();
        ApplySurfaceGrip();
        if (route)
        {
            body.linearDamping = savedLinearDamping + route.GetSurfaceDrag(transform.position);
            if (controller) controller.SetSandSinkDepth(route.GetSandSinkDepth(transform.position));
        }
    }

    void ApplyJoltDamage()
    {
        if (!body) return;
        Vector3 v = body.linearVelocity;
        bool afterImpact = Time.time - lastImpact < 0.5f;

        if (hasLastJoltVelocity && !afterImpact && Time.timeSinceLevelLoad > 3f)
        {
            // Accélération ressentie par la marchandise : 1 g à l'arrêt, 0 g en chute libre.
            Vector3 a = (v - lastJoltVelocity) / Time.fixedDeltaTime;
            float g = (a - Physics.gravity).magnitude / 9.81f;
            // Au-delà de joltMaxG, c'est probablement une téléportation (touche R, livraison).
            if (g > joltThresholdG && g < joltMaxG)
            {
                float before = cargoCondition;
                cargoCondition = Mathf.Max(0f, cargoCondition - (g - joltThresholdG) * joltCargoDamage * Time.fixedDeltaTime);
                float lost = before - cargoCondition;
                if (lost >= 0.12f && Time.time - lastDamageNoticeTime > 3f)
                    ReportCargoDamage("GROSSE SECOUSSE", lost, g / Mathf.Max(joltMaxG, 1f));
            }

            bool roughGround = route && (route.IsOnGravelSurface(transform.position)
                || route.IsOnLooseEarth(transform.position) || route.IsOnSandTrack(transform.position));
            if (roughGround && g > 5.2f && g < 25f && Time.time - lastTerrainDamageTime > 0.8f)
            {
                float suspensionDamage = Mathf.Clamp((g - 5.2f) * 0.42f, 0.25f, 2.2f);
                vehicleCondition = Mathf.Max(0f, vehicleCondition - suspensionDamage);
                lastTerrainDamageTime = Time.time;
            }
        }

        lastJoltVelocity = v;
        hasLastJoltVelocity = true;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.GetComponentInParent<FallingRock>() != null) return;
        float impact = collision.relativeVelocity.magnitude;
        if (impact < 5f || Time.time - lastImpact < 0.7f) return;
        lastImpact = Time.time;
        float damage = Mathf.Clamp((impact - 4f) * 1.35f, 0f, 35f);
        vehicleCondition = Mathf.Max(0f, vehicleCondition - damage);
        missionImpactCount++;
        float cargoImpactMultiplier = route && route.IsOnCanyonTrack(transform.position) ? 1.45f
            : route && route.IsOnRuralDetour(transform.position) ? 1.2f : 1f;
        float cargoDamage = damage * 0.72f * cargoImpactMultiplier;
        float beforeCargo = cargoCondition;
        cargoCondition = Mathf.Max(0f, cargoCondition - cargoDamage);
        if (cargoCondition < beforeCargo)
            ReportCargoDamage(cargoImpactMultiplier > 1f ? "CHOC CONTRE LA ROCHE" : "IMPACT", beforeCargo - cargoCondition,
                Mathf.Clamp01(impact / 22f));
        if (police) police.ReportCrash(impact);
    }

    void ReportCargoDamage(string cause, float lost, float severity)
    {
        if (truckFeedback) truckFeedback.NotifyCargoImpact(severity);
        if (Time.time - lastDamageNoticeTime < 1.35f) return;
        lastDamageNoticeTime = Time.time;
        ShowNotice($"{cause}  •  CARGAISON -{lost:0.0}%", 1.7f);
    }

    void CompleteDelivery()
    {
        delivered = true;
        missionFailed = false;
        if (mountainTimerPanel) mountainTimerPanel.SetActive(false);
        SetTimerWarningEdges(false, 0f);
        int routeTier = Mathf.Max(bestRouteTier, GetCurrentRouteTier());
        float elapsed = Mathf.Max(0f, Time.time - missionStartTime);
        float vehicleBeforeRefuge = vehicleCondition;
        float refugeRepair = routeTier == 1 ? 14f : 0f;
        if (refugeRepair > 0f) vehicleCondition = Mathf.Min(100f, vehicleCondition + refugeRepair);
        float actualRefugeRepair = vehicleCondition - vehicleBeforeRefuge;
        int conditionReward = Mathf.RoundToInt(deliveryValue * (cargoCondition / 100f) * (vehicleCondition / 100f));
        float routeMultiplier = routeTier == 0 ? 1f
            : routeTier == 1 ? 1.10f
            : routeTier == 2 ? 1.25f
            : cargoCondition >= 65f && vehicleCondition >= 65f ? 1.65f : 1.35f;
        int earned = Mathf.RoundToInt(conditionReward * routeMultiplier);
        int routeBonus = earned - conditionReward;
        cash += earned;
        PlayerPrefs.SetInt("SmugglerRun_Cash", cash);
        string timeKey = "SmugglerRun_BestTime_Route_" + routeTier;
        float previousBest = PlayerPrefs.GetFloat(timeKey, 0f);
        bool newRecord = previousBest <= 0f || elapsed < previousBest;
        if (newRecord)
        {
            PlayerPrefs.SetFloat(timeKey, elapsed);
            if (runGhost) runGhost.SaveRoute(routeTier);
        }
        PlayerPrefs.SetFloat("SmugglerRun_BestProgress_Route_" + routeTier, 1f);
        PlayerPrefs.Save();
        bool legendary = routeTier == 3 && cargoCondition >= 65f && vehicleCondition >= 65f;
        resultsTitleText.text = legendary ? "CONTREBANDIER LÉGENDAIRE" : "LIVRAISON RÉUSSIE";
        if (resultsActionButtonText) resultsActionButtonText.text = "CONTINUER  •  NOUVELLE LIVRAISON";
        int minutes = Mathf.FloorToInt(elapsed / 60f);
        int seconds = Mathf.FloorToInt(elapsed % 60f);
        string repairLine = actualRefugeRepair > 0f ? $"REFUGE DE L'OUED  •  PICKUP +{actualRefugeRepair:0}% réparé\n" : "";
        string recordLine = newRecord ? "NOUVEAU RECORD DE TEMPS" : $"Record précédent  •  {Mathf.FloorToInt(previousBest / 60f):00}:{Mathf.FloorToInt(previousBest % 60f):00}";
        string bonusLine = routeBonus > 0 ? $"Prime de risque  •  +{routeBonus:N0} $" : "Prime de risque  •  aucune";
        resultsBodyText.text = $"ITINÉRAIRE  •  {GetRouteLabel(routeTier)}\nPROGRESSION  •  100%\nTEMPS ÉCOULÉ  •  {minutes:00}:{seconds:00}   •   {recordLine}\nCARGAISON  •  {cargoCondition:0}%     PICKUP  •  {vehicleCondition:0}%\n{repairLine}RISQUE  •  {missionImpactCount} choc(s)\n{bonusLine}\nRÉCOMPENSE  •  +{earned:N0} $";
        resultsPanel.SetActive(true);
        if (noticePanel) noticePanel.SetActive(false);
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Debug.Log($"Livraison réussie : +{earned} $ (dont {routeBonus} $ de bonus d'itinéraire). La cargaison et le pickup déterminent la récompense.");
    }

    void UpdateMountainEscapeTimer()
    {
        if (!route) return;
        int currentTier = GetCurrentRouteTier();
        if (!mountainTimerStarted && (currentTier == 2 || currentTier == 3))
        {
            mountainTimerStarted = true;
            timedRouteTier = currentTier;
            mountainTimeRemaining = currentTier == 3
                ? extremeRouteEscapeSeconds : difficultRouteEscapeSeconds;
            ShowNotice(currentTier == 3
                ? "SORTIE DU CHAMBI  •  ATTEINS LE REFUGE AVANT LES RENFORTS"
                : "SORTIE DES CRÊTES  •  ATTEINS LE REFUGE AVANT LES RENFORTS", 4.5f);
        }

        if (mountainTimerPanel) mountainTimerPanel.SetActive(mountainTimerStarted);
        if (!mountainTimerStarted)
        {
            bool hazardWarning = Time.unscaledTime < hazardWarningUntil;
            float hazardPulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
            SetTimerWarningEdges(hazardWarning, hazardWarning ? 0.07f + hazardPulse * 0.07f : 0f);
            return;
        }

        if (!mountainInterceptionStarted)
        {
            mountainTimeRemaining = Mathf.Max(0f, mountainTimeRemaining - Time.deltaTime);
            if (mountainTimeRemaining <= 0f)
            {
                mountainInterceptionStarted = true;
                if (police) police.ActivateMountainExitBlockade(timedRouteTier);
                ShowNotice("LES RENFORTS ARRIVENT  •  LA SORTIE EST BLOQUÉE", 5f);
            }
        }

        bool urgent = !mountainInterceptionStarted && mountainTimeRemaining <= 30f;
        if (mountainTimerText)
        {
            if (mountainInterceptionStarted)
                mountainTimerText.text = "SORTIE BLOQUÉE  •  POLICE AU REFUGE";
            else
            {
                int seconds = Mathf.CeilToInt(mountainTimeRemaining);
                mountainTimerText.text = $"RENFORTS DANS  •  {seconds / 60:00}:{seconds % 60:00}";
            }
            float pulse = urgent ? 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 8f) : 0f;
            mountainTimerText.color = urgent
                ? Color.Lerp(new Color(1f, 0.67f, 0.22f), new Color(1f, 0.17f, 0.12f), pulse)
                : new Color(1f, 0.82f, 0.42f);
        }
        if (mountainTimerBackground)
            mountainTimerBackground.color = urgent
                ? Color.Lerp(new Color(0.19f, 0.075f, 0.018f, 0.94f), new Color(0.28f, 0.025f, 0.02f, 0.98f),
                    0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f))
                : new Color(0.025f, 0.035f, 0.052f, 0.94f);
        bool hazardWarningActive = Time.unscaledTime < hazardWarningUntil;
        float warningPulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
        float edgeAlpha = urgent
            ? 0.10f + 0.16f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f))
            : hazardWarningActive ? 0.07f + warningPulse * 0.07f : 0f;
        SetTimerWarningEdges(urgent || hazardWarningActive, edgeAlpha);

        if (mountainInterceptionStarted)
        {
            Vector3 local = route.transform.InverseTransformPoint(transform.position);
            if (route.RouteEndZ - local.z <= 62f)
                FailMission("BARRAGE POLICIER À LA SORTIE");
        }
    }

    void SetTimerWarningEdges(bool visible, float alpha)
    {
        for (int i = 0; i < timerWarningEdges.Count; i++)
        {
            if (!timerWarningEdges[i]) continue;
            timerWarningEdges[i].enabled = visible;
            Color color = timerWarningEdges[i].color;
            color.a = Mathf.Clamp01(alpha);
            timerWarningEdges[i].color = color;
        }
    }

    void FailMission(string reason)
    {
        if (delivered) return;
        delivered = true;
        missionFailed = true;
        Vector3 local = route ? route.transform.InverseTransformPoint(transform.position) : transform.position;
        float remaining = route ? Mathf.Max(0f, route.RouteEndZ - local.z) : 0f;
        int routeTier = Mathf.Max(bestRouteTier, Mathf.Max(timedRouteTier, GetCurrentRouteTier()));
        resultsTitleText.text = remaining <= 40f
            ? $"À {Mathf.CeilToInt(remaining)} MÈTRES DU REFUGE"
            : "FUITE INTERROMPUE";
        int minutes = Mathf.FloorToInt(Mathf.Max(0f, Time.time - missionStartTime) / 60f);
        int seconds = Mathf.FloorToInt(Mathf.Max(0f, Time.time - missionStartTime) % 60f);
        float progress = route ? Mathf.Clamp01(local.z / Mathf.Max(1f, route.RouteEndZ)) : 0f;
        string progressKey = "SmugglerRun_BestProgress_Route_" + routeTier;
        float previousProgress = PlayerPrefs.GetFloat(progressKey, 0f);
        float bestProgress = Mathf.Max(previousProgress, progress);
        bool newProgressRecord = progress > previousProgress + 0.001f;
        if (newProgressRecord)
        {
            PlayerPrefs.SetFloat(progressKey, progress);
            PlayerPrefs.Save();
        }
        string progressRecordLine = newProgressRecord ? "NOUVEAU RECORD DE PROGRESSION"
            : $"Record précédent  •  {bestProgress * 100f:0}%";
        resultsBodyText.text = $"CAUSE  •  {reason}\nITINÉRAIRE  •  {GetRouteLabel(routeTier)}\nPROGRESSION  •  {progress * 100f:0}%  •  {progressRecordLine}\nDISTANCE RESTANTE  •  {remaining:0} m\nTEMPS ÉCOULÉ  •  {minutes:00}:{seconds:00}\nCARGAISON  •  {cargoCondition:0}%     PICKUP  •  {vehicleCondition:0}%\n\nLa tentative est gagnable : ajuste ton passage et réessaie.";
        if (resultsActionButtonText) resultsActionButtonText.text = "RÉESSAYER";
        resultsPanel.SetActive(true);
        if (noticePanel) noticePanel.SetActive(false);
        if (mountainTimerPanel) mountainTimerPanel.SetActive(false);
        SetTimerWarningEdges(false, 0f);
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        AudioListener.pause = true;
    }

    void ResetMountainEscapeTimer()
    {
        mountainTimerStarted = false;
        mountainInterceptionStarted = false;
        mountainTimeRemaining = 0f;
        timedRouteTier = -1;
        hazardWarningUntil = 0f;
        if (mountainTimerPanel) mountainTimerPanel.SetActive(false);
        SetTimerWarningEdges(false, 0f);
    }

    void ContinueAfterDelivery()
    {
        if (missionFailed)
        {
            missionFailed = false;
            if (resultsPanel) resultsPanel.SetActive(false);
            Time.timeScale = previousTimeScale;
            AudioListener.pause = false;
            ReturnToStart();
            return;
        }
        if (resultsPanel) resultsPanel.SetActive(false);
        Time.timeScale = previousTimeScale;
        AudioListener.pause = false;
        cargoCondition = 100f;
        vehicleCondition = Mathf.Max(vehicleCondition, 55f);
        delivered = false;
        missionFailed = false;
        lastImpact = Time.time;
        hasLastJoltVelocity = false;
        bestRouteTier = 0;
        currentRouteTier = -1;
        currentRouteTierDuration = 0f;
        activeExtremeCheckpointIndex = -1;
        missionStartTime = Time.time;
        if (runGhost)
        {
            runGhost.SetCurrentRoute(0);
            runGhost.BeginRun();
        }
        missionImpactCount = 0;
        maximumWantedDuringMission = 0;
        ResetMountainEscapeTimer();
        if (route) route.ResetPlayerToStart();
        if (police)
        {
            police.ResetPursuit();
            police.BeginOpeningPursuit();
        }
        lastWantedLevel = police ? police.WantedLevel : 0;
        warnedCheckpoint = false;
        if (truckFeedback) truckFeedback.SetCargoCondition(1f);
        ApplyUpgrades();
        UpdateHUD();
    }

    int GetCurrentRouteTier()
    {
        if (!route) return 0;
        Vector3 position = transform.position;
        if (route.IsOnCanyonTrack(position)) return 3;
        if (route.IsOnRuralDetour(position)) return 2;
        if (route.IsOnWadiRoute(position)) return 1;
        return 0;
    }

    static string GetRouteTierName(int tier)
    {
        switch (tier)
        {
            case 1: return "de l'oued";
            case 2: return "des crêtes";
            case 3: return "du Chambi";
            default: return "principale";
        }
    }

    static string GetRouteLabel(int tier)
    {
        switch (tier)
        {
            case 1: return "OUED EXTÉRIEUR • MOYENNE";
            case 2: return "CRÊTES ROCHEUSES • DIFFICILE";
            case 3: return "CORNICHE DU CHAMBI • EXTRÊME";
            default: return "AVENUE RN13 • FACILE";
        }
    }

    void UpdateRouteRewardTracking()
    {
        int tier = GetCurrentRouteTier();
        if (tier != currentRouteTier)
        {
            currentRouteTier = tier;
            currentRouteTierDuration = 0f;
            if (runGhost) runGhost.SetCurrentRoute(tier);
        }
        else if (tier > 0)
        {
            if (!controller || controller.SpeedKmh > 15f)
                currentRouteTierDuration += Time.deltaTime;
            if (currentRouteTierDuration >= 5f)
                bestRouteTier = Mathf.Max(bestRouteTier, tier);
        }
    }

    void ShowNotice(string message, float duration, bool reward = false)
    {
        if (!noticePanel || !noticeText) return;
        noticeText.text = message;
        noticeText.color = reward ? new Color(0.83f, 0.96f, 0.84f) : Color.white;
        Image background = noticePanel.GetComponent<Image>();
        if (background)
            background.color = reward
                ? new Color(0.035f, 0.12f, 0.075f, 0.96f)
                : new Color(0.025f, 0.035f, 0.052f, 0.94f);
        noticePanel.SetActive(true);
        noticeUntil = Time.unscaledTime + duration;
    }

    /// <summary>Affiche une alerte de danger de terrain sans utiliser le son de la police.</summary>
    public void ShowEnvironmentalWarning(string message, float duration = 4.5f)
    {
        ShowNotice("DANGER  •  " + message, duration);
        hazardWarningUntil = Time.unscaledTime + duration;
        if (followCamera) followCamera.TriggerShake(0.025f, 0.5f);
        if (hazardSound) hazardSound.PlayOneShot(hazardSoundClip, 0.28f);
        if (noticeText) noticeText.color = new Color(1f, 0.78f, 0.28f);
        if (noticePanel)
        {
            Image background = noticePanel.GetComponent<Image>();
            if (background) background.color = new Color(0.19f, 0.075f, 0.018f, 0.96f);
        }
    }

    /// <summary>Impact sourd du rocher qui tombe; aucun son de sirène ou d'alerte de police.</summary>
    public void NotifyRockfallImpact()
    {
        if (followCamera) followCamera.TriggerShake(0.075f, 0.6f);
        if (hazardSound) hazardSound.PlayOneShot(hazardSoundClip, 0.48f);
    }

    /// <summary>Applique les dégâts habituels quand un rocher dynamique touche le pickup.</summary>
    public void ReportRockfallPlayerHit(float impactSpeed)
    {
        if (impactSpeed < 5f || Time.time - lastImpact < 0.7f) return;
        lastImpact = Time.time;
        float damage = Mathf.Clamp((impactSpeed - 4f) * 1.35f, 0f, 35f);
        vehicleCondition = Mathf.Max(0f, vehicleCondition - damage);
        missionImpactCount++;
        float cargoImpactMultiplier = route && route.IsOnCanyonTrack(transform.position) ? 1.45f
            : route && route.IsOnRuralDetour(transform.position) ? 1.2f : 1f;
        float cargoDamage = damage * 0.72f * cargoImpactMultiplier;
        float beforeCargo = cargoCondition;
        cargoCondition = Mathf.Max(0f, cargoCondition - cargoDamage);
        if (cargoCondition < beforeCargo)
            ReportCargoDamage("CHOC DE PIERRE", beforeCargo - cargoCondition,
                Mathf.Clamp01(impactSpeed / 22f));
        if (police) police.ReportCrash(impactSpeed);
    }

    /// <summary>Son de rupture du pont après le passage rapide, sans alerte de police.</summary>
    public void NotifyBridgeCollapse()
    {
        if (followCamera) followCamera.TriggerShake(0.035f, 0.45f);
        if (hazardSound) hazardSound.PlayOneShot(hazardSoundClip, 0.22f);
    }

    public bool IsPickupAirborne()
    {
        if (wheels == null || wheels.Length == 0) return false;
        int groundedWheels = 0;
        for (int i = 0; i < wheels.Length; i++)
            if (wheels[i] && wheels[i].GetGroundHit(out _)) groundedWheels++;
        return groundedWheels <= 1;
    }

    public void ReportExtremeWashoutCrossing(float speedMetersPerSecond, bool airborne)
    {
        if (delivered) return;
        // Une approche lente doit coûter des dégâts et de l'élan, pas déclencher
        // une défaite automatique : la brèche reste franchissable sans saut parfait.
        float severity = Mathf.InverseLerp(8f, 38f, speedMetersPerSecond);
        float vehicleDamage = airborne ? Mathf.Lerp(3f, 8f, severity) : Mathf.Lerp(6f, 24f, severity);
        float cargoDamage = airborne ? Mathf.Lerp(2f, 7f, severity) : Mathf.Lerp(4f, 20f, severity);
        vehicleCondition = Mathf.Max(0f, vehicleCondition - vehicleDamage);
        cargoCondition = Mathf.Max(0f, cargoCondition - cargoDamage);
        lastImpact = Time.time;
        missionImpactCount++;
        if (followCamera) followCamera.TriggerShake(airborne ? 0.07f : 0.13f, 0.75f);
        if (truckFeedback) truckFeedback.NotifyCargoImpact(Mathf.Clamp01(severity + (airborne ? 0.15f : 0.45f)));
        string crossingMessage = speedMetersPerSecond < 15f
            ? $"BRÈCHE  •  PASSAGE LENT  •  PICKUP -{vehicleDamage:0}%  CARGAISON -{cargoDamage:0}%"
            : airborne
                ? $"BRÈCHE  •  RÉCEPTION DURE  •  PICKUP -{vehicleDamage:0}%  CARGAISON -{cargoDamage:0}%"
                : $"BRÈCHE  •  CHOC  •  PICKUP -{vehicleDamage:0}%  CARGAISON -{cargoDamage:0}%";
        ShowNotice(crossingMessage, 3.5f);
        if (vehicleCondition <= 0f || cargoCondition <= 0f)
            FailMission(vehicleCondition <= 0f ? "PICKUP HORS SERVICE APRÈS L'EFFONDREMENT" : "CARGAISON DÉTRUITE DANS LA BRÈCHE");
    }

    public void ReportBridgePlayerFell()
    {
        FailExtremeRouteFall();
    }

    void FailExtremeRouteFall()
    {
        if (delivered || missionFailed) return;
        if (!route || !route.TryGetExtremeCheckpoint(activeExtremeCheckpointIndex,
            out Vector3 checkpointPosition, out Quaternion checkpointRotation))
        {
            vehicleCondition = 0f;
            FailMission("CHUTE MORTELLE DANS LE RAVIN");
            return;
        }

        // A ravine fall costs vehicle and cargo condition, but uses the two
        // route checkpoints so the player does not need to repeat the whole run.
        vehicleCondition = Mathf.Max(0f, vehicleCondition - 22f);
        cargoCondition = Mathf.Max(0f, cargoCondition - 12f);
        transform.SetPositionAndRotation(checkpointPosition, checkpointRotation);
        if (body)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
        lastImpact = Time.time;
        hasLastJoltVelocity = false;
        missionImpactCount++;
        if (controller) controller.SetDamageCondition(vehicleCondition / 100f);
        if (followCamera) followCamera.TriggerShake(0.18f, 1.1f);
        ApplyUpgrades();
        string checkpointLabel = activeExtremeCheckpointIndex < 0
            ? "DÉPART DE LA CORNICHE"
            : "POINT DE REPRISE " + (activeExtremeCheckpointIndex + 1);
        ShowNotice($"CHUTE DANS LE RAVIN  •  {checkpointLabel}  •  PICKUP -22 %  CARGAISON -12 %", 5f);
    }

    /// <summary>Le pont de montagne secoue la cargaison si le pickup le franchit trop vite.</summary>
    public void ReportFragileBridgeCrossing(float speedMetersPerSecond)
    {
        if (delivered) return;
        if (speedMetersPerSecond < 18f)
        {
            ShowNotice("PONT FRAGILE  •  PASSAGE MAÎTRISÉ", 3f);
            return;
        }

        float severity = Mathf.InverseLerp(18f, 40f, speedMetersPerSecond);
        if (followCamera) followCamera.TriggerShake(0.065f + severity * 0.05f, 0.65f);
        float vehicleDamage = Mathf.Lerp(1.5f, 8f, severity);
        float cargoDamage = Mathf.Lerp(1f, 5f, severity);
        vehicleCondition = Mathf.Max(0f, vehicleCondition - vehicleDamage);
        cargoCondition = Mathf.Max(0f, cargoCondition - cargoDamage);
        lastImpact = Time.time;
        missionImpactCount++;
        if (truckFeedback) truckFeedback.NotifyCargoImpact(Mathf.Clamp01(severity));
        ShowNotice($"PONT FRAGILE  •  CHOC  •  PICKUP -{vehicleDamage:0}%  CARGAISON -{cargoDamage:0}%", 3.5f);
    }

    void ApplyUpgrades()
    {
        if (controller && wheels != null)
        {
            controller.motorTorque = 7000f * (1f + engineLevel * 0.12f);
            controller.maxSpeedKmh = 140f + engineLevel * 4f;
            for (int i = 0; i < wheels.Length; i++)
            {
                WheelCollider wheel = wheels[i];
                if (!wheel) continue;
                JointSpring spring = stockSprings[i];
                float condition = Mathf.Clamp01(vehicleCondition / 100f);
                spring.spring *= (1f + suspensionLevel * 0.14f) * Mathf.Lerp(0.78f, 1f, condition);
                spring.damper *= (1f + suspensionLevel * 0.12f) * Mathf.Lerp(0.58f, 1f, condition);
                wheel.suspensionSpring = spring;
            }
            lastAppliedSuspensionCondition = vehicleCondition;
        }
        ApplySurfaceGrip();
    }

    void ApplySurfaceGrip()
    {
        if (wheels == null) return;
        Vector3 position = transform.position;
        float surfaceGrip = route ? route.GetSurfaceGrip(position) : 1f;
        // Météo and rough-ground effects stay mostly lateral/visual; they must not
        // halve forward traction or make the truck feel capped on long stretches.
        if (weatherMode == WeatherMode.Rain) surfaceGrip *= 0.98f;
        else if (weatherMode == WeatherMode.Dust) surfaceGrip *= 0.99f;

        float tireUpgrade = 1f + tireLevel * 0.08f;
        bool onExtremeCorniche = route && route.IsOnCanyonTrack(position);
        float frontLateral = route ? route.GetFrontSidewaysFactor(position) : 1f;
        float rearLateral = route ? route.GetRearSidewaysFactor(position) : 1f;
        float damperFactor = route ? route.GetRoughSuspensionDamperFactor(position) : 1f;
        float condition = Mathf.Clamp01(vehicleCondition / 100f);
        float lateralMultiplier = onExtremeCorniche ? route.extremeLateralGrip : lateralGripMultiplier;
        for (int i = 0; i < wheels.Length; i++)
        {
            WheelCollider wheel = wheels[i];
            if (!wheel) continue;
            WheelFrictionCurve forward = stockForwardFriction[i];
            WheelFrictionCurve sideways = stockSidewaysFriction[i];
            forward.stiffness *= tireUpgrade * surfaceGrip;
            // Les routes ordinaires gardent l'assistance au contrôle; seule la corniche reçoit
            // une adhérence réduite, mais encore assez lisible pour éviter le drift incontrôlé.
            sideways.stiffness *= tireUpgrade * lateralMultiplier
                                * (i < 2 ? frontLateral : rearLateral);
            wheel.forwardFriction = forward;
            wheel.sidewaysFriction = sideways;

            JointSpring spring = stockSprings[i];
            spring.spring *= (1f + suspensionLevel * 0.14f) * Mathf.Lerp(0.78f, 1f, condition);
            spring.damper *= (1f + suspensionLevel * 0.12f) * Mathf.Lerp(0.58f, 1f, condition)
                           * damperFactor;
            wheel.suspensionSpring = spring;
        }
    }

    void CacheWheelSetup()
    {
        if (!controller) return;
        wheels = new[] { controller.frontLeft, controller.frontRight, controller.rearLeft, controller.rearRight };
        stockSprings = new JointSpring[wheels.Length];
        stockForwardFriction = new WheelFrictionCurve[wheels.Length];
        stockSidewaysFriction = new WheelFrictionCurve[wheels.Length];
        for (int i = 0; i < wheels.Length; i++)
        {
            if (!wheels[i]) continue;
            stockSprings[i] = wheels[i].suspensionSpring;
            stockForwardFriction[i] = wheels[i].forwardFriction;
            stockSidewaysFriction[i] = wheels[i].sidewaysFriction;
        }
    }

    void CreateDamageSmoke()
    {
        GameObject emitter = new GameObject("Engine damage smoke");
        emitter.transform.SetParent(transform, false);
        emitter.transform.localPosition = new Vector3(0f, 0.95f, 1.65f);
        engineSmoke = emitter.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = engineSmoke.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.5f);
        main.startColor = new Color(0.42f, 0.42f, 0.42f, 0.35f);
        main.maxParticles = 70;
        ParticleSystem.EmissionModule emission = engineSmoke.emission;
        emission.rateOverTime = 0f;
        ParticleSystem.ShapeModule shape = engineSmoke.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 16f;
        shape.radius = 0.12f;
        ParticleSystemRenderer renderer = engineSmoke.GetComponent<ParticleSystemRenderer>();
        smokeMaterial = SoftParticleMaterial.Create();
        if (smokeMaterial != null) renderer.material = smokeMaterial;
    }

    void UpdateDamageFeedback()
    {
        float condition = Mathf.Clamp01(vehicleCondition / 100f);
        if (controller) controller.SetDamageCondition(condition);
        if (Mathf.Abs(vehicleCondition - lastAppliedSuspensionCondition) > 0.05f)
            ApplyUpgrades();
        if (!engineSmoke) return;
        ParticleSystem.EmissionModule emission = engineSmoke.emission;
        emission.rateOverTime = condition < 0.65f ? Mathf.Lerp(18f, 3f, condition / 0.65f) : 0f;
        ParticleSystem.MainModule main = engineSmoke.main;
        main.startColor = condition < 0.25f
            ? new Color(0.12f, 0.12f, 0.12f, 0.58f)
            : new Color(0.42f, 0.42f, 0.42f, 0.35f);
    }

    void CreateEngineSound()
    {
        const int sampleRate = 22050;
        float[] samples = new float[sampleRate];
        for (int i = 0; i < samples.Length; i++)
        {
            float phase = 2f * Mathf.PI * i / sampleRate;
            samples[i] = (Mathf.Sin(phase * 72f) * 0.52f + Mathf.Sin(phase * 144f) * 0.22f + Mathf.Sin(phase * 216f) * 0.1f) * 0.18f;
        }
        engineClip = AudioClip.Create("Pickup engine idle", sampleRate, 1, sampleRate, false);
        engineClip.SetData(samples, 0);
        GameObject soundObject = new GameObject("Pickup engine sound");
        soundObject.transform.SetParent(transform, false);
        engineSound = soundObject.AddComponent<AudioSource>();
        engineSound.clip = engineClip;
        engineSound.loop = true;
        engineSound.playOnAwake = false;
        engineSound.spatialBlend = 0.55f;
        engineSound.minDistance = 5f;
        engineSound.maxDistance = 45f;
        engineSound.volume = 0.12f;
        engineLowPass = soundObject.AddComponent<AudioLowPassFilter>();
        engineLowPass.cutoffFrequency = 22000f;
        engineReverb = soundObject.AddComponent<AudioReverbFilter>();
        engineReverb.reverbPreset = AudioReverbPreset.Off;
        engineSound.Play();
    }

    void UpdateEngineSound()
    {
        if (!engineSound || !controller) return;
        float speed = Mathf.Clamp01(controller.SpeedKmh / Mathf.Max(controller.maxSpeedKmh, 1f));
        float revs = Mathf.Clamp01(speed * 0.72f + Mathf.Abs(controller.ThrottleInput) * 0.38f);
        engineSound.pitch = Mathf.Lerp(0.78f, 1.72f, revs);
        float cityMix = GetCityAudioBlend();
        bool mountain = route && (route.IsOnRuralDetour(transform.position) || route.IsOnCanyonTrack(transform.position));
        engineLowPass.cutoffFrequency = Mathf.Lerp(22000f, 2350f, cityMix);
        engineReverb.reverbPreset = cityMix > 0.5f ? AudioReverbPreset.City
            : mountain ? AudioReverbPreset.Mountains : AudioReverbPreset.Off;
        float level = Mathf.Lerp(1f, 0.78f, cityMix);
        engineSound.volume = Mathf.Lerp(0.1f, 0.24f, Mathf.Max(speed, Mathf.Abs(controller.ThrottleInput) * 0.5f)) * level;
        if (cityAmbienceSound) cityAmbienceSound.volume = cityMix * 0.48f;
    }

    void CreateCityAmbience()
    {
        const int sampleRate = 22050;
        const float duration = 10f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random random = new System.Random(8137);
        float trafficLow = 0f;
        float trafficMid = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)sampleRate;
            float noise = (float)random.NextDouble() * 2f - 1f;
            trafficLow = Mathf.Lerp(trafficLow, noise, 0.0018f);
            trafficMid = Mathf.Lerp(trafficMid, noise, 0.017f);
            float distantTraffic = trafficLow * 0.028f + trafficMid * 0.007f
                + Mathf.Sin(2f * Mathf.PI * 58f * time) * 0.008f
                + Mathf.Sin(2f * Mathf.PI * 91f * time) * 0.004f;

            // Deux coups de klaxon très lointains dans la boucle, sans tonalité de sirène.
            float horn = 0f;
            float firstHorn = Mathf.InverseLerp(0f, 0.13f, time - 2.6f)
                * (1f - Mathf.InverseLerp(0.55f, 0.72f, time - 2.6f));
            float secondHorn = Mathf.InverseLerp(0f, 0.13f, time - 7.4f)
                * (1f - Mathf.InverseLerp(0.48f, 0.65f, time - 7.4f));
            horn = (firstHorn + secondHorn) * (Mathf.Sin(2f * Mathf.PI * 392f * time)
                + 0.42f * Mathf.Sin(2f * Mathf.PI * 494f * time)) * 0.012f;
            samples[i] = Mathf.Clamp(distantTraffic + horn, -0.06f, 0.06f);
        }

        cityAmbienceClip = AudioClip.Create("Distant Kasserine traffic", sampleCount, 1, sampleRate, false);
        cityAmbienceClip.SetData(samples, 0);
        GameObject ambienceObject = new GameObject("Distant city traffic ambience");
        ambienceObject.transform.SetParent(transform, false);
        cityAmbienceSound = ambienceObject.AddComponent<AudioSource>();
        cityAmbienceSound.clip = cityAmbienceClip;
        cityAmbienceSound.loop = true;
        cityAmbienceSound.playOnAwake = false;
        cityAmbienceSound.spatialBlend = 0f;
        cityAmbienceSound.volume = 0f;
        cityAmbienceSound.Play();
    }

    float GetCityAudioBlend()
    {
        if (!route || GetCurrentRouteTier() != 0) return 0f;
        float z = route.transform.InverseTransformPoint(transform.position).z;
        return Mathf.InverseLerp(route.terrainSize * 0.36f, route.terrainSize * 0.39f, z)
             * (1f - Mathf.InverseLerp(route.terrainSize * 0.61f, route.terrainSize * 0.64f, z));
    }

    void CreateSurfaceAudio()
    {
        const int sampleRate = 22050;
        float[] gravelSamples = new float[sampleRate];
        System.Random random = new System.Random(9163);
        float filteredNoise = 0f;
        for (int i = 0; i < gravelSamples.Length; i++)
        {
            float noise = (float)random.NextDouble() * 2f - 1f;
            filteredNoise = Mathf.Lerp(filteredNoise, noise, 0.12f);
            float lowRumble = Mathf.Sin(2f * Mathf.PI * 82f * i / sampleRate) * 0.22f;
            gravelSamples[i] = (filteredNoise * 0.7f + lowRumble) * 0.16f;
        }
        roadSoundClip = AudioClip.Create("Tires on gravel", gravelSamples.Length, 1, sampleRate, false);
        roadSoundClip.SetData(gravelSamples, 0);
        roadSound = gameObject.AddComponent<AudioSource>();
        roadSound.clip = roadSoundClip;
        roadSound.loop = true;
        roadSound.playOnAwake = false;
        roadSound.spatialBlend = 0.28f;
        roadSound.minDistance = 4f;
        roadSound.maxDistance = 36f;
        roadSound.dopplerLevel = 0f;
        roadSound.volume = 0f;
        roadSound.Play();

    }

    void UpdateSurfaceAudio()
    {
        if (!roadSound || !controller) return;
        float surface = 0f;
        if (route)
        {
            if (route.IsOnSandTrack(transform.position)) surface = 1f;
            else if (route.IsOnWadiRoute(transform.position))
                surface = Mathf.Lerp(0.45f, 0.9f, route.GetWadiLoosePatchBlend(transform.position));
            else if (route.IsOnGravelSurface(transform.position)) surface = 0.82f;
            else if (route.IsOnLooseEarth(transform.position)) surface = 0.62f;
        }

        float speed = Mathf.Clamp01(controller.SpeedKmh / 115f);
        roadSound.volume = surface * speed * 0.20f;
        roadSound.pitch = Mathf.Lerp(0.72f, 1.2f, speed);
    }

    void CreateTensionAudio()
    {
        const int sampleRate = 22050;
        const float heartbeatDuration = 0.8f;
        int heartbeatSamples = Mathf.RoundToInt(sampleRate * heartbeatDuration);
        float[] heart = new float[heartbeatSamples];
        for (int i = 0; i < heart.Length; i++)
        {
            float t = i / (float)sampleRate;
            float beatA = HeartbeatPulse(t);
            float beatB = HeartbeatPulse(t - 0.22f) * 0.68f;
            float pulse = beatA + beatB;
            heart[i] = (Mathf.Sin(2f * Mathf.PI * 48f * t)
                + 0.38f * Mathf.Sin(2f * Mathf.PI * 72f * t)) * pulse * 0.24f;
        }
        heartbeatClip = AudioClip.Create("Low heartbeat", heart.Length, 1, sampleRate, false);
        heartbeatClip.SetData(heart, 0);
        heartbeatSound = gameObject.AddComponent<AudioSource>();
        heartbeatSound.clip = heartbeatClip;
        heartbeatSound.loop = true;
        heartbeatSound.playOnAwake = false;
        heartbeatSound.spatialBlend = 0f;
        heartbeatSound.volume = 0f;

        const float rumbleDuration = 1.15f;
        int rumbleSamples = Mathf.RoundToInt(sampleRate * rumbleDuration);
        float[] rumble = new float[rumbleSamples];
        System.Random random = new System.Random(4627);
        float filteredNoise = 0f;
        for (int i = 0; i < rumble.Length; i++)
        {
            float t = i / (float)sampleRate;
            float noise = (float)random.NextDouble() * 2f - 1f;
            filteredNoise = Mathf.Lerp(filteredNoise, noise, 0.045f);
            float envelope = Mathf.Exp(-t * 2.4f);
            float fallingTone = Mathf.Sin(2f * Mathf.PI * (78f - 34f * t) * t);
            float crack = Mathf.Sin(2f * Mathf.PI * 173f * t) * Mathf.Exp(-Mathf.Abs(t - 0.16f) * 42f);
            rumble[i] = (filteredNoise * 0.56f + fallingTone * 0.34f + crack * 0.12f) * envelope * 0.22f;
        }
        hazardSoundClip = AudioClip.Create("Distant rockfall rumble", rumble.Length, 1, sampleRate, false);
        hazardSoundClip.SetData(rumble, 0);
        hazardSound = gameObject.AddComponent<AudioSource>();
        hazardSound.playOnAwake = false;
        hazardSound.spatialBlend = 0f;
        hazardSound.volume = 0.42f;
    }

    static float HeartbeatPulse(float t)
    {
        if (t < 0f || t > 0.19f) return 0f;
        float shape = Mathf.Sin(Mathf.PI * t / 0.19f);
        return shape * Mathf.Exp(-t * 10f);
    }

    void UpdateTensionAudio()
    {
        if (!heartbeatSound) return;
        bool urgent = mountainTimerStarted && !mountainInterceptionStarted
            && !delivered && mountainTimeRemaining <= 30f;
        if (!urgent)
        {
            heartbeatSound.volume = 0f;
            if (heartbeatSound.isPlaying) heartbeatSound.Stop();
            return;
        }

        float stress = 1f - Mathf.Clamp01(mountainTimeRemaining / 30f);
        heartbeatSound.pitch = Mathf.Lerp(0.88f, 1.32f, stress);
        heartbeatSound.volume = Mathf.Lerp(0.035f, 0.12f, stress);
        if (!heartbeatSound.isPlaying) heartbeatSound.Play();
    }

    void CreateSkidMarks()
    {
        if (controller == null || controller.rearLeft == null || controller.rearRight == null) return;
        skidTrails = new TrailRenderer[2];
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        effectMaterial = new Material(shader);
        effectMaterial.color = new Color(0.08f, 0.07f, 0.06f, 0.75f);
        for (int i = 0; i < skidTrails.Length; i++)
        {
            GameObject mark = new GameObject("Rear tire mark " + (i + 1));
            TrailRenderer trail = mark.AddComponent<TrailRenderer>();
            trail.time = 15f;
            trail.startWidth = 0.20f;
            trail.endWidth = 0.10f;
            trail.minVertexDistance = 0.12f;
            trail.material = effectMaterial;
            trail.emitting = false;
            skidTrails[i] = trail;
        }
    }

    void UpdateSkidMarks()
    {
        if (skidTrails == null || controller == null) return;
        WheelCollider[] rearWheels = { controller.rearLeft, controller.rearRight };
        for (int i = 0; i < skidTrails.Length; i++)
        {
            WheelCollider wheel = rearWheels[i];
            if (!wheel || !skidTrails[i]) continue;
            wheel.GetWorldPose(out Vector3 position, out _);
            skidTrails[i].transform.position = position - transform.up * (wheel.radius - 0.025f);
            bool slipping = wheel.GetGroundHit(out WheelHit hit) && Mathf.Abs(hit.sidewaysSlip) > 0.75f;
            skidTrails[i].emitting = controller.SpeedKmh > 12f && slipping;
        }
    }

    void BuyUpgrade(ref int level, string key, int price, string label)
    {
        if (level >= 3 || cash < price) return;
        cash -= price;
        level++;
        PlayerPrefs.SetInt(key, level);
        PlayerPrefs.SetInt("SmugglerRun_Cash", cash);
        PlayerPrefs.Save();
        ApplyUpgrades();
        Debug.Log(label + " amélioré au niveau " + level + ".");
    }

    void CreateHeadlights()
    {
        headlights = new Light[2];
        for (int i = 0; i < 2; i++)
        {
            var lamp = new GameObject("Phare " + (i + 1));
            lamp.transform.SetParent(transform, false);
            lamp.transform.localPosition = new Vector3(i == 0 ? -0.65f : 0.65f, 0.55f, 2.1f);
            lamp.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.88f, 0.67f);
            light.range = 68f;
            light.spotAngle = 58f;
            light.intensity = 6f;
            light.enabled = false;
            headlights[i] = light;
        }
    }

    void SetNight(bool enabled)
    {
        night = enabled;
        ApplyWeatherAtmosphere();
        RenderSettings.ambientMode = enabled ? AmbientMode.Flat : savedAmbientMode;
        RenderSettings.ambientLight = enabled ? new Color(0.08f, 0.10f, 0.17f) : savedAmbient;
        RenderSettings.ambientIntensity = enabled ? 0.35f : savedAmbientIntensity;
        if (sun) sun.intensity = enabled ? savedSunIntensity * 0.12f : savedSunIntensity;
        if (sun && !enabled) sun.color = savedSunColor;
        if (nightSkybox && nightSkybox.HasProperty("_Exposure"))
            nightSkybox.SetFloat("_Exposure", enabled ? savedSkyboxExposure * 0.12f : savedSkyboxExposure);
        if (police) police.SetNightMode(enabled);
    }

    void ApplyWeatherAtmosphere()
    {
        UpdateRouteAtmosphere();
    }

    void UpdateRouteAtmosphere()
    {
        bool dusty = weatherMode == WeatherMode.Dust;
        float z = route ? route.transform.InverseTransformPoint(transform.position).z : 0f;
        float tier = GetCurrentRouteTier();
        float mountain = route && route.IsOnCanyonTrack(transform.position) ? 1f
            : route && route.IsOnRuralDetour(transform.position) ? 0.68f
            : route && route.IsOnWadiRoute(transform.position) ? 0.30f : 0f;
        float city = route && tier == 0
            ? Mathf.InverseLerp(route.terrainSize * 0.36f, route.terrainSize * 0.39f, z)
              * (1f - Mathf.InverseLerp(route.terrainSize * 0.61f, route.terrainSize * 0.64f, z)) : 0f;
        RenderSettings.fog = savedFogEnabled || night || dusty || mountain > 0f;
        RenderSettings.fogMode = savedFogMode;
        if (savedFogMode == FogMode.Linear)
        {
            float rangeScale = dusty ? (night ? 0.22f : 0.30f) : (night ? 0.48f : 1f);
            rangeScale *= Mathf.Lerp(1.28f, 0.62f, mountain);
            RenderSettings.fogStartDistance = savedFogStartDistance * (dusty ? 0.55f : 1f) * Mathf.Lerp(1f, 0.82f, mountain);
            RenderSettings.fogEndDistance = Mathf.Max(RenderSettings.fogStartDistance + 45f,
                savedFogEndDistance * rangeScale);
        }
        else
        {
            RenderSettings.fogDensity = savedFogDensity * (night ? 1.5f : 1f)
                + (dusty ? 0.009f : 0f) + mountain * (night ? 0.0016f : 0.0028f);
        }
        Color fogColor = night ? new Color(0.075f, 0.095f, 0.14f)
            : dusty ? new Color(0.48f, 0.42f, 0.31f) : savedFogColor;
        if (!night && !dusty)
            fogColor = Color.Lerp(fogColor, new Color(0.72f, 0.68f, 0.58f), mountain * 0.48f + city * 0.2f);
        RenderSettings.fogColor = fogColor;
        if (!night)
        {
            RenderSettings.ambientIntensity = savedAmbientIntensity * Mathf.Lerp(1f, 0.78f, mountain);
            RenderSettings.ambientLight = Color.Lerp(savedAmbient, savedAmbient * 0.78f, mountain);
            if (sun)
            {
                sun.intensity = savedSunIntensity * Mathf.Lerp(1f, 1.035f, city);
                sun.color = Color.Lerp(savedSunColor, new Color(1f, 0.91f, 0.78f), city * 0.12f);
            }
        }
    }

    string GetWeatherLabel()
    {
        if (weatherMode == WeatherMode.Rain) return "PLUIE";
        if (weatherMode == WeatherMode.Dust) return "POUSSIÈRE";
        return "SEC";
    }

    void CycleWeather()
    {
        weatherMode = (WeatherMode)(((int)weatherMode + 1) % 3);
        if (rainWeather)
        {
            if (weatherMode == WeatherMode.Rain) rainWeather.Play();
            else rainWeather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (dustStorm)
        {
            if (weatherMode == WeatherMode.Dust) dustStorm.Play();
            else dustStorm.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (route) route.SetWetRoad(weatherMode == WeatherMode.Rain);
        ApplyWeatherAtmosphere();
        ApplySurfaceGrip();
        UpdateHUD();
    }

    void CreateWeatherEffects()
    {
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
        if (particleShader == null) particleShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (particleShader == null) return;
        weatherParticleMaterial = new Material(particleShader);
        weatherParticleMaterial.color = Color.white;
        rainWeather = CreateWeatherEmitter("Rain over route", new Color(0.72f, 0.81f, 0.9f, 0.58f), true);
        dustStorm = CreateWeatherEmitter("Kasserine dust wind", new Color(0.67f, 0.48f, 0.28f, 0.32f), false);
    }

    ParticleSystem CreateWeatherEmitter(string label, Color color, bool rain)
    {
        GameObject emitter = new GameObject(label);
        emitter.transform.SetParent(transform, false);
        emitter.transform.localPosition = rain ? new Vector3(0f, 11f, 0f) : new Vector3(0f, 3f, 0f);
        ParticleSystem particles = emitter.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = rain ? new ParticleSystem.MinMaxCurve(0.55f, 0.9f) : new ParticleSystem.MinMaxCurve(2.2f, 4.5f);
        main.startSpeed = 0f;
        main.startSize = rain ? new ParticleSystem.MinMaxCurve(0.025f, 0.045f) : new ParticleSystem.MinMaxCurve(0.45f, 1.4f);
        main.startColor = color;
        main.maxParticles = rain ? 1800 : 550;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = rain ? new Vector3(30f, 0.1f, 30f) : new Vector3(36f, 10f, 36f);
        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = rain ? new ParticleSystem.MinMaxCurve(-0.7f, 0.7f) : new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
        velocity.y = rain ? new ParticleSystem.MinMaxCurve(-22f, -15f) : new ParticleSystem.MinMaxCurve(-0.2f, 0.8f);
        velocity.z = rain ? new ParticleSystem.MinMaxCurve(-1f, 1f) : new ParticleSystem.MinMaxCurve(-1.5f, 3f);
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = rain ? 850f : 120f;
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.material = weatherParticleMaterial;
        renderer.renderMode = rain ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        if (rain) renderer.velocityScale = 0.06f;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    void OnDestroy()
    {
        if (night) SetNight(false);
        if (engineSound) engineSound.Stop();
        if (roadSound) roadSound.Stop();
        if (cityAmbienceSound) cityAmbienceSound.Stop();
        if (body) body.linearDamping = savedLinearDamping;
        if (controller) controller.SetSandSinkDepth(0f);
        if (garageOpen || pauseOpen || delivered)
        {
            Time.timeScale = previousTimeScale;
            AudioListener.pause = false;
        }
        if (hudCanvas) Destroy(hudCanvas.gameObject);
        if (minimapPaperSprite) Destroy(minimapPaperSprite);
        if (minimapPaperTexture) Destroy(minimapPaperTexture);
        if (rainWeather) rainWeather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (dustStorm) dustStorm.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (weatherParticleMaterial) Destroy(weatherParticleMaterial);
        if (route) route.SetWetRoad(false);
        RenderSettings.fog = savedFogEnabled;
        RenderSettings.fogDensity = savedFogDensity;
        RenderSettings.fogMode = savedFogMode;
        RenderSettings.fogStartDistance = savedFogStartDistance;
        RenderSettings.fogEndDistance = savedFogEndDistance;
        RenderSettings.fogColor = savedFogColor;
        RenderSettings.ambientMode = savedAmbientMode;
        RenderSettings.ambientLight = savedAmbient;
        RenderSettings.ambientIntensity = savedAmbientIntensity;
        if (nightSkybox && nightSkybox.HasProperty("_Exposure"))
            nightSkybox.SetFloat("_Exposure", savedSkyboxExposure);
        if (sun) sun.intensity = savedSunIntensity;
        if (sun) sun.color = savedSunColor;
        if (engineClip) Destroy(engineClip);
        if (cityAmbienceClip) Destroy(cityAmbienceClip);
        if (roadSoundClip) Destroy(roadSoundClip);
        if (heartbeatClip) Destroy(heartbeatClip);
        if (hazardSoundClip) Destroy(hazardSoundClip);
        if (effectMaterial) Destroy(effectMaterial);
        if (smokeMaterial) Destroy(smokeMaterial);
        if (skidTrails != null)
            foreach (TrailRenderer trail in skidTrails)
                if (trail) Destroy(trail.gameObject);
    }

    void BuildHUD()
    {
        GameObject canvasObject = new GameObject("Smuggler Run HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        hudCanvas = canvasObject.GetComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 20;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("Game UI Events", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        RectTransform status = CreatePanel(canvasObject.transform, "Driving status", new Vector2(0f, 1f), new Vector2(22f, -22f), new Vector2(370f, 398f));
        status.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.052f, 0.9f);
        CreateImage(status, "Cyan accent", new Vector2(0f, 0f), new Vector2(4f, 398f), new Color(0.16f, 0.72f, 0.87f, 1f));
        CreateText(status, font, "Game title", "SMUGGLER RUN", 21, FontStyle.Bold, new Vector2(18f, 14f), new Vector2(260f, 28f), Color.white, TextAnchor.UpperLeft);
        locationText = CreateText(status, font, "Route district and weather", "KASSERINE • SEC • M : MÉTÉO", 11, FontStyle.Bold, new Vector2(20f, 44f), new Vector2(310f, 20f), new Color(0.45f, 0.75f, 0.82f), TextAnchor.UpperLeft);
        speedText = CreateText(status, font, "Speed readout", "0", 66, FontStyle.Bold, new Vector2(15f, 61f), new Vector2(190f, 82f), Color.white, TextAnchor.UpperLeft);
        CreateText(status, font, "Speed unit", "KM/H", 15, FontStyle.Bold, new Vector2(172f, 106f), new Vector2(70f, 24f), new Color(0.66f, 0.72f, 0.78f), TextAnchor.UpperLeft);
        wantedText = CreateText(status, font, "Pursuit level", "POURSUITE  •  0/5", 13, FontStyle.Bold, new Vector2(218f, 73f), new Vector2(142f, 25f), new Color(0.62f, 0.76f, 0.79f), TextAnchor.UpperLeft);
        cashText = CreateText(status, font, "Cash readout", "0 $", 18, FontStyle.Bold, new Vector2(218f, 103f), new Vector2(142f, 26f), new Color(0.45f, 0.86f, 0.64f), TextAnchor.UpperLeft);
        cargoPercentText = CreateText(status, font, "Cargo condition", "CARGAISON  100%", 15, FontStyle.Bold, new Vector2(18f, 141f), new Vector2(330f, 21f), Color.white, TextAnchor.UpperLeft);
        cargoBar = CreateBar(status, new Vector2(18f, 166f), new Vector2(330f, 10f), new Color(0.22f, 0.82f, 0.54f));
        vehiclePercentText = CreateText(status, font, "Vehicle condition", "PICKUP  100%", 15, FontStyle.Bold, new Vector2(18f, 193f), new Vector2(330f, 21f), Color.white, TextAnchor.UpperLeft);
        vehicleBar = CreateBar(status, new Vector2(18f, 218f), new Vector2(330f, 10f), new Color(0.22f, 0.82f, 0.54f));
        CreateButton(status, font, "Open garage", "ATELIER  [G]", 232f, ToggleGarageFromButton, true);
        CreateButton(status, font, "Return to start", "RETOUR AU DÉPART", 276f, ReturnToStart, true);
        recoveryButton = CreateButton(status, font, "Recover pickup", "DÉGAGEMENT  [T]  •  150 $ SI BLOQUÉ", 320f, RecoverVehicle, true);
        recoveryButton.interactable = false;
        CreateText(status, font, "Controls hint", "ÉCHAP menu  •  H fantôme  •  P photo  •  M météo", 10, FontStyle.Normal, new Vector2(18f, 367f), new Vector2(340f, 17f), new Color(0.62f, 0.68f, 0.74f), TextAnchor.UpperLeft);

        BuildMinimap(canvasObject.transform, font);
        noticePanel = CreatePanel(canvasObject.transform, "Mission notification", new Vector2(0.5f, 1f),
            new Vector2(0f, -102f), new Vector2(650f, 96f)).gameObject;
        noticePanel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.052f, 0.94f);
        noticePanel.SetActive(false);
        noticeText = CreateText(noticePanel.transform, font, "Mission notification text", "", 16,
            FontStyle.Bold, new Vector2(18f, 15f), new Vector2(614f, 72f), Color.white, TextAnchor.MiddleCenter);
        noticeText.horizontalOverflow = HorizontalWrapMode.Wrap;

        mountainTimerPanel = CreatePanel(canvasObject.transform, "Mountain escape countdown", new Vector2(0.5f, 1f),
            new Vector2(0f, -26f), new Vector2(480f, 72f)).gameObject;
        mountainTimerBackground = mountainTimerPanel.GetComponent<Image>();
        mountainTimerBackground.color = new Color(0.025f, 0.035f, 0.052f, 0.94f);
        CreateImage(mountainTimerPanel.transform, "Timer amber accent", new Vector2(0f, 0f),
            new Vector2(4f, 72f), new Color(0.91f, 0.56f, 0.16f, 1f));
        mountainTimerText = CreateText(mountainTimerPanel.transform, font, "Mountain timer text",
            "RENFORTS DANS  •  02:30", 24, FontStyle.Bold, new Vector2(16f, 10f),
            new Vector2(448f, 50f), new Color(1f, 0.82f, 0.42f), TextAnchor.MiddleCenter);
        mountainTimerPanel.SetActive(false);
        CreateTimerWarningEdges(canvasObject.transform);

        garagePanel = CreatePanel(canvasObject.transform, "Garage panel", new Vector2(1f, 1f), new Vector2(-22f, -22f), new Vector2(360f, 316f)).gameObject;
        garagePanel.SetActive(false);
        Transform garage = garagePanel.transform;
        garagePanel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.052f, 0.96f);
        CreateImage(garage, "Garage accent", new Vector2(0f, 0f), new Vector2(4f, 316f), new Color(0.95f, 0.59f, 0.19f, 1f));
        CreateText(garage, font, "Garage title", "ATELIER DU PICKUP", 19, FontStyle.Bold, new Vector2(20f, 17f), new Vector2(270f, 27f), Color.white, TextAnchor.UpperLeft);
        CreateText(garage, font, "Garage subtitle", "AMÉLIORATIONS PERMANENTES", 10, FontStyle.Bold, new Vector2(21f, 46f), new Vector2(280f, 18f), new Color(0.62f, 0.72f, 0.78f), TextAnchor.UpperLeft);
        engineButton = CreateButton(garage, font, "Engine upgrade", "MOTEUR  •  NIVEAU 0/3", 76f, BuyEngine, false);
        suspensionButton = CreateButton(garage, font, "Suspension upgrade", "SUSPENSION  •  NIVEAU 0/3", 122f, BuySuspension, false);
        tireButton = CreateButton(garage, font, "Tire upgrade", "PNEUS  •  NIVEAU 0/3", 168f, BuyTires, false);
        repairButton = CreateButton(garage, font, "Repair vehicle", "RÉPARER PICKUP + CARGAISON  •  300 $", 214f, RepairVehicle, false);
        CreateButton(garage, font, "Close garage", "REPRENDRE LA ROUTE  •  ÉCHAP", 260f, CloseGarage, true);

        BuildPauseMenu(canvasObject.transform, font);

        RectTransform resultsRect = CreatePanel(canvasObject.transform, "Mission results", new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(720f, 460f));
        resultsPanel = resultsRect.gameObject;
        resultsPanel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.052f, 0.97f);
        CreateImage(resultsRect, "Results gold accent", new Vector2(0f, 0f), new Vector2(5f, 460f), new Color(0.88f, 0.58f, 0.2f, 1f));
        resultsTitleText = CreateText(resultsRect, font, "Mission result title", "LIVRAISON RÉUSSIE", 26,
            FontStyle.Bold, new Vector2(30f, 26f), new Vector2(650f, 42f), Color.white, TextAnchor.UpperLeft);
        resultsBodyText = CreateText(resultsRect, font, "Mission result summary", "", 17,
            FontStyle.Normal, new Vector2(32f, 92f), new Vector2(650f, 245f), new Color(0.88f, 0.9f, 0.91f), TextAnchor.UpperLeft);
        resultsBodyText.verticalOverflow = VerticalWrapMode.Overflow;
        resultsActionButton = CreateButton(resultsRect, font, "Continue or retry", "CONTINUER  •  NOUVELLE LIVRAISON", 390f,
            ContinueAfterDelivery, true);
        resultsActionButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(209f, -390f);
        resultsActionButtonText = resultsActionButton.GetComponentInChildren<Text>(true);
        resultsPanel.SetActive(false);

        GameObject photoCanvasObject = new GameObject("Mode photo — commandes", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        photoCanvas = photoCanvasObject.GetComponent<Canvas>();
        photoCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        photoCanvas.sortingOrder = 40;
        CanvasScaler photoScaler = photoCanvasObject.GetComponent<CanvasScaler>();
        photoScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        photoScaler.referenceResolution = new Vector2(1920f, 1080f);
        photoScaler.matchWidthOrHeight = 0.5f;
        RectTransform photoPanel = CreatePanel(photoCanvasObject.transform, "Photo controls", new Vector2(0.5f, 1f),
            new Vector2(0f, -20f), new Vector2(750f, 54f));
        photoPanel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.052f, 0.82f);
        photoHintText = CreateText(photoPanel, font, "Photo mode instructions",
            "MODE PHOTO  •  WASD / FLÈCHES : BOUGER  •  E/C : HAUT/BAS  •  SOURIS : REGARDER  •  F12 : CAPTURE  •  P/ÉCHAP : RETOUR",
            13, FontStyle.Bold, new Vector2(14f, 10f), new Vector2(720f, 34f), Color.white, TextAnchor.MiddleCenter);
        photoCanvas.enabled = false;
    }

    void TogglePhotoMode(bool open)
    {
        if (open == photoMode) return;
        photoMode = open;
        if (open)
        {
            photoCamera = Camera.main;
            if (!photoCamera) photoCamera = FindFirstObjectByType<Camera>();
            if (!photoCamera) { photoMode = false; return; }
            previousTimeScale = Time.timeScale;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            followCameraWasEnabled = followCamera && followCamera.enabled;
            if (followCamera) followCamera.enabled = false;
            photoYaw = photoCamera.transform.eulerAngles.y;
            photoPitch = photoCamera.transform.eulerAngles.x;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            if (hudCanvas) hudCanvas.enabled = false;
            if (photoCanvas) photoCanvas.enabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Time.timeScale = previousTimeScale;
            AudioListener.pause = false;
            if (hudCanvas) hudCanvas.enabled = true;
            if (photoCanvas) photoCanvas.enabled = false;
            if (followCamera) followCamera.enabled = followCameraWasEnabled;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
    }

    void UpdatePhotoMode()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || photoCamera == null) return;
        if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame)
        {
            TogglePhotoMode(false);
            return;
        }
        if (kb.f12Key.wasPressedThisFrame)
        {
            string fileName = "SmugglerRun_Photo_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            string path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
            ScreenCapture.CaptureScreenshot(path);
            if (photoHintText) photoHintText.text = "CAPTURE ENREGISTRÉE DANS LE DOSSIER DU JEU  •  F12 POUR UNE AUTRE";
        }
        if (Mouse.current != null)
        {
            Vector2 look = Mouse.current.delta.ReadValue();
            photoYaw += look.x * 0.12f;
            photoPitch = Mathf.Clamp(photoPitch - look.y * 0.12f, -80f, 80f);
            photoCamera.transform.rotation = Quaternion.Euler(photoPitch, photoYaw, 0f);
        }

        Vector3 movement = Vector3.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) movement.z += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) movement.z -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) movement.x += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) movement.x -= 1f;
        if (kb.eKey.isPressed) movement.y += 1f;
        if (kb.cKey.isPressed) movement.y -= 1f;
        if (movement.sqrMagnitude > 1f) movement.Normalize();
        float speed = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? photoMoveSpeed * 4f : photoMoveSpeed;
        photoCamera.transform.position += photoCamera.transform.TransformDirection(movement) * speed * Time.unscaledDeltaTime;
    }

    void BuildPauseMenu(Transform parent, Font font)
    {
        RectTransform panel = CreatePanel(parent, "Pause and settings", new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(520f, 660f));
        pausePanel = panel.gameObject;
        pausePanel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.052f, 0.98f);
        CreateImage(panel, "Pause gold accent", new Vector2(0f, 0f), new Vector2(5f, 660f),
            new Color(0.88f, 0.58f, 0.2f, 1f));
        CreateText(panel, font, "Pause title", "PAUSE  •  SMUGGLER RUN", 25, FontStyle.Bold,
            new Vector2(28f, 24f), new Vector2(450f, 40f), Color.white, TextAnchor.UpperLeft);
        CreateText(panel, font, "Pause controls", "CLAVIER  •  WASD / flèches, Espace, R\nMANETTE  •  stick gauche, RT/LT, A frein renforcé, B redresser\nREPRENDRE  •  ÉCHAP",
            13, FontStyle.Normal, new Vector2(30f, 76f), new Vector2(455f, 52f),
            new Color(0.72f, 0.78f, 0.81f), TextAnchor.UpperLeft);

        Button volumeButton = CreateButton(panel, font, "Cycle volume", "VOLUME  •  100%", 150f,
            CycleVolume, false);
        pauseFirstButton = volumeButton;
        pauseVolumeText = volumeButton.GetComponentInChildren<Text>(true);
        Button qualityButton = CreateButton(panel, font, "Cycle graphics quality", "QUALITÉ  •  ÉLEVÉE", 198f,
            CycleGraphicsQuality, false);
        pauseQualityText = qualityButton.GetComponentInChildren<Text>(true);
        Button displayButton = CreateButton(panel, font, "Toggle fullscreen", "PLEIN ÉCRAN  •  OUI", 246f,
            ToggleFullscreen, false);
        pauseDisplayText = displayButton.GetComponentInChildren<Text>(true);
        steeringButton = CreateButton(panel, font, "Steering sensitivity", "DIRECTION  •  NORMALE", 294f,
            CycleSteeringResponse, false);
        pauseSteeringText = steeringButton.GetComponentInChildren<Text>(true);
        ghostButton = CreateButton(panel, font, "Best run ghost", "FANTÔME  •  OUI", 342f,
            ToggleBestRunGhost, false);
        pauseGhostText = ghostButton.GetComponentInChildren<Text>(true);
        dailySeedButton = CreateButton(panel, font, "Daily route seed", "GRAINE DU JOUR  •  NON", 390f,
            ToggleDailySeed, false);
        pauseDailySeedText = dailySeedButton.GetComponentInChildren<Text>(true);
        CreateButton(panel, font, "Open photo mode", "MODE PHOTO  •  CAPTURE F12", 438f, OpenPhotoMode, false);
        pauseRecordsText = CreateText(panel, font, "Personal route records", "RECORDS PERSONNELS", 12,
            FontStyle.Bold, new Vector2(30f, 484f), new Vector2(455f, 76f),
            new Color(0.82f, 0.87f, 0.88f), TextAnchor.UpperLeft);
        CreateButton(panel, font, "Restart mission", "RETOUR AU DÉPART", 562f, ReturnToStart, false);
        CreateButton(panel, font, "Resume mission", "REPRENDRE LA FUITE  •  ÉCHAP", 610f,
            () => TogglePause(false), true);
        UpdatePauseSettingsLabels();
        pausePanel.SetActive(false);
    }

    void TogglePause(bool open)
    {
        if (pausePanel == null || pauseOpen == open || delivered) return;
        pauseOpen = open;
        pausePanel.SetActive(open);
        if (minimapPanel) minimapPanel.SetActive(!open && !garageOpen);
        if (open)
        {
            UpdatePauseSettingsLabels();
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            if (pauseFirstButton && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(pauseFirstButton.gameObject);
        }
        else
        {
            Time.timeScale = previousTimeScale;
            AudioListener.pause = false;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }
    }

    void CycleVolume()
    {
        float step = Mathf.Round(AudioListener.volume * 4f) / 4f;
        AudioListener.volume = step >= 0.999f ? 0f : step + 0.25f;
        PlayerPrefs.SetFloat("SmugglerRun_Volume", AudioListener.volume);
        PlayerPrefs.Save();
        UpdatePauseSettingsLabels();
    }

    void CycleGraphicsQuality()
    {
        if (QualitySettings.names.Length == 0) return;
        int next = (QualitySettings.GetQualityLevel() + 1) % QualitySettings.names.Length;
        QualitySettings.SetQualityLevel(next, true);
        PlayerPrefs.SetInt("SmugglerRun_Quality", next);
        PlayerPrefs.Save();
        UpdatePauseSettingsLabels();
    }

    void ToggleFullscreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
        PlayerPrefs.SetInt("SmugglerRun_Fullscreen", Screen.fullScreen ? 1 : 0);
        PlayerPrefs.Save();
        UpdatePauseSettingsLabels();
    }

    void UpdatePauseSettingsLabels()
    {
        if (pauseVolumeText) pauseVolumeText.text = "VOLUME  •  " + Mathf.RoundToInt(AudioListener.volume * 100f) + "%";
        if (pauseQualityText)
        {
            string quality = QualitySettings.names.Length > 0
                ? QualitySettings.names[QualitySettings.GetQualityLevel()].ToUpperInvariant() : "PAR DÉFAUT";
            pauseQualityText.text = "QUALITÉ  •  " + quality;
        }
        if (pauseDisplayText) pauseDisplayText.text = "PLEIN ÉCRAN  •  " + (Screen.fullScreen ? "OUI" : "NON");
        if (pauseSteeringText)
        {
            float response = controller ? controller.steeringResponse : 3.5f;
            string label = response < 2.75f ? "DOUCE" : response < 3.75f ? "NORMALE" : "RAPIDE";
            pauseSteeringText.text = "DIRECTION  •  " + label;
        }
        if (pauseGhostText) pauseGhostText.text = "FANTÔME  •  " + (runGhost && runGhost.IsVisible ? "OUI" : "NON");
        if (pauseDailySeedText)
            pauseDailySeedText.text = "GRAINE DU JOUR  •  " + (PlayerPrefs.GetInt("SmugglerRun_DailySeed", 0) != 0 ? "OUI" : "NON");
        if (pauseRecordsText)
        {
            pauseRecordsText.text = "RECORDS PERSONNELS  •  TEMPS PAR ITINÉRAIRE\n"
                + "RN13  " + FormatRouteRecord(0) + "     OUED  " + FormatRouteRecord(1) + "\n"
                + "CRÊTES  " + FormatRouteRecord(2) + "     CHAMBI  " + FormatRouteRecord(3);
        }
    }

    string FormatRouteRecord(int tier)
    {
        float time = PlayerPrefs.GetFloat("SmugglerRun_BestTime_Route_" + tier, 0f);
        return time <= 0f ? "—" : $"{Mathf.FloorToInt(time / 60f):00}:{Mathf.FloorToInt(time % 60f):00}";
    }

    void CycleSteeringResponse()
    {
        if (!controller) return;
        float response = controller.steeringResponse;
        controller.steeringResponse = response < 2.75f ? 3.5f : response < 3.75f ? 4.5f : 2.5f;
        PlayerPrefs.SetFloat("SmugglerRun_SteeringResponse", controller.steeringResponse);
        PlayerPrefs.Save();
        UpdatePauseSettingsLabels();
    }

    void ToggleBestRunGhost()
    {
        if (!runGhost) return;
        bool show = !runGhost.IsVisible;
        runGhost.SetVisible(show);
        UpdatePauseSettingsLabels();
        ShowNotice(show ? "FANTÔME DU MEILLEUR PASSAGE ACTIVÉ  •  H POUR MASQUER"
            : "FANTÔME MASQUÉ  •  H POUR AFFICHER", 2.5f);
    }

    void ToggleDailySeed()
    {
        bool enable = PlayerPrefs.GetInt("SmugglerRun_DailySeed", 0) == 0;
        PlayerPrefs.SetInt("SmugglerRun_DailySeed", enable ? 1 : 0);
        PlayerPrefs.Save();
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Scene scene = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(scene.name)) SceneManager.LoadScene(scene.name);
    }

    void OpenPhotoMode()
    {
        TogglePause(false);
        TogglePhotoMode(true);
    }

    void CreateTimerWarningEdges(Transform parent)
    {
        CreateTimerWarningEdge(parent, "Timer warning left edge", new Vector2(0f, 0f),
            new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(24f, 0f));
        CreateTimerWarningEdge(parent, "Timer warning right edge", new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(24f, 0f));
        CreateTimerWarningEdge(parent, "Timer warning top edge", new Vector2(0f, 1f),
            new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 24f));
        CreateTimerWarningEdge(parent, "Timer warning bottom edge", new Vector2(0f, 0f),
            new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f));
    }

    void CreateTimerWarningEdge(Transform parent, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 pivot, Vector2 size)
    {
        GameObject edgeObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        edgeObject.transform.SetParent(parent, false);
        RectTransform rect = edgeObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        Image image = edgeObject.GetComponent<Image>();
        image.color = new Color(0.92f, 0.035f, 0.02f, 0f);
        image.raycastTarget = false;
        image.enabled = false;
        timerWarningEdges.Add(image);
    }

    void BuildMinimap(Transform parent, Font font)
    {
        minimapPanel = CreatePanel(parent, "GPS de contrebande", Vector2.one,
            new Vector2(-22f, -22f), new Vector2(310f, 448f)).gameObject;
        Image panelImage = minimapPanel.GetComponent<Image>();
        minimapPaperTexture = CreateMinimapPaperTexture();
        minimapPaperSprite = Sprite.Create(minimapPaperTexture,
            new Rect(0f, 0f, minimapPaperTexture.width, minimapPaperTexture.height),
            new Vector2(0.5f, 0.5f), 100f);
        panelImage.sprite = minimapPaperSprite;
        panelImage.color = Color.white;

        CreateText(minimapPanel.transform, font, "Map title", "GPS DE CONTREBANDE", 18,
            FontStyle.Bold, new Vector2(16f, 13f), new Vector2(276f, 24f),
            new Color(0.20f, 0.16f, 0.11f), TextAnchor.UpperLeft);
        CreateText(minimapPanel.transform, font, "Map subtitle", "KASSERINE • CARTE TACTIQUE", 10,
            FontStyle.Bold, new Vector2(17f, 37f), new Vector2(276f, 17f),
            new Color(0.38f, 0.31f, 0.22f), TextAnchor.UpperLeft);

        minimapArea = CreatePanel(minimapPanel.transform, "Tactical map", new Vector2(0f, 1f),
            new Vector2(18f, 61f), new Vector2(274f, 278f));
        minimapArea.GetComponent<Image>().color = new Color(0.79f, 0.72f, 0.57f, 0.82f);
        CreateText(minimapArea, font, "North marker", "N  ▲", 10, FontStyle.Bold,
            new Vector2(235f, 5f), new Vector2(34f, 17f), new Color(0.25f, 0.20f, 0.14f),
            TextAnchor.UpperCenter);
        DrawMinimapRoutes();

        CreateMinimapLegendItem(font, new Vector2(18f, 350f), new Color(0.16f, 0.48f, 0.28f), "FACILE  •  RN13");
        CreateMinimapLegendItem(font, new Vector2(164f, 350f), new Color(0.72f, 0.55f, 0.13f), "MOYEN  •  OUED");
        CreateMinimapLegendItem(font, new Vector2(18f, 376f), new Color(0.82f, 0.36f, 0.12f), "DIFFICILE  •  CRÊTES");
        CreateMinimapLegendItem(font, new Vector2(164f, 376f), new Color(0.65f, 0.16f, 0.13f), "EXTRÊME  •  CHAMBI");
        CreateText(minimapPanel.transform, font, "Route reward hint", "PRIME DE RISQUE • JUSQU'À +65 %", 9,
            FontStyle.Bold, new Vector2(18f, 402f), new Vector2(276f, 16f),
            new Color(0.36f, 0.29f, 0.18f), TextAnchor.UpperLeft);
        minimapRouteText = CreateText(minimapPanel.transform, font, "Current route", "ROUTE : RN13 • FACILE", 11,
            FontStyle.Bold, new Vector2(18f, 423f), new Vector2(276f, 20f),
            new Color(0.20f, 0.16f, 0.11f), TextAnchor.UpperLeft);

        GameObject marker = new GameObject("Pickup map marker", typeof(RectTransform), typeof(Image));
        marker.transform.SetParent(minimapArea, false);
        minimapPlayerMarker = marker.GetComponent<RectTransform>();
        minimapPlayerMarker.anchorMin = new Vector2(0f, 1f);
        minimapPlayerMarker.anchorMax = new Vector2(0f, 1f);
        minimapPlayerMarker.pivot = new Vector2(0.5f, 0.5f);
        minimapPlayerMarker.sizeDelta = new Vector2(21f, 21f);
        Image markerBg = marker.GetComponent<Image>();
        markerBg.color = new Color(0.13f, 0.16f, 0.15f, 0.9f);
        markerBg.raycastTarget = false;
        minimapPlayerArrow = CreateText(marker.transform, font, "Pickup heading", "▲", 17,
            FontStyle.Bold, Vector2.zero, new Vector2(21f, 21f), new Color(1f, 0.91f, 0.64f),
            TextAnchor.MiddleCenter);
        RectTransform arrowRect = minimapPlayerArrow.rectTransform;
        arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
        arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.anchoredPosition = Vector2.zero;
    }

    Texture2D CreateMinimapPaperTexture()
    {
        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Procedural worn map paper";
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        System.Random random = new System.Random(4187);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float grain = (float)random.NextDouble() * 0.045f;
            float cloud = (Mathf.PerlinNoise(x * 0.055f, y * 0.055f) - 0.5f) * 0.12f;
            float edgeDistance = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
            float edge = Mathf.Clamp01(edgeDistance / 13f);
            float shade = grain + cloud - (1f - edge) * 0.09f;
            texture.SetPixel(x, y, new Color(
                Mathf.Clamp01(0.73f + shade),
                Mathf.Clamp01(0.65f + shade * 0.92f),
                Mathf.Clamp01(0.51f + shade * 0.74f), 1f));
        }
        texture.Apply(false, false);
        return texture;
    }

    void DrawMinimapRoutes()
    {
        if (!route || !minimapArea) return;
        float length = route.terrainSize;
        float mapWidth = minimapArea.sizeDelta.x;
        float mapHeight = minimapArea.sizeDelta.y;
        Color fold = new Color(0.42f, 0.34f, 0.22f, 0.18f);
        for (int i = 1; i <= 3; i++)
        {
            float x = mapWidth * i * 0.25f;
            float y = mapHeight * i * 0.25f;
            CreateMapSegment(new Vector2(x, 8f), new Vector2(x, mapHeight - 8f), 0.8f, fold);
            CreateMapSegment(new Vector2(8f, y), new Vector2(mapWidth - 8f, y), 0.8f, fold);
        }
        Color sharedRoad = new Color(0.37f, 0.34f, 0.28f, 1f);
        DrawMapRoute(0f, route.RouteForkStartZ, route.GetRoadCenterX, sharedRoad, 5f, 32);
        DrawMapRoute(route.RouteForkStartZ, route.RouteEndZ, route.GetRoadCenterX,
            new Color(0.16f, 0.48f, 0.28f, 1f), 5.2f, 120);
        if (route.enableWadiRoute)
            DrawMapRoute(route.RouteForkStartZ, route.RouteEndZ, route.GetWadiRouteCenterX,
                new Color(0.72f, 0.55f, 0.13f, 1f), 4.2f, 120);
        if (route.enableRuralDetour)
            DrawMapRoute(route.RouteForkStartZ, route.RouteEndZ, route.GetRuralDetourCenterX,
                new Color(0.82f, 0.36f, 0.12f, 1f), 4.6f, 120);
        if (route.enableCanyonTrack)
            DrawMapRoute(route.RouteForkStartZ, route.RouteEndZ, route.GetCanyonTrackCenterX,
                new Color(0.65f, 0.16f, 0.13f, 1f), 4.8f, 120);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        CreateText(minimapArea, font, "City label", "KASSERINE",
            8, FontStyle.Bold, new Vector2(147f, 116f), new Vector2(68f, 14f),
            new Color(0.34f, 0.25f, 0.16f), TextAnchor.UpperLeft);
        DrawRouteJunctionMarkers(font);
    }

    void DrawRouteJunctionMarkers(Font font)
    {
        float forkZ = route.RouteForkStartZ;
        AddMinimapJunctionMarker(route.GetRoadCenterX(forkZ), forkZ, font);
    }

    void AddMinimapJunctionMarker(float localX, float localZ, Font font)
    {
        float mapWidth = minimapArea.sizeDelta.x;
        float mapHeight = minimapArea.sizeDelta.y;
        float x = 8f + Mathf.Clamp01(localX / route.terrainSize) * (mapWidth - 16f);
        float y = 8f + (1f - Mathf.Clamp01(localZ / route.terrainSize)) * (mapHeight - 16f);
        Text marker = CreateText(minimapArea, font, "Route junction marker", "+", 14,
            FontStyle.Bold, new Vector2(x - 8f, y - 9f), new Vector2(18f, 18f),
            new Color(0.96f, 0.77f, 0.27f, 1f), TextAnchor.MiddleCenter);
        minimapJunctionMarkers.Add(marker.rectTransform);
    }

    void DrawMapRoute(float startZ, float endZ, System.Func<float, float> centerX, Color color,
        float width, int segments = 1)
    {
        if (endZ <= startZ) return;
        float size = route.terrainSize;
        float mapWidth = minimapArea.sizeDelta.x;
        float mapHeight = minimapArea.sizeDelta.y;
        Vector2 MapPoint(float z)
        {
            float x = centerX(z);
            return new Vector2(8f + Mathf.Clamp01(x / size) * (mapWidth - 16f),
                mapHeight - 8f - Mathf.Clamp01(z / size) * (mapHeight - 16f));
        }

        Vector2 previous = MapPoint(startZ);
        for (int i = 1; i <= segments; i++)
        {
            float z = Mathf.Lerp(startZ, endZ, i / (float)segments);
            Vector2 current = MapPoint(z);
            CreateMapSegment(previous, current, width + 2.2f,
                new Color(0.22f, 0.18f, 0.13f, 0.68f));
            CreateMapSegment(previous, current, width, color);
            previous = current;
        }
    }

    void DrawMapRoute(float z, float startX, float endX, Color color, float width)
    {
        float mapWidth = minimapArea.sizeDelta.x;
        float mapHeight = minimapArea.sizeDelta.y;
        Vector2 a = new Vector2(8f + Mathf.Clamp01(startX / route.terrainSize) * (mapWidth - 16f),
            mapHeight - 8f - Mathf.Clamp01(z / route.terrainSize) * (mapHeight - 16f));
        Vector2 b = new Vector2(8f + Mathf.Clamp01(endX / route.terrainSize) * (mapWidth - 16f), a.y);
        CreateMapSegment(a, b, width + 2f, new Color(0.22f, 0.18f, 0.13f, 0.68f));
        CreateMapSegment(a, b, width, color);
    }

    void CreateMapSegment(Vector2 a, Vector2 b, float width, Color color)
    {
        GameObject line = new GameObject("Map route segment", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(minimapArea, false);
        RectTransform rect = line.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        Vector2 delta = b - a;
        rect.sizeDelta = new Vector2(delta.magnitude, width);
        rect.anchoredPosition = new Vector2((a.x + b.x) * 0.5f, -(a.y + b.y) * 0.5f);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg);
        Image image = line.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    void CreateMinimapLegendItem(Font font, Vector2 position, Color color, string label)
    {
        Image swatch = CreateImage(minimapPanel.transform, "Route difficulty swatch",
            new Vector2(position.x, position.y + 2f), new Vector2(12f, 12f), color);
        swatch.raycastTarget = false;
        int fontSize = label.Length > 21 ? 8 : 9;
        CreateText(minimapPanel.transform, font, "Route difficulty label", label, fontSize,
            FontStyle.Bold, new Vector2(position.x + 18f, position.y), new Vector2(128f, 18f),
            new Color(0.24f, 0.19f, 0.13f), TextAnchor.UpperLeft);
    }

    void UpdateMinimap()
    {
        if (minimapArea == null || minimapPlayerMarker == null) return;
        for (int i = 0; i < minimapJunctionMarkers.Count; i++)
        {
            float pulse = 1f + 0.24f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f + i * 0.8f));
            minimapJunctionMarkers[i].localScale = Vector3.one * pulse;
        }
        if (!route)
        {
            minimapPlayerMarker.gameObject.SetActive(false);
            return;
        }

        minimapPlayerMarker.gameObject.SetActive(true);
        Vector3 localPosition = route.transform.InverseTransformPoint(transform.position);
        float size = Mathf.Max(route.terrainSize, 1f);
        float x = 8f + Mathf.Clamp01(localPosition.x / size) * (minimapArea.sizeDelta.x - 16f);
        float y = minimapArea.sizeDelta.y - 8f - Mathf.Clamp01(localPosition.z / size) * (minimapArea.sizeDelta.y - 16f);
        minimapPlayerMarker.anchoredPosition = new Vector2(x, -y);
        Vector3 localForward = route.transform.InverseTransformDirection(transform.forward);
        minimapPlayerArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
            -Mathf.Atan2(localForward.x, localForward.z) * Mathf.Rad2Deg);

        if (route.IsOnCanyonTrack(transform.position))
            SetMinimapRoute("CHAMBI • EXTRÊME", new Color(0.65f, 0.16f, 0.13f));
        else if (route.IsOnRuralDetour(transform.position))
            SetMinimapRoute("CRÊTES • DIFFICILE", new Color(0.82f, 0.36f, 0.12f));
        else if (route.IsOnWadiRoute(transform.position))
            SetMinimapRoute("OUED • MOYEN", new Color(0.72f, 0.55f, 0.13f));
        else SetMinimapRoute("RN13 • FACILE", new Color(0.16f, 0.48f, 0.28f));
    }

    void SetMinimapRoute(string label, Color color)
    {
        if (!minimapRouteText) return;
        minimapRouteText.text = "ROUTE : " + label;
        minimapRouteText.color = color;
    }

    RectTransform CreatePanel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    Image CreateImage(Transform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(position.x, -position.y);
        rect.sizeDelta = size;
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    Text CreateText(Transform parent, Font font, string name, string value, int size, FontStyle style,
        Vector2 position, Vector2 dimensions, Color color, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(position.x, -position.y);
        rect.sizeDelta = dimensions;
        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    Image CreateBar(Transform parent, Vector2 position, Vector2 size, Color fillColor)
    {
        Image background = CreateImage(parent, "Condition bar background", position, size, new Color(1f, 1f, 1f, 0.12f));
        GameObject fillObject = new GameObject("Condition bar fill", typeof(RectTransform), typeof(Image));
        fillObject.transform.SetParent(background.transform, false);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fill = fillObject.GetComponent<Image>();
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillAmount = 1f;
        fill.color = fillColor;
        fill.raycastTarget = false;
        return fill;
    }

    Button CreateButton(Transform parent, Font font, string name, string label, float top,
        UnityEngine.Events.UnityAction action, bool accent)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(18f, -top);
        rect.sizeDelta = new Vector2(parent.name == "Garage panel" ? 322f : 302f, 38f);
        Image image = buttonObject.GetComponent<Image>();
        image.color = accent ? new Color(0.08f, 0.35f, 0.43f, 1f) : new Color(0.11f, 0.14f, 0.18f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = accent ? new Color(0.12f, 0.48f, 0.58f) : new Color(0.18f, 0.23f, 0.29f);
        colors.pressedColor = new Color(0.08f, 0.58f, 0.68f);
        colors.disabledColor = new Color(0.12f, 0.13f, 0.14f, 0.55f);
        button.colors = colors;
        button.onClick.AddListener(action);
        Text labelText = CreateText(buttonObject.transform, font, "Label", label, 12, FontStyle.Bold,
            new Vector2(12f, 10f), new Vector2(rect.sizeDelta.x - 24f, 20f), Color.white, TextAnchor.MiddleLeft);
        labelText.alignment = TextAnchor.MiddleLeft;
        return button;
    }

    void UpdateHUD()
    {
        if (speedText == null) return;
        UpdateMinimap();
        speedText.text = controller ? Mathf.RoundToInt(controller.SpeedKmh).ToString() : "0";
        cashText.text = cash.ToString("N0") + " $";
        int wanted = police ? police.WantedLevel : 0;
        wantedText.text = "POURSUITE  •  " + wanted + "/5";
        wantedText.color = new Color(0.62f, 0.76f, 0.79f);
        cargoPercentText.text = "CARGAISON  " + Mathf.RoundToInt(cargoCondition) + "%";
        vehiclePercentText.text = "PICKUP  " + Mathf.RoundToInt(vehicleCondition) + "%";
        cargoBar.fillAmount = Mathf.Clamp01(cargoCondition / 100f);
        vehicleBar.fillAmount = Mathf.Clamp01(vehicleCondition / 100f);
        cargoBar.color = Color.Lerp(new Color(0.93f, 0.24f, 0.2f), new Color(0.22f, 0.82f, 0.54f), cargoBar.fillAmount);
        vehicleBar.color = Color.Lerp(new Color(0.93f, 0.24f, 0.2f), new Color(0.22f, 0.82f, 0.54f), vehicleBar.fillAmount);
        float z = route ? route.transform.InverseTransformPoint(transform.position).z : transform.position.z;
        bool onWadiTrack = route && route.IsOnWadiRoute(transform.position);
        bool onRuralTrack = route && route.IsOnRuralDetour(transform.position);
        bool onExtremeTrack = route && route.IsOnCanyonTrack(transform.position);
        string district = onExtremeTrack ? "CRÊTES DU CHAMBI • EXTRÊME"
            : onRuralTrack ? "PISTE DES CRÊTES • DIFFICILE"
            : onWadiTrack ? "OUED • MOYEN"
            : "RN13 • FACILE";
        locationText.text = district.ToUpperInvariant() + "  •  " + (night ? "NUIT" : "AUBE")
                          + "  •  " + GetWeatherLabel() + "  •  M";
        if (engineButton)
        {
            engineButton.GetComponentInChildren<Text>(true).text = engineLevel >= 3 ? "MOTEUR  •  MAXIMUM" : "MOTEUR  •  NIVEAU " + engineLevel + "/3  •  600 $";
            suspensionButton.GetComponentInChildren<Text>(true).text = suspensionLevel >= 3 ? "SUSPENSION  •  MAXIMUM" : "SUSPENSION  •  NIVEAU " + suspensionLevel + "/3  •  450 $";
            tireButton.GetComponentInChildren<Text>(true).text = tireLevel >= 3 ? "PNEUS  •  MAXIMUM" : "PNEUS  •  NIVEAU " + tireLevel + "/3  •  350 $";
            engineButton.interactable = engineLevel < 3 && cash >= 600;
            suspensionButton.interactable = suspensionLevel < 3 && cash >= 450;
            tireButton.interactable = tireLevel < 3 && cash >= 350;
            repairButton.interactable = cash >= 300 && (vehicleCondition < 100f || cargoCondition < 100f);
        }
        if (recoveryButton) recoveryButton.interactable = !garageOpen && CanRecoverVehicle();
    }

    void ToggleGarageFromButton() => ToggleGarage(!garageOpen);
    void CloseGarage() => ToggleGarage(false);

    void ReturnToStart()
    {
        if (pauseOpen)
        {
            pauseOpen = false;
            if (pausePanel) pausePanel.SetActive(false);
            Time.timeScale = previousTimeScale;
            AudioListener.pause = false;
        }
        if (delivered)
        {
            Time.timeScale = previousTimeScale;
            AudioListener.pause = false;
        }
        if (garageOpen) ToggleGarage(false);
        if (!route) route = FindFirstObjectByType<RoughRoadTerrain>();
        if (!route) return;

        delivered = false;
        missionFailed = false;
        cargoCondition = 100f;
        vehicleCondition = 100f;
        lastImpact = Time.time;
        lastTerrainDamageTime = Time.time;
        hasLastJoltVelocity = false;
        stoppedDuration = 0f;
        bestRouteTier = 0;
        currentRouteTier = -1;
        currentRouteTierDuration = 0f;
        warnedCheckpoint = false;
        activeExtremeCheckpointIndex = -1;
        ResetMountainEscapeTimer();
        missionStartTime = Time.time;
        if (runGhost)
        {
            runGhost.SetCurrentRoute(0);
            runGhost.BeginRun();
        }
        missionImpactCount = 0;
        maximumWantedDuringMission = 0;
        if (resultsPanel) resultsPanel.SetActive(false);
        route.ResetPlayerToStart();
        if (police)
        {
            police.ResetPursuit();
            police.BeginOpeningPursuit();
        }
        lastWantedLevel = police ? police.WantedLevel : 0;
        if (controller) controller.SetDamageCondition(1f);
        if (skidTrails != null)
            foreach (TrailRenderer trail in skidTrails)
                if (trail) trail.Clear();
        ApplyUpgrades();
        UpdateHUD();
    }

    void UpdateRecoveryTimer()
    {
        float speed = body ? body.linearVelocity.magnitude : 0f;
        stoppedDuration = speed < 1.5f ? stoppedDuration + Time.deltaTime : 0f;
        if (recoveryButton) recoveryButton.interactable = CanRecoverVehicle();
    }

    bool CanRecoverVehicle()
    {
        if (!route || !body) return false;
        bool overturned = Vector3.Dot(transform.up, Vector3.up) < 0.25f;
        bool offRoad = route.IsOutsideRoad(transform.position, 0.5f);
        return overturned || offRoad || stoppedDuration >= 4f;
    }

    void RecoverVehicle()
    {
        if (!CanRecoverVehicle())
        {
            ShowNotice("DÉGAGEMENT DISPONIBLE SI LE PICKUP EST BLOQUÉ", 2.5f);
            return;
        }
        bool recoveredNearby = route.RecoverPlayerNearRoad(transform);
        if (!recoveredNearby) route.ResetPlayerToStart();

        const int recoveryFee = 150;
        int paid = Mathf.Min(cash, recoveryFee);
        cash -= paid;
        cargoCondition = Mathf.Max(0f, cargoCondition - 2f);
        vehicleCondition = Mathf.Max(0f, vehicleCondition - 2f);
        lastImpact = Time.time;
        lastTerrainDamageTime = Time.time;
        hasLastJoltVelocity = false;
        stoppedDuration = 0f;
        if (police)
        {
            police.ReportRecovery();
            lastWantedLevel = police.WantedLevel;
        }
        PlayerPrefs.SetInt("SmugglerRun_Cash", cash);
        PlayerPrefs.Save();
        ApplyUpgrades();
        ShowNotice(recoveredNearby
            ? $"DÉPANNAGE  •  -{paid} $  •  POLICE +1  •  DÉGÂTS LÉGERS"
            : $"PASSAGE BLOQUÉ  •  RETOUR AU DÉPART  •  -{paid} $  •  POLICE +1", 4f);
        UpdateHUD();
    }

    void ToggleGarage(bool open)
    {
        if (garagePanel == null || garageOpen == open) return;
        garageOpen = open;
        garagePanel.SetActive(open);
        if (minimapPanel) minimapPanel.SetActive(!open);
        if (open)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }
        else
        {
            Time.timeScale = previousTimeScale;
            AudioListener.pause = false;
        }
        UpdateHUD();
    }

    void BuyEngine() => BuyUpgrade(ref engineLevel, "SmugglerRun_Engine", 600, "Moteur");
    void BuySuspension() => BuyUpgrade(ref suspensionLevel, "SmugglerRun_Suspension", 450, "Suspension");
    void BuyTires() => BuyUpgrade(ref tireLevel, "SmugglerRun_Tires", 350, "Pneus");

    void RepairVehicle()
    {
        if (cash < 300) return;
        cash -= 300;
        vehicleCondition = 100f;
        cargoCondition = 100f;
        PlayerPrefs.SetInt("SmugglerRun_Cash", cash);
        PlayerPrefs.Save();
        UpdateHUD();
    }

}
