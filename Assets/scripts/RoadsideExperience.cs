using System.Collections.Generic;
using UnityEngine;
using SmugglerRun.Track;

/// <summary>Construit des repères, un obstacle contournable et une poursuite qui monte après les sorties de route.</summary>
public class RoadsideExperience : MonoBehaviour
{
    public RoughRoadTerrain route;
    public Transform car;
    public float obstacleZ = 285f;
    public int firstPoliceInfraction = 1;
    public int backupPoliceInfraction = 3;

    static readonly float[] CityStartFractions = { 0.35f };
    static readonly float[] CityEndFractions = { 0.65f };
    static readonly string[] CityNames = { "Kasserine — centre-ville" };
    static readonly float[] CityCheckpointFractions = { 0.415f, 0.505f, 0.595f };

    [Header("Modèles de ville importés")]
    [Tooltip("Bâtiments petit, moyen et grand du Downtown City MegaKit")]
    public GameObject[] cityBuildingModels;
    [Tooltip("Utilise les modèles du Downtown City MegaKit, ajustés à l'échelle de maisons locales.")]
    public bool useImportedCityBuildings = true;
    [Header("Véhicule de police optionnel")]
    [Tooltip("Un modèle importé est ajusté automatiquement; le SUV intégré reste utilisé si ce champ est vide.")]
    public GameObject policeVehiclePrefab;
    [Header("Détails réalistes de chaussée")]
    [Tooltip("Atlas de marquages usés T_Street_Decals du pack de ville")]
    public Texture2D wornRoadDecals;
    [Header("Éboulement interactif — corniche extrême")]
    [Tooltip("Prefab de rocher physique (vide = rocher provisoire intégré).")]
    public GameObject rockfallRockPrefab;
    [Tooltip("Distance de détection, étendue pour laisser le temps aux rochers de tomber à grande vitesse.")]
    [Min(100f)] public float rockfallDetectionDistanceM = 140f;
    [Tooltip("Grondement optionnel joué au début de l'éboulement.")]
    public AudioClip rockfallRumbleClip;
    [Header("Rochers importés — pack PBR")]
    // Imported sample assets can be serialized by Unity as Prefab objects
    // rather than GameObject roots. Keep references broad and instantiate via
    // UnityEngine.Object so decorative rocks cannot abort hazard generation.
    public UnityEngine.Object largeRockVisualPrefab;
    public UnityEngine.Object mediumRockVisualPrefab;
    public UnityEngine.Object smallRockVisualPrefab;

    readonly List<Transform> patrolCars = new List<Transform>();
    readonly List<Vector3> patrolVelocities = new List<Vector3>();
    readonly List<Light> streetLights = new List<Light>();
    readonly List<Light[]> policeBeacons = new List<Light[]>();
    readonly List<Light> watchLights = new List<Light>();
    readonly List<Renderer> watchVisuals = new List<Renderer>();
    readonly List<Material> ownedMaterials = new List<Material>();
    readonly List<Mesh> ownedMeshes = new List<Mesh>();
    readonly List<Texture2D> ownedTextures = new List<Texture2D>();
    readonly List<MountainHazardWarning> mountainHazards = new List<MountainHazardWarning>();
    readonly List<RockfallHazard> interactiveRockfalls = new List<RockfallHazard>();
    readonly List<BridgeHazard> fragileBridges = new List<BridgeHazard>();

    class MountainHazardWarning
    {
        public float z;
        public bool extremeRoute;
        public bool shown;
        public bool bridge;
        public bool triggered;
        public bool punishingGap;
        public bool fallStarted;
        public bool rockLanded;
        public float fallStartedAt;
        public float warningTimeSeconds;
        public float bridgeDamageUntil;
        public float bridgeCollapseStartedAt;
        public bool bridgeCollapseArmed;
        public bool bridgeCollapseStarted;
        public Transform fallingRock;
        public Rigidbody fallingRockBody;
        public Vector3 rockStartPosition;
        public Vector3 rockLandingPosition;
        public Quaternion rockStartRotation;
        public Quaternion rockLandingRotation;
        public Transform[] fallingFragments;
        public Rigidbody[] fallingFragmentBodies;
        public Vector3[] fragmentStartPositions;
        public Vector3[] fragmentLandingPositions;
        public Quaternion[] fragmentStartRotations;
        public Quaternion[] fragmentLandingRotations;
        public Transform[] bridgePlanks;
        public Vector3[] bridgeBaseLocalPositions;
        public Quaternion[] bridgeBaseRotations;
        public string message;
    }

    struct CableSegment
    {
        public Vector3 start;
        public Vector3 end;
        public float radius;

        public CableSegment(Vector3 start, Vector3 end, float radius)
        {
            this.start = start;
            this.end = end;
            this.radius = radius;
        }
    }

    Material wood;
    Material leaf;
    Material steppeGrass;
    Material cactusGreen;
    Material stone;
    Material sunBleachedWood;
    Material scarpClay;
    Material scarpRock;
    Material scarpShadow;
    Material scarpFracture;
    Material bridgeRope;
    Material signColor;
    Material policeWhite;
    Material policeDark;
    Material blue;
    Material red;
    Material concrete;
    Material roadWhitePaint;
    Material wornRoadPaint;
    Material roadMetal;
    Material windowGlass;
    Material emberGlow;
    Material rustedMetal;
    Material rooftopWaterTank;
    Material[] facades;
    Material[] awnings;
    Material[] civilianCarPaints;
    bool wasOffRoad;
    bool nightMode;
    bool materialsCreated;
    int infractions;
    readonly List<Light[]> checkpointBeacons = new List<Light[]>();
    GameObject difficultExitBlockade;
    GameObject extremeExitBlockade;
    GameObject easyNearMissBlockade;
    GameObject wadiNearMissBlockade;
    GameObject difficultNearMissBlockade;
    GameObject extremeNearMissBlockade;
    bool mountainInterceptionActive;
    public int WantedLevel => Mathf.Clamp(infractions, 0, 5);

    void Start()
    {
        if (route == null) route = GetComponent<RoughRoadTerrain>();
        if (car == null && route != null) car = route.car;
        if (car == null)
        {
#if UNITY_2023_1_OR_NEWER
            CarController foundCar = FindFirstObjectByType<CarController>();
#else
            CarController foundCar = FindObjectOfType<CarController>();
#endif
            if (foundCar != null) car = foundCar.transform;
        }
        if (route == null || car == null)
        {
            Debug.LogWarning("RoadsideExperience : assigne Route et Car dans l'Inspector.");
            enabled = false;
            return;
        }

        // Le nouveau générateur remplace les primitives de végétation de l'ancien décor.
        KasserineVegetation vegetation = GetComponent<KasserineVegetation>();
        if (vegetation == null) vegetation = gameObject.AddComponent<KasserineVegetation>();
        vegetation.route = route;

        CreateMaterials();
        BuildLandmarksAndScenery();
        BuildRouteEntranceLandmarks();
        BuildDetourDelineators();
        BuildWadiRouteDelineators();
        BuildCanyonRouteDelineators();
        BuildWadiRockBanks();
        BuildCanyonRockBanks();
        BuildTrailsideClutter();
        BuildMountainHazards();
        BuildMountainExitBlockades();
        for (int i = 0; i < CityNames.Length; i++)
            BuildCityDistrict(route.terrainSize * CityStartFractions[i],
                route.terrainSize * CityEndFractions[i], i);
        BuildRoadMarkings();
        BuildRoadObstacle();
        BuildNightWatchLights();
        BuildPoliceCheckpoint();
        SetNightMode(nightMode);
    }

    void Update()
    {
        if (car == null || route == null) return;
        bool outside = route.IsOutsideRoad(car.position, 2f);
        if (outside && !wasOffRoad)
        {
            RegisterInfraction();
            wasOffRoad = true;
        }
        else if (!outside)
        {
            wasOffRoad = false;
        }

        if (nightMode)
        {
            for (int checkpoint = 0; checkpoint < checkpointBeacons.Count; checkpoint++)
            {
                Light[] beacons = checkpointBeacons[checkpoint];
                if (beacons == null || beacons.Length < 2) continue;
                bool flash = Mathf.Sin(Time.time * 13f + checkpoint * 1.7f) > 0f;
                if (beacons[0]) beacons[0].enabled = flash;
                if (beacons[1]) beacons[1].enabled = !flash;
            }
        }

        UpdateMountainHazardWarnings();
        UpdatePatrolCars();
    }

    public void ReportCrash(float impactSpeed)
    {
        if (impactSpeed > 12f) RegisterInfraction();
    }

    public void ReportRecovery()
    {
        RegisterInfraction();
    }

    public bool IsCheckpointAhead(Vector3 playerPosition, float warningDistance)
    {
        if (route == null) return false;
        Vector3 local = route.transform.InverseTransformPoint(playerPosition);
        if (route.IsOnWadiRoute(playerPosition) || route.IsOnRuralDetour(playerPosition)
            || route.IsOnCanyonTrack(playerPosition)) return false;
        for (int i = 0; i < CityCheckpointFractions.Length; i++)
        {
            float checkpointZ = route.terrainSize * CityCheckpointFractions[i];
            if (local.z > checkpointZ || local.z < checkpointZ - warningDistance) continue;
            float lateralDistance = Mathf.Abs(local.x - route.GetRoadCenterX(local.z));
            if (lateralDistance <= route.GetRoadWidthAt(local.z) * 0.65f) return true;
        }
        return false;
    }

    public void ResetPursuit()
    {
        ResetEnvironmentalHazards();
        infractions = 0;
        wasOffRoad = false;
        mountainInterceptionActive = false;
        if (difficultExitBlockade) difficultExitBlockade.SetActive(false);
        if (extremeExitBlockade) extremeExitBlockade.SetActive(false);
        if (easyNearMissBlockade) easyNearMissBlockade.SetActive(false);
        if (wadiNearMissBlockade) wadiNearMissBlockade.SetActive(false);
        if (difficultNearMissBlockade) difficultNearMissBlockade.SetActive(false);
        if (extremeNearMissBlockade) extremeNearMissBlockade.SetActive(false);
        foreach (MountainHazardWarning hazard in mountainHazards)
        {
            hazard.shown = false;
            hazard.triggered = false;
            hazard.fallStarted = false;
            hazard.fallStartedAt = 0f;
            hazard.rockLanded = false;
            hazard.bridgeDamageUntil = 0f;
            hazard.bridgeCollapseArmed = false;
            hazard.bridgeCollapseStarted = false;
            hazard.bridgeCollapseStartedAt = 0f;
            if (hazard.fallingRockBody)
            {
                if (!hazard.fallingRockBody.isKinematic)
                {
                    hazard.fallingRockBody.linearVelocity = Vector3.zero;
                    hazard.fallingRockBody.angularVelocity = Vector3.zero;
                }
                hazard.fallingRockBody.isKinematic = true;
            }
            if (hazard.fallingRock)
            {
                hazard.fallingRock.position = hazard.rockStartPosition;
                hazard.fallingRock.rotation = hazard.rockStartRotation;
                hazard.fallingRock.gameObject.SetActive(false);
            }
            if (hazard.fallingFragments != null)
            {
                for (int fragmentIndex = 0; fragmentIndex < hazard.fallingFragments.Length; fragmentIndex++)
                {
                    Transform fragment = hazard.fallingFragments[fragmentIndex];
                    if (!fragment) continue;
                    if (hazard.fallingFragmentBodies != null
                        && fragmentIndex < hazard.fallingFragmentBodies.Length
                        && hazard.fallingFragmentBodies[fragmentIndex])
                    {
                        Rigidbody fragmentBody = hazard.fallingFragmentBodies[fragmentIndex];
                        if (!fragmentBody.isKinematic)
                        {
                            fragmentBody.linearVelocity = Vector3.zero;
                            fragmentBody.angularVelocity = Vector3.zero;
                        }
                        fragmentBody.isKinematic = true;
                    }
                    fragment.gameObject.SetActive(false);
                    if (hazard.fragmentStartPositions != null && fragmentIndex < hazard.fragmentStartPositions.Length)
                        fragment.position = hazard.fragmentStartPositions[fragmentIndex];
                    if (hazard.fragmentStartRotations != null && fragmentIndex < hazard.fragmentStartRotations.Length)
                        fragment.rotation = hazard.fragmentStartRotations[fragmentIndex];
                }
            }
            if (hazard.bridgePlanks != null && hazard.bridgeBaseLocalPositions != null
                && hazard.bridgeBaseRotations != null)
            {
                for (int plankIndex = 0; plankIndex < hazard.bridgePlanks.Length
                    && plankIndex < hazard.bridgeBaseLocalPositions.Length
                    && plankIndex < hazard.bridgeBaseRotations.Length; plankIndex++)
                {
                    Transform plank = hazard.bridgePlanks[plankIndex];
                    if (!plank) continue;
                    plank.gameObject.SetActive(true);
                    plank.localPosition = hazard.bridgeBaseLocalPositions[plankIndex];
                    plank.localRotation = hazard.bridgeBaseRotations[plankIndex];
                }
            }
        }
        foreach (Transform patrol in patrolCars)
            if (patrol != null) Destroy(patrol.gameObject);
        patrolCars.Clear();
        patrolVelocities.Clear();
        policeBeacons.Clear();
    }

    public void ResetEnvironmentalHazards()
    {
        for (int i = 0; i < interactiveRockfalls.Count; i++)
            if (interactiveRockfalls[i]) interactiveRockfalls[i].ResetHazard();
        for (int i = 0; i < fragileBridges.Count; i++)
            if (fragileBridges[i]) fragileBridges[i].ResetHazard();
    }

    public void ActivateMountainExitBlockade(int routeTier)
    {
        if (mountainInterceptionActive) return;
        mountainInterceptionActive = true;
        if (difficultExitBlockade) difficultExitBlockade.SetActive(routeTier == 2);
        if (extremeExitBlockade) extremeExitBlockade.SetActive(routeTier == 3);
    }

    public void ActivateNearMissInterception(int routeTier)
    {
        if (routeTier == 0 && easyNearMissBlockade) easyNearMissBlockade.SetActive(true);
        else if (routeTier == 1 && wadiNearMissBlockade) wadiNearMissBlockade.SetActive(true);
        else if (routeTier == 2 && difficultNearMissBlockade) difficultNearMissBlockade.SetActive(true);
        else if (routeTier == 3 && extremeNearMissBlockade) extremeNearMissBlockade.SetActive(true);
    }

    /// <summary>Place une patrouille derrière le pickup au départ, sans sirène ni compte à rebours.</summary>
    public void BeginOpeningPursuit()
    {
        if (route == null || car == null || patrolCars.Count > 0) return;
        CreateMaterials();
        infractions = Mathf.Max(1, firstPoliceInfraction);
        patrolCars.Add(CreatePoliceVehicle("Patrol SUV", 0, true));
    }

    public void SetNightMode(bool enabled)
    {
        nightMode = enabled;
        foreach (Light lamp in streetLights)
            if (lamp != null) lamp.enabled = enabled;
        foreach (Light lamp in watchLights)
            if (lamp != null) lamp.enabled = enabled;
        foreach (Renderer visual in watchVisuals)
            if (visual != null) visual.enabled = enabled;
        foreach (Light[] beacons in checkpointBeacons)
            if (beacons != null)
                foreach (Light beacon in beacons)
                    if (beacon != null) beacon.enabled = enabled;
    }

    void RegisterInfraction()
    {
        infractions = Mathf.Min(infractions + 1, 5);
        if (infractions >= firstPoliceInfraction && patrolCars.Count < 1)
            patrolCars.Add(CreatePoliceVehicle("Patrol SUV", 0));
        if (infractions >= backupPoliceInfraction && patrolCars.Count < 2)
            patrolCars.Add(CreatePoliceVehicle("Backup SUV", 1));
        if (infractions >= 5 && patrolCars.Count < 3)
            patrolCars.Add(CreatePoliceVehicle("Highway Patrol SUV", 2));
    }

    void CreateMaterials()
    {
        if (materialsCreated) return;
        materialsCreated = true;
        wood = MakeMaterial(new Color(0.25f, 0.15f, 0.09f));
        wood.SetFloat("_Metallic", 0f);
        wood.SetFloat("_Smoothness", 0.14f);
        sunBleachedWood = MakeMaterial(new Color(0.38f, 0.29f, 0.18f));
        sunBleachedWood.SetFloat("_Metallic", 0f);
        sunBleachedWood.SetFloat("_Smoothness", 0.11f);
        scarpClay = MakeMaterial(new Color(0.38f, 0.27f, 0.18f));
        scarpRock = MakeMaterial(new Color(0.49f, 0.45f, 0.37f));
        scarpShadow = MakeMaterial(new Color(0.24f, 0.22f, 0.19f));
        scarpFracture = MakeMaterial(new Color(0.32f, 0.30f, 0.26f));
        bridgeRope = MakeMaterial(new Color(0.36f, 0.27f, 0.16f));
        scarpClay.SetFloat("_Metallic", 0f);
        scarpRock.SetFloat("_Metallic", 0f);
        scarpShadow.SetFloat("_Metallic", 0f);
        scarpFracture.SetFloat("_Metallic", 0f);
        bridgeRope.SetFloat("_Metallic", 0f);
        scarpClay.SetFloat("_Smoothness", 0.04f);
        scarpRock.SetFloat("_Smoothness", 0.035f);
        TerrainTextureFactory.Create(SurfaceKind.Limestone, route ? route.seed + 53 : 53,
            out Texture2D cliffAlbedo, out Texture2D cliffNormal);
        ownedTextures.Add(cliffAlbedo);
        ownedTextures.Add(cliffNormal);
        ApplyCliffMaps(scarpRock, cliffAlbedo, cliffNormal, 0.14f, 0.15f, 1f);
        ApplyCliffMaps(scarpClay, cliffAlbedo, cliffNormal, 0.14f, 0.15f, 0.62f);
        scarpShadow.SetFloat("_Smoothness", 0.025f);
        scarpFracture.SetFloat("_Smoothness", 0.02f);
        bridgeRope.SetFloat("_Smoothness", 0.18f);
        leaf = MakeMaterial(new Color(0.19f, 0.29f, 0.16f));
        steppeGrass = MakeMaterial(new Color(0.50f, 0.49f, 0.31f));
        cactusGreen = MakeMaterial(new Color(0.31f, 0.40f, 0.25f));
        stone = MakeMaterial(new Color(0.52f, 0.50f, 0.44f));
        stone.SetFloat("_Metallic", 0f);
        stone.SetFloat("_Smoothness", 0.035f);
        signColor = MakeMaterial(new Color(0.78f, 0.68f, 0.42f));
        policeWhite = MakeMaterial(new Color(0.83f, 0.84f, 0.82f));
        policeDark = MakeMaterial(new Color(0.08f, 0.12f, 0.16f));
        blue = MakeMaterial(new Color(0.05f, 0.24f, 0.95f));
        red = MakeMaterial(new Color(0.92f, 0.06f, 0.04f));
        concrete = MakeMaterial(new Color(0.42f, 0.41f, 0.37f));
        roadWhitePaint = MakeMaterial(new Color(0.78f, 0.76f, 0.68f));
        wornRoadPaint = MakeWornRoadMaterial(wornRoadDecals);
        roadMetal = MakeMaterial(new Color(0.25f, 0.27f, 0.27f));
        roadMetal.SetFloat("_Metallic", 0.58f);
        roadMetal.SetFloat("_Smoothness", 0.48f);
        windowGlass = MakeMaterial(new Color(0.10f, 0.19f, 0.23f));
        windowGlass.SetFloat("_Metallic", 0.42f);
        windowGlass.SetFloat("_Smoothness", 0.78f);
        emberGlow = MakeMaterial(new Color(1f, 0.37f, 0.08f));
        emberGlow.EnableKeyword("_EMISSION");
        emberGlow.SetColor("_EmissionColor", new Color(1f, 0.28f, 0.035f) * 2.6f);
        rustedMetal = MakeMaterial(new Color(0.34f, 0.18f, 0.10f));
        rustedMetal.SetFloat("_Metallic", 0.32f);
        rustedMetal.SetFloat("_Smoothness", 0.2f);
        rooftopWaterTank = MakeMaterial(new Color(0.055f, 0.065f, 0.06f));
        rooftopWaterTank.SetFloat("_Metallic", 0f);
        rooftopWaterTank.SetFloat("_Smoothness", 0.16f);
        facades = new[]
        {
            MakeMaterial(new Color(0.78f, 0.73f, 0.62f)),
            MakeMaterial(new Color(0.72f, 0.68f, 0.57f)),
            MakeMaterial(new Color(0.66f, 0.57f, 0.45f)),
            MakeMaterial(new Color(0.84f, 0.80f, 0.70f)),
            MakeMaterial(new Color(0.62f, 0.61f, 0.52f))
        };
        awnings = new[]
        {
            MakeMaterial(new Color(0.65f, 0.17f, 0.10f)),
            MakeMaterial(new Color(0.12f, 0.31f, 0.24f)),
            MakeMaterial(new Color(0.15f, 0.26f, 0.43f)),
            MakeMaterial(new Color(0.74f, 0.48f, 0.12f))
        };
        civilianCarPaints = new[]
        {
            MakeMaterial(new Color(0.70f, 0.69f, 0.63f)),
            MakeMaterial(new Color(0.42f, 0.20f, 0.13f)),
            MakeMaterial(new Color(0.18f, 0.25f, 0.29f)),
            MakeMaterial(new Color(0.72f, 0.72f, 0.67f))
        };
    }

    string GetCityName(int districtIndex)
    {
        return CityNames[Mathf.Clamp(districtIndex, 0, CityNames.Length - 1)];
    }

    bool IsInCity(float z)
    {
        for (int i = 0; i < CityNames.Length; i++)
            if (z >= route.terrainSize * CityStartFractions[i] &&
                z <= route.terrainSize * CityEndFractions[i])
                return true;
        return false;
    }

    public string GetRouteDistrictName(float z)
    {
        for (int i = 0; i < CityNames.Length; i++)
            if (z >= route.terrainSize * CityStartFractions[i] &&
                z <= route.terrainSize * CityEndFractions[i])
                return GetCityName(i).ToUpperInvariant();

        if (z < route.terrainSize * route.mountainZoneEnd)
            return "COLS DU CHAMBI";
        if (z < route.terrainSize * route.forestZoneEnd)
            return "CHAAB FORESTIER";
        if (z > route.terrainSize * CityEndFractions[CityNames.Length - 1])
            return "PLAINE DE SBEÏTLA";
        return "PISTE DE TERRE BATTUE";
    }

    void BuildLandmarksAndScenery()
    {
        float length = route.terrainSize;
        CreateSign(length * 0.035f, "DJEBEL CHAMBI", "CONTREFORTS • RN13");
        CreateSign(length * 0.34f, "KASSERINE", "ENTRÉE DE VILLE • 50");
        CreateSign(length * 0.50f, "CENTRE-VILLE", "RN13 • RALENTIR");
        CreateSign(length * 0.335f, "RN13 • FACILE", "AVENUE • ARRIVÉE DROITE");
        CreateBranchRouteSign(route.RouteForkStartZ - 14f, route.roadWidth,
            route.GetRoadCenterX, "4 ITINÉRAIRES", "CHOISIS TA ROUTE");
        CreateBranchRouteSign(route.RouteForkEndZ + 42f, route.wadiRouteWidth,
            route.GetWadiRouteCenterX, "OUED • MOYEN", "GRAVIER • PISTE OUVERTE");
        CreateBranchRouteSign(route.RouteForkEndZ + 42f, route.detourWidth,
            route.GetRuralDetourCenterX, "CRÊTES • DIFFICILE", "PIERRE • LACETS");
        CreateBranchRouteSign(route.RouteForkEndZ + 42f, route.canyonTrackWidth,
            route.GetCanyonTrackCenterX, "CHAMBI • EXTRÊME", "CORNICHE • ÉPINGLES");
        CreateBranchRouteSign(route.RouteEndZ - 30f, route.roadWidth,
            route.GetRoadCenterX, "ARRIVÉE RN13", "ROUTE FACILE");
        CreateBranchRouteSign(route.RouteEndZ - 30f, route.wadiRouteWidth,
            route.GetWadiRouteCenterX, "ARRIVÉE OUED", "ROUTE MOYENNE");
        CreateBranchRouteSign(route.RouteEndZ - 30f, route.detourWidth,
            route.GetRuralDetourCenterX, "ARRIVÉE CRÊTES", "ROUTE DIFFICILE");
        CreateBranchRouteSign(route.RouteEndZ - 30f, route.GetExtremeTrackWidth(route.RouteEndZ - 30f),
            route.GetCanyonTrackCenterX, "ARRIVÉE CHAMBI", "ROUTE EXTRÊME");
        CreateSign(length * 0.665f, "FIN DE KASSERINE", "PISTES ROCHEUSES");

        // Pins, oliviers, alfa, figuiers de Barbarie et rochers viennent du générateur dédié.
        if (GetComponent<KasserineVegetation>() != null) return;

        System.Random random = new System.Random(route.seed + 731);
        for (float z = 18f; z < route.terrainSize - 20f;)
        {
            float progress = z / route.terrainSize;
            if (!IsInCity(z))
            for (int side = -1; side <= 1; side += 2)
            {
                float lateral = side * (route.roadWidth * 0.5f + 13f + (float)random.NextDouble() * 42f);
                float x = Mathf.Clamp(route.GetRoadCenterX(z) + lateral, 5f, route.terrainSize - 5f);
                float ground = route.SampleGroundHeight(x, z);
                if (!route.IsOutsideRoad(new Vector3(x, ground, z), 12f)) continue;
                float choice = (float)random.NextDouble();
                Vector3 position = new Vector3(x, ground, z);

                if (progress < route.mountainZoneEnd)
                {
                    if (choice < 0.62f) CreateTree(position, (float)random.NextDouble());
                    else CreateRock(position, (float)random.NextDouble());
                }
                else if (progress < route.forestZoneEnd)
                {
                    if (choice < 0.39f) CreateTree(position, (float)random.NextDouble());
                    else if (choice < 0.68f) CreateSteppeTuft(position, (float)random.NextDouble());
                    else if (choice < 0.82f) CreatePricklyPear(position, (float)random.NextDouble());
                    else CreateRock(position, (float)random.NextDouble());
                }
                else if (choice < 0.44f)
                    CreateSteppeTuft(position, (float)random.NextDouble());
                else if (choice < 0.59f)
                    CreateTree(position, (float)random.NextDouble());
                else if (choice < 0.70f)
                    CreatePricklyPear(position, (float)random.NextDouble());
                else
                    CreateRock(position, (float)random.NextDouble());
            }
            z += progress < route.mountainZoneEnd ? 18f
               : progress < route.forestZoneEnd ? 13f : 23f;
        }
    }

    void BuildRouteEntranceLandmarks()
    {
        float z = route.RouteForkEndZ + 8f;
        BuildEasyRouteEntrance(z);
        BuildWadiRouteEntrance(z);
        BuildHardRouteEntrance(z);
        BuildExtremeRouteEntrance(z);
    }

    void BuildEasyRouteEntrance(float z)
    {
        Vector3 forward = route.GetRoadDirection(z);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 center = new Vector3(route.GetRoadCenterX(z), 0f, z);
        float sideOffset = route.GetRoadWidthAt(z) * 0.5f + 7f;
        Vector3 basePoint = center + right * sideOffset;
        basePoint.y = route.SampleGroundHeight(basePoint.x, basePoint.z);
        CreatePrimitive("RN13 easy route billboard post", PrimitiveType.Cube,
            basePoint + Vector3.up * 2.2f, new Vector3(0.28f, 4.4f, 0.28f), roadMetal, true);
        GameObject board = CreatePrimitive("RN13 easy route billboard", PrimitiveType.Cube,
            basePoint + Vector3.up * 4.5f, new Vector3(8.2f, 2.1f, 0.24f), signColor, true);
        AddLandmarkText(board, "RN13  •  KASSERINE", "AVENUE PRINCIPALE", new Color(0.10f, 0.14f, 0.16f));

        Vector3 signalPoint = center - right * (route.GetRoadWidthAt(z) * 0.5f + 4.2f);
        signalPoint.y = route.SampleGroundHeight(signalPoint.x, signalPoint.z);
        CreatePrimitive("RN13 traffic signal pole", PrimitiveType.Cylinder,
            signalPoint + Vector3.up * 2.1f, new Vector3(0.14f, 2.1f, 0.14f), roadMetal, true);
        CreatePrimitive("RN13 traffic signal housing", PrimitiveType.Cube,
            signalPoint + Vector3.up * 4.1f, new Vector3(0.62f, 1.65f, 0.48f), policeDark, true);
        Material[] lamps = { red, signColor, leaf };
        for (int i = 0; i < lamps.Length; i++)
            CreatePrimitive("RN13 signal lamp", PrimitiveType.Sphere,
                signalPoint + Vector3.up * (4.62f - i * 0.5f) + forward * 0.26f,
                new Vector3(0.26f, 0.26f, 0.12f), lamps[i], false);
    }

    void BuildWadiRouteEntrance(float z)
    {
        Vector3 forward = route.GetWadiRouteDirection(z);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 center = new Vector3(route.GetWadiRouteCenterX(z), 0f, z);
        float roadWidth = route.wadiRouteWidth;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 barrel = center + right * side * (roadWidth * 0.5f + 2.3f);
            barrel.y = route.SampleGroundHeight(barrel.x, barrel.z);
            CreatePrimitive("Wadi route rusted barrel", PrimitiveType.Cylinder,
                barrel + Vector3.up * 0.55f, new Vector3(0.72f, 0.55f, 0.72f), rustedMetal, true);
            CreatePrimitive("Wadi barrel rim", PrimitiveType.Cylinder,
                barrel + Vector3.up * 1.04f, new Vector3(0.74f, 0.035f, 0.74f), roadMetal, false);
        }

        // Petit pont de pierre avec un dégagement de 3,9 m, hors de la trajectoire du pickup.
        float bridgeZ = z + 11f;
        Vector3 bridgeCenter = new Vector3(route.GetWadiRouteCenterX(bridgeZ), 0f, bridgeZ);
        float roadY = route.SampleGroundHeight(bridgeCenter.x, bridgeCenter.z);
        bridgeCenter.y = roadY;
        Quaternion bridgeRotation = Quaternion.LookRotation(forward, Vector3.up);
        float pillarOffset = roadWidth * 0.5f + 3.2f;
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject pier = CreatePrimitive("Low stone bridge pier", PrimitiveType.Cube,
                bridgeCenter + right * side * pillarOffset + Vector3.up * 1.95f,
                new Vector3(1.25f, 3.9f, 1.8f), stone, true);
            pier.transform.rotation = bridgeRotation;
        }
        GameObject lintel = CreatePrimitive("Low stone bridge lintel", PrimitiveType.Cube,
            bridgeCenter + Vector3.up * 4.2f,
            new Vector3(roadWidth + 8.2f, 0.58f, 2.1f), stone, true);
        lintel.transform.rotation = bridgeRotation;
        GameObject parapetLeft = CreatePrimitive("Stone bridge parapet left", PrimitiveType.Cube,
            bridgeCenter + right * (roadWidth * 0.5f + 1.3f) + Vector3.up * 0.72f,
            new Vector3(0.38f, 1.1f, 2.1f), concrete, true);
        parapetLeft.transform.rotation = bridgeRotation;
        GameObject parapetRight = CreatePrimitive("Stone bridge parapet right", PrimitiveType.Cube,
            bridgeCenter - right * (roadWidth * 0.5f + 1.3f) + Vector3.up * 0.72f,
            new Vector3(0.38f, 1.1f, 2.1f), concrete, true);
        parapetRight.transform.rotation = bridgeRotation;
    }

    void BuildHardRouteEntrance(float z)
    {
        Vector3 forward = new Vector3(route.GetRuralDetourCenterX(z + 0.5f)
            - route.GetRuralDetourCenterX(z - 0.5f), 0f, 1f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 center = new Vector3(route.GetRuralDetourCenterX(z), 0f, z);
        float width = route.detourWidth;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 point = center + right * side * (width * 0.5f + 3f);
            point.y = route.SampleGroundHeight(point.x, point.z);
            Vector3 markerScale = new Vector3(2.1f, 1.65f, 1.8f);
            CreateFacetedRock("Hard route limestone marker",
                point + Vector3.up * (markerScale.y * 0.3f), markerScale,
                Quaternion.Euler(9f, side * 17f, 13f), route.seed + Mathf.RoundToInt(z) + side * 31,
                true, mediumRockVisualPrefab);
        }
        CreateRouteEntranceBoard(center, right, width, z, "PISTE ROCHEUSE", "CRÊTES • DIFFICILE", signColor);
        Vector3 warningPoint = center + right * (width * 0.5f + 4f);
        warningPoint.y = route.SampleGroundHeight(warningPoint.x, warningPoint.z);
        CreatePrimitive("Hard route warning board", PrimitiveType.Cube,
            warningPoint + Vector3.up * 1.7f,
            new Vector3(0.42f, 0.22f, 0.08f), red, false);
    }

    void BuildExtremeRouteEntrance(float z)
    {
        Vector3 forward = new Vector3(route.GetCanyonTrackCenterX(z + 0.5f)
            - route.GetCanyonTrackCenterX(z - 0.5f), 0f, 1f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 center = new Vector3(route.GetCanyonTrackCenterX(z), 0f, z);
        float width = route.GetExtremeTrackWidth(z);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 point = center + right * side * (width * 0.5f + 2.6f);
            point.y = route.SampleGroundHeight(point.x, point.z);
            GameObject barrier = CreatePrimitive("Bent extreme route barrier", PrimitiveType.Cube,
                point + Vector3.up * 0.35f, new Vector3(0.38f, 0.7f, 3.2f), roadMetal, true);
            barrier.transform.rotation = Quaternion.LookRotation(forward, Vector3.up)
                * Quaternion.Euler(0f, 0f, side * 12f);
        }
        CreateRouteEntranceBoard(center, right, width, z, "ZONE ROCHEUSE", "CHAMBI • EXTRÊME", red);
    }

    void CreateRouteEntranceBoard(Vector3 center, Vector3 right, float width, float z,
        string heading, string subtitle, Material boardMaterial)
    {
        Vector3 point = center + right * (width * 0.5f + 5.5f);
        point.y = route.SampleGroundHeight(point.x, z);
        CreatePrimitive("Route entry sign post", PrimitiveType.Cylinder,
            point + Vector3.up * 1.0f, new Vector3(0.13f, 1.0f, 0.13f), roadMetal, true);
        GameObject board = CreatePrimitive("Route entry landmark sign", PrimitiveType.Cube,
            point + Vector3.up * 2.1f, new Vector3(3.7f, 1.15f, 0.18f), boardMaterial, true);
        board.transform.rotation = Quaternion.LookRotation(-right, Vector3.up);
        AddLandmarkText(board, heading, subtitle, boardMaterial == red
            ? Color.white : new Color(0.10f, 0.09f, 0.07f));
    }

    void AddLandmarkText(GameObject board, string heading, string subtitle, Color color)
    {
        GameObject textObject = new GameObject("Route entrance lettering");
        textObject.transform.SetParent(board.transform, false);
        textObject.transform.localPosition = new Vector3(0f, 0f, 0.13f);
        TextMesh text = textObject.AddComponent<TextMesh>();
        text.text = heading + "\n" + subtitle;
        text.fontSize = 38;
        text.characterSize = 0.036f;
        text.alignment = TextAlignment.Center;
        text.anchor = TextAnchor.MiddleCenter;
        text.color = color;
    }

    void CreateBranchRouteSign(float z, float width, System.Func<float, float> centerline,
        string topLine, string bottomLine)
    {
        Vector3 forward = new Vector3(centerline(z + 0.5f) - centerline(z - 0.5f), 0f, 1f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 basePosition = new Vector3(centerline(z), 0f, z) + right * (width * 0.5f + 2.4f);
        basePosition.y = route.SampleGroundHeight(basePosition.x, basePosition.z);
        CreatePrimitive("Route direction sign post", PrimitiveType.Cylinder,
            basePosition + Vector3.up * 1.05f, new Vector3(0.11f, 1.05f, 0.11f), wood, true);
        GameObject board = CreatePrimitive("Route direction sign", PrimitiveType.Cube,
            basePosition + Vector3.up * 2.05f, new Vector3(2.8f, 0.9f, 0.14f), signColor, true);
        board.transform.rotation = Quaternion.LookRotation(-right, Vector3.up);
        GameObject labelObject = new GameObject("Route direction lettering");
        labelObject.transform.SetParent(board.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0f, 0.09f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = topLine + "\n" + bottomLine;
        label.fontSize = 42;
        label.characterSize = 0.028f;
        label.alignment = TextAlignment.Center;
        label.anchor = TextAnchor.MiddleCenter;
        label.color = new Color(0.12f, 0.10f, 0.07f);
    }

    void CreateSign(float z, string topLine, string bottomLine)
    {
        float x = Mathf.Clamp(route.GetRoadCenterX(z) + route.GetRoadWidthAt(z) * 0.5f + 2.4f,
            4f, route.terrainSize - 4f);
        float y = route.SampleGroundHeight(x, z);
        GameObject post = CreatePrimitive("Road sign post", PrimitiveType.Cylinder,
            new Vector3(x, y + 1.05f, z), new Vector3(0.11f, 1.05f, 0.11f), wood, true);
        post.transform.SetParent(transform, true);
        GameObject board = CreatePrimitive("Road sign", PrimitiveType.Cube,
            new Vector3(x, y + 2.05f, z), new Vector3(1.7f, 0.72f, 0.14f), signColor, true);
        board.transform.SetParent(transform, true);
        board.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        GameObject labelObject = new GameObject("Sign lettering");
        labelObject.transform.SetParent(board.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0f, 0.09f);
        labelObject.transform.localRotation = Quaternion.identity;
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = topLine + "\n" + bottomLine;
        label.fontSize = 46;
        label.characterSize = 0.027f;
        label.alignment = TextAlignment.Center;
        label.anchor = TextAnchor.MiddleCenter;
        label.color = new Color(0.12f, 0.10f, 0.07f);
    }

    void CreateWadiRouteSign(float z, string topLine, string bottomLine)
    {
        Vector3 forward = route.GetWadiRouteDirection(z);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 basePosition = new Vector3(route.GetWadiRouteCenterX(z), 0f, z)
            - right * (route.wadiRouteWidth * 0.5f + 2.4f);
        basePosition.y = route.SampleGroundHeight(basePosition.x, basePosition.z);
        CreatePrimitive("Wadi route sign post", PrimitiveType.Cylinder,
            basePosition + Vector3.up * 1.05f, new Vector3(0.11f, 1.05f, 0.11f), wood, true);
        GameObject board = CreatePrimitive("Wadi route sign", PrimitiveType.Cube,
            basePosition + Vector3.up * 2.05f, new Vector3(1.7f, 0.72f, 0.14f), signColor, true);
        board.transform.rotation = Quaternion.LookRotation(right, Vector3.up);
        GameObject labelObject = new GameObject("Wadi sign lettering");
        labelObject.transform.SetParent(board.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0f, 0.09f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = topLine + "\n" + bottomLine;
        label.fontSize = 46;
        label.characterSize = 0.027f;
        label.alignment = TextAlignment.Center;
        label.anchor = TextAnchor.MiddleCenter;
        label.color = new Color(0.12f, 0.10f, 0.07f);
    }

    void CreateCanyonRouteSign(float z, string topLine, string bottomLine)
    {
        Vector3 forward = new Vector3(
            route.GetCanyonTrackCenterX(z + 0.5f) - route.GetCanyonTrackCenterX(z - 0.5f), 0f, 1f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 basePosition = new Vector3(route.GetCanyonTrackCenterX(z), 0f, z)
            + right * (route.GetExtremeTrackWidth(z) * 0.5f + 2.4f);
        basePosition.y = route.SampleGroundHeight(basePosition.x, basePosition.z);
        CreatePrimitive("Canyon route sign post", PrimitiveType.Cylinder,
            basePosition + Vector3.up * 1.05f, new Vector3(0.11f, 1.05f, 0.11f), wood, true);
        GameObject board = CreatePrimitive("Canyon route sign", PrimitiveType.Cube,
            basePosition + Vector3.up * 2.05f, new Vector3(1.9f, 0.78f, 0.14f), signColor, true);
        board.transform.rotation = Quaternion.LookRotation(-right, Vector3.up);
        GameObject labelObject = new GameObject("Canyon sign lettering");
        labelObject.transform.SetParent(board.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0f, 0.09f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = topLine + "\n" + bottomLine;
        label.fontSize = 44;
        label.characterSize = 0.027f;
        label.alignment = TextAlignment.Center;
        label.anchor = TextAnchor.MiddleCenter;
        label.color = new Color(0.12f, 0.10f, 0.07f);
    }

    void CreateTree(Vector3 position, float variation)
    {
        float progress = position.z / route.terrainSize;
        if (progress < 0.35f)
        {
            // Pin d'Alep : tronc élancé et couronne irrégulière en étages, pas un cône géométrique.
            float height = 6f + variation * 4f;
            CreatePrimitive("Chambi pine trunk", PrimitiveType.Cylinder,
                position + Vector3.up * (height * 0.43f),
                new Vector3(0.22f + variation * 0.12f, height * 0.43f, 0.22f + variation * 0.12f), wood, false);
            List<GameObject> crownParts = new List<GameObject>();
            for (int tier = 0; tier < 4; tier++)
            {
                float tierHeight = height * (0.48f + tier * 0.115f);
                int branches = 4 + (tier % 2);
                for (int branch = 0; branch < branches; branch++)
                {
                    float angle = (branch * 360f / branches) + variation * 117f + tier * 31f;
                    float length = height * (0.25f - tier * 0.025f) * (0.82f + variation * 0.32f);
                    Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    GameObject bough = CreatePrimitive("Aleppo pine bough", PrimitiveType.Sphere,
                        position + Vector3.up * tierHeight + direction * length * 0.46f,
                        new Vector3(length * 0.95f, 0.24f + variation * 0.12f, length * 0.46f), leaf, false);
                    bough.transform.rotation = Quaternion.Euler(0f, angle, -7f + variation * 14f);
                    crownParts.Add(bough);
                }
            }
            CombineFoliage(crownParts, "Aleppo pine crown");
            return;
        }

        // Oliviers bas et noueux sur les plaines autour de Kasserine.
        float oliveHeight = 2.8f + variation * 1.8f;
        CreatePrimitive("Olive tree trunk", PrimitiveType.Cylinder,
            position + Vector3.up * (oliveHeight * 0.38f),
            new Vector3(0.34f, oliveHeight * 0.38f, 0.34f), wood, false);
        List<GameObject> oliveParts = new List<GameObject>();
        int clumps = 5 + Mathf.RoundToInt(variation * 3f);
        for (int i = 0; i < clumps; i++)
        {
            float angle = i * 137.5f + variation * 80f;
            float radius = 0.48f + (i % 3) * 0.25f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(radius, 0f, 0f);
            GameObject crownPart = CreatePrimitive("Olive foliage cluster", PrimitiveType.Sphere,
                position + Vector3.up * (oliveHeight * (0.72f + (i % 2) * 0.08f)) + offset,
                new Vector3(1.15f, 0.68f + (i % 3) * 0.08f, 0.92f) * (0.78f + variation * 0.26f), leaf, false);
            crownPart.transform.rotation = Quaternion.Euler(0f, angle, (i % 2 == 0 ? -1f : 1f) * 9f);
            oliveParts.Add(crownPart);
        }
        CombineFoliage(oliveParts, "Wild olive crown");
    }

    void CombineFoliage(List<GameObject> parts, string label)
    {
        if (parts == null || parts.Count == 0) return;
        List<CombineInstance> combines = new List<CombineInstance>(parts.Count);
        foreach (GameObject part in parts)
        {
            if (!part) continue;
            MeshFilter filter = part.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            combines.Add(new CombineInstance
            {
                mesh = filter.sharedMesh,
                transform = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix
            });
            part.SetActive(false);
            Destroy(part);
        }
        if (combines.Count == 0) return;
        Mesh mesh = new Mesh { name = label + " mesh" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.CombineMeshes(combines.ToArray(), true, true, false);
        ownedMeshes.Add(mesh);
        GameObject crown = new GameObject(label);
        crown.transform.SetParent(transform, false);
        crown.transform.localPosition = Vector3.zero;
        crown.AddComponent<MeshFilter>().sharedMesh = mesh;
        crown.AddComponent<MeshRenderer>().sharedMaterial = leaf;
    }

    void CreateSteppeTuft(Vector3 position, float variation)
    {
        int blades = 5 + Mathf.RoundToInt(variation * 3f);
        for (int i = 0; i < blades; i++)
        {
            float angle = i * (360f / blades) + variation * 70f;
            float height = 0.55f + ((i * 17 % blades) / (float)blades) * 0.75f;
            float lean = Mathf.Lerp(8f, 28f, variation);
            GameObject blade = CreatePrimitive("Alfa steppe tuft", PrimitiveType.Capsule,
                position + Vector3.up * (height * 0.43f),
                new Vector3(0.08f, height * 0.48f, 0.08f), steppeGrass, false);
            blade.transform.rotation = Quaternion.Euler(lean, angle, lean * 0.45f);
        }
    }

    void CreatePricklyPear(Vector3 position, float variation)
    {
        float scale = 0.75f + variation * 0.55f;
        for (int pad = 0; pad < 3; pad++)
        {
            float angle = variation * 90f + pad * 120f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0.25f * scale, 0.55f * scale, 0f);
            GameObject leafPad = CreatePrimitive("Prickly pear pad", PrimitiveType.Sphere,
                position + offset, new Vector3(0.42f, 0.62f, 0.16f) * scale, cactusGreen, false);
            leafPad.transform.rotation = Quaternion.Euler(0f, angle, variation * 12f);
        }
    }

    void BuildDetourDelineators()
    {
        if (!route.enableRuralDetour) return;
        float start = route.RouteForkEndZ + 18f;
        float end = route.RouteEndZ - 12f;
        for (float z = start; z < end; z += 32f)
        {
            Vector3 forward = new Vector3(
                route.GetRuralDetourCenterX(z + 0.5f) - route.GetRuralDetourCenterX(z - 0.5f),
                0f, 1f).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float centerX = route.GetRuralDetourCenterX(z);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 position = new Vector3(centerX, 0f, z)
                    + right * (side * (route.detourWidth * 0.5f + 1.05f));
                position.y = route.SampleGroundHeight(position.x, position.z);
                CreatePrimitive("Rural track reflector", PrimitiveType.Cube,
                    position + Vector3.up * 0.34f, new Vector3(0.12f, 0.68f, 0.12f), roadWhitePaint, false);
                CreatePrimitive("Red reflector cap", PrimitiveType.Cube,
                    position + Vector3.up * 0.52f, new Vector3(0.16f, 0.13f, 0.15f), red, false);
            }
        }
    }

    void BuildWadiRouteDelineators()
    {
        if (!route.enableWadiRoute) return;
        float start = route.RouteForkEndZ + 18f;
        float end = route.RouteEndZ - 12f;
        for (float z = start; z < end; z += 42f)
        {
            if (!route.IsWadiRouteActiveAt(z)) continue;
            Vector3 forward = route.GetWadiRouteDirection(z);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float centerX = route.GetWadiRouteCenterX(z);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 position = new Vector3(centerX, 0f, z)
                    + right * (side * (route.wadiRouteWidth * 0.5f + 1.2f));
                position.y = route.SampleGroundHeight(position.x, position.z);
                CreatePrimitive("Wadi route reflector", PrimitiveType.Cube,
                    position + Vector3.up * 0.3f, new Vector3(0.12f, 0.6f, 0.12f), signColor, false);
                CreatePrimitive("Wadi route reflector cap", PrimitiveType.Cube,
                    position + Vector3.up * 0.49f, new Vector3(0.16f, 0.12f, 0.15f), red, false);
            }
        }
    }

    void BuildCanyonRouteDelineators()
    {
        if (!route.enableCanyonTrack) return;
        float start = route.RouteForkEndZ + 18f;
        float end = route.RouteEndZ - 12f;
        for (float z = start; z < end; z += 42f)
        {
            Vector3 forward = new Vector3(
                route.GetCanyonTrackCenterX(z + 0.5f) - route.GetCanyonTrackCenterX(z - 0.5f), 0f, 1f).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 position = new Vector3(route.GetCanyonTrackCenterX(z), 0f, z)
                    + right * (side * (route.GetExtremeTrackWidth(z) * 0.5f + 1.2f));
                position.y = route.SampleGroundHeight(position.x, position.z);
                CreatePrimitive("Canyon route red reflector", PrimitiveType.Cube,
                    position + Vector3.up * 0.34f, new Vector3(0.13f, 0.68f, 0.13f), red, false);
            }
        }
    }

    void BuildMountainHazards()
    {
        float routeLength = route.RouteEndZ - route.RouteForkEndZ;
        if (route.enableRuralDetour)
        {
            float hazardZ = route.RouteForkEndZ + routeLength * 0.25f;
            CreateRockfallHazard(hazardZ, route.GetRuralDetourCenterX,
                route.detourWidth, false, 1, "ÉBOULEMENT");
            float washoutJumpZ = route.RouteForkEndZ + routeLength * 0.66f;
            CreateErosionJumpWarning(washoutJumpZ, route.GetRuralDetourCenterX,
                route.detourWidth, false, "CASSURE D'ÉROSION  •  GARDE DE L'ÉLAN");
            float washoutZ = route.RouteForkEndZ + routeLength * 0.73f;
            CreateRockfallHazard(washoutZ, route.GetRuralDetourCenterX,
                route.detourWidth, false, 1, "CHAUSSÉE EFFONDRÉE");
            BuildRoadCollapseScarp(washoutZ, route.GetRuralDetourCenterX,
                route.detourWidth, 1);
        }
        if (route.enableCanyonTrack)
        {
            // The seven sections total 2,500 m; distances here are measured from
            // the point where the four routes finish separating.
            float jumpZ = route.GetExtremeRouteZAtDistance(route.GetExtremeSectionDistance(3, 0.625f));
            float jumpWidth = route.GetExtremeTrackWidth(jumpZ);
            CreateErosionJumpWarning(jumpZ, route.GetCanyonTrackCenterX,
                jumpWidth, true, "FISSURE 6 M  •  GARDE L'ÉLAN", true);
            BuildJumpGapDebris(jumpZ, route.GetCanyonTrackCenterX, jumpWidth);

            CreateExtremeRouteWarning(route.GetExtremeSectionDistance(2, 0.43f), "VIRAGE AVEUGLE", "VIRAGE AVEUGLE  •  FREINE AVANT LA CRÊTE");
            CreateExtremeRouteWarning(route.GetExtremeSectionDistance(5, 0.45f), "ÉPINGLE AVEUGLE", "DESCENTE AVEUGLE  •  FREINE AVANT L'ÉPINGLE");

            float rockfallZ = route.GetExtremeRouteZAtDistance(route.GetExtremeSectionDistance(4, 0.5f));
            CreateInteractiveRockfallHazard(rockfallZ, route.GetCanyonTrackCenterX,
                route.GetExtremeTrackWidth(rockfallZ), "CHUTE DE PIERRES • ATTENTION");

            CreateExtremeCheckpointMarker(route.GetExtremeCheckpointDistance(0), "REPRISE 1", "APRÈS LA FISSURE");
            CreateExtremeCheckpointMarker(route.GetExtremeCheckpointDistance(1), "REPRISE 2", "APRÈS L'ÉPINGLE 3");

            float bridgeZ = route.GetExtremeRouteZAtDistance(route.GetExtremeSectionDistance(6, 1f / 3f));
            BuildFragileMountainBridge(bridgeZ, route.GetCanyonTrackCenterX,
                route.GetExtremeTrackWidth(bridgeZ));
        }
    }

    void CreateExtremeRouteWarning(float distance, string signLabel, string message)
    {
        float z = route.GetExtremeRouteZAtDistance(distance);
        float signZ = Mathf.Max(route.RouteForkEndZ + 20f, z - 125f);
        float width = route.GetExtremeTrackWidth(signZ);
        Vector3 forward = new Vector3(
            route.GetCanyonTrackCenterX(signZ + 0.5f) - route.GetCanyonTrackCenterX(signZ - 0.5f),
            0f, 1f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 signCenter = route.transform.TransformPoint(
            new Vector3(route.GetCanyonTrackCenterX(signZ), 0f, signZ));
        signCenter.y = route.SampleGroundHeight(
            route.GetCanyonTrackCenterX(signZ), signZ);
        CreateMountainHazardSign(signCenter, right, width, 1, signLabel);
        mountainHazards.Add(new MountainHazardWarning
        {
            z = z,
            extremeRoute = true,
            message = message
        });
    }

    void CreateExtremeCheckpointMarker(float distance, string heading, string subtitle)
    {
        float z = route.GetExtremeRouteZAtDistance(distance);
        float width = route.GetExtremeTrackWidth(z);
        float x = route.GetCanyonTrackCenterX(z);
        Vector3 forward = new Vector3(
            route.GetCanyonTrackCenterX(z + 0.5f) - route.GetCanyonTrackCenterX(z - 0.5f),
            0f, 1f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 center = route.transform.TransformPoint(new Vector3(x, 0f, z));
        center.y = route.SampleGroundHeight(x, z);
        Vector3 postPosition = center + right * (width * 0.5f + 2.8f);
        Vector3 postLocal = route.transform.InverseTransformPoint(postPosition);
        postPosition.y = route.SampleGroundHeight(postLocal.x, postLocal.z);
        CreatePrimitive("Extreme route checkpoint post", PrimitiveType.Cylinder,
            postPosition + Vector3.up * 1.05f, new Vector3(0.16f, 1.05f, 0.16f), roadMetal, false);
        GameObject board = CreatePrimitive("Extreme route checkpoint sign", PrimitiveType.Cube,
            postPosition + Vector3.up * 2.15f, new Vector3(3.6f, 1.15f, 0.16f), signColor, false);
        board.transform.rotation = Quaternion.LookRotation(-right, Vector3.up);
        AddLandmarkText(board, heading, subtitle, new Color(0.10f, 0.16f, 0.13f));
    }

    void CreateErosionJumpWarning(float z, System.Func<float, float> centerline,
        float trackWidth, bool extremeRoute, string message, bool punishingGap = false)
    {
        float warningZ = z - 145f;
        Vector3 forward = new Vector3(
            centerline(warningZ + 0.5f) - centerline(warningZ - 0.5f), 0f, 1f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 warningCenter = new Vector3(centerline(warningZ), 0f, warningZ);
        CreateMountainHazardSign(warningCenter, right, trackWidth, 1, message);
        mountainHazards.Add(new MountainHazardWarning
        {
            z = z,
            extremeRoute = extremeRoute,
            punishingGap = punishingGap,
            message = message
        });
    }

    void CreateInteractiveRockfallHazard(float z, System.Func<float, float> centerline,
        float trackWidth, string message)
    {
        float x = centerline(z);
        float signZ = Mathf.Max(route.RouteForkEndZ + 20f, z - 80f);
        float signX = centerline(signZ);
        Vector3 signForward = new Vector3(
            centerline(signZ + 0.5f) - centerline(signZ - 0.5f), 0f, 1f).normalized;
        Vector3 signRight = Vector3.Cross(Vector3.up, signForward).normalized;
        Vector3 signCenter = route.transform.TransformPoint(new Vector3(signX, 0f, signZ));
        signCenter.y = route.SampleGroundHeight(signX, signZ);
        CreateMountainHazardSign(signCenter, signRight, route.GetExtremeTrackWidth(signZ),
            1, "CHUTE DE PIERRES");

        Vector3 localForward = new Vector3(
            centerline(z + 0.5f) - centerline(z - 0.5f), 0f, 1f).normalized;
        Vector3 worldForward = route.transform.TransformDirection(localForward).normalized;
        Vector3 worldCenter = route.transform.TransformPoint(
            new Vector3(x, route.SampleGroundHeight(x, z), z));
        GameObject hazardObject = new GameObject("Interactive rockfall — extreme route");
        hazardObject.transform.SetPositionAndRotation(worldCenter,
            Quaternion.LookRotation(worldForward, Vector3.up));

        RockfallHazard hazard = hazardObject.AddComponent<RockfallHazard>();
        hazard.rockPrefab = rockfallRockPrefab;
        if (hazard.rockPrefab == null && largeRockVisualPrefab is GameObject importedRock)
            hazard.rockPrefab = importedRock;
        hazard.triggerDistanceM = Mathf.Max(rockfallDetectionDistanceM, 140f);
        hazard.useTrigger = false;
        hazard.progressFrame = route.transform;
        hazard.progressTargetZ = z;
        hazard.spawnHeightM = 8f;
        hazard.landMarginSeconds = 0.8f;
        hazard.minBigDelaySeconds = 0.5f;
        hazard.seed = route.seed + Mathf.RoundToInt(z * 31f);
        hazard.minImpactSpeed = 5f;
        hazard.rumbleClip = rockfallRumbleClip;
        hazard.Configure(trackWidth, 60f, message, car);
        hazard.onWarn.AddListener(HandleInteractiveRockfallWarning);
        hazard.onRocksFall.AddListener(HandleInteractiveRockfallStart);
        hazard.onPlayerHit.AddListener(HandleInteractiveRockfallHit);
        interactiveRockfalls.Add(hazard);
    }

    void HandleInteractiveRockfallWarning(string message)
    {
        TruckGameplay gameplay = car ? car.GetComponent<TruckGameplay>() : null;
        if (!gameplay && car) gameplay = car.GetComponentInParent<TruckGameplay>();
        if (gameplay) gameplay.ShowEnvironmentalWarning(message, 6f);
    }

    void HandleInteractiveRockfallStart()
    {
        TruckGameplay gameplay = car ? car.GetComponent<TruckGameplay>() : null;
        if (!gameplay && car) gameplay = car.GetComponentInParent<TruckGameplay>();
        if (gameplay) gameplay.NotifyRockfallImpact();
    }

    void HandleInteractiveRockfallHit(float impactSpeed)
    {
        TruckGameplay gameplay = car ? car.GetComponent<TruckGameplay>() : null;
        if (!gameplay && car) gameplay = car.GetComponentInParent<TruckGameplay>();
        if (gameplay) gameplay.ReportRockfallPlayerHit(impactSpeed);
    }

    void CreateRockfallHazard(float z, System.Func<float, float> centerline,
        float trackWidth, bool extremeRoute, int blockedSide, string hazardName)
    {
        Vector3 forward = new Vector3(centerline(z + 0.5f) - centerline(z - 0.5f), 0f, 1f).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 center = new Vector3(centerline(z), 0f, z);
        float halfWidth = trackWidth * 0.5f;

        // Éboulement en bord de chaussée : il mord sur une voie, mais laisse
        // toujours un passage large de l'autre côté pour éviter le blocage.
        float rockInset = extremeRoute ? 0.6f : 1.8f;
        Vector3 mainRockFoot = center + right * (blockedSide * Mathf.Max(0.5f, halfWidth - rockInset));
        mainRockFoot.y = route.SampleGroundHeight(mainRockFoot.x, mainRockFoot.z);
        // On the 4 m extreme road, a 4 m boulder would seal the entire lane.
        // Keep a visible, collidable rock on the edge and preserve a 2.5 m passage.
        Vector3 mainRockScale = extremeRoute
            ? new Vector3(1.35f, 1.9f, 2.4f)
            : new Vector3(6.0f, 4.0f, 5.2f);
        GameObject mainRock = CreateFacetedRock(
            hazardName + " — bloc anguleux",
            mainRockFoot + Vector3.up * 1.05f,
            mainRockScale,
            Quaternion.Euler(-24f, 18f * blockedSide, 32f),
            route.seed + Mathf.RoundToInt(z * 31f) + blockedSide * 173,
            true, largeRockVisualPrefab);
        Rigidbody mainRockBody = mainRock.AddComponent<Rigidbody>();
        mainRockBody.mass = 180f;
        mainRockBody.isKinematic = true;
        mainRockBody.interpolation = RigidbodyInterpolation.Interpolate;
        mainRockBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        mainRockBody.linearDamping = 0.08f;
        mainRockBody.angularDamping = 1.8f;
        Vector3 landingPosition = mainRock.transform.position;
        Quaternion landingRotation = Quaternion.Euler(13f, 27f * blockedSide, 17f);
        Vector3 rockStartFoot = center + right * (blockedSide * (halfWidth + 12f));
        rockStartFoot.y = route.SampleGroundHeight(rockStartFoot.x, rockStartFoot.z);
        // Le bloc arrive du haut de la paroi, pas d'un petit rebond au bord de la piste.
        Vector3 startPosition = rockStartFoot + Vector3.up * 13.5f;
        Quaternion startRotation = Quaternion.Euler(-24f, 18f * blockedSide, 32f);
        mainRock.transform.SetPositionAndRotation(startPosition, startRotation);
        mainRock.SetActive(false);

        List<Transform> fallingFragments = new List<Transform>();
        List<Vector3> fragmentStarts = new List<Vector3>();
        List<Vector3> fragmentLandings = new List<Vector3>();
        List<Quaternion> fragmentStartRotations = new List<Quaternion>();
        List<Quaternion> fragmentLandingRotations = new List<Quaternion>();
        List<Rigidbody> fragmentBodies = new List<Rigidbody>();
        for (int fragmentIndex = 0; fragmentIndex < 2; fragmentIndex++)
        {
            float fragmentSize = extremeRoute
                ? (fragmentIndex == 0 ? 1.15f : 0.9f)
                : (fragmentIndex == 0 ? 2.8f : 2.2f);
            float fragmentInset = extremeRoute ? 0.4f : 1.55f;
            float fragmentLateral = blockedSide * (halfWidth - fragmentInset);
            float fragmentAlong = fragmentIndex == 0 ? -2.4f : 2.7f;
            Vector3 fragmentFoot = center + right * fragmentLateral + forward * fragmentAlong;
            fragmentFoot.y = route.SampleGroundHeight(fragmentFoot.x, fragmentFoot.z);
            Quaternion fragmentLandingRotation = Quaternion.Euler(
                18f + fragmentIndex * 9f, 37f * blockedSide + fragmentIndex * 31f,
                -14f + fragmentIndex * 19f);
            GameObject fragment = CreateFacetedRock("Falling landslide fragment",
                fragmentFoot + Vector3.up * fragmentSize * 0.38f,
                new Vector3(fragmentSize, fragmentSize * 0.72f, fragmentSize * 0.88f),
                fragmentLandingRotation,
                route.seed + Mathf.RoundToInt(z * 17f) + fragmentIndex * 733,
                true, mediumRockVisualPrefab);
            Vector3 fragmentLanding = fragment.transform.position;
            Vector3 fragmentStart = fragmentLanding
                + right * (blockedSide * (7f + fragmentIndex * 1.5f))
                + Vector3.up * (8.5f + fragmentIndex * 2.0f);
            Quaternion fragmentStartRotation = Quaternion.Euler(-28f, 48f * blockedSide, 24f * (fragmentIndex + 1));
            fragment.transform.SetPositionAndRotation(fragmentStart, fragmentStartRotation);
            Rigidbody fragmentBody = fragment.AddComponent<Rigidbody>();
            fragmentBody.mass = fragmentIndex == 0 ? 45f : 30f;
            fragmentBody.isKinematic = true;
            fragmentBody.interpolation = RigidbodyInterpolation.Interpolate;
            fragmentBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            fragmentBody.linearDamping = 0.12f;
            fragmentBody.angularDamping = 2.2f;
            fragment.SetActive(false);
            fallingFragments.Add(fragment.transform);
            fragmentBodies.Add(fragmentBody);
            fragmentStarts.Add(fragmentStart);
            fragmentLandings.Add(fragmentLanding);
            fragmentStartRotations.Add(fragmentStartRotation);
            fragmentLandingRotations.Add(fragmentLandingRotation);
        }

        // Keep the secondary rubble near the main rock. Placing it closer to
        // the road centre can leave a car-width gap too narrow to pass,
        // especially on the 11 m extreme route.
        Vector3 smallerFoot = center + forward * 2.2f
            + right * (blockedSide * (extremeRoute ? halfWidth + 1.2f
                : Mathf.Max(0.45f, halfWidth - 2.8f)));
        smallerFoot.y = route.SampleGroundHeight(smallerFoot.x, smallerFoot.z);
        GameObject smallerRock = CreateFacetedRock("Rockfall angular rubble beside the clear lane",
            smallerFoot + Vector3.up * 0.42f, new Vector3(1.85f, 1.35f, 1.7f),
            Quaternion.Euler(9f, 41f, 12f), route.seed + Mathf.RoundToInt(z * 13f), true,
            mediumRockVisualPrefab);

        // A broad scree fan makes the collapse read as a slope failure rather
        // than a pair of isolated balls. Keep every chunk outside the free lane.
        System.Random screeRandom = new System.Random(route.seed + Mathf.RoundToInt(z * 7f) + 991);
        for (int chunk = 0; chunk < 9; chunk++)
        {
            float lateral = halfWidth + 1.1f + (float)screeRandom.NextDouble() * 7.5f;
            float along = ((float)screeRandom.NextDouble() - 0.5f) * 15f;
            Vector3 foot = center + right * (blockedSide * lateral) + forward * along;
            foot.y = route.SampleGroundHeight(foot.x, foot.z);
            float size = 0.8f + (float)screeRandom.NextDouble() * 1.65f;
            float height = size * (0.48f + (float)screeRandom.NextDouble() * 0.38f);
            CreateFacetedRock("Landslide scree chunk",
                foot + Vector3.up * (height * 0.32f),
                new Vector3(size, height, size * (0.7f + (float)screeRandom.NextDouble() * 0.55f)),
                Quaternion.Euler((float)screeRandom.NextDouble() * 25f,
                    (float)screeRandom.NextDouble() * 360f, (float)screeRandom.NextDouble() * 25f),
                route.seed + chunk * 67 + Mathf.RoundToInt(z), true, smallRockVisualPrefab);
        }

        float warningZ = z - 145f;
        Vector3 warningForward = new Vector3(
            centerline(warningZ + 0.5f) - centerline(warningZ - 0.5f), 0f, 1f).normalized;
        Vector3 warningRight = Vector3.Cross(Vector3.up, warningForward).normalized;
        Vector3 warningCenter = new Vector3(centerline(warningZ), 0f, warningZ);
        CreateMountainHazardSign(warningCenter, warningRight, trackWidth, blockedSide,
            hazardName);

        mountainHazards.Add(new MountainHazardWarning
        {
            z = z,
            extremeRoute = extremeRoute,
            warningTimeSeconds = extremeRoute ? 4f : 0f,
            fallingRock = mainRock.transform,
            fallingRockBody = mainRockBody,
            rockStartPosition = startPosition,
            rockLandingPosition = landingPosition,
            rockStartRotation = startRotation,
            rockLandingRotation = landingRotation,
            fallingFragments = fallingFragments.ToArray(),
            fallingFragmentBodies = fragmentBodies.ToArray(),
            fragmentStartPositions = fragmentStarts.ToArray(),
            fragmentLandingPositions = fragmentLandings.ToArray(),
            fragmentStartRotations = fragmentStartRotations.ToArray(),
            fragmentLandingRotations = fragmentLandingRotations.ToArray(),
            message = hazardName
                + "  •  PASSE À " + (blockedSide > 0 ? "GAUCHE" : "DROITE")
        });
    }

    void BuildFragileMountainBridge(float z, System.Func<float, float> centerline, float width)
    {
        // The terrain is already cut for this ravine. The bridge spans its full
        // 120 m washout, with the deck overlapping the road approaches by 1 m.
        float deckHalfSpan = route.extremeBridgeGapHalfSpan + 9f;
        float startZ = z - deckHalfSpan;
        float endZ = z + deckHalfSpan;
        float startY = route.SampleGroundHeight(centerline(startZ), startZ) + 0.16f;
        float endY = route.SampleGroundHeight(centerline(endZ), endZ) + 0.16f;
        float deckWidth = Mathf.Max(width * 0.90f, 3.2f);
        const int plankIntervals = 120;
        List<CableSegment> cables = new List<CableSegment>();

        // The component builds collidable planks and owns the collapse/reset logic.
        Vector3 bridgePosition = route.transform.TransformPoint(
            new Vector3(centerline(startZ), 0f, startZ));
        bridgePosition.y = startY;
        Vector3 bridgeForward = route.transform.TransformDirection(
            BridgeForward(centerline, startZ)).normalized;
        GameObject bridgeObject = new GameObject("Fragile suspension bridge hazard");
        bridgeObject.transform.SetPositionAndRotation(bridgePosition,
            Quaternion.LookRotation(bridgeForward, route.transform.up));
        BridgeHazard bridge = bridgeObject.AddComponent<BridgeHazard>();
        bridge.widthM = deckWidth;
        bridge.lengthM = endZ - startZ;
        bridge.plankLengthM = 0.5f;
        bridge.plankGapM = 0.02f;
        bridge.plankThicknessM = 0.20f;
        bridge.sagAmplitudeM = 0.48f;
        bridge.endHeightOffsetM = endY - startY;
        bridge.deckOverlapM = 0f;
        bridge.buildRails = false; // Keep the existing sculpted cable and hanger mesh below.
        bridge.warningDistanceM = 50f;
        bridge.collapseSpeedKmh = 45f;
        bridge.headStartM = 20f;
        bridge.crackZoneM = 6f;
        bridge.plankMaterial = sunBleachedWood;
        bridge.railMaterial = bridgeRope;
        bridge.player = car;
        bridge.progressFrame = route.transform;
        bridge.progressStartZ = startZ;
        bridge.Configure(deckWidth, endZ - startZ);
        bridge.SetDeckPath(
            progress =>
            {
                float plankZ = startZ + progress;
                float t = Mathf.Clamp01(progress / Mathf.Max(1f, bridge.lengthM));
                float plankY = Mathf.Lerp(startY, endY, t) - 0.48f * Mathf.Sin(Mathf.PI * t);
                return BridgePathPoint(centerline, plankZ, plankY);
            },
            progress =>
            {
                float plankZ = startZ + progress;
                Vector3 forward = route.transform.TransformDirection(
                    BridgeForward(centerline, plankZ)).normalized;
                return Quaternion.LookRotation(forward, route.transform.up);
            });
        bridge.onApproach.AddListener(HandleBridgeApproach);
        bridge.onCollapseStart.AddListener(HandleBridgeCollapseStart);
        bridge.onPlayerFell.AddListener(HandleBridgePlayerFell);
        fragileBridges.Add(bridge);

        const float towerHeight = 6.2f;
        float towerStartZ = startZ + 0.5f;
        float towerEndZ = endZ - 0.5f;
        foreach (float towerZ in new[] { towerStartZ, towerEndZ })
        {
            float t = Mathf.InverseLerp(startZ, endZ, towerZ);
            float deckY = Mathf.Lerp(startY, endY, t) - 0.48f * Mathf.Sin(Mathf.PI * t);
            Vector3 center = BridgePathPoint(centerline, towerZ, deckY);
            Vector3 forward = BridgeForward(centerline, towerZ);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 foot = center + right * (side * (deckWidth * 0.54f));
                CreatePrimitive("Suspension bridge timber tower post", PrimitiveType.Cube,
                    foot + Vector3.up * (towerHeight * 0.5f),
                    new Vector3(0.38f, towerHeight, 0.38f), sunBleachedWood, false);
            }
            GameObject crossbeam = CreatePrimitive("Suspension bridge tower crossbeam",
                PrimitiveType.Cube, center + Vector3.up * towerHeight,
                new Vector3(deckWidth + 1.0f, 0.38f, 0.42f), sunBleachedWood, false);
            crossbeam.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        // Main cables, handrails and hangers retain the existing bridge silhouette.
        for (int side = -1; side <= 1; side += 2)
        {
            const int cableIntervals = 36;
            Vector3 previous = BridgeCablePoint(centerline, startZ, 0f, side,
                startY, endY, deckWidth);
            for (int cableIndex = 1; cableIndex <= cableIntervals; cableIndex++)
            {
                float t = cableIndex / (float)cableIntervals;
                float cableZ = Mathf.Lerp(startZ, endZ, t);
                Vector3 next = BridgeCablePoint(centerline, cableZ, t, side,
                    startY, endY, deckWidth);
                cables.Add(new CableSegment(previous, next, 0.095f));
                previous = next;
            }

            Vector3 handrailPrevious = BridgePathPoint(centerline, startZ, startY + 1.05f)
                + Vector3.Cross(Vector3.up, BridgeForward(centerline, startZ)).normalized
                    * (side * deckWidth * 0.54f);
            for (int cableIndex = 1; cableIndex <= cableIntervals; cableIndex++)
            {
                float t = cableIndex / (float)cableIntervals;
                float cableZ = Mathf.Lerp(startZ, endZ, t);
                float handrailY = Mathf.Lerp(startY, endY, t)
                    - 0.48f * Mathf.Sin(Mathf.PI * t) + 1.05f;
                Vector3 handrail = BridgePathPoint(centerline, cableZ, handrailY)
                    + Vector3.Cross(Vector3.up, BridgeForward(centerline, cableZ)).normalized
                        * (side * deckWidth * 0.54f);
                cables.Add(new CableSegment(handrailPrevious, handrail, 0.055f));
                handrailPrevious = handrail;
            }

            for (int plankIndex = 2; plankIndex < plankIntervals; plankIndex += 2)
            {
                float t = plankIndex / (float)plankIntervals;
                float hangerZ = Mathf.Lerp(startZ, endZ, t);
                float deckY = Mathf.Lerp(startY, endY, t) - 0.48f * Mathf.Sin(Mathf.PI * t);
                Vector3 deckPoint = BridgePathPoint(centerline, hangerZ, deckY);
                Vector3 right = Vector3.Cross(Vector3.up, BridgeForward(centerline, hangerZ)).normalized;
                Vector3 hangerBottom = deckPoint + right * (side * (deckWidth * 0.48f))
                    + Vector3.up * 0.12f;
                Vector3 hangerTop = BridgeCablePoint(centerline, hangerZ, t, side,
                    startY, endY, deckWidth);
                cables.Add(new CableSegment(hangerBottom, hangerTop, 0.026f));
            }
        }
        CreateBridgeCableMesh(cables);

        // A dry, boulder-strewn oued far below the deck gives the crossing scale.
        System.Random ravineRandom = new System.Random(route.seed + Mathf.RoundToInt(z * 19f) + 441);
        for (int rockIndex = 0; rockIndex < 13; rockIndex++)
        {
            float rockZ = z + ((float)ravineRandom.NextDouble() - 0.5f)
                * route.extremeBridgeGapHalfSpan * 1.55f;
            float rockX = centerline(rockZ)
                + ((float)ravineRandom.NextDouble() - 0.5f) * deckWidth * 1.7f;
            Vector3 foot = route.transform.TransformPoint(new Vector3(rockX, 0f, rockZ));
            foot.y = route.SampleGroundHeight(rockX, rockZ);
            float size = 1.2f + (float)ravineRandom.NextDouble() * 2.6f;
            CreateFacetedRock("Boulders in the dry ravine below the bridge",
                foot + Vector3.up * size * 0.25f,
                new Vector3(size, size * (0.45f + (float)ravineRandom.NextDouble() * 0.45f),
                    size * (0.7f + (float)ravineRandom.NextDouble() * 0.5f)),
                Quaternion.Euler((float)ravineRandom.NextDouble() * 18f,
                    (float)ravineRandom.NextDouble() * 360f, (float)ravineRandom.NextDouble() * 18f),
                route.seed + rockIndex * 131 + Mathf.RoundToInt(z), true, smallRockVisualPrefab);
        }

        BuildBridgeRavineFace(z, centerline, width, true);
        BuildBridgeRavineFace(z, centerline, width, false);
        float warningZ = startZ - 55f;
        Vector3 warningForward = BridgeForward(centerline, warningZ);
        Vector3 warningRight = Vector3.Cross(Vector3.up, warningForward).normalized;
        Vector3 warningCenter = route.transform.TransformPoint(
            new Vector3(centerline(warningZ), 0f, warningZ));
        warningCenter.y = route.SampleGroundHeight(centerline(warningZ), warningZ);
        CreateMountainHazardSign(warningCenter, warningRight, width, 1,
            "PONT FRAGILE • ACCÉLÈRE");
    }

    void HandleBridgeApproach(string message)
    {
        TruckGameplay gameplay = car ? car.GetComponent<TruckGameplay>() : null;
        if (!gameplay && car) gameplay = car.GetComponentInParent<TruckGameplay>();
        if (gameplay) gameplay.ShowEnvironmentalWarning(message, 5f);
    }

    void HandleBridgeCollapseStart(string message)
    {
        TruckGameplay gameplay = car ? car.GetComponent<TruckGameplay>() : null;
        if (!gameplay && car) gameplay = car.GetComponentInParent<TruckGameplay>();
        if (!gameplay) return;
        gameplay.ShowEnvironmentalWarning(message, 4f);
        gameplay.NotifyBridgeCollapse();
    }

    void HandleBridgePlayerFell()
    {
        TruckGameplay gameplay = car ? car.GetComponent<TruckGameplay>() : null;
        if (!gameplay && car) gameplay = car.GetComponentInParent<TruckGameplay>();
        if (gameplay) gameplay.ReportBridgePlayerFell();
        for (int i = 0; i < fragileBridges.Count; i++)
            if (fragileBridges[i]) fragileBridges[i].ResetHazard();
    }

    Vector3 BridgePathPoint(System.Func<float, float> centerline, float z, float worldY)
    {
        Vector3 point = route.transform.TransformPoint(new Vector3(centerline(z), 0f, z));
        point.y = worldY;
        return point;
    }

    void BuildRoadCollapseScarp(float z, System.Func<float, float> centerline,
        float trackWidth, int side)
    {
        const int segments = 28;
        Vector3[] top = new Vector3[segments + 1];
        Vector3[] bottom = new Vector3[segments + 1];
        const float halfLength = 7f;
        for (int i = 0; i <= segments; i++)
        {
            float sampleZ = z - halfLength + 2f * halfLength * i / segments;
            Vector3 forward = BridgeForward(centerline, sampleZ);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized * side;
            float edgeNoise = (Mathf.PerlinNoise(sampleZ * 0.21f + route.seed * 0.003f,
                41.7f) - 0.5f) * 0.50f;
            float topLateral = trackWidth * 0.18f + edgeNoise;
            float lowerLateral = trackWidth * 0.235f + edgeNoise * 0.65f;
            Vector3 center = route.transform.TransformPoint(new Vector3(centerline(sampleZ), 0f, sampleZ));
            Vector3 topWorld = center + right * topLateral;
            Vector3 bottomWorld = center + right * lowerLateral;
            Vector3 topLocal = route.transform.InverseTransformPoint(topWorld);
            Vector3 bottomLocal = route.transform.InverseTransformPoint(bottomWorld);
            topWorld.y = route.SampleGroundHeight(topLocal.x, topLocal.z) + 0.06f;
            bottomWorld.y = route.SampleGroundHeight(bottomLocal.x, bottomLocal.z) + 0.06f;
            // Pull the exposed face slightly toward the intact lane so it
            // remains visible above the terrain's smooth heightmap slope.
            top[i] = transform.InverseTransformPoint(topWorld - right * 0.12f);
            bottom[i] = transform.InverseTransformPoint(bottomWorld - right * 0.12f);
        }
        Vector3 normal = Vector3.Cross(Vector3.up, BridgeForward(centerline, z)) * -side;
        CreateLayeredScarpMesh("Exposed soil wall at the collapsed hard road", top, bottom, normal);
        BuildCollapseRoadCracks(z, centerline, trackWidth, side);
        BuildCollapseScree(z, centerline, trackWidth, side);
    }

    void BuildCollapseRoadCracks(float z, System.Func<float, float> centerline,
        float trackWidth, int side)
    {
        // Fissures fines sur la partie encore praticable : elles rendent la
        // chaussée rompue lisible depuis la caméra sans former de relief/collider.
        List<Vector3> vertices = new List<Vector3>(240);
        List<int> triangles = new List<int>(360);
        System.Random random = new System.Random(route.seed + Mathf.RoundToInt(z * 23f) + 7181);
        const int crackCount = 11;
        const int segments = 5;
        for (int crack = 0; crack < crackCount; crack++)
        {
            float crackZ = z + ((float)random.NextDouble() - 0.5f) * 13f;
            float outerLateral = side * trackWidth * Mathf.Lerp(0.18f, 0.28f,
                (float)random.NextDouble());
            float innerLateral = side * trackWidth * Mathf.Lerp(0.015f, 0.12f,
                (float)random.NextDouble());
            float width = Mathf.Lerp(0.035f, 0.075f, (float)random.NextDouble());
            int first = vertices.Count;
            for (int pointIndex = 0; pointIndex <= segments; pointIndex++)
            {
                float t = pointIndex / (float)segments;
                float lateral = Mathf.Lerp(outerLateral, innerLateral, t);
                float zigzag = (float)(random.NextDouble() - 0.5) * 0.55f;
                float pointZ = crackZ + zigzag * Mathf.Sin(t * Mathf.PI);
                Vector3 forward = BridgeForward(centerline, pointZ);
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 center = new Vector3(centerline(pointZ), 0f, pointZ) + right * lateral;
                center.y = route.SampleGroundHeight(center.x, center.z) + 0.075f;
                Vector3 edgeA = center - forward * width;
                Vector3 edgeB = center + forward * width;
                vertices.Add(transform.InverseTransformPoint(edgeA));
                vertices.Add(transform.InverseTransformPoint(edgeB));
                if (pointIndex == 0) continue;
                int previous = first + (pointIndex - 1) * 2;
                int current = first + pointIndex * 2;
                triangles.Add(previous); triangles.Add(current); triangles.Add(previous + 1);
                triangles.Add(previous + 1); triangles.Add(current); triangles.Add(current + 1);
            }
        }

        Mesh mesh = new Mesh { name = "Fractures around the road washout" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        ownedMeshes.Add(mesh);
        GameObject cracks = new GameObject("Hairline cracks leading to the collapsed edge");
        cracks.transform.SetParent(transform, false);
        cracks.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = cracks.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = scarpFracture;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    void BuildCollapseScree(float z, System.Func<float, float> centerline,
        float trackWidth, int side)
    {
        // Petits blocs visibles dans le talus sous la rupture. Ils restent en
        // dehors du couloir de conduite et n'ajoutent donc aucun obstacle caché.
        System.Random random = new System.Random(route.seed + Mathf.RoundToInt(z * 11f) + 2401);
        for (int i = 0; i < 14; i++)
        {
            float sampleZ = z + ((float)random.NextDouble() - 0.5f) * 24f;
            Vector3 forward = BridgeForward(centerline, sampleZ);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float lateral = side * (trackWidth * 0.5f + 1f + (float)random.NextDouble() * 5.5f);
            Vector3 foot = new Vector3(centerline(sampleZ), 0f, sampleZ) + right * lateral;
            foot.y = route.SampleGroundHeight(foot.x, foot.z);
            float size = 0.45f + (float)random.NextDouble() * 1.15f;
            float height = size * (0.45f + (float)random.NextDouble() * 0.42f);
            GameObject rubble = CreateFacetedRock("Fresh limestone scree below the road collapse",
                foot + Vector3.up * height * 0.28f,
                new Vector3(size, height, size * (0.7f + (float)random.NextDouble() * 0.6f)),
                Quaternion.Euler((float)random.NextDouble() * 22f,
                    (float)random.NextDouble() * 360f, (float)random.NextDouble() * 22f),
                route.seed + i * 89 + Mathf.RoundToInt(z), false, smallRockVisualPrefab);
            rubble.transform.SetParent(transform, true);
        }
    }

    void BuildJumpGapDebris(float z, System.Func<float, float> centerline, float trackWidth)
    {
        // Des pierres restent visibles au fond de la brèche, sans collider :
        // le relief donne le danger, aucun obstacle invisible ne bloque la voiture.
        System.Random random = new System.Random(route.seed + Mathf.RoundToInt(z * 29f) + 6317);
        for (int i = 0; i < 12; i++)
        {
            float sampleZ = z + ((float)random.NextDouble() - 0.5f)
                * route.extremeJumpGapHalfSpan * 1.45f;
            Vector3 forward = BridgeForward(centerline, sampleZ);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float lateral = ((float)random.NextDouble() - 0.5f) * trackWidth * 0.78f;
            Vector3 foot = new Vector3(centerline(sampleZ), 0f, sampleZ) + right * lateral;
            foot.y = route.SampleGroundHeight(foot.x, foot.z);
            float size = 0.35f + (float)random.NextDouble() * 0.9f;
            float height = size * (0.42f + (float)random.NextDouble() * 0.42f);
            CreateFacetedRock("Limestone debris in the road washout",
                foot + Vector3.up * height * 0.28f,
                new Vector3(size, height, size * (0.75f + (float)random.NextDouble() * 0.5f)),
                Quaternion.Euler((float)random.NextDouble() * 20f,
                    (float)random.NextDouble() * 360f, (float)random.NextDouble() * 20f),
                route.seed + i * 127 + Mathf.RoundToInt(z), false, smallRockVisualPrefab);
        }
    }

    void BuildBridgeRavineFace(float bridgeZ, System.Func<float, float> centerline,
        float bridgeWidth, bool entrance)
    {
        BuildRoadGapRavineFace(bridgeZ, centerline, bridgeWidth,
            route.extremeBridgeGapHalfSpan, entrance,
            entrance ? "Raw cliff face at bridge entrance" : "Raw cliff face at bridge exit");
    }

    void BuildRoadGapRavineFace(float gapZ, System.Func<float, float> centerline,
        float gapWidth, float halfSpan, bool entrance, string label)
    {
        const int segments = 18;
        float edgeZ = gapZ + (entrance ? -halfSpan : halfSpan);
        float outsideZ = edgeZ + (entrance ? -10f : 10f);
        float insideZ = edgeZ + (entrance ? 2f : -2f);
        float faceZ = edgeZ + (entrance ? 0.16f : -0.16f);
        float halfWidth = gapWidth * 0.5f + 4.5f;
        Vector3 forward = BridgeForward(centerline, edgeZ);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 faceCenter = route.transform.TransformPoint(new Vector3(centerline(edgeZ), 0f, faceZ));
        Vector3[] top = new Vector3[segments + 1];
        Vector3[] bottom = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float lateral = Mathf.Lerp(-halfWidth, halfWidth, i / (float)segments);
            Vector3 edgeWorld = faceCenter + right * lateral;
            Vector3 outsideRight = Vector3.Cross(Vector3.up, BridgeForward(centerline, outsideZ)).normalized;
            Vector3 insideRight = Vector3.Cross(Vector3.up, BridgeForward(centerline, insideZ)).normalized;
            Vector3 outsideWorld = route.transform.TransformPoint(new Vector3(centerline(outsideZ), 0f, outsideZ))
                + outsideRight * lateral;
            Vector3 insideWorld = route.transform.TransformPoint(new Vector3(centerline(insideZ), 0f, insideZ))
                + insideRight * lateral;
            Vector3 outsideLocal = route.transform.InverseTransformPoint(outsideWorld);
            Vector3 insideLocal = route.transform.InverseTransformPoint(insideWorld);
            edgeWorld.y = route.SampleGroundHeight(outsideLocal.x, outsideLocal.z) - 0.10f;
            Vector3 lowerWorld = faceCenter + right * lateral;
            lowerWorld.y = route.SampleGroundHeight(insideLocal.x, insideLocal.z) + 0.08f;
            top[i] = transform.InverseTransformPoint(edgeWorld);
            bottom[i] = transform.InverseTransformPoint(lowerWorld);
        }
        Vector3 towardApproach = entrance ? -forward : forward;
        CreateLayeredScarpMesh(label, top, bottom, towardApproach);
    }

    void CreateLayeredScarpMesh(string label, Vector3[] top, Vector3[] bottom,
        Vector3 preferredNormal)
    {
        if (top == null || bottom == null || top.Length < 2 || top.Length != bottom.Length) return;
        preferredNormal = transform.InverseTransformDirection(preferredNormal).normalized;
        List<Vector3> vertices = new List<Vector3>((top.Length - 1) * 48);
        List<Vector2> uvs = new List<Vector2>((top.Length - 1) * 48);
        List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };
        for (int i = 0; i < top.Length - 1; i++)
        {
            float variation = (Mathf.PerlinNoise(i * 0.37f + route.seed * 0.002f, 83.1f) - 0.5f) * 0.10f;
            float[] bands = { 0f, Mathf.Clamp(0.34f + variation, 0.24f, 0.44f),
                Mathf.Clamp(0.70f - variation, 0.60f, 0.80f), 1f };
            for (int band = 0; band < 3; band++)
            {
                Vector3 a = Vector3.Lerp(top[i], bottom[i], bands[band]);
                Vector3 b = Vector3.Lerp(top[i + 1], bottom[i + 1], bands[band]);
                Vector3 c = Vector3.Lerp(top[i + 1], bottom[i + 1], bands[band + 1]);
                Vector3 d = Vector3.Lerp(top[i], bottom[i], bands[band + 1]);
                AddDoubleSidedScarpQuad(a, b, c, d, preferredNormal, vertices, uvs, triangles[band]);
            }
        }

        Mesh mesh = new Mesh { name = label };
        mesh.indexFormat = vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = triangles.Length;
        for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        ownedMeshes.Add(mesh);

        GameObject face = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
        face.transform.SetParent(transform, false);
        face.GetComponent<MeshFilter>().sharedMesh = mesh;
        face.GetComponent<MeshRenderer>().sharedMaterials = new[] { scarpClay, scarpRock, scarpShadow };
    }

    void AddDoubleSidedScarpQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        Vector3 preferredNormal, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
    {
        bool frontFacing = Vector3.Dot(Vector3.Cross(b - a, c - a), preferredNormal) >= 0f;
        int first = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        AddScarpUV(a, uvs); AddScarpUV(b, uvs); AddScarpUV(c, uvs); AddScarpUV(d, uvs);
        if (frontFacing)
        {
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }
        else
        {
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
            triangles.Add(first); triangles.Add(first + 3); triangles.Add(first + 2);
        }

        int back = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        AddScarpUV(a, uvs); AddScarpUV(b, uvs); AddScarpUV(c, uvs); AddScarpUV(d, uvs);
        if (frontFacing)
        {
            triangles.Add(back); triangles.Add(back + 2); triangles.Add(back + 1);
            triangles.Add(back); triangles.Add(back + 3); triangles.Add(back + 2);
        }
        else
        {
            triangles.Add(back); triangles.Add(back + 1); triangles.Add(back + 2);
            triangles.Add(back); triangles.Add(back + 2); triangles.Add(back + 3);
        }
    }

    static void AddScarpUV(Vector3 point, List<Vector2> uvs)
    {
        // Coordinates in metres make the cliff texture tile consistently across long faces.
        uvs.Add(new Vector2(point.z, point.y));
    }

    static void ApplyCliffMaps(Material material, Texture2D albedo, Texture2D normal,
        float tileX, float tileY, float normalStrength)
    {
        if (!material) return;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
        else if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", albedo);
        if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", normal);
        if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", normalStrength);
        material.mainTextureScale = new Vector2(tileX, tileY);
        material.color = Color.white;
    }

    Vector3 BridgeForward(System.Func<float, float> centerline, float z)
    {
        float dx = centerline(z + 0.5f) - centerline(z - 0.5f);
        return route.transform.TransformDirection(new Vector3(dx, 0f, 1f)).normalized;
    }

    Vector3 BridgeCablePoint(System.Func<float, float> centerline, float z, float t, int side,
        float startY, float endY, float deckWidth)
    {
        float deckY = Mathf.Lerp(startY, endY, t) - 0.48f * Mathf.Sin(Mathf.PI * t);
        float arch = 3.7f + 2.5f * Mathf.Pow(Mathf.Abs(2f * t - 1f), 1.25f);
        Vector3 center = BridgePathPoint(centerline, z, deckY + arch);
        Vector3 right = Vector3.Cross(Vector3.up, BridgeForward(centerline, z)).normalized;
        return center + right * (side * deckWidth * 0.54f);
    }

    void CreateBridgeCableMesh(List<CableSegment> segments)
    {
        List<Vector3> vertices = new List<Vector3>(segments.Count * 8);
        List<int> triangles = new List<int>(segments.Count * 24);
        for (int i = 0; i < segments.Count; i++)
            AddCablePrism(segments[i], vertices, triangles);

        Mesh mesh = new Mesh { name = "Suspension bridge cables and hangers" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        ownedMeshes.Add(mesh);

        GameObject cableObject = new GameObject("Suspension bridge rope cables and handrails");
        cableObject.transform.SetParent(transform, false);
        cableObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        cableObject.AddComponent<MeshRenderer>().sharedMaterial = bridgeRope;
    }

    void AddCablePrism(CableSegment segment, List<Vector3> vertices, List<int> triangles)
    {
        Vector3 direction = (segment.end - segment.start).normalized;
        Vector3 axisA = Vector3.Cross(direction, Vector3.up);
        if (axisA.sqrMagnitude < 0.001f) axisA = Vector3.Cross(direction, Vector3.forward);
        axisA = axisA.normalized * segment.radius;
        Vector3 axisB = Vector3.Cross(direction, axisA.normalized).normalized * segment.radius;
        Vector3[] ringA = new Vector3[4];
        Vector3[] ringB = new Vector3[4];
        for (int corner = 0; corner < 4; corner++)
        {
            float angle = corner * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            Vector3 offset = axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle);
            ringA[corner] = transform.InverseTransformPoint(segment.start + offset);
            ringB[corner] = transform.InverseTransformPoint(segment.end + offset);
        }

        int first = vertices.Count;
        vertices.AddRange(ringA);
        vertices.AddRange(ringB);
        for (int corner = 0; corner < 4; corner++)
        {
            int next = (corner + 1) % 4;
            triangles.Add(first + corner);
            triangles.Add(first + next);
            triangles.Add(first + 4 + next);
            triangles.Add(first + corner);
            triangles.Add(first + 4 + next);
            triangles.Add(first + 4 + corner);
        }
    }

    void LaunchFallingRock(Rigidbody body, Vector3 start, Vector3 landing, float flightTime)
    {
        if (!body) return;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
        body.position = start;
        body.gameObject.SetActive(true);
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.isKinematic = false;
        float duration = Mathf.Max(0.2f, flightTime);
        body.linearVelocity = (landing - start - 0.5f * Physics.gravity * duration * duration) / duration;
        body.angularVelocity = new Vector3(0.55f, 0.8f, -0.65f);
    }

    void CreateMountainHazardSign(Vector3 center, Vector3 right, float trackWidth,
        int side, string hazardName)
    {
        Vector3 point = center + right * (side * (trackWidth * 0.5f + 2.7f));
        point.y = route.SampleGroundHeight(point.x, point.z);
        CreatePrimitive("Mountain hazard warning post", PrimitiveType.Cylinder,
            point + Vector3.up * 1.05f, new Vector3(0.14f, 1.05f, 0.14f), roadMetal, false);
        GameObject board = CreatePrimitive("Mountain hazard warning sign", PrimitiveType.Cube,
            point + Vector3.up * 2.15f, new Vector3(3.8f, 1.25f, 0.16f), signColor, false);
        board.transform.rotation = Quaternion.LookRotation(-right * side, Vector3.up);
        AddLandmarkText(board, "DANGER", hazardName, new Color(0.12f, 0.09f, 0.06f));
    }

    void UpdateInteractiveBridgeApproach()
    {
        if (fragileBridges.Count == 0 || car == null || route == null
            || !route.IsOnCanyonTrack(car.position)) return;

        float carZ = route.transform.InverseTransformPoint(car.position).z;
        for (int i = 0; i < fragileBridges.Count; i++)
        {
            BridgeHazard bridge = fragileBridges[i];
            if (!bridge) continue;
            float distanceToBridge = bridge.progressStartZ - carZ;
            float progressFromStart = carZ - bridge.progressStartZ;
            bridge.CheckApproach(car, distanceToBridge, progressFromStart);
        }
    }

    void UpdateInteractiveRockfallApproach()
    {
        if (interactiveRockfalls.Count == 0 || car == null || route == null
            || !route.IsOnCanyonTrack(car.position)) return;

        Vector3 localCar = route.transform.InverseTransformPoint(car.position);
        Rigidbody playerBody = car.GetComponent<Rigidbody>();
        if (!playerBody) playerBody = car.GetComponentInParent<Rigidbody>();
        for (int i = 0; i < interactiveRockfalls.Count; i++)
        {
            RockfallHazard hazard = interactiveRockfalls[i];
            if (!hazard) continue;
            float hazardZ = route.transform.InverseTransformPoint(hazard.transform.position).z;
            float forwardDistance = hazardZ - localCar.z;
            hazard.CheckApproach(playerBody, car, forwardDistance);
        }
    }

    void UpdateMountainHazardWarnings()
    {
        UpdateInteractiveBridgeApproach();
        UpdateInteractiveRockfallApproach();
        if (mountainHazards.Count == 0 || car == null || route == null) return;
        Vector3 localCar = route.transform.InverseTransformPoint(car.position);
        TruckGameplay gameplay = car.GetComponent<TruckGameplay>();
        if (!gameplay) gameplay = car.GetComponentInParent<TruckGameplay>();
        for (int i = 0; i < mountainHazards.Count; i++)
        {
            MountainHazardWarning hazard = mountainHazards[i];
            bool onCorrectRoute = hazard.extremeRoute
                ? route.IsOnCanyonTrack(car.position)
                : route.IsOnRuralDetour(car.position);
            if (!onCorrectRoute) continue;
            float distance = hazard.z - localCar.z;
            Rigidbody playerBodyForWarning = car.GetComponent<Rigidbody>();
            if (!playerBodyForWarning) playerBodyForWarning = car.GetComponentInParent<Rigidbody>();
            float speed = playerBodyForWarning ? playerBodyForWarning.linearVelocity.magnitude : 0f;
            float warningDistance = hazard.warningTimeSeconds > 0f
                ? Mathf.Max(55f, speed * hazard.warningTimeSeconds) : 160f;
            if (!hazard.shown && distance > 0f && distance <= warningDistance)
            {
                hazard.shown = true;
                if (gameplay) gameplay.ShowEnvironmentalWarning(hazard.message, 6f);
            }
            if (hazard.punishingGap && !hazard.triggered && distance <= 5f && distance >= -18f)
            {
                hazard.triggered = true;
                Rigidbody playerBody = car.GetComponent<Rigidbody>();
                if (!playerBody) playerBody = car.GetComponentInParent<Rigidbody>();
                if (gameplay)
                    gameplay.ReportExtremeWashoutCrossing(
                        playerBody ? playerBody.linearVelocity.magnitude : 0f,
                        gameplay.IsPickupAirborne());
            }
            if (hazard.fallingRock && !hazard.fallStarted
                && distance <= Mathf.Max(28f, speed * 2f) && distance >= 0f)
            {
                hazard.fallStarted = true;
                hazard.fallStartedAt = Time.time;
            }
            if (hazard.fallingRock && hazard.fallStarted)
            {
                float elapsed = Time.time - hazard.fallStartedAt;
                if (hazard.fallingRockBody && hazard.fallingRockBody.isKinematic)
                    LaunchFallingRock(hazard.fallingRockBody, hazard.rockStartPosition,
                        hazard.rockLandingPosition, 1.5f);
                if (!hazard.rockLanded && elapsed >= 1.5f)
                {
                    hazard.rockLanded = true;
                    if (gameplay) gameplay.NotifyRockfallImpact();
                }

                if (hazard.fallingFragments != null && hazard.fallingFragmentBodies != null)
                {
                    for (int fragmentIndex = 0; fragmentIndex < hazard.fallingFragmentBodies.Length; fragmentIndex++)
                    {
                        Rigidbody fragmentBody = hazard.fallingFragmentBodies[fragmentIndex];
                        float launchDelay = 0.18f * (fragmentIndex + 1);
                        if (fragmentBody && fragmentBody.isKinematic && elapsed >= launchDelay)
                            LaunchFallingRock(fragmentBody,
                                hazard.fragmentStartPositions[fragmentIndex],
                                hazard.fragmentLandingPositions[fragmentIndex], 1.55f + launchDelay);
                    }
                }
            }
            if (hazard.bridge && hazard.bridgePlanks != null
                && hazard.bridgeBaseLocalPositions != null && hazard.bridgeBaseRotations != null)
            {
                float near = 1f - Mathf.Clamp01(Mathf.Abs(distance) / 36f);
                float wobble = near * 1.25f;
                if (Time.time < hazard.bridgeDamageUntil)
                    wobble = Mathf.Max(wobble, 4.5f * Mathf.Clamp01((hazard.bridgeDamageUntil - Time.time) / 0.8f));
                if (hazard.bridgeCollapseArmed && !hazard.bridgeCollapseStarted && distance <= -8f)
                {
                    hazard.bridgeCollapseStarted = true;
                    hazard.bridgeCollapseStartedAt = Time.time;
                    if (gameplay) gameplay.NotifyBridgeCollapse();
                }
                for (int plankIndex = 0; plankIndex < hazard.bridgePlanks.Length
                    && plankIndex < hazard.bridgeBaseLocalPositions.Length
                    && plankIndex < hazard.bridgeBaseRotations.Length; plankIndex++)
                {
                    Transform plank = hazard.bridgePlanks[plankIndex];
                    if (!plank) continue;
                    if (hazard.bridgeCollapseStarted)
                    {
                        // After a fast crossing, the deck gives way in sequence
                        // behind the pickup. The ravine remains deep, so the
                        // collapse is timed to follow the vehicle across.
                        float collapseTime = Time.time - hazard.bridgeCollapseStartedAt
                            - plankIndex * 0.09f;
                        float progress = Mathf.Clamp01(collapseTime / 0.72f);
                        float eased = progress * progress * (3f - 2f * progress);
                        plank.localPosition = hazard.bridgeBaseLocalPositions[plankIndex]
                            + Vector3.down * (eased * (route.extremeBridgeGapDepth + 3f));
                        plank.localRotation = hazard.bridgeBaseRotations[plankIndex]
                            * Quaternion.Euler(eased * 82f, 0f, eased * ((plankIndex % 2 == 0) ? 24f : -24f));
                        if (progress >= 1f) plank.gameObject.SetActive(false);
                    }
                    else
                    {
                        float phase = Time.time * 17f + plankIndex * 1.7f;
                        float tilt = Mathf.Sin(phase) * wobble;
                        plank.localPosition = hazard.bridgeBaseLocalPositions[plankIndex];
                        plank.localRotation = hazard.bridgeBaseRotations[plankIndex]
                            * Quaternion.Euler(tilt * 0.25f, 0f, tilt);
                    }
                }
            }
            if (hazard.bridge && !hazard.triggered && distance <= 12f && distance >= -24f)
            {
                hazard.triggered = true;
                hazard.bridgeDamageUntil = Time.time + 0.8f;
                Rigidbody vehicleBody = car.GetComponent<Rigidbody>();
                float bridgeSpeed = vehicleBody ? vehicleBody.linearVelocity.magnitude : 0f;
                hazard.bridgeCollapseArmed = bridgeSpeed >= 26f;
                if (gameplay) gameplay.ReportFragileBridgeCrossing(bridgeSpeed);
            }
        }
    }

    void BuildMountainExitBlockades()
    {
        easyNearMissBlockade = CreateMountainExitBlockade(
            "Police interception — RN13 finish", route.GetRoadCenterX, route.postCityRoadWidth, 8f);
        if (route.enableWadiRoute)
            wadiNearMissBlockade = CreateMountainExitBlockade(
                "Police interception — wadi finish", route.GetWadiRouteCenterX, route.wadiRouteWidth, 8f);
        if (route.enableRuralDetour)
            difficultNearMissBlockade = CreateMountainExitBlockade(
                "Police interception — difficult finish", route.GetRuralDetourCenterX, route.detourWidth, 8f);
        if (route.enableCanyonTrack)
            extremeNearMissBlockade = CreateMountainExitBlockade(
                "Police interception — extreme finish", route.GetCanyonTrackCenterX, route.canyonTrackWidth, 8f);
        if (route.enableRuralDetour)
            difficultExitBlockade = CreateMountainExitBlockade(
                "Police blockade — difficult mountain exit", route.GetRuralDetourCenterX, route.detourWidth);
        if (route.enableCanyonTrack)
            extremeExitBlockade = CreateMountainExitBlockade(
                "Police blockade — extreme Chambi exit", route.GetCanyonTrackCenterX, route.canyonTrackWidth);

    }

    GameObject CreateMountainExitBlockade(string label, System.Func<float, float> centerline,
        float width, float distanceBeforeExit = 56f)
    {
        float z = route.RouteEndZ - distanceBeforeExit;
        float centerX = centerline(z);
        float slope = centerline(z + 0.5f) - centerline(z - 0.5f);
        Vector3 direction = new Vector3(slope, 0f, 1f).normalized;
        GameObject group = new GameObject(label);
        group.transform.SetParent(transform, false);
        group.transform.SetPositionAndRotation(
            new Vector3(centerX, route.SampleGroundHeight(centerX, z), z),
            Quaternion.LookRotation(direction, Vector3.up));

        GameObject suv = new GameObject("Waiting border patrol SUV");
        suv.transform.SetParent(group.transform, false);
        suv.transform.localPosition = new Vector3(0f, 0f, -2.5f);
        suv.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        AddPolicePart(suv.transform, "Patrol SUV body", PrimitiveType.Cube,
            new Vector3(0f, 0.62f, 0f), new Vector3(1.9f, 0.58f, 4.1f), policeWhite);
        AddPolicePart(suv.transform, "Patrol SUV cabin", PrimitiveType.Cube,
            new Vector3(0f, 1.03f, 0.2f), new Vector3(1.62f, 0.76f, 1.95f), policeWhite);
        AddPolicePart(suv.transform, "Patrol SUV windshield", PrimitiveType.Cube,
            new Vector3(0f, 1.16f, 1.19f), new Vector3(1.37f, 0.43f, 0.055f), policeDark);
        AddPolicePart(suv.transform, "Patrol SUV light bar", PrimitiveType.Cube,
            new Vector3(0f, 1.45f, 0.25f), new Vector3(0.75f, 0.14f, 0.24f), policeDark);
        AddPolicePart(suv.transform, "Patrol SUV blue beacon", PrimitiveType.Cube,
            new Vector3(-0.24f, 1.57f, 0.25f), new Vector3(0.24f, 0.12f, 0.2f), blue);
        AddPolicePart(suv.transform, "Patrol SUV red beacon", PrimitiveType.Cube,
            new Vector3(0.24f, 1.57f, 0.25f), new Vector3(0.24f, 0.12f, 0.2f), red);

        GameObject barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
        barrier.name = "Exit blockade beam";
        barrier.transform.SetParent(group.transform, false);
        barrier.transform.localPosition = new Vector3(0f, 0.65f, 2.2f);
        barrier.transform.localScale = new Vector3(width + 1.5f, 0.86f, 0.62f);
        barrier.GetComponent<Renderer>().sharedMaterial = concrete;
        for (int i = 0; i < 8; i++)
        {
            float x = Mathf.Lerp(-width * 0.48f, width * 0.48f, i / 7f);
            AddPolicePart(group.transform, "Exit blockade reflector", PrimitiveType.Cube,
                new Vector3(x, 0.68f, 1.87f), new Vector3(0.46f, 0.62f, 0.035f),
                i % 2 == 0 ? red : signColor);
        }

        group.SetActive(false);
        return group;
    }

    void BuildWadiRockBanks()
    {
        if (!route.enableWadiRoute) return;
        System.Random random = new System.Random(route.seed + 18031);
        float start = route.RouteForkEndZ + 24f;
        float end = route.RouteEndZ - 24f;
        for (float z = start; z < end; z += 92f)
        {
            if (!route.IsWadiRouteActiveAt(z)) continue;
            Vector3 forward = route.GetWadiRouteDirection(z);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int side = -1; side <= 1; side += 2)
            {
                float lateral = side * (route.wadiRouteWidth * 0.5f + 6.5f + (float)random.NextDouble() * 3.5f);
                Vector3 foot = new Vector3(route.GetWadiRouteCenterX(z), 0f, z) + right * lateral;
                foot.y = route.SampleGroundHeight(foot.x, foot.z);
                float size = 2.4f + (float)random.NextDouble() * 2.4f;
                Vector3 boulderScale = new Vector3(size * 1.45f, size * 0.76f, size);
                CreateFacetedRock("Châab limestone boulder",
                    foot + Vector3.up * (boulderScale.y * 0.3f), boulderScale,
                    Quaternion.Euler((float)random.NextDouble() * 24f,
                        (float)random.NextDouble() * 360f, (float)random.NextDouble() * 20f),
                    route.seed + Mathf.RoundToInt(z * 11f) + side * 107, true, largeRockVisualPrefab);

                if (random.NextDouble() < 0.55)
                {
                    Vector3 smaller = foot + right * (side * (size * 0.75f + 1.2f));
                    smaller.y = route.SampleGroundHeight(smaller.x, smaller.z);
                    float smallSize = size * 0.52f;
                    Vector3 smallScale = new Vector3(smallSize * 1.4f, smallSize * 0.75f, smallSize);
                    CreateFacetedRock("Châab loose limestone rock",
                        smaller + Vector3.up * (smallScale.y * 0.3f), smallScale,
                        Quaternion.Euler(11f, (float)random.NextDouble() * 360f, 8f),
                        route.seed + Mathf.RoundToInt(z * 17f) + side * 173,
                        true, mediumRockVisualPrefab);
                }
            }
        }
    }

    void BuildCanyonRockBanks()
    {
        if (!route.enableCanyonTrack) return;
        System.Random random = new System.Random(route.seed + 27611);
        float start = route.RouteForkEndZ + 28f;
        float end = route.RouteEndZ - 24f;
        for (float z = start; z < end; z += 58f)
        {
            Vector3 forward = new Vector3(
                route.GetCanyonTrackCenterX(z + 0.5f) - route.GetCanyonTrackCenterX(z - 0.5f), 0f, 1f).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int side = -1; side <= 1; side += 2)
            {
                float lateral = side * (route.canyonTrackWidth * 0.5f + 17f + (float)random.NextDouble() * 5f);
                Vector3 foot = new Vector3(route.GetCanyonTrackCenterX(z), 0f, z) + right * lateral;
                foot.y = route.SampleGroundHeight(foot.x, foot.z);
                float size = 5.5f + (float)random.NextDouble() * 4f;
                Vector3 cliffScale = new Vector3(size * 1.25f, size * 1.75f, size * 0.8f);
                CreateFacetedRock("Canyon limestone outcrop",
                    foot + Vector3.up * (cliffScale.y * 0.3f), cliffScale,
                    Quaternion.Euler((float)random.NextDouble() * 12f,
                        (float)random.NextDouble() * 360f, (float)random.NextDouble() * 10f),
                    route.seed + Mathf.RoundToInt(z * 23f) + side * 211, true, largeRockVisualPrefab);
            }
        }
    }

    void BuildTrailsideClutter()
    {
        if (!route.enableWadiRoute) return;
        System.Random random = new System.Random(route.seed + 20617);
        float start = route.RouteForkEndZ + 36f;
        float end = route.RouteEndZ - 28f;
        int index = 0;
        for (float z = start; z < end; z += 168f)
        {
            if (!route.IsWadiRouteActiveAt(z)) continue;
            Vector3 forward = route.GetWadiRouteDirection(z);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            int side = index++ % 2 == 0 ? -1 : 1;
            float lateral = side * (route.wadiRouteWidth * 0.5f + 4.8f + (float)random.NextDouble() * 2.8f);
            Vector3 foot = new Vector3(route.GetWadiRouteCenterX(z), 0f, z) + right * lateral;
            foot.y = route.SampleGroundHeight(foot.x, foot.z);
            double choice = random.NextDouble();
            if (choice < 0.38)
            {
                // Deux pierres décalées sur l'accotement, sans fermer la trajectoire.
                for (int rockIndex = 0; rockIndex < 2; rockIndex++)
                {
                    Vector3 rockPosition = foot + forward * (rockIndex * 2.1f - 0.8f)
                        + right * (side * rockIndex * 1.1f);
                    rockPosition.y = route.SampleGroundHeight(rockPosition.x, rockPosition.z);
                    float size = 0.8f + (float)random.NextDouble() * 1.0f;
                    Vector3 rockScale = new Vector3(size * 1.35f, size * 0.72f, size);
                    CreateFacetedRock("Loose limestone beside the track",
                        rockPosition + Vector3.up * (rockScale.y * 0.3f), rockScale,
                        Quaternion.Euler(12f, (float)random.NextDouble() * 360f, 9f),
                        route.seed + Mathf.RoundToInt(z * 19f) + rockIndex * 37, false,
                        smallRockVisualPrefab);
                }
            }
            else if (choice < 0.72)
            {
                GameObject barrel = CreatePrimitive("Rusty border barrel", PrimitiveType.Cylinder,
                    foot + Vector3.up * 0.58f, new Vector3(0.58f, 0.58f, 0.58f), rustedMetal, false);
                barrel.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f,
                    random.NextDouble() < 0.5 ? 0f : side * 18f);
            }
            else
            {
                GameObject barrier = CreatePrimitive("Broken concrete roadside barrier", PrimitiveType.Cube,
                    foot + Vector3.up * 0.34f, new Vector3(2.8f, 0.68f, 0.72f), concrete, false);
                barrier.transform.rotation = Quaternion.LookRotation(forward, Vector3.up)
                    * Quaternion.Euler(0f, 0f, side * (4f + (float)random.NextDouble() * 8f));
                if (random.NextDouble() < 0.5)
                {
                    GameObject fragment = CreatePrimitive("Barrier fragment", PrimitiveType.Cube,
                        foot + forward * 1.6f + Vector3.up * 0.15f,
                        new Vector3(1.0f, 0.3f, 0.48f), stone, false);
                    fragment.transform.rotation = barrier.transform.rotation * Quaternion.Euler(0f, 16f, 21f);
                }
            }
        }
    }

    void CreateRock(Vector3 position, float variation)
    {
        float size = 0.8f + variation * 1.2f;
        Vector3 scale = new Vector3(size * 1.4f, size * 0.72f, size);
        int seed = route ? route.seed + Mathf.RoundToInt(position.x * 31f + position.z * 13f) : 0;
        CreateFacetedRock("Roadside limestone rock", position + Vector3.up * (scale.y * 0.3f),
            scale, Quaternion.Euler(variation * 25f, variation * 300f, variation * 20f),
            seed, false, smallRockVisualPrefab);
    }

    void BuildCityDistrict(float startZ, float endZ, int districtIndex)
    {
        System.Random random = new System.Random(route.seed + districtIndex * 977 + 51);
        for (float z = startZ; z < endZ; z += 7.5f)
        {
            Vector3 forward = route.GetRoadDirection(z);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 center = new Vector3(route.GetRoadCenterX(z), 0f, z);
            float ground = route.SampleGroundHeight(center.x, center.z);
            bool atJunction = TownCrossStreetDistance(z, startZ) < 9f;
            for (int side = -1; side <= 1; side += 2)
            {
                if (atJunction) continue;
                Vector3 curbPosition = center + right * (side * (route.roadWidth * 0.5f + 0.18f));
                curbPosition.y = ground + 0.11f;
                Material curbPaint = ((Mathf.FloorToInt((z - startZ) / 7.5f) + side) & 1) == 0
                    ? roadWhitePaint : policeDark;
                GameObject curb = CreatePrimitive("Black and white painted city curb", PrimitiveType.Cube,
                    curbPosition, new Vector3(0.24f, 0.22f, 7.7f), curbPaint, true);
                curb.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

                Vector3 pavementPosition = center + right * (side * (route.roadWidth * 0.5f + 1.45f));
                pavementPosition.y = ground + 0.08f;
                GameObject pavement = CreatePrimitive("Town sidewalk", PrimitiveType.Cube,
                    pavementPosition, new Vector3(2.35f, 0.16f, 7.7f), concrete, true);
                pavement.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }

        }

        for (float z = startZ + 8f; z < endZ - 3f;)
        {
            bool commercial = z / route.terrainSize >= 0.46f && z / route.terrainSize <= 0.54f;
            if (TownCrossStreetDistance(z, startZ) >= 9f)
            for (int side = -1; side <= 1; side += 2)
            {
                float frontage = commercial
                    ? 12.5f + (float)random.NextDouble() * 4f
                    : 9.5f + (float)random.NextDouble() * 3.5f;
                float depth = commercial
                    ? 6.2f + (float)random.NextDouble() * 2.2f
                    : 5.2f + (float)random.NextDouble() * 1.8f;
                int floors = commercial ? random.Next(2, 4) : random.Next(1, 3);
                int style = random.Next(0, 5);
                CreateCityBuilding(z, side, frontage, depth, floors, style, districtIndex);
            }
            z += commercial ? 19f : 14.5f;
        }

        for (float z = startZ + 12f; z < endZ - 5f; z += 24f)
            for (int side = -1; side <= 1; side += 2)
                CreateStreetLamp(z, side);

        BuildPedestrianCrossings(startZ, endZ);
        BuildTownCrossStreets(startZ, endZ);
        BuildTownDrainage(startZ, endZ);
        BuildTownBoundaryWalls(startZ, endZ);
        BuildTownUtilityPoles(startZ, endZ);
        BuildParkedCivilianCars(startZ, endZ, districtIndex);
        BuildCityPoliceAmbush(startZ, endZ);
    }

    void BuildParkedCivilianCars(float startZ, float endZ, int districtIndex)
    {
        // Quelques voitures garées en bord de chaussée donnent une échelle et créent
        // des trajectoires de slalom sans barrer l'avenue.
        float[] fractions = { 0.18f, 0.39f, 0.63f, 0.84f };
        for (int i = 0; i < fractions.Length; i++)
        {
            float avenueZ = Mathf.Lerp(startZ, endZ, fractions[i]);
            float junctionIndex = Mathf.Round((avenueZ - (startZ + 70f)) / 100f);
            float z = Mathf.Clamp(startZ + 70f + junctionIndex * 100f, startZ + 24f, endZ - 24f);
            int side = (i & 1) == 0 ? 1 : -1;
            Vector3 avenueForward = route.GetRoadDirection(z);
            Vector3 right = Vector3.Cross(Vector3.up, avenueForward).normalized;
            Vector3 position = new Vector3(route.GetRoadCenterX(z), 0f, z)
                + right * (side * 19f);
            position.y = route.SampleGroundHeight(position.x, position.z) + 0.04f;

            GameObject parked = new GameObject("Parked civilian car");
            parked.transform.SetParent(transform, false);
            // Les voitures civiles restent dans les rues secondaires, hors des voies de la RN13.
            parked.transform.SetPositionAndRotation(position, Quaternion.LookRotation(right * side, Vector3.up));
            Material paint = civilianCarPaints[(i + districtIndex) % civilianCarPaints.Length];
            AddBuildingPart(parked.transform, "Sedan body", PrimitiveType.Cube,
                new Vector3(0f, 0.59f, 0f), new Vector3(1.72f, 0.48f, 4.05f), paint, true);
            AddBuildingPart(parked.transform, "Sedan cabin", PrimitiveType.Cube,
                new Vector3(0f, 1.00f, -0.10f), new Vector3(1.48f, 0.66f, 2.05f), paint, true);
            AddBuildingPart(parked.transform, "Front windshield", PrimitiveType.Cube,
                new Vector3(0f, 1.03f, 0.91f), new Vector3(1.34f, 0.48f, 0.045f), windowGlass, false);
            AddBuildingPart(parked.transform, "Rear windshield", PrimitiveType.Cube,
                new Vector3(0f, 1.03f, -1.12f), new Vector3(1.32f, 0.45f, 0.045f), windowGlass, false);
            for (int wheelSide = -1; wheelSide <= 1; wheelSide += 2)
            for (int axle = -1; axle <= 1; axle += 2)
                AddBuildingPart(parked.transform, "Sedan tire", PrimitiveType.Cylinder,
                    new Vector3(wheelSide * 0.88f, 0.36f, axle * 1.28f),
                    new Vector3(0.34f, 0.16f, 0.34f), policeDark, true,
                    Quaternion.Euler(0f, 0f, 90f));
        }
    }

    float TownCrossStreetDistance(float z, float startZ)
    {
        float first = startZ + 70f;
        float index = Mathf.Round((z - first) / 100f);
        float nearest = first + index * 100f;
        return Mathf.Abs(z - nearest);
    }

    void BuildTownCrossStreets(float startZ, float endZ)
    {
        for (float z = startZ + 70f; z < endZ - 20f; z += 100f)
        {
            Vector3 forward = route.GetRoadDirection(z);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 center = new Vector3(route.GetRoadCenterX(z), 0f, z);
            float span = 120f;

            // Trottoirs en dalles de part et d'autre des rues qui traversent le hub.
            for (float lateral = -span * 0.5f + 7f; lateral < span * 0.5f; lateral += 14f)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 pavement = center + right * lateral + forward * (side * 5.8f);
                    pavement.y = route.SampleGroundHeight(pavement.x, pavement.z) + 0.08f;
                    GameObject slab = CreatePrimitive("Hub cross-street sidewalk", PrimitiveType.Cube,
                        pavement, new Vector3(2.2f, 0.16f, 14f), concrete, true);
                    slab.transform.rotation = Quaternion.LookRotation(right, Vector3.up);
                }
            }
        }
    }

    void BuildCityPoliceAmbush(float startZ, float endZ)
    {
        float z = Mathf.Lerp(startZ, endZ, 0.58f);
        Vector3 forward = route.GetRoadDirection(z);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float centerX = route.GetRoadCenterX(z);
        float ground = route.SampleGroundHeight(centerX, z);

        // Le barrage ferme une voie et laisse un passage dégagé de l'autre côté.
        Vector3 barrierPosition = new Vector3(centerX, ground + 0.46f, z) - right * 3.55f;
        GameObject barrier = CreatePrimitive("Kasserine police roadblock", PrimitiveType.Cube,
            barrierPosition, new Vector3(14.4f, 0.86f, 0.72f), concrete, true);
        barrier.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        for (int i = 0; i < 4; i++)
        {
            float lateral = -7f + i * 2.2f;
            Vector3 conePosition = new Vector3(centerX, ground + 0.42f, z - 2.2f) + right * lateral;
            CreatePrimitive("City checkpoint cone", PrimitiveType.Cylinder, conePosition,
                new Vector3(0.44f, 0.42f, 0.44f), red, false);
        }

        // Herse visuelle au sol, devant la voie barrée.
        Vector3 strip = new Vector3(centerX, ground + 0.055f, z + 1.8f) - right * 3.5f;
        GameObject spikeBase = CreatePrimitive("Police spike strip base", PrimitiveType.Cube,
            strip, new Vector3(12.6f, 0.08f, 0.28f), policeDark, false);
        spikeBase.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        for (int i = 0; i < 26; i++)
        {
            Vector3 spikePosition = strip + right * (-6.0f + i * 0.48f) + Vector3.up * 0.12f;
            CreatePrimitive("Herse spike", PrimitiveType.Cylinder,
                spikePosition, new Vector3(0.055f, 0.12f, 0.055f), roadMetal, false);
        }
    }

    void CreateCityBuilding(float z, int side, float frontage, float depth, int floors, int style, int districtIndex)
    {
        Vector3 forward = route.GetRoadDirection(z);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float lateral = route.roadWidth * 0.5f + 3.1f + depth * 0.5f;
        Vector3 center = new Vector3(route.GetRoadCenterX(z), 0f, z) + right * (side * lateral);
        center.y = route.SampleGroundHeight(center.x, center.z);

        if (useImportedCityBuildings && TryCreateImportedCityBuilding(center, right, side, frontage, depth, districtIndex, style))
            return;

        string districtName = GetCityName(districtIndex);
        GameObject building = new GameObject(districtName + " town building");
        building.transform.SetParent(transform, false);
        building.transform.SetPositionAndRotation(center, Quaternion.LookRotation(forward, Vector3.up));

        float floorHeight = 3.15f;
        float height = floors * floorHeight + 0.4f;
        Material facade = facades[(style + districtIndex) % facades.Length];
        AddBuildingPart(building.transform, "Plastered facade", PrimitiveType.Cube,
            new Vector3(0f, height * 0.5f, 0f), new Vector3(depth, height, frontage), facade, true);
        AddBuildingPart(building.transform, "Stone foundation", PrimitiveType.Cube,
            new Vector3(0f, 0.22f, 0f), new Vector3(depth + 0.12f, 0.44f, frontage + 0.12f), stone, true);
        AddBuildingPart(building.transform, "Roof parapet", PrimitiveType.Cube,
            new Vector3(0f, height + 0.16f, 0f), new Vector3(depth + 0.28f, 0.32f, frontage + 0.28f), facades[(style + 2) % facades.Length], false);

        float streetFaceX = -side * (depth * 0.5f + 0.045f);
        float doorZ = frontage * 0.5f - 0.8f;
        Material shutter = awnings[(style + districtIndex) % awnings.Length];
        bool commercial = z / route.terrainSize >= 0.46f && z / route.terrainSize <= 0.54f;
        int displayCount = commercial && frontage > 14f ? 3 : 2;
        if (commercial)
        {
            AddBuildingPart(building.transform, "Colored metal shop shutter", PrimitiveType.Cube,
                new Vector3(streetFaceX, 1.18f, doorZ), new Vector3(0.09f, 2.25f, 1.05f), shutter, false);
            for (int slat = 0; slat < 7; slat++)
                AddBuildingPart(building.transform, "Shop shutter slat", PrimitiveType.Cube,
                    new Vector3(streetFaceX - side * 0.055f, 0.38f + slat * 0.24f, doorZ),
                    new Vector3(0.025f, 0.035f, 0.98f), roadMetal, false);
            for (int pane = 0; pane < displayCount; pane++)
            {
                float paneZ = (pane - (displayCount - 1) * 0.5f) * 2.35f;
                AddBuildingPart(building.transform, "Shopfront glass", PrimitiveType.Cube,
                    new Vector3(streetFaceX, 1.55f, paneZ), new Vector3(0.07f, 1.55f, 1.8f), windowGlass, false);
            }
        }
        else
        {
            AddBuildingPart(building.transform, "Residential metal entrance door", PrimitiveType.Cube,
                new Vector3(streetFaceX, 1.12f, doorZ), new Vector3(0.08f, 2.15f, 0.95f), roadMetal, false);
            for (int pane = -1; pane <= 1; pane += 2)
            {
                float paneZ = pane * Mathf.Min(1.5f, frontage * 0.18f);
                AddBuildingPart(building.transform, "Ground floor barred window", PrimitiveType.Cube,
                    new Vector3(streetFaceX, 1.55f, paneZ), new Vector3(0.07f, 1.15f, 1.2f), windowGlass, false);
                for (int bar = -1; bar <= 1; bar++)
                    AddBuildingPart(building.transform, "Ground floor window grille", PrimitiveType.Cube,
                        new Vector3(streetFaceX - side * 0.055f, 1.55f, paneZ + bar * 0.32f),
                        new Vector3(0.045f, 1.18f, 0.045f), roadMetal, false);
            }
        }

        for (int floor = 1; floor < floors; floor++)
        {
            for (int pane = 0; pane < displayCount; pane++)
            {
                float paneZ = (pane - (displayCount - 1) * 0.5f) * 2.35f;
                AddBuildingPart(building.transform, "Upper floor window", PrimitiveType.Cube,
                    new Vector3(streetFaceX, floor * floorHeight + 1.75f, paneZ),
                    new Vector3(0.075f, 1.0f, 1.05f), windowGlass, false);
                for (int bar = -1; bar <= 1; bar++)
                    AddBuildingPart(building.transform, "Forged window grille", PrimitiveType.Cube,
                        new Vector3(streetFaceX - side * 0.055f, floor * floorHeight + 1.75f, paneZ + bar * 0.31f),
                        new Vector3(0.045f, 1.03f, 0.045f), roadMetal, false);
                for (int rail = -1; rail <= 1; rail += 2)
                    AddBuildingPart(building.transform, "Window grille rail", PrimitiveType.Cube,
                        new Vector3(streetFaceX - side * 0.06f, floor * floorHeight + 1.75f + rail * 0.28f, paneZ),
                        new Vector3(0.05f, 0.045f, 1.08f), roadMetal, false);
                AddBuildingPart(building.transform, "Window sill", PrimitiveType.Cube,
                    new Vector3(streetFaceX - side * 0.07f, floor * floorHeight + 1.2f, paneZ),
                    new Vector3(0.16f, 0.12f, 1.25f), signColor, false);
            }
        }

        if (commercial)
        {
            Material awningMaterial = awnings[(style + districtIndex) % awnings.Length];
            AddBuildingPart(building.transform, "Shop sign board", PrimitiveType.Cube,
                new Vector3(streetFaceX - side * 0.12f, 3.05f, 0f),
                new Vector3(0.12f, 0.48f, frontage * 0.55f), awningMaterial, false);
            AddBuildingPart(building.transform, "Shop awning", PrimitiveType.Cube,
                new Vector3(-side * (depth * 0.5f + 0.42f), 2.55f, 0f),
                new Vector3(0.72f, 0.22f, frontage * 0.82f), awningMaterial, false);
        }
        bool residential = z / route.terrainSize < 0.46f || z / route.terrainSize > 0.54f;
        if (residential && (Mathf.FloorToInt(z / 14.5f) + style) % 3 == 0)
        {
            AddBuildingPart(building.transform, "Black rooftop water tank", PrimitiveType.Cylinder,
                new Vector3(0f, height + 0.82f, frontage * 0.22f),
                new Vector3(0.82f, 0.72f, 0.82f), rooftopWaterTank, false);
            AddBuildingPart(building.transform, "Rooftop satellite dish", PrimitiveType.Cylinder,
                new Vector3(depth * 0.28f, height + 0.38f, -frontage * 0.24f),
                new Vector3(0.48f, 0.07f, 0.48f), roadMetal, false,
                Quaternion.Euler(18f, 0f, 0f));
        }
        if (commercial)
            AddShopName(building.transform, streetFaceX - side * 0.20f, 3.05f, side,
                new[] { "CAFÉ", "HÔTEL", "MÉCANIQUE", "ÉPICERIE", "PHARMACIE" }[(style + districtIndex) % 5]);
        CombineBuildingMeshes(building.transform);
    }

    bool TryCreateImportedCityBuilding(Vector3 center, Vector3 roadRight, int side,
        float frontage, float depth, int districtIndex, int style)
    {
        if (cityBuildingModels == null || cityBuildingModels.Length == 0)
            return false;

        int wanted = style <= 1 ? 0 : (style <= 3 ? 1 : 2);
        wanted = Mathf.Clamp(wanted, 0, cityBuildingModels.Length - 1);
        GameObject source = cityBuildingModels[wanted];
        if (source == null)
        {
            for (int i = 0; i < cityBuildingModels.Length; i++)
                if (cityBuildingModels[i] != null) { source = cityBuildingModels[i]; break; }
        }
        if (source == null) return false;

        string districtName = GetCityName(districtIndex);
        GameObject building = Instantiate(source, Vector3.zero, Quaternion.identity, transform);
        building.name = districtName + " - " + source.name;
        Renderer[] renderers = building.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Destroy(building);
            return false;
        }

        // Mesure le modèle dans ses axes d'origine pour conserver sa forme.
        Bounds localBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) localBounds.Encapsulate(renderers[i].bounds);
        float longSide = Mathf.Max(localBounds.size.x, localBounds.size.z);
        float shortSide = Mathf.Min(localBounds.size.x, localBounds.size.z);
        if (longSide < 0.01f || shortSide < 0.01f || localBounds.size.y < 0.01f)
        {
            Destroy(building);
            return false;
        }

        float targetFrontage = Mathf.Min(frontage, 14.5f);
        float targetDepth = Mathf.Clamp(depth, 6.5f, 9.5f);
        // Garde les bourgs à l'échelle des maisons régionales, même avec les modèles importés.
        float heightLimit = 9.5f;
        float scale = Mathf.Min(targetFrontage / longSide,
            targetDepth / shortSide, heightLimit / localBounds.size.y, 1.15f);
        scale = Mathf.Max(scale, 0.2f);

        Vector3 towardRoad = -roadRight * side;
        building.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(towardRoad, Vector3.up));
        building.transform.localScale = Vector3.one * scale;

        Bounds worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);
        Vector3 targetBase = new Vector3(center.x, center.y, center.z);
        Vector3 currentBase = new Vector3(worldBounds.center.x, worldBounds.min.y, worldBounds.center.z);
        building.transform.position += targetBase - currentBase;

        // Une collision simple pour que le pickup ne traverse pas les bâtiments.
        BoxCollider buildingCollider = building.AddComponent<BoxCollider>();
        buildingCollider.center = localBounds.center;
        buildingCollider.size = localBounds.size;
        return true;
    }

    void AddShopName(Transform parent, float x, float y, int side, string text)
    {
        GameObject labelObject = new GameObject("Shop sign text");
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.localPosition = new Vector3(x, y, 0f);
        labelObject.transform.localRotation = Quaternion.Euler(0f, -side * 90f, 0f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = text;
        label.fontSize = 64;
        label.characterSize = 0.035f;
        label.alignment = TextAlignment.Center;
        label.anchor = TextAnchor.MiddleCenter;
        label.color = Color.white;
    }

    void CreateStreetLamp(float z, int side)
    {
        Vector3 forward = route.GetRoadDirection(z);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float lateral = route.roadWidth * 0.5f + 0.9f;
        Vector3 basePosition = new Vector3(route.GetRoadCenterX(z), 0f, z) + right * (side * lateral);
        basePosition.y = route.SampleGroundHeight(basePosition.x, basePosition.z);
        GameObject lamp = new GameObject("Town street lamp");
        lamp.transform.SetParent(transform, false);
        lamp.transform.SetPositionAndRotation(basePosition, Quaternion.LookRotation(forward, Vector3.up));
        AddBuildingPart(lamp.transform, "Lamp pole", PrimitiveType.Cylinder,
            new Vector3(0f, 2.7f, 0f), new Vector3(0.13f, 2.7f, 0.13f), policeDark, false);
        AddBuildingPart(lamp.transform, "Lamp arm", PrimitiveType.Cube,
            new Vector3(-side * 0.42f, 5.25f, 0f), new Vector3(0.9f, 0.12f, 0.14f), policeDark, false);
        AddBuildingPart(lamp.transform, "Lamp shade", PrimitiveType.Cube,
            new Vector3(-side * 0.83f, 5.12f, 0f), new Vector3(0.42f, 0.16f, 0.36f), signColor, false);
        GameObject fixture = new GameObject("Warm street light");
        fixture.transform.SetParent(lamp.transform, false);
        fixture.transform.localPosition = new Vector3(-side * 0.83f, 4.98f, 0f);
        fixture.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Light streetLight = fixture.AddComponent<Light>();
        streetLight.type = LightType.Spot;
        streetLight.color = new Color(1f, 0.72f, 0.42f);
        streetLight.intensity = 6f;
        streetLight.range = 19f;
        streetLight.spotAngle = 92f;
        streetLight.shadows = LightShadows.None;
        streetLight.enabled = false;
        streetLights.Add(streetLight);
    }

    GameObject AddBuildingPart(Transform parent, string label, PrimitiveType type,
        Vector3 localPosition, Vector3 localScale, Material material, bool keepCollider,
        Quaternion? rotation = null)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = label;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        if (rotation.HasValue) part.transform.localRotation = rotation.Value;
        part.GetComponent<Renderer>().sharedMaterial = material;
        Collider collider = part.GetComponent<Collider>();
        if (!keepCollider && collider != null) Destroy(collider);
        return part;
    }

    void CombineBuildingMeshes(Transform building)
    {
        Dictionary<Material, List<CombineInstance>> groups = new Dictionary<Material, List<CombineInstance>>();
        MeshFilter[] filters = building.GetComponentsInChildren<MeshFilter>();
        foreach (MeshFilter filter in filters)
        {
            if (filter.sharedMesh == null || filter.GetComponent<TextMesh>() != null) continue;
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterial == null) continue;

            Material material = renderer.sharedMaterial;
            if (!groups.TryGetValue(material, out List<CombineInstance> combines))
            {
                combines = new List<CombineInstance>();
                groups.Add(material, combines);
            }
            combines.Add(new CombineInstance
            {
                mesh = filter.sharedMesh,
                transform = building.worldToLocalMatrix * filter.transform.localToWorldMatrix
            });
        }

        if (groups.Count == 0) return;
        foreach (KeyValuePair<Material, List<CombineInstance>> group in groups)
        {
            Mesh combined = new Mesh { name = building.name + " " + group.Key.name + " mesh" };
            combined.CombineMeshes(group.Value.ToArray(), true, true, false);
            ownedMeshes.Add(combined);
            GameObject combinedObject = new GameObject("Combined " + group.Key.name);
            combinedObject.transform.SetParent(building, false);
            combinedObject.transform.localPosition = Vector3.zero;
            combinedObject.transform.localRotation = Quaternion.identity;
            combinedObject.transform.localScale = Vector3.one;
            combinedObject.AddComponent<MeshFilter>().sharedMesh = combined;
            combinedObject.AddComponent<MeshRenderer>().sharedMaterial = group.Key;
        }

        foreach (MeshFilter filter in filters)
        {
            if (filter == null || filter.sharedMesh == null || filter.GetComponent<TextMesh>() != null) continue;
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null) Destroy(renderer);
            Destroy(filter);
        }
    }

    void BuildRoadMarkings()
    {
        List<Vector3> edgeVertices = new List<Vector3>();
        List<int> edgeTriangles = new List<int>();
        List<Vector2> edgeUvs = new List<Vector2>();
        List<Vector3> centerVertices = new List<Vector3>();
        List<int> centerTriangles = new List<int>();
        List<Vector2> centerUvs = new List<Vector2>();
        for (float z = 24f; z < route.terrainSize - 8f; z += 1f)
        {
            float halfRoad = route.GetRoadWidthAt(z) * 0.5f;
            bool inTown = IsInCity(z);
            if (inTown)
            {
                AddRoadRibbon(edgeVertices, edgeTriangles, edgeUvs, z, z + 1f, -halfRoad + 0.28f, 0.08f);
                AddRoadRibbon(edgeVertices, edgeTriangles, edgeUvs, z, z + 1f, halfRoad - 0.28f, 0.08f);
                float dashPhase = (z - 24f) % 9f;
                if (dashPhase < 1.8f)
                    AddRoadRibbon(centerVertices, centerTriangles, centerUvs, z, z + 1f, 0f, 0.10f);
            }
        }

        Material markingMaterial = wornRoadPaint != null ? wornRoadPaint : roadWhitePaint;
        CreateMarkingMesh("Cracked white road edges", edgeVertices, edgeTriangles, edgeUvs, markingMaterial);
        CreateMarkingMesh("Worn dashed center line", centerVertices, centerTriangles, centerUvs, markingMaterial);
    }

    void AddRoadRibbon(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs,
        float z0, float z1, float lateralOffset, float width)
    {
        Vector3 forward0 = route.GetRoadDirection(z0);
        Vector3 forward1 = route.GetRoadDirection(z1);
        Vector3 right0 = Vector3.Cross(Vector3.up, forward0).normalized;
        Vector3 right1 = Vector3.Cross(Vector3.up, forward1).normalized;
        Vector3 center0 = new Vector3(route.GetRoadCenterX(z0), 0f, z0) + right0 * lateralOffset;
        Vector3 center1 = new Vector3(route.GetRoadCenterX(z1), 0f, z1) + right1 * lateralOffset;
        Vector3 left0 = center0 - right0 * (width * 0.5f);
        Vector3 rightEdge0 = center0 + right0 * (width * 0.5f);
        Vector3 left1 = center1 - right1 * (width * 0.5f);
        Vector3 rightEdge1 = center1 + right1 * (width * 0.5f);
        left0.y = route.SampleGroundHeight(left0.x, left0.z) + 0.045f;
        rightEdge0.y = route.SampleGroundHeight(rightEdge0.x, rightEdge0.z) + 0.045f;
        left1.y = route.SampleGroundHeight(left1.x, left1.z) + 0.045f;
        rightEdge1.y = route.SampleGroundHeight(rightEdge1.x, rightEdge1.z) + 0.045f;

        int first = vertices.Count;
        vertices.Add(left0); vertices.Add(rightEdge0); vertices.Add(left1); vertices.Add(rightEdge1);
        float u0 = z0 / 28f;
        float u1 = z1 / 28f;
        uvs.Add(new Vector2(u0, 0.938f));
        uvs.Add(new Vector2(u0, 0.991f));
        uvs.Add(new Vector2(u1, 0.938f));
        uvs.Add(new Vector2(u1, 0.991f));
        triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
        triangles.Add(first + 1); triangles.Add(first + 2); triangles.Add(first + 3);
    }

    void CreateMarkingMesh(string label, List<Vector3> vertices, List<int> triangles,
        List<Vector2> uvs, Material material)
    {
        if (vertices.Count == 0) return;
        Mesh mesh = new Mesh { name = label };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        ownedMeshes.Add(mesh);
        GameObject marking = new GameObject(label);
        marking.transform.SetParent(transform, false);
        marking.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = marking.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
    }

    Material MakeWornRoadMaterial(Texture2D texture)
    {
        if (texture == null) return null;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null) return null;

        Material material = new Material(shader) { name = "Cracked faded road paint" };
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        ownedMaterials.Add(material);
        return material;
    }

    void BuildPedestrianCrossings(float startZ, float endZ)
    {
        if (wornRoadPaint == null) return;
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        for (float crossingZ = startZ + 42f; crossingZ < endZ - 16f; crossingZ += 74f)
        {
            for (int stripe = 0; stripe < 7; stripe++)
            {
                float z = crossingZ + (stripe - 3) * 0.98f;
                AddCrossingStripe(vertices, uvs, triangles, z, route.roadWidth - 1.1f);
            }
        }
        CreateMarkingMesh("Worn pedestrian crossings", vertices, triangles, uvs, wornRoadPaint);
    }

    void AddCrossingStripe(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        float z, float width)
    {
        Vector3 forward = route.GetRoadDirection(z);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 center = new Vector3(route.GetRoadCenterX(z), 0f, z);
        Vector3 leftFront = center - right * (width * 0.5f) - forward * 0.31f;
        Vector3 rightFront = center + right * (width * 0.5f) - forward * 0.31f;
        Vector3 leftBack = center - right * (width * 0.5f) + forward * 0.31f;
        Vector3 rightBack = center + right * (width * 0.5f) + forward * 0.31f;
        leftFront.y = route.SampleGroundHeight(leftFront.x, leftFront.z) + 0.052f;
        rightFront.y = route.SampleGroundHeight(rightFront.x, rightFront.z) + 0.052f;
        leftBack.y = route.SampleGroundHeight(leftBack.x, leftBack.z) + 0.052f;
        rightBack.y = route.SampleGroundHeight(rightBack.x, rightBack.z) + 0.052f;
        int first = vertices.Count;
        vertices.Add(leftFront); vertices.Add(rightFront); vertices.Add(leftBack); vertices.Add(rightBack);
        uvs.Add(new Vector2(0f, 0.938f));
        uvs.Add(new Vector2(1f, 0.938f));
        uvs.Add(new Vector2(0f, 0.991f));
        uvs.Add(new Vector2(1f, 0.991f));
        triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
        triangles.Add(first + 1); triangles.Add(first + 2); triangles.Add(first + 3);
    }

    void BuildTownDrainage(float startZ, float endZ)
    {
        for (float z = startZ + 24f; z < endZ - 12f; z += 44f)
        {
            Vector3 forward = route.GetRoadDirection(z);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 center = new Vector3(route.GetRoadCenterX(z), 0f, z);

            float manholeSide = ((int)((z - startZ) / 44f) % 2 == 0) ? -1f : 1f;
            Vector3 manhole = center + right * (manholeSide * route.roadWidth * 0.23f);
            manhole.y = route.SampleGroundHeight(manhole.x, manhole.z) + 0.035f;
            CreatePrimitive("Road manhole cover", PrimitiveType.Cylinder, manhole,
                new Vector3(0.78f, 0.025f, 0.78f), roadMetal, false);

            for (int side = -1; side <= 1; side += 2)
            {
                float drainZ = z + side * 14f;
                Vector3 drain = new Vector3(route.GetRoadCenterX(drainZ), 0f, drainZ)
                    + Vector3.Cross(Vector3.up, route.GetRoadDirection(drainZ)).normalized
                    * (side * (route.roadWidth * 0.5f - 0.72f));
                drain.y = route.SampleGroundHeight(drain.x, drain.z) + 0.035f;
                GameObject grate = CreatePrimitive("Storm drain grate", PrimitiveType.Cube,
                    drain, new Vector3(0.46f, 0.045f, 0.78f), roadMetal, false);
                grate.transform.rotation = Quaternion.LookRotation(route.GetRoadDirection(drainZ), Vector3.up);
            }
        }
    }

    void BuildTownBoundaryWalls(float startZ, float endZ)
    {
        float offset = route.roadWidth * 0.5f + 19f;
        int segment = 0;
        for (float z = startZ + 10f; z < endZ - 8f; z += 27f, segment++)
        {
            if (TownCrossStreetDistance(z, startZ) < 15f) continue;
            // Des ouvertures régulières représentent les accès aux maisons et aux parcelles.
            if (segment % 5 == 4) continue;
            Vector3 forward = route.GetRoadDirection(z);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 center = new Vector3(route.GetRoadCenterX(z), 0f, z) + right * (side * offset);
                center.y = route.SampleGroundHeight(center.x, center.z) + 0.62f;
                GameObject wall = CreatePrimitive("Low plaster village wall", PrimitiveType.Cube,
                    center, new Vector3(0.24f, 1.24f, 25f), facades[(segment + side + facades.Length) % facades.Length], true);
                wall.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }
    }

    void BuildTownUtilityPoles(float startZ, float endZ)
    {
        float offset = route.roadWidth * 0.5f + 34f;
        Vector3[,] polePositions = new Vector3[2, Mathf.CeilToInt((endZ - startZ) / 62f) + 2];
        int poleCount = 0;
        for (float z = startZ + 18f; z < endZ - 8f; z += 62f)
        {
            if (TownCrossStreetDistance(z, startZ) < 12f) continue;
            Vector3 forward = route.GetRoadDirection(z);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                int side = sideIndex == 0 ? -1 : 1;
                Vector3 foot = new Vector3(route.GetRoadCenterX(z), 0f, z) + right * (side * offset);
                foot.y = route.SampleGroundHeight(foot.x, foot.z);
                Vector3 top = foot + Vector3.up * 7.2f;
                GameObject pole = CreatePrimitive("Concrete utility pole", PrimitiveType.Cylinder,
                    foot + Vector3.up * 3.6f, new Vector3(0.18f, 3.6f, 0.18f), concrete, false);
                GameObject arm = CreatePrimitive("Utility pole crossarm", PrimitiveType.Cube,
                    top, new Vector3(2.2f, 0.12f, 0.16f), roadMetal, false);
                arm.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                if (poleCount < polePositions.GetLength(1)) polePositions[sideIndex, poleCount / 2] = top;
            }
            poleCount += 2;
        }

        int countPerSide = poleCount / 2;
        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            for (int i = 0; i < countPerSide - 1; i++)
            {
                Vector3 a = polePositions[sideIndex, i] + Vector3.up * 0.15f;
                Vector3 b = polePositions[sideIndex, i + 1] + Vector3.up * 0.15f;
                Vector3 delta = b - a;
                GameObject wire = CreatePrimitive("Overhead village power line", PrimitiveType.Cylinder,
                    (a + b) * 0.5f, new Vector3(0.025f, delta.magnitude * 0.5f, 0.025f), roadMetal, false);
                wire.transform.up = delta.normalized;
            }
        }
    }

    void BuildRoadObstacle()
    {
        float z = Mathf.Clamp(obstacleZ, 30f, route.terrainSize - 25f);
        bool onDetour = route.IsDetourActiveAt(z);
        bool onWadi = !onDetour && route.IsWadiRouteActiveAt(z);
        float centerX = onDetour ? route.GetRuralDetourCenterX(z)
            : onWadi ? route.GetWadiRouteCenterX(z) : route.GetRoadCenterX(z);
        Vector3 forward = onDetour ? new Vector3(
            route.GetRuralDetourCenterX(z + 0.5f) - route.GetRuralDetourCenterX(z - 0.5f), 0f, 1f).normalized
            : onWadi ? route.GetWadiRouteDirection(z) : route.GetRoadDirection(z);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float x = centerX - (onDetour ? 1.15f : 1.75f) * right.x;
        float pointZ = z - (onDetour ? 1.15f : 1.75f) * right.z;
        float y = route.SampleGroundHeight(x, pointZ);
        GameObject log = CreatePrimitive("Fallen tree across the road", PrimitiveType.Cylinder,
            new Vector3(x, y + 0.38f, pointZ), new Vector3(0.72f, onDetour ? 1.4f : 2.15f, 0.72f), wood, true);
        log.transform.rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, 90f);

        float roadHalfWidth = onDetour ? route.detourWidth * 0.5f
            : onWadi ? route.wadiRouteWidth * 0.5f : route.GetRoadWidthAt(z) * 0.5f;
        float signX = Mathf.Clamp(centerX + roadHalfWidth + 2f, 3f, route.terrainSize - 3f);
        float signY = route.SampleGroundHeight(signX, z);
        CreatePrimitive("Hazard marker", PrimitiveType.Cube,
            new Vector3(signX, signY + 0.65f, z - 5f), new Vector3(0.9f, 1.3f, 0.12f), red, false);
    }

    void BuildNightWatchLights()
    {
        for (int i = 0; i < 5; i++)
        {
            float z = route.terrainSize * (0.055f + i * 0.045f);
            int side = i % 2 == 0 ? -1 : 1;
            float distance = route.roadWidth * 0.5f + 68f + (i % 3) * 24f;
            float x = Mathf.Clamp(route.GetRoadCenterX(z) + side * distance,
                12f, route.terrainSize - 12f);
            Vector3 position = new Vector3(x, route.SampleGroundHeight(x, z), z);

            GameObject glow = CreatePrimitive("Distant hill campfire", PrimitiveType.Sphere,
                position + Vector3.up * 1.05f, Vector3.one * 0.65f, emberGlow, false);
            watchVisuals.Add(glow.GetComponent<Renderer>());
            GameObject lampObject = new GameObject("Distant lookout light");
            lampObject.transform.SetParent(transform, false);
            lampObject.transform.position = position + Vector3.up * 1.15f;
            Light glowLight = lampObject.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = new Color(1f, 0.44f, 0.12f);
            glowLight.intensity = 2.2f;
            glowLight.range = 46f;
            glowLight.shadows = LightShadows.None;
            glowLight.enabled = nightMode;
            watchLights.Add(glowLight);
        }
    }

    void BuildPoliceCheckpoint()
    {
        checkpointBeacons.Clear();
        for (int i = 0; i < CityCheckpointFractions.Length; i++)
            BuildCityCheckpoint(route.terrainSize * CityCheckpointFractions[i], i);
    }

    void BuildCityCheckpoint(float z, int checkpointIndex)
    {
        Vector3 forward = route.GetRoadDirection(z);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float centerX = route.GetRoadCenterX(z);
        float roadHalf = route.GetRoadWidthAt(z) * 0.5f;
        float beamWidth = route.GetRoadWidthAt(z) * 0.52f;
        int blockedSide = checkpointIndex % 2 == 0 ? -1 : 1;
        float beamOffset = blockedSide * (roadHalf - beamWidth * 0.5f);
        Vector3 beamPosition = new Vector3(centerX, 0f, z) + right * beamOffset;
        beamPosition.y = route.SampleGroundHeight(beamPosition.x, z) + 0.52f;
        GameObject beam = CreatePrimitive("RN13 police checkpoint chicane", PrimitiveType.Cube,
            beamPosition, new Vector3(beamWidth, 0.95f, 0.62f), roadMetal, true);
        beam.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        for (float lateral = -beamWidth * 0.5f + 0.9f; lateral < beamWidth * 0.5f; lateral += 2.1f)
        {
            Vector3 stripePosition = beamPosition + right * lateral + forward * 0.33f;
            GameObject stripe = CreatePrimitive("Checkpoint red white reflector", PrimitiveType.Cube,
                stripePosition, new Vector3(1.0f, 0.86f, 0.055f),
                Mathf.RoundToInt(lateral / 2.1f) % 2 == 0 ? red : signColor, false);
            stripe.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        Vector3 conePosition = new Vector3(centerX, 0f, z - 9f)
            + right * (-blockedSide * (roadHalf - 1.1f));
        conePosition.y = route.SampleGroundHeight(conePosition.x, conePosition.z) + 0.4f;
        CreatePrimitive("Checkpoint chicane cone", PrimitiveType.Cylinder, conePosition,
            new Vector3(0.48f, 0.42f, 0.48f), red, false);

        float signSide = -blockedSide;
        float signX = centerX + right.x * signSide * (roadHalf + 3f);
        float signZ = z - 12f + right.z * signSide * (roadHalf + 3f);
        Vector3 signBase = new Vector3(signX, route.SampleGroundHeight(signX, signZ), signZ);
        CreatePrimitive("Checkpoint signpost", PrimitiveType.Cylinder,
            signBase + Vector3.up * 1.15f, new Vector3(0.12f, 1.15f, 0.12f), roadMetal, false);
        GameObject sign = CreatePrimitive("Checkpoint warning sign", PrimitiveType.Cube,
            signBase + Vector3.up * 2.1f, new Vector3(2.3f, 1f, 0.15f), signColor, false);
        sign.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        GameObject labelObject = new GameObject("Checkpoint sign lettering");
        labelObject.transform.SetParent(sign.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0f, 0.09f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = "CONTRÔLE\nPASSAGE À " + (signSide > 0f ? "DROITE" : "GAUCHE");
        label.fontSize = 46;
        label.characterSize = 0.027f;
        label.alignment = TextAlignment.Center;
        label.anchor = TextAnchor.MiddleCenter;
        label.color = new Color(0.12f, 0.10f, 0.07f);

        Light[] pair = new Light[2];
        Color[] colors = { new Color(0.08f, 0.25f, 1f), new Color(1f, 0.06f, 0.025f) };
        for (int i = 0; i < pair.Length; i++)
        {
            GameObject beaconObject = new GameObject("Checkpoint beacon");
            beaconObject.transform.SetParent(transform, false);
            beaconObject.transform.position = beamPosition + Vector3.up * 1.1f
                + right * (i == 0 ? -beamWidth * 0.34f : beamWidth * 0.34f);
            Light beacon = beaconObject.AddComponent<Light>();
            beacon.type = LightType.Point;
            beacon.color = colors[i];
            beacon.intensity = 3.2f;
            beacon.range = 20f;
            beacon.shadows = LightShadows.None;
            beacon.enabled = nightMode;
            pair[i] = beacon;
        }
        checkpointBeacons.Add(pair);
    }

    Transform CreatePoliceVehicle(string label, int index, bool closeStart = false)
    {
        GameObject root = new GameObject(label);
        root.transform.SetParent(transform, false);
        float beaconHeight = policeVehiclePrefab != null
            ? PrepareImportedPoliceVehicle(root.transform) : 1.57f;
        if (policeVehiclePrefab == null)
        {
            AddPolicePart(root.transform, "Body", PrimitiveType.Cube,
                new Vector3(0f, 0.62f, 0f), new Vector3(1.9f, 0.58f, 4.1f), policeWhite);
            AddPolicePart(root.transform, "White SUV cabin", PrimitiveType.Cube,
                new Vector3(0f, 1.03f, 0.20f), new Vector3(1.62f, 0.76f, 1.95f), policeWhite);
            AddPolicePart(root.transform, "Front windshield", PrimitiveType.Cube,
                new Vector3(0f, 1.16f, 1.19f), new Vector3(1.37f, 0.43f, 0.055f), policeDark);
            AddPolicePart(root.transform, "Rear windshield", PrimitiveType.Cube,
                new Vector3(0f, 1.16f, -0.80f), new Vector3(1.34f, 0.42f, 0.055f), policeDark);
            for (int side = -1; side <= 1; side += 2)
            {
                AddPolicePart(root.transform, "Side window", PrimitiveType.Cube,
                    new Vector3(side * 0.82f, 1.18f, 0.20f), new Vector3(0.045f, 0.40f, 0.82f), policeDark);
                AddPolicePart(root.transform, "Navy patrol stripe", PrimitiveType.Cube,
                    new Vector3(side * 0.96f, 0.68f, -0.05f), new Vector3(0.035f, 0.16f, 2.75f), policeDark);
            }
            AddPolicePart(root.transform, "Front bumper", PrimitiveType.Cube,
                new Vector3(0f, 0.38f, 2.02f), new Vector3(2.0f, 0.22f, 0.18f), policeDark);
            for (int side = -1; side <= 1; side += 2)
                AddPolicePart(root.transform, "Headlight", PrimitiveType.Cube,
                    new Vector3(side * 0.62f, 0.77f, 2.06f), new Vector3(0.30f, 0.16f, 0.06f), signColor);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int axle = -1; axle <= 1; axle += 2)
                {
                    AddPolicePart(root.transform, "Wheel", PrimitiveType.Cylinder,
                        new Vector3(side * 0.95f, 0.35f, axle * 1.32f), new Vector3(0.38f, 0.17f, 0.38f), policeDark,
                        Quaternion.Euler(0f, 0f, 90f));
                }
            }
        }

        AddPolicePart(root.transform, "Police light bar", PrimitiveType.Cube,
            new Vector3(0f, beaconHeight - 0.10f, 0.25f), new Vector3(0.82f, 0.14f, 0.25f), policeDark);
        AddPolicePart(root.transform, "Blue police beacon", PrimitiveType.Cube,
            new Vector3(-0.25f, beaconHeight, 0.25f), new Vector3(0.25f, 0.12f, 0.2f), blue);
        AddPolicePart(root.transform, "Red police beacon", PrimitiveType.Cube,
            new Vector3(0.25f, beaconHeight, 0.25f), new Vector3(0.25f, 0.12f, 0.2f), red);
        Light blueFlash = AddBeaconLight(root.transform, new Vector3(-0.25f, beaconHeight, 0.25f), new Color(0.08f, 0.22f, 1f));
        Light redFlash = AddBeaconLight(root.transform, new Vector3(0.25f, beaconHeight, 0.25f), new Color(1f, 0.05f, 0.02f));
        policeBeacons.Add(new[] { blueFlash, redFlash });

        Vector3 start = car.position - car.forward * (closeStart ? 8.5f : 34f + index * 12f);
        start.y = route.SampleGroundHeight(start.x, start.z) + 0.1f;
        root.transform.SetPositionAndRotation(start, Quaternion.LookRotation(car.forward, Vector3.up));
        patrolVelocities.Add(Vector3.zero);
        return root.transform;
    }

    float PrepareImportedPoliceVehicle(Transform root)
    {
        GameObject visual = Instantiate(policeVehiclePrefab, root, false);
        visual.name = "Imported police vehicle model";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Destroy(visual);
            return 1.57f;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float scale = Mathf.Min(4.3f / Mathf.Max(bounds.size.z, 0.01f),
            2.05f / Mathf.Max(bounds.size.x, 0.01f),
            1.9f / Mathf.Max(bounds.size.y, 0.01f));
        Vector3 localCenter = root.InverseTransformPoint(bounds.center);
        Vector3 localBottom = root.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
        visual.transform.localScale *= scale;
        visual.transform.localPosition = new Vector3(-localCenter.x * scale,
            -localBottom.y * scale + 0.08f, -localCenter.z * scale);

        Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders) collider.enabled = false;
        Rigidbody[] bodies = visual.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody vehicleBody in bodies)
        {
            vehicleBody.isKinematic = true;
            vehicleBody.useGravity = false;
        }
        AudioSource[] audioSources = visual.GetComponentsInChildren<AudioSource>(true);
        foreach (AudioSource source in audioSources) source.enabled = false;
        MonoBehaviour[] behaviours = visual.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours) behaviour.enabled = false;
        return bounds.size.y * scale + 0.12f;
    }

    void UpdatePatrolCars()
    {
        bool onMountainRoute = route.IsOnRuralDetour(car.position) || route.IsOnCanyonTrack(car.position);
        bool hidePatrols = onMountainRoute;
        for (int i = 0; i < patrolCars.Count; i++)
            if (patrolCars[i]) patrolCars[i].gameObject.SetActive(!hidePatrols);
        if (hidePatrols) return;

        for (int i = 0; i < patrolCars.Count; i++)
        {
            Transform patrol = patrolCars[i];
            if (patrol == null) continue;
            TruckFeedback playerDust = car.GetComponent<TruckFeedback>();
            float dustBuffer = playerDust != null && playerDust.DustCloudActive ? 12f : 0f;
            float playerZ = route.transform.InverseTransformPoint(car.position).z;
            float routeBuffer = 0f;
            if (route.IsOnCanyonTrack(car.position) || route.IsOnRuralDetour(car.position))
                routeBuffer = 15f;
            else if (route.IsOnSandTrack(car.position))
                routeBuffer = 12f;
            else if (route.IsOnWadiRoute(car.position))
                routeBuffer = -2f;
            else if (IsInCity(playerZ))
                routeBuffer = -4f;

            // L'oued laisse les patrouilles mobiles se rapprocher; les pistes de
            // montagne restent sans poursuite jusqu'à l'interception de sortie.
            bool openingPursuit = playerZ <= route.RouteForkStartZ + 1f;
            float distanceBehind = openingPursuit ? 8.5f
                : Mathf.Max(12f, 20f + i * 16f + dustBuffer + routeBuffer);
            float laneOffset = i == 0 ? -1.2f : 1.2f;
            Vector3 desired = car.position - car.forward * distanceBehind + car.right * laneOffset;
            bool wadiAmbush = i > 0 && route.IsOnWadiRoute(car.position)
                && infractions >= backupPoliceInfraction;
            float patrolMaxSpeed = 40f;
            if (wadiAmbush)
            {
                // L'unité de renfort coupe la prochaine courbe de l'oued au lieu
                // de rester collée derrière le joueur comme la première patrouille.
                float ambushZ = playerZ + 24f;
                float ambushX = route.GetWadiRouteCenterX(ambushZ) + laneOffset * 1.4f;
                Vector3 localAmbush = new Vector3(ambushX, 0f, ambushZ);
                desired = route.transform.TransformPoint(localAmbush);
                patrolMaxSpeed = 52f;
            }
            desired.y = route.SampleGroundHeight(desired.x, desired.z) + 0.1f;
            Vector3 velocity = patrolVelocities[i];
            patrol.position = Vector3.SmoothDamp(patrol.position, desired,
                ref velocity, 1.25f, patrolMaxSpeed, Time.deltaTime);
            patrolVelocities[i] = velocity;
            if (i < policeBeacons.Count)
            {
                bool nearby = Vector3.Distance(patrol.position, car.position) < 105f;
                bool blueOn = Mathf.Sin(Time.time * 15f + i * 2.1f) > 0f;
                policeBeacons[i][0].enabled = nearby && blueOn;
                policeBeacons[i][1].enabled = nearby && !blueOn;
            }
            Vector3 direction = Vector3.ProjectOnPlane(car.position - patrol.position, Vector3.up);
            if (direction.sqrMagnitude > 0.01f)
                patrol.rotation = Quaternion.Slerp(patrol.rotation, Quaternion.LookRotation(direction, Vector3.up),
                    1f - Mathf.Exp(-2f * Time.deltaTime));
        }
    }

    Light AddBeaconLight(Transform parent, Vector3 localPosition, Color color)
    {
        GameObject beacon = new GameObject("Emergency beacon light");
        beacon.transform.SetParent(parent, false);
        beacon.transform.localPosition = localPosition;
        Light light = beacon.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = 4f;
        light.range = 9f;
        light.shadows = LightShadows.None;
        light.enabled = false;
        return light;
    }

    GameObject CreatePrimitive(string label, PrimitiveType type, Vector3 position, Vector3 scale, Material material, bool keepCollider)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = label;
        obj.transform.SetParent(transform, false);
        obj.transform.position = position;
        obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        Collider collider = obj.GetComponent<Collider>();
        if (!keepCollider && collider != null) Destroy(collider);
        return obj;
    }

    GameObject CreateFacetedRock(string label, Vector3 position, Vector3 scale,
        Quaternion rotation, int seed, bool keepCollider, UnityEngine.Object visualPrefab = null)
    {
        Mesh mesh = BuildFacetedRockMesh(seed);
        ownedMeshes.Add(mesh);
        GameObject rock = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
        rock.transform.SetParent(transform, false);
        rock.transform.SetPositionAndRotation(position, rotation);
        rock.transform.localScale = scale;
        rock.GetComponent<MeshFilter>().sharedMesh = mesh;
        rock.GetComponent<MeshRenderer>().sharedMaterial = stone;
        if (keepCollider)
        {
            MeshCollider collider = rock.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;
        }
        if (visualPrefab != null) AddImportedRockVisual(rock, visualPrefab, scale);
        return rock;
    }

    void AddImportedRockVisual(GameObject rock, UnityEngine.Object prefab, Vector3 targetSize)
    {
        if (!rock || !prefab) return;

        // Use the non-generic overload: some imported Prefab assets do not
        // marshal as GameObject references until Unity has instantiated them.
        UnityEngine.Object instance;
        try
        {
            instance = UnityEngine.Object.Instantiate(prefab, rock.transform, false);
        }
        catch (System.InvalidCastException)
        {
            // The procedural mesh remains as a usable fallback. A decorative
            // asset must never stop construction of the extreme-route bridge.
            return;
        }

        GameObject visual = instance as GameObject;
        if (!visual && instance is Component rootComponent)
            visual = rootComponent.gameObject;
        if (!visual)
        {
            if (instance) Destroy(instance);
            return;
        }
        visual.name = prefab.name + " — rendu PBR";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        // Garder le MeshCollider convexe du générateur pour la physique; les
        // colliders du prefab sont désactivés afin d'éviter des doubles impacts.
        Collider[] visualColliders = visual.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < visualColliders.Length; i++) visualColliders[i].enabled = false;

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Destroy(visual);
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float currentSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float desiredSize = Mathf.Max(targetSize.x, targetSize.y, targetSize.z);
        if (currentSize > 0.001f)
            visual.transform.localScale *= desiredSize / currentSize;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        visual.transform.position += rock.transform.position - bounds.center;

        MeshRenderer proceduralRenderer = rock.GetComponent<MeshRenderer>();
        if (proceduralRenderer) proceduralRenderer.enabled = false;
    }

    Mesh BuildFacetedRockMesh(int seed)
    {
        const int sides = 7;
        Vector3[,] rings = new Vector3[3, sides];
        float[] heights = { -0.30f, -0.06f, 0.25f };
        float[] radii = { 0.72f, 1.00f, 0.68f };
        System.Random random = new System.Random(seed);
        for (int ring = 0; ring < 3; ring++)
        {
            float phase = (float)random.NextDouble() * Mathf.PI * 2f;
            for (int side = 0; side < sides; side++)
            {
                float angle = phase + side * Mathf.PI * 2f / sides
                    + ((float)random.NextDouble() - 0.5f) * 0.19f;
                float radius = radii[ring] * (0.78f + (float)random.NextDouble() * 0.34f) * 0.5f;
                float y = heights[ring] + ((float)random.NextDouble() - 0.5f) * 0.11f;
                rings[ring, side] = new Vector3(Mathf.Cos(angle) * radius, y,
                    Mathf.Sin(angle) * radius);
            }
        }

        Vector3 bottom = new Vector3(0f, -0.48f, 0f);
        Vector3 peak = new Vector3(((float)random.NextDouble() - 0.5f) * 0.16f,
            0.48f + (float)random.NextDouble() * 0.04f,
            ((float)random.NextDouble() - 0.5f) * 0.16f);
        List<Vector3> vertices = new List<Vector3>(sides * 24);
        List<int> triangles = new List<int>(sides * 24);
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            AddFlatRockTriangle(bottom, rings[0, next], rings[0, side], vertices, triangles);
            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 a = rings[ring, side];
                Vector3 b = rings[ring, next];
                Vector3 c = rings[ring + 1, side];
                Vector3 d = rings[ring + 1, next];
                if ((side + ring) % 2 == 0)
                {
                    AddFlatRockTriangle(a, b, d, vertices, triangles);
                    AddFlatRockTriangle(a, d, c, vertices, triangles);
                }
                else
                {
                    AddFlatRockTriangle(a, b, c, vertices, triangles);
                    AddFlatRockTriangle(b, d, c, vertices, triangles);
                }
            }
            AddFlatRockTriangle(rings[2, side], rings[2, next], peak, vertices, triangles);
        }

        Mesh mesh = new Mesh { name = "Irregular angular rock" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AddFlatRockTriangle(Vector3 a, Vector3 b, Vector3 c,
        List<Vector3> vertices, List<int> triangles)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f) < 0f)
        {
            Vector3 swap = b;
            b = c;
            c = swap;
        }
        int first = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        triangles.Add(first);
        triangles.Add(first + 1);
        triangles.Add(first + 2);
    }

    GameObject AddPolicePart(Transform parent, string label, PrimitiveType type,
        Vector3 localPosition, Vector3 localScale, Material material, Quaternion? rotation = null)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = label;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        if (rotation.HasValue) part.transform.localRotation = rotation.Value;
        part.GetComponent<Renderer>().sharedMaterial = material;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        return part;
    }

    Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        ownedMaterials.Add(material);
        return material;
    }

    void OnDestroy()
    {
        foreach (Transform patrol in patrolCars)
        {
            if (patrol == null) continue;
            AudioSource siren = patrol.GetComponent<AudioSource>();
            if (siren) siren.Stop();
        }
        foreach (Material material in ownedMaterials)
            if (material != null) Destroy(material);
        foreach (Mesh mesh in ownedMeshes)
            if (mesh != null) Destroy(mesh);
        foreach (Texture2D texture in ownedTextures)
            if (texture != null) Destroy(texture);
    }
}
