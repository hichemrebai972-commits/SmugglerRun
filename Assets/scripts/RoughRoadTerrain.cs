using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Génère au lancement une portion commune puis quatre itinéraires indépendants jusqu'à l'arrivée :
/// RN13 facile, oued moyen, crêtes difficiles et corniche extrême du Chambi.
/// À mettre sur un objet vide placé en (0, 0, 0). Glisser la voiture dans le champ "Car".
/// </summary>
public class RoughRoadTerrain : MonoBehaviour
{
    [Header("Voiture à placer au début de la route")]
    public Transform car;

    [Header("Terrain")]
    public int seed = 12345;
    public float terrainSize = 3220f;
    public float terrainHeight = 220f;
    [Tooltip("513, 1025 ou 2049 (plus grand = route plus détaillée mais plus lente à générer)")]
    public int heightmapResolution = 2049;
    public int alphamapResolution = 1024;
    public float baseHeight = 25f;

    [Header("Les trois paysages")]
    [Range(0.18f, 0.38f)] public float mountainZoneEnd = 0.28f;
    [Range(0.48f, 0.82f)] public float forestZoneEnd = 0.68f;
    public float mountainCurveAmplitude = 68f;
    public float forestCurveAmplitude = 42f;
    [HideInInspector, Range(0f, 0.25f)] public float sandSinkDepth = 0.12f;

    [Header("Relief réservé aux pistes difficiles")]
    [Range(1.5f, 4f)] public float hardSwitchbackCycles = 1.5f;
    [Range(1.5f, 4.5f)] public float extremeSwitchbackCycles = 3f;
    public float hardDetourClimbHeight = 18f;
    public float hardCanyonClimbHeight = 30f;
    [Range(18f, 65f)] public float hardTrackRavineDepth = 42f;
    [Tooltip("Profondeur d'une courte cassure franchissable, sans créer un trou qui bloque la route.")]
    [Range(0f, 1.4f)] public float hardWashoutJumpDepth = 0.55f;
    [Range(0f, 1.6f)] public float extremeWashoutJumpDepth = 0.82f;
    [Tooltip("Affaissement de l'épaule extérieure sur la piste difficile.")]
    [Range(0f, 12f)] public float hardRoadEdgeWashoutDepth = 6.5f;
    [Tooltip("Profondeur de la brèche franchissable par élan sur la piste extrême.")]
    [Range(1.5f, 6f)] public float extremeJumpGapDepth = 2.5f;
    [Range(2f, 8f)] public float extremeJumpGapHalfSpan = 3f;
    [Tooltip("Profondeur de la brèche franchie par le pont suspendu de la piste extrême.")]
    [Range(8f, 30f)] public float extremeBridgeGapDepth = 20f;
    [Range(35f, 65f)] public float extremeBridgeGapHalfSpan = 51f;
    [Tooltip("Profondeur du ravin juste au bord de la corniche extrême.")]
    [Range(38f, 70f)] public float extremeRavineDepth = 42f;

    [Header("Réseau des quatre itinéraires")]
    [Min(30f)] public float routeForkStartDistance = 150f;
    [Min(100f)] public float routeForkEndDistance = 700f;
    [Min(10f)] public float routeEndMargin = 12f;
    [Min(80f)] public float mediumRouteOffset = 300f;
    [Min(100f)] public float difficultRouteOffset = 430f;
    [Min(140f)] public float extremeRouteOffset = 660f;
    [Range(20f, 110f)] public float difficultSwitchbackAmplitude = 30f;
    [Range(60f, 180f)] public float extremeSwitchbackAmplitude = 125f;
    [Tooltip("Amplitude des virages en S supplémentaires, en mètres.")]
    [Range(0f, 60f)] public float extremeWindingAmplitude = 27f;
    [Min(0)] public int easyRoadPotholeCount = 16;

    [Header("Tracé de la route")]
    [Tooltip("Garde la RN13 principale droite, comme une longue rue de ville.")]
    public bool straightMainRoad = true;
    public float roadWidth = 16f;
    [Tooltip("Largeur progressive de la piste après la ville.")]
    public float postCityRoadWidth = 30f;
    public float shoulderWidth = 6f;
    public float curveAmplitude = 75f;
    public float curveTurns = 1.75f;
    public float roadHillHeight = 8f;

    [Header("Piste difficile — crêtes rocheuses")]
    public bool enableRuralDetour = true;
    [HideInInspector] public float detourStartFraction = 0.28f;
    [HideInInspector] public float detourEndFraction = 0.42f;
    [HideInInspector] public float detourLateralDistance = 90f;
    public float detourWidth = 13.5f;
    [Tooltip("Largeur de transition douce entre la piste rocheuse et le terrain.")]
    public float detourShoulderWidth = 12f;
    public int detourPotholeCount = 16;
    [Range(1f, 3f)] public float detourRoughnessMultiplier = 1.45f;

    [Header("Piste moyenne — oued sec")]
    [FormerlySerializedAs("enableSandRoute")] public bool enableWadiRoute = true;
    [HideInInspector, FormerlySerializedAs("sandRouteStartFraction")] public float wadiRouteStartFraction = 0.28f;
    [HideInInspector, FormerlySerializedAs("sandRouteEndFraction")] public float wadiRouteEndFraction = 0.985f;
    [HideInInspector, FormerlySerializedAs("sandRouteLateralDistance")] public float wadiRouteLateralDistance = 235f;
    [FormerlySerializedAs("sandRouteWidth")] public float wadiRouteWidth = 13.5f;
    [FormerlySerializedAs("sandRouteShoulderWidth"), Tooltip("Transition large pour suivre le relief naturel sans créer de parois.")]
    public float wadiRouteShoulderWidth = 20f;
    [FormerlySerializedAs("sandRoughnessMultiplier"), Range(0.8f, 2.5f)] public float wadiRoughnessMultiplier = 1.5f;
    [FormerlySerializedAs("sandPotholeCount")] public int wadiPotholeCount = 48;
    [Range(0f, 0.8f)] public float wadiMicroBumpHeight = 0.32f;
    [Range(0f, 1.2f)] public float wadiRollingWaveHeight = 0.62f;
    [Range(0f, 0.08f)] public float wadiCrossSlope = 0.04f;

    [Header("Piste extrême — corniche du Chambi")]
    public bool enableCanyonTrack = true;
    [HideInInspector] public float canyonTrackStartFraction = 0.28f;
    [HideInInspector] public float canyonTrackEndFraction = 0.985f;
    [HideInInspector] public float canyonTrackLateralDistance = 265f;
    [Range(4f, 12f)] public float canyonTrackWidth = 7f;
    [Range(2f, 12f)] public float canyonTrackShoulderWidth = 2.8f;
    [Tooltip("Dévers vers l'extérieur des épingles, uniquement sur la corniche extrême.")]
    [Range(0f, 0.16f)] public float extremeOutwardCrossfall = 0.135f;
    [Tooltip("Adhérence latérale conservée sur le gravier de la corniche extrême.")]
    [Range(0.72f, 1f)] public float extremeLateralGrip = 0.90f;
    [Tooltip("Écart vertical sous la chaussée qui déclenche l'échec de chute dans le ravin.")]
    [Range(4f, 16f)] public float extremeFallFailDepth = 9f;

    [HideInInspector] public float canyonStartFraction = 0.82f;
    [HideInInspector] public float canyonEndFraction = 0.96f;
    [Tooltip("Adhérence vers l'avant conservée dans le canyon pour ne pas brider la vitesse.")]
    [HideInInspector] public float canyonGrip = 0.90f;
    [HideInInspector] public float canyonSinkDepth = 0.055f;

    [Header("Textures PBR de la route et du paysage")]
    public Texture2D asphaltAlbedo;
    public Texture2D asphaltNormal;
    public Texture2D dirtAlbedo;
    public Texture2D dirtNormal;
    [FormerlySerializedAs("sandAlbedo")] public Texture2D wadiAlbedo;
    [FormerlySerializedAs("sandNormal")] public Texture2D wadiNormal;
    public bool useProceduralSurfaceTextures = true;

    [Header("Mauvais état de la route")]
    public float bumpHeight = 0.075f;
    public float undulationHeight = 0.19f;
    public float rutDepth = 0.085f;
    [HideInInspector] public int potholeCount = 120;
    public Vector2 potholeRadius = new Vector2(0.8f, 1.65f);
    public Vector2 potholeDepth = new Vector2(0.055f, 0.16f);

    [Header("Paysage autour de la route")]
    public float hillHeight = 18f;

    [Header("Tronçons de la corniche extrême — réglables dans l'Inspector")]
    [Tooltip("Longueurs en mètres : 1 départ, 2 épingle 1, 3 épingle 2, 4 crête/fissure, 5 chicane en S, 6 épingle 3, 7 pont. Total par défaut : 2 500 m.")]
    public float[] extremeSegmentLengths = { 250f, 300f, 350f, 400f, 350f, 400f, 450f };
    [Tooltip("Largeurs en mètres dans l'ordre des sept tronçons : 9, 8, 7, 7, 6, 7 et 6 m par défaut.")]
    public float[] extremeSegmentWidths = { 9f, 8f, 7f, 7f, 6f, 7f, 6f };
    [Tooltip("Pente de début en pourcentage, dans le même ordre.")]
    public float[] extremeSegmentSlopeStart = { 0f, 0f, 0f, 0f, 0f, 0f, 0f };
    [Tooltip("Pente de fin en pourcentage, dans le même ordre.")]
    public float[] extremeSegmentSlopeEnd = { 0f, 0f, 0f, 0f, 0f, 0f, 0f };
    [Tooltip("Hauteur maximale des quelques bosses de montagne, en mètres.")]
    [Range(0f, 15f)] public float extremeOccasionalRiseHeight = 8f;
    int ExtremeSegmentCount
    {
        get
        {
            int lengths = extremeSegmentLengths != null ? extremeSegmentLengths.Length : 0;
            int widths = extremeSegmentWidths != null ? extremeSegmentWidths.Length : 0;
            int starts = extremeSegmentSlopeStart != null ? extremeSegmentSlopeStart.Length : 0;
            int ends = extremeSegmentSlopeEnd != null ? extremeSegmentSlopeEnd.Length : 0;
            return Mathf.Min(Mathf.Min(lengths, widths), Mathf.Min(starts, ends));
        }
    }

    float ExtremeDesignedLength
    {
        get
        {
            float total = 0f;
            for (int i = 0; i < ExtremeSegmentCount; i++)
                total += Mathf.Max(1f, extremeSegmentLengths[i]);
            return Mathf.Max(1f, total);
        }
    }

    float seedOffset;
    Terrain terrain;
    TerrainData data;
    TerrainLayer asphaltLayer;
    TerrainLayer wadiLayer;
    readonly List<PotholeMark> potholeMarks = new List<PotholeMark>();
    readonly List<Material> generatedMaterials = new List<Material>();
    readonly List<Mesh> generatedMeshes = new List<Mesh>();
    readonly List<Texture2D> generatedTextures = new List<Texture2D>();
    struct PotholeMark
    {
        public float x, z, radius;
        public bool isWadi;
        public PotholeMark(float x, float z, float radius, bool isWadi)
        {
            this.x = x;
            this.z = z;
            this.radius = radius;
            this.isWadi = isWadi;
        }
    }

    void Awake()
    {
        if (PlayerPrefs.GetInt("SmugglerRun_DailySeed", 0) != 0)
            seed = SeedForUtcDate(System.DateTime.UtcNow);
        terrainSize = Mathf.Max(terrainSize, 1800f);
        roadWidth = Mathf.Max(roadWidth, 16f);
        seedOffset = (seed % 1000) * 0.37f;
        Build();
        PlaceCar();
    }

    /// <summary>Graine quotidienne stable pour tous les joueurs, calculée en UTC.</summary>
    public static int SeedForUtcDate(System.DateTime date)
    {
        string stamp = date.ToUniversalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        unchecked
        {
            uint hash = 2166136261;
            for (int i = 0; i < stamp.Length; i++)
                hash = (hash ^ stamp[i]) * 16777619;
            return (int)(hash & 0x7fffffff);
        }
    }

    // ---------- Tracé de la route ----------

    float RoadX(float z)
    {
        float progress = Mathf.Clamp01(z / terrainSize);
        if (!straightMainRoad)
        {
            float t = progress * Mathf.PI * 2f;
            return terrainSize * 0.5f
                 + curveAmplitude * Mathf.Sin(t * curveTurns)
                 + curveAmplitude * 0.35f * Mathf.Sin(t * curveTurns * 2.3f + 1f);
        }

        float mountain = SS(0.015f, 0.06f, progress)
                       * (1f - SS(mountainZoneEnd - 0.045f, mountainZoneEnd + 0.06f, progress));
        float mountainT = progress / Mathf.Max(mountainZoneEnd, 0.01f);
        // L'axe principal reste une route ouverte. Les lacets serrés sont réservés
        // aux itinéraires alternatifs difficiles.
        float mainMountainBends = Mathf.Sin(mountainT * Mathf.PI * 2f * 1.35f + 0.25f)
                                * mountainCurveAmplitude * 0.38f * mountain;

        float forestStart = mountainZoneEnd + 0.12f;
        float forest = SS(forestStart, forestStart + 0.07f, progress)
                     * (1f - SS(forestZoneEnd, forestZoneEnd + 0.09f, progress));
        // L'avenue facile traverse le centre-ville en ligne droite. Les courbes de
        // la zone forestière reprennent progressivement après la sortie du hub.
        float townCurve = SS(0.31f, 0.35f, progress) * (1f - SS(0.65f, 0.69f, progress));
        forest *= 1f - townCurve;
        float forestT = (progress - forestStart) / Mathf.Max(forestZoneEnd - forestStart, 0.01f);
        float chaabTurns = Mathf.Sin(forestT * Mathf.PI * 2f * 2.4f + 1.1f)
                         * forestCurveAmplitude * forest;
        return terrainSize * 0.5f + mainMountainBends + chaabTurns;
    }

    public int GetRouteZone(float z)
    {
        float progress = z / Mathf.Max(terrainSize, 1f);
        return progress < mountainZoneEnd ? 0 : (progress < forestZoneEnd ? 1 : 2);
    }

    bool IsMainRoadAt(Vector3 position)
    {
        Vector3 local = transform.InverseTransformPoint(position);
        if (local.z < 0f || local.z > terrainSize) return false;
        float slope = RoadSlope(local.z);
        float distance = Mathf.Abs(local.x - RoadX(local.z)) / Mathf.Sqrt(1f + slope * slope);
        return distance <= GetRoadWidthAt(local.z) * 0.5f + 2.5f;
    }

    bool IsTownCrossStreetAt(Vector3 position)
    {
        Vector3 local = transform.InverseTransformPoint(position);
        if (!IsTownAt(local.z)) return false;
        float firstCross = terrainSize * 0.35f + 70f;
        float crossIndex = Mathf.Round((local.z - firstCross) / 100f);
        float crossZ = firstCross + crossIndex * 100f;
        if (crossZ < terrainSize * 0.35f || crossZ > terrainSize * 0.65f) return false;
        float across = Mathf.Abs(local.x - RoadX(crossZ));
        return Mathf.Abs(local.z - crossZ) <= 5.5f && across <= 82f;
    }

    bool IsTownAt(float z)
    {
        float p = z / Mathf.Max(terrainSize, 1f);
        return p >= 0.35f && p <= 0.65f;
    }

    public float GetRoadWidthAt(float z)
    {
        float progress = z / Mathf.Max(terrainSize, 1f);
        float exitBlend = SS(0.655f, 0.72f, progress);
        return Mathf.Lerp(roadWidth, Mathf.Max(roadWidth, postCityRoadWidth), exitBlend);
    }

    float GetTownSurfaceBlend(float z)
    {
        float p = z / Mathf.Max(terrainSize, 1f);
        // La terre se compacte et se teinte progressivement avant l'asphalte du hub.
        return SS(0.31f, 0.36f, p) * (1f - SS(0.64f, 0.69f, p));
    }

    public bool IsOnSandTrack(Vector3 worldPosition)
    {
        // Les quatre itinéraires sont secs : l'oued est graveleux, pas une dune de sable.
        return false;
    }

    /// <summary>Route extrême en corniche, séparée des trois autres après l'embranchement.</summary>
    public bool IsOnCanyonTrack(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        if (!IsCanyonTrackActiveAt(local.z)) return false;
        float slope = GetCanyonTrackCenterX(local.z + 0.5f) - GetCanyonTrackCenterX(local.z - 0.5f);
        float distance = Mathf.Abs(local.x - GetCanyonTrackCenterX(local.z)) / Mathf.Sqrt(1f + slope * slope);
        float mainSlope = RoadSlope(local.z);
        float mainDistance = Mathf.Abs(local.x - RoadX(local.z)) / Mathf.Sqrt(1f + mainSlope * mainSlope);
        return distance <= GetExtremeTrackWidth(local.z) * 0.5f + 3f && distance + 2f < mainDistance;
    }

    /// <summary>Route extrême complète des crêtes du Chambi.</summary>
    public bool IsOnCanyonRoute(Vector3 worldPosition)
    {
        return IsOnCanyonTrack(worldPosition);
    }

    public bool IsOnGravelSurface(Vector3 worldPosition)
    {
        return IsOnCanyonTrack(worldPosition) || IsOnWadiRoute(worldPosition);
    }

    public bool IsOnLooseEarth(Vector3 worldPosition)
    {
        return IsOnRuralDetour(worldPosition) || IsOnCanyonTrack(worldPosition);
    }

    public float GetSurfaceGrip(Vector3 worldPosition)
    {
        // Le sol rugueux doit secouer et faire du bruit, sans réduire artificiellement
        // la puissance transmise aux roues ni la vitesse maximale.
        return 1f;
    }

    public float GetFrontSidewaysFactor(Vector3 worldPosition)
    {
        if (IsOnCanyonTrack(worldPosition)) return 0.98f;
        return Mathf.Lerp(1f, 0.95f, GetWadiLoosePatchBlend(worldPosition));
    }

    public float GetRearSidewaysFactor(Vector3 worldPosition)
    {
        if (IsOnCanyonTrack(worldPosition)) return 0.92f;
        return Mathf.Lerp(1f, 0.91f, GetWadiLoosePatchBlend(worldPosition));
    }

    /// <summary>Détecte une chute réelle sous le niveau de la corniche extrême.</summary>
    public bool HasFallenFromExtremeTrack(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        if (local.z < RouteForkEndZ + 25f || !IsCanyonTrackActiveAt(local.z)) return false;

        float centerX = GetCanyonTrackCenterX(local.z);
        float slope = GetCanyonTrackCenterX(local.z + 0.5f) - GetCanyonTrackCenterX(local.z - 0.5f);
        float lateralDistance = Mathf.Abs(local.x - centerX) / Mathf.Sqrt(1f + slope * slope);
        // Garde la détection active pendant toute la chute : à grande vitesse,
        // le pickup peut déjà être loin du bord avant de descendre de 9 m.
        if (lateralDistance > GetExtremeTrackWidth(local.z) * 0.5f + 70f) return false;

        float roadHeight = NaturalHeightAlongTrack(local.z, GetCanyonTrackCenterX)
            + CanyonTrackElevation(local.z);
        float expectedWorldY = transform.TransformPoint(new Vector3(centerX, roadHeight, local.z)).y;
        return worldPosition.y < expectedWorldY - extremeFallFailDepth;
    }

    public float GetRoughSuspensionDamperFactor(Vector3 worldPosition)
    {
        if (IsOnCanyonRoute(worldPosition)) return 0.78f;
        if (IsOnSandTrack(worldPosition)) return 0.52f;
        if (IsOnGravelSurface(worldPosition)) return 0.62f;
        if (IsOnLooseEarth(worldPosition)) return 0.58f;
        return 1f;
    }

    public float GetDustFactor(Vector3 worldPosition)
    {
        if (IsTownCrossStreetAt(worldPosition)) return 0.12f;
        if (IsOnSandTrack(worldPosition)) return 1.7f;
        if (IsOnWadiRoute(worldPosition)) return Mathf.Lerp(0.52f, 1.25f, GetWadiLoosePatchBlend(worldPosition));
        if (IsOnLooseEarth(worldPosition)) return 1.15f;
        if (IsOnGravelSurface(worldPosition)) return 0.8f;
        return 0.12f;
    }

    public float GetSandSinkDepth(Vector3 worldPosition) =>
        0f;

    public float GetSurfaceDrag(Vector3 worldPosition)
    {
        // Rolling resistance comes from the wheel/ground interaction, not extra Rigidbody damping.
        return 0f;
    }

    // Pente latérale de la route (dx/dz)
    float RoadSlope(float z)
    {
        return RoadX(z + 0.5f) - RoadX(z - 0.5f);
    }

    float RoadHeight(float z)
    {
        float n = Mathf.PerlinNoise(z * 0.007f + seedOffset, seedOffset + 0.5f);
        float mountainRise = SS(0f, mountainZoneEnd * terrainSize * 0.42f, z)
                           * (1f - SS(mountainZoneEnd * terrainSize * 0.85f,
                               mountainZoneEnd * terrainSize * 1.28f, z));
        float lowland = SS(forestZoneEnd * terrainSize,
            (forestZoneEnd + 0.12f) * terrainSize, z);
        // Relief modéré sur la voie principale; les fortes montées sont ajoutées
        // uniquement aux pistes de montagne difficiles dans Build().
        const float mainMountainGrade = 17f;
        float naturalHeight = baseHeight + (n - 0.5f) * 2f * roadHillHeight
                            + mountainRise * mainMountainGrade - lowland * 5f;
        float townGrade = baseHeight + mountainRise * mainMountainGrade - lowland * 5f
                        + (n - 0.5f) * 2f * 1.4f;
        return Mathf.Lerp(naturalHeight, townGrade, GetTownSurfaceBlend(z));
    }

    public float RouteForkStartZ => Mathf.Clamp(routeForkStartDistance, 30f, terrainSize * 0.18f);
    public float RouteForkEndZ => Mathf.Clamp(routeForkEndDistance, RouteForkStartZ + 30f, terrainSize * 0.34f);
    public float RouteEndZ => Mathf.Clamp(terrainSize - Mathf.Max(routeEndMargin, 10f),
        RouteForkEndZ + 100f, terrainSize - 5f);

    float DetourStartZ => RouteForkStartZ;
    float DetourEndZ => RouteEndZ;

    float ForkBlend(float z) => SS(RouteForkStartZ, RouteForkEndZ, z);

    float ForkLaneCenter(float z, float lateralOffset)
    {
        float startX = RoadX(RouteForkStartZ);
        float targetX = Mathf.Clamp(startX + lateralOffset, 90f, terrainSize - 90f);
        return Mathf.Lerp(RoadX(z), targetX, ForkBlend(z));
    }

    float RouteProgress(float z) => Mathf.Clamp01(
        (z - RouteForkEndZ) / Mathf.Max(RouteEndZ - RouteForkEndZ, 1f));

    public bool IsDetourActiveAt(float z) =>
        enableRuralDetour && z >= RouteForkStartZ && z <= RouteEndZ;

    public float GetRuralDetourCenterX(float z)
    {
        if (!enableRuralDetour || z < RouteForkStartZ) return RoadX(z);
        float t = RouteProgress(z);
        float turnsIn = SS(RouteForkEndZ, RouteForkEndZ + 160f, z);
        float switchback = HardSwitchback(t, hardSwitchbackCycles + 0.8f, 0.35f, 2.5f);
        float targetOffset = Mathf.Max(difficultRouteOffset, terrainSize * 0.12f);
        return ForkLaneCenter(z, targetOffset)
             + switchback * difficultSwitchbackAmplitude * turnsIn;
    }

    float HardSwitchback(float progress, float cycles, float phase, float sharpness = 4.5f)
    {
        float wave = Mathf.Sin(progress * Mathf.PI * 2f * cycles + phase);
        return Mathf.Atan(wave * sharpness) / Mathf.Atan(sharpness);
    }

    float RuralDetourElevation(float z)
    {
        if (!IsDetourActiveAt(z)) return 0f;
        float riseShape = Mathf.Sin(RouteProgress(z) * Mathf.PI * 2f);
        return hardDetourClimbHeight * riseShape * riseShape;
    }

    bool IsHardCanyonSectionAt(float z)
    {
        return IsCanyonTrackActiveAt(z);
    }

    float CanyonTrackElevation(float z)
    {
        if (!IsHardCanyonSectionAt(z) || ExtremeSegmentCount == 0) return 0f;
        float distance = GetExtremeRouteDistance(z);
        int segment = GetExtremeSegmentIndex(distance);
        float completed = 0f;
        for (int i = 0; i < segment; i++)
            completed += GetExtremeSegmentLength(i)
                * (extremeSegmentSlopeStart[i] + extremeSegmentSlopeEnd[i]) * 0.005f;
        float segmentStart = GetExtremeSegmentStartDistance(segment);
        float segmentLength = GetExtremeSegmentLength(segment);
        float localDistance = Mathf.Clamp(distance - segmentStart, 0f, segmentLength);
        float t = localDistance / segmentLength;
        float currentSlope = Mathf.Lerp(extremeSegmentSlopeStart[segment], extremeSegmentSlopeEnd[segment], t);
        float profileElevation = completed
            + localDistance * (extremeSegmentSlopeStart[segment] + currentSlope) * 0.005f;

        // La route reste basse ; quatre bosses courtes rompent ponctuellement le profil plat.
        float occasionalRise = 0f;
        if (ExtremeSegmentCount > 5)
        {
            occasionalRise += ExtremeSegmentRise(distance, 1, 0.08f, 0.50f, 0.90f, 0.55f);
            occasionalRise += ExtremeSegmentRise(distance, 3, 0.10f, 0.45f, 0.80f, 1.00f);
            occasionalRise += ExtremeSegmentRise(distance, 4, 0.10f, 0.50f, 0.88f, 0.45f);
            occasionalRise += ExtremeSegmentRise(distance, 5, 0.12f, 0.48f, 0.82f, 0.65f);
        }
        return profileElevation + occasionalRise;
    }

    float ExtremeSegmentRise(float distance, int segment, float startT, float crestT, float endT, float heightScale)
    {
        float start = GetExtremeSegmentStartDistance(segment);
        float length = GetExtremeSegmentLength(segment);
        float riseStart = start + length * startT;
        float crest = start + length * crestT;
        float riseEnd = start + length * endT;
        if (distance <= riseStart || distance >= riseEnd) return 0f;
        if (distance < crest)
            return extremeOccasionalRiseHeight * heightScale
                * Mathf.SmoothStep(0f, 1f, (distance - riseStart) / Mathf.Max(0.01f, crest - riseStart));
        return extremeOccasionalRiseHeight * heightScale
            * (1f - Mathf.SmoothStep(0f, 1f, (distance - crest) / Mathf.Max(0.01f, riseEnd - crest)));
    }

    /// <summary>Distance sur le profil de 2,5 km de la corniche extrême.</summary>
    public float GetExtremeRouteDistance(float z)
    {
        float progress = Mathf.Clamp01((z - RouteForkEndZ) / Mathf.Max(1f, RouteEndZ - RouteForkEndZ));
        return progress * ExtremeDesignedLength;
    }

    public float GetExtremeRouteZAtDistance(float distance)
    {
        float progress = Mathf.Clamp01(distance / Mathf.Max(1f, ExtremeDesignedLength));
        return Mathf.Lerp(RouteForkEndZ, RouteEndZ, progress);
    }

    int GetExtremeSegmentIndex(float distance)
    {
        int count = ExtremeSegmentCount;
        if (count == 0) return 0;
        float cursor = 0f;
        for (int i = 0; i < count - 1; i++)
        {
            cursor += Mathf.Max(1f, extremeSegmentLengths[i]);
            if (distance < cursor) return i;
        }
        return count - 1;
    }

    public float GetExtremeSegmentStartDistance(int segment)
    {
        float start = 0f;
        int count = Mathf.Clamp(segment, 0, ExtremeSegmentCount);
        for (int i = 0; i < count; i++) start += Mathf.Max(1f, extremeSegmentLengths[i]);
        return start;
    }

    public float GetExtremeSegmentLength(int segment)
    {
        return ExtremeSegmentCount == 0 ? 0f
            : Mathf.Max(1f, extremeSegmentLengths[Mathf.Clamp(segment, 0, ExtremeSegmentCount - 1)]);
    }

    public float GetExtremeSectionDistance(int segment, float progress)
    {
        return GetExtremeSegmentStartDistance(segment)
            + GetExtremeSegmentLength(segment) * Mathf.Clamp01(progress);
    }

    public float GetExtremeCheckpointDistance(int checkpointIndex)
    {
        return GetExtremeSegmentStartDistance(checkpointIndex <= 0 ? 4 : 6);
    }

    public int GetExtremeCheckpointIndex(float z)
    {
        if (ExtremeSegmentCount < 7) return -1;
        float distance = GetExtremeRouteDistance(z);
        if (distance >= GetExtremeCheckpointDistance(1)) return 1;
        if (distance >= GetExtremeCheckpointDistance(0)) return 0;
        return -1;
    }

    public bool TryGetExtremeCheckpoint(int checkpointIndex, out Vector3 position, out Quaternion rotation)
    {
        if (ExtremeSegmentCount < 7)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }
        float distance = checkpointIndex < 0 ? 0f : GetExtremeCheckpointDistance(checkpointIndex);
        float z = GetExtremeRouteZAtDistance(distance);
        float x = GetCanyonTrackCenterX(z);
        Vector3 forward = new Vector3(
            GetCanyonTrackCenterX(z + 0.5f) - GetCanyonTrackCenterX(z - 0.5f),
            CanyonTrackElevation(z + 0.5f) - CanyonTrackElevation(z - 0.5f), 1f).normalized;
        position = transform.TransformPoint(new Vector3(x, CanyonTrackElevation(z), z));
        position.y = terrain != null ? terrain.SampleHeight(position) + 1.25f : position.y + 1.25f;
        rotation = Quaternion.LookRotation(transform.TransformDirection(forward), transform.up);
        return enableCanyonTrack;
    }

    public float GetExtremeTrackWidth(float z)
    {
        if (ExtremeSegmentCount == 0) return canyonTrackWidth;
        float distance = Mathf.Clamp(GetExtremeRouteDistance(z), 0f, ExtremeDesignedLength);
        int segment = GetExtremeSegmentIndex(distance);
        float start = GetExtremeSegmentStartDistance(segment);
        float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distance - start) / 24f));
        float from = segment == 0 ? extremeSegmentWidths[0] : extremeSegmentWidths[segment - 1];
        return Mathf.Lerp(from, extremeSegmentWidths[segment], blend);
    }

    float GetExtremeCrossfall(float z) => GetExtremeSegmentIndex(GetExtremeRouteDistance(z)) == 1
        ? extremeOutwardCrossfall : 0f;

    float ExtremeSegmentTurnOffset(float distance, int segment, float amplitude)
    {
        if (ExtremeSegmentCount == 0 || segment < 0 || segment >= ExtremeSegmentCount) return 0f;
        float start = GetExtremeSegmentStartDistance(segment);
        float t = Mathf.Clamp01((distance - start) / GetExtremeSegmentLength(segment));
        float turn = 0f;
        if (segment == 1) turn = amplitude * Mathf.Sin(Mathf.PI * t);
        else if (segment == 2) turn = -amplitude * Mathf.Sin(Mathf.PI * t);
        else if (segment == 3) turn = amplitude * 0.28f * Mathf.Sin(Mathf.PI * t);
        else if (segment == 4) turn = amplitude * 0.52f * Mathf.Sin(Mathf.PI * 3f * t);
        else if (segment == 5) turn = amplitude * 0.9f * Mathf.Sin(Mathf.PI * t);
        else if (segment == 6) turn = amplitude * 0.12f * Mathf.Sin(Mathf.PI * t);

        // Plusieurs S alternés sur les six tronçons de montagne ; le pont final reste stable.
        if (segment >= 6) return turn;
        float windingLength = GetExtremeSegmentStartDistance(6);
        float routeProgress = Mathf.Clamp01(distance / Mathf.Max(1f, windingLength));
        float winding = extremeWindingAmplitude
            * Mathf.Sin(2f * Mathf.PI * extremeSwitchbackCycles * routeProgress);
        return turn + winding;
    }

    float ErosionJumpDip(float z, float lateral, float trackWidth, float centerZ, float depth)
    {
        float approach = SS(centerZ - 8f, centerZ - 1f, z);
        float exit = 1f - SS(centerZ + 1f, centerZ + 8f, z);
        float across = 1f - SS(trackWidth * 0.27f, trackWidth * 0.51f, Mathf.Abs(lateral));
        return depth * approach * exit * across;
    }

    float RoadEdgeWashout(float z, float lateral, float trackWidth, float centerZ, float depth)
    {
        float approach = SS(centerZ - 16f, centerZ - 8f, z);
        float exit = 1f - SS(centerZ + 8f, centerZ + 16f, z);
        // Une rupture assez nette laisse une voie intérieure praticable et
        // transforme le bord extérieur en banquette effondrée.
        float outerShoulder = SS(trackWidth * 0.18f, trackWidth * 0.23f, lateral);
        return depth * approach * exit * outerShoulder;
    }

    float SuspensionBridgeGapCut(float z, float lateral, float trackWidth, float centerZ)
    {
        float rampLength = 8f;
        float approach = SS(centerZ - extremeBridgeGapHalfSpan - rampLength,
            centerZ - extremeBridgeGapHalfSpan, z);
        float exit = 1f - SS(centerZ + extremeBridgeGapHalfSpan,
            centerZ + extremeBridgeGapHalfSpan + rampLength, z);
        float across = 1f - SS(trackWidth * 0.5f + 2f, trackWidth * 0.5f + 10f,
            Mathf.Abs(lateral));
        return extremeBridgeGapDepth * approach * exit * across;
    }

    float ExtremeJumpLaunchRamp(float z, float lateral, float trackWidth, float centerZ)
    {
        // Rampe basse et large avant la fissure : elle donne un appel au saut
        // sans imposer une vitesse impossible ni créer un ressaut vertical.
        float rise = SS(centerZ - 38f, centerZ - 30f, z);
        float release = 1f - SS(centerZ - 20f, centerZ - 14f, z);
        float across = 1f - SS(trackWidth * 0.48f, trackWidth * 0.72f, Mathf.Abs(lateral));
        return 0.95f * rise * release * across;
    }

    float ExtremeJumpGapCut(float z, float lateral, float trackWidth, float centerZ)
    {
        // La fissure garde 6 m de creux et des rampes graduelles pour rester franchissable.
        const float rampLength = 16f;
        float approach = SS(centerZ - extremeJumpGapHalfSpan - rampLength,
            centerZ - extremeJumpGapHalfSpan, z);
        float exit = 1f - SS(centerZ + extremeJumpGapHalfSpan,
            centerZ + extremeJumpGapHalfSpan + rampLength, z);
        float across = 1f - SS(trackWidth * 0.5f + 2f,
            trackWidth * 0.5f + 9f, Mathf.Abs(lateral));
        return extremeJumpGapDepth * approach * exit * across;
    }

    public bool IsWadiRouteActiveAt(float z) =>
        enableWadiRoute && z >= RouteForkStartZ && z <= RouteEndZ;

    public float GetWadiRouteCenterX(float z)
    {
        if (!enableWadiRoute || z < RouteForkStartZ) return RoadX(z);
        float targetOffset = -Mathf.Max(mediumRouteOffset, terrainSize * 0.08f);
        float meander = Mathf.Sin(z * 0.0068f + seedOffset) * 36f
                      + Mathf.Sin(z * 0.0145f + seedOffset + 2.4f) * 15f
                      + Mathf.Sin(z * 0.0028f + seedOffset + 1.1f) * 10f;
        return ForkLaneCenter(z, targetOffset)
             + meander * SS(RouteForkEndZ, RouteForkEndZ + 160f, z);
    }

    float WadiLoosePatchAtZ(float z)
    {
        float patchWave = 0.5f + 0.5f * Mathf.Sin(z * 0.012f + seedOffset * 0.7f + 0.9f);
        return SS(0.42f, 0.78f, patchWave);
    }

    public float GetWadiLoosePatchBlend(Vector3 worldPosition)
    {
        if (!IsOnWadiRoute(worldPosition)) return 0f;
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        float distance = Mathf.Abs(local.x - GetWadiRouteCenterX(local.z));
        float acrossTrack = 1f - SS(wadiRouteWidth * 0.28f, wadiRouteWidth * 0.52f, distance);
        return WadiLoosePatchAtZ(local.z) * acrossTrack;
    }

    // La corniche doit rester physiquement continue après la ligne d'arrivée :
    // le pickup a encore besoin d'avancer pour déclencher la livraison. Si le
    // terrain revenait ici au relief de base à RouteEndZ, cette transition
    // formait un mur transversal infranchissable devant le véhicule.
    bool IsCanyonTrackActiveAt(float z) => enableCanyonTrack
        && z >= RouteForkStartZ && z <= terrainSize;

    public float GetCanyonTrackCenterX(float z)
    {
        if (!enableCanyonTrack || z < RouteForkStartZ) return RoadX(z);
        float targetOffset = Mathf.Max(extremeRouteOffset, terrainSize * 0.19f);
        float turnsIn = SS(RouteForkEndZ, RouteForkEndZ + 160f, z);
        float distance = GetExtremeRouteDistance(z);
        int segment = GetExtremeSegmentIndex(distance);
        float switchback = ExtremeSegmentTurnOffset(distance, segment, extremeSwitchbackAmplitude);
        return ForkLaneCenter(z, targetOffset)
             + switchback * turnsIn;
    }

    public Vector3 GetWadiRouteDirection(float z)
    {
        float dx = GetWadiRouteCenterX(z + 0.5f) - GetWadiRouteCenterX(z - 0.5f);
        return new Vector3(dx, 0f, 1f).normalized;
    }

    public float GetRoadCenterX(float z) => RoadX(z);
    public Vector3 GetRoadDirection(float z) => new Vector3(RoadSlope(z), 0f, 1f).normalized;

    public bool IsOnRuralDetour(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        if (!IsDetourActiveAt(local.z)) return false;
        float slope = GetRuralDetourCenterX(local.z + 0.5f) - GetRuralDetourCenterX(local.z - 0.5f);
        float distance = Mathf.Abs(local.x - GetRuralDetourCenterX(local.z))
                       / Mathf.Sqrt(1f + slope * slope);
        float mainSlope = RoadSlope(local.z);
        float mainDistance = Mathf.Abs(local.x - RoadX(local.z)) / Mathf.Sqrt(1f + mainSlope * mainSlope);
        return distance <= detourWidth * 0.5f + 3f && distance + 2f < mainDistance;
    }

    public bool IsOnWadiRoute(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        if (!IsWadiRouteActiveAt(local.z)) return false;
        float slope = GetWadiRouteCenterX(local.z + 0.5f) - GetWadiRouteCenterX(local.z - 0.5f);
        float distance = Mathf.Abs(local.x - GetWadiRouteCenterX(local.z)) / Mathf.Sqrt(1f + slope * slope);
        float mainSlope = RoadSlope(local.z);
        float mainDistance = Mathf.Abs(local.x - RoadX(local.z)) / Mathf.Sqrt(1f + mainSlope * mainSlope);
        return distance <= wadiRouteWidth * 0.5f + 3f && distance + 2f < mainDistance;
    }

    public void SetWetRoad(bool wet)
    {
        if (asphaltLayer != null)
            asphaltLayer.smoothness = wet ? 0.35f : 0.10f;
        if (wadiLayer != null)
            wadiLayer.smoothness = wet ? 0.08f : 0.05f;
    }

    public float SampleGroundHeight(float x, float z)
    {
        if (terrain == null) return transform.position.y + RoadHeight(z);
        Vector3 worldPosition = transform.TransformPoint(new Vector3(x, 0f, z));
        return terrain.SampleHeight(worldPosition) + terrain.transform.position.y;
    }

    public bool IsOutsideRoad(Vector3 worldPosition, float margin)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        float mainSlope = RoadSlope(local.z);
        float mainDistance = Mathf.Abs(local.x - RoadX(local.z)) / Mathf.Sqrt(1f + mainSlope * mainSlope);
        if (mainDistance <= GetRoadWidthAt(local.z) * 0.5f + margin) return false;
        if (IsTownCrossStreetAt(worldPosition)) return false;

        if (IsDetourActiveAt(local.z))
        {
            float detourSlope = GetRuralDetourCenterX(local.z + 0.5f) - GetRuralDetourCenterX(local.z - 0.5f);
            float detourDistance = Mathf.Abs(local.x - GetRuralDetourCenterX(local.z))
                                 / Mathf.Sqrt(1f + detourSlope * detourSlope);
            if (detourDistance <= detourWidth * 0.5f + margin) return false;
        }
        if (IsWadiRouteActiveAt(local.z))
        {
            float wadiSlope = GetWadiRouteCenterX(local.z + 0.5f) - GetWadiRouteCenterX(local.z - 0.5f);
            float wadiDistance = Mathf.Abs(local.x - GetWadiRouteCenterX(local.z))
                               / Mathf.Sqrt(1f + wadiSlope * wadiSlope);
            if (wadiDistance <= wadiRouteWidth * 0.5f + margin) return false;
        }
        if (IsCanyonTrackActiveAt(local.z))
        {
            float canyonSlope = GetCanyonTrackCenterX(local.z + 0.5f) - GetCanyonTrackCenterX(local.z - 0.5f);
            float canyonDistance = Mathf.Abs(local.x - GetCanyonTrackCenterX(local.z))
                                 / Mathf.Sqrt(1f + canyonSlope * canyonSlope);
            if (canyonDistance <= GetExtremeTrackWidth(local.z) * 0.5f + margin) return false;
        }
        return true;
    }

    // ---------- Relief ----------

    float RoadRoughness(float x, float z, float lateral)
    {
        float bumps = (Mathf.PerlinNoise(x * 0.55f + seedOffset, z * 0.55f + seedOffset) - 0.5f) * 2f * bumpHeight;
        float undul = (Mathf.PerlinNoise(x * 0.12f + seedOffset + 50f, z * 0.12f + seedOffset + 50f) - 0.5f) * 2f * undulationHeight;
        float a = (lateral - 1f) / 0.3f;
        float b = (lateral + 1f) / 0.3f;
        float ruts = -rutDepth * (Mathf.Exp(-a * a) + Mathf.Exp(-b * b));
        float zoneRoughness = GetRouteZone(z) == 0 ? 1.3f
                            : GetRouteZone(z) == 1 ? 1.12f : 0.92f;
        return (bumps + undul + ruts) * zoneRoughness * (1f - GetTownSurfaceBlend(z));
    }

    float OffRoad(float x, float z, float d, float roadH)
    {
        float valley = Mathf.Min(d, 100f) * 0.035f;
        float hills = (Mathf.PerlinNoise(x * 0.011f + seedOffset + 100f, z * 0.011f + seedOffset + 100f) - 0.5f) * 2f
                      * hillHeight * 0.42f * SS(18f, 100f, d);
        float small = (Mathf.PerlinNoise(x * 0.05f + seedOffset + 200f, z * 0.05f + seedOffset + 200f) - 0.5f) * 2f
                      * 1.35f * SS(8f, 35f, d);
        float mountainWindow = SS(terrainSize * 0.00f, terrainSize * 0.12f, z)
                             * (1f - SS(mountainZoneEnd * terrainSize,
                                 (mountainZoneEnd + 0.12f) * terrainSize, z));
        float ridgeNoise = Mathf.PerlinNoise(x * 0.0035f + seedOffset + 470f,
            z * 0.0025f + seedOffset + 470f);
        float rockyFoothills = mountainWindow * SS(70f, 230f, d) * (4f + ridgeNoise * 9f);
        // Arêtes calcaires plus marquées dans le massif : relief sec et strié plutôt que collines lisses.
        float fracturedNoise = Mathf.PerlinNoise(x * 0.018f + seedOffset + 331f,
            z * 0.015f + seedOffset + 719f);
        float brokenRockRidges = mountainWindow * SS(70f, 190f, d)
            * Mathf.Pow(Mathf.Clamp01((fracturedNoise - 0.43f) * 2.0f), 2f) * 3.2f;
        float naturalHeight = roadH + valley + hills + small + rockyFoothills + brokenRockRidges;
        float townBlend = GetTownSurfaceBlend(z) * (1f - SS(90f, 190f, d));
        if (IsTownAt(z))
            townBlend = Mathf.Max(townBlend, 1f - SS(110f, 310f, d));
        return Mathf.Lerp(naturalHeight, RoadHeight(z), townBlend);
    }

    float AddHardTrackCliffs(float height, float x, float z, float centerX,
        float trackWidth, float trackShoulder, float slope, float outsideSign, float strength,
        float insideWallScale = 1f, float insideWallStartOffset = 8f,
        bool extremeCorniche = false, float ravineDepth = -1f)
    {
        float lateral = (x - centerX) / Mathf.Sqrt(1f + slope * slope);
        float outsideDistance = lateral * outsideSign;
        float insideDistance = -outsideDistance;
        float edge = trackWidth * 0.5f + trackShoulder * (extremeCorniche ? 0.12f : 0.6f);
        float noise = Mathf.PerlinNoise(x * 0.021f + seedOffset + 81f,
            z * 0.017f + seedOffset + 147f);
        float ravineProfile = extremeCorniche
            ? SS(edge + 0.8f, edge + 5.8f, outsideDistance)
              * (1f - SS(edge + 48f, edge + 112f, outsideDistance))
            : SS(edge + 2f, edge + 13f, outsideDistance)
              * (1f - SS(edge + 115f, edge + 235f, outsideDistance));
        float wallStart = edge + (extremeCorniche ? 2.5f : insideWallStartOffset);
        float wallFadeStart = extremeCorniche ? 26f : 58f;
        float wallFadeEnd = extremeCorniche ? 58f : 118f;
        float rockWallProfile = SS(wallStart, wallStart + (extremeCorniche ? 9f : 22f), insideDistance)
                              * (1f - SS(wallStart + wallFadeStart, wallStart + wallFadeEnd, insideDistance));
        float effectiveRavineDepth = ravineDepth > 0f ? ravineDepth : hardTrackRavineDepth;
        return height - ravineProfile * (effectiveRavineDepth + noise * (extremeCorniche ? 8f : 10f)) * strength
                     + rockWallProfile * (extremeCorniche ? 23f + noise * 9f : 12f + noise * 12f)
                       * strength * insideWallScale;
    }

    float HardCliffClearance(float distance, float roadWidth, float roadShoulder)
    {
        // Laisse une zone d'échappement autour des chaussées faciles et moyennes.
        return SS(roadWidth * 0.5f + roadShoulder + 8f,
            roadWidth * 0.5f + roadShoulder + 38f, distance);
    }

    // Suit le relief naturel au milieu d'une piste, lissé sur 120 m pour éviter les murs.
    float NaturalHeightAlongTrack(float z, System.Func<float, float> trackCenterX)
    {
        float sum = 0f;
        int count = 0;
        for (float dz = -60f; dz <= 60.1f; dz += 15f)
        {
            float zz = Mathf.Clamp(z + dz, 0f, terrainSize);
            float cx = trackCenterX(zz);
            sum += OffRoad(cx, zz, Mathf.Abs(cx - RoadX(zz)), RoadHeight(zz));
            count++;
        }
        return sum / count;
    }

    static float SS(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    // ---------- Construction ----------

    void Build()
    {
        data = new TerrainData();
        data.heightmapResolution = heightmapResolution;
        data.size = new Vector3(terrainSize, terrainHeight, terrainSize);
        int res = data.heightmapResolution;
        float step = terrainSize / (res - 1);
        float[,] h = new float[res, res];
        for (int zi = 0; zi < res; zi++)
        {
            float z = zi * step;
            float rx = RoadX(z);
            float slope = RoadSlope(z);
            float norm = 1f / Mathf.Sqrt(1f + slope * slope);
            float rh = RoadHeight(z);
            float halfRoad = GetRoadWidthAt(z) * 0.5f;
            bool detourActive = IsDetourActiveAt(z);
            float detourX = detourActive ? GetRuralDetourCenterX(z) : rx;
            float detourSlope = detourActive
                ? GetRuralDetourCenterX(z + 0.5f) - GetRuralDetourCenterX(z - 0.5f) : 0f;
            float detourCurvature = detourActive
                ? TrackCurvature(GetRuralDetourCenterX, z) : 0f;
            float detourOutsideSign = detourCurvature >= 0f ? -1f : 1f;
            float detourT = detourActive ? Mathf.InverseLerp(DetourStartZ, DetourEndZ, z) : 0f;
            float detourCliffStrength = detourActive
                ? SS(0f, 0.045f, detourT) * (1f - SS(0.94f, 1f, detourT)) : 0f;
            float detourNaturalHeight = detourActive ? NaturalHeightAlongTrack(z, GetRuralDetourCenterX) : rh;
            bool wadiActive = IsWadiRouteActiveAt(z);
            float wadiX = wadiActive ? GetWadiRouteCenterX(z) : rx;
            float wadiSlope = wadiActive
                ? GetWadiRouteCenterX(z + 0.5f) - GetWadiRouteCenterX(z - 0.5f) : 0f;
            float wadiNaturalHeight = wadiActive ? NaturalHeightAlongTrack(z, GetWadiRouteCenterX) : rh;
            bool canyonTrackActive = IsCanyonTrackActiveAt(z);
            bool hardCanyonActive = canyonTrackActive && IsHardCanyonSectionAt(z);
            float canyonX = canyonTrackActive ? GetCanyonTrackCenterX(z) : rx;
            float canyonWidth = canyonTrackActive ? GetExtremeTrackWidth(z) : canyonTrackWidth;
            float canyonSlope = canyonTrackActive
                ? GetCanyonTrackCenterX(z + 0.5f) - GetCanyonTrackCenterX(z - 0.5f) : 0f;
            float canyonCurvature = canyonTrackActive ? TrackCurvature(GetCanyonTrackCenterX, z) : 0f;
            float canyonOutsideSign = canyonCurvature >= 0f ? -1f : 1f;
            float canyonBank = Mathf.Clamp(canyonCurvature * 70f, -1f, 1f);
            float canyonProgress = RouteProgress(z);
            float canyonCliffStrength = hardCanyonActive
                ? SS(0f, 0.035f, canyonProgress)
                    * (1f - SS(0.965f, 1f, canyonProgress)) : 0f;
            float canyonNaturalHeight = canyonTrackActive ? NaturalHeightAlongTrack(z, GetCanyonTrackCenterX) : rh;

            for (int xi = 0; xi < res; xi++)
            {
                float x = xi * step;
                float lateral = (x - rx) * norm;
                float d = Mathf.Abs(lateral);
                float mainRouteClearance = HardCliffClearance(d, GetRoadWidthAt(z), shoulderWidth);
                float wadiRouteClearance = 1f;
                if (wadiActive)
                {
                    float wadiLateralForClearance = (x - wadiX) / Mathf.Sqrt(1f + wadiSlope * wadiSlope);
                    wadiRouteClearance = HardCliffClearance(Mathf.Abs(wadiLateralForClearance),
                        wadiRouteWidth, wadiRouteShoulderWidth);
                }

                float roadSurface = rh + RoadRoughness(x, z, lateral);
                float k = SS(halfRoad, halfRoad + shoulderWidth, d);
                float height = Mathf.Lerp(roadSurface, OffRoad(x, z, d, rh), k);

                if (detourActive)
                {
                    float detourLateral = (x - detourX) / Mathf.Sqrt(1f + detourSlope * detourSlope);
                    float detourDistance = Mathf.Abs(detourLateral);
                    float detourShoulder = SS(detourWidth * 0.5f,
                        detourWidth * 0.5f + detourShoulderWidth, detourDistance);
                    // Le début de branche reste au niveau de la portion commune;
                    // les montées rocheuses prennent ensuite leur propre profil.
                    float detourSurface = detourNaturalHeight + RuralDetourElevation(z)
                        + RoadRoughness(x, z, detourLateral) * detourRoughnessMultiplier;
                    float detourDipZ = RouteForkEndZ + (RouteEndZ - RouteForkEndZ) * 0.66f;
                    float detourSurfaceCut = ErosionJumpDip(z, detourLateral, detourWidth,
                        detourDipZ, hardWashoutJumpDepth);
                    float edgeCollapseZ = RouteForkEndZ + (RouteEndZ - RouteForkEndZ) * 0.73f;
                    detourSurfaceCut += RoadEdgeWashout(z, detourLateral, detourWidth,
                        edgeCollapseZ, hardRoadEdgeWashoutDepth);
                    float detourHeight = Mathf.Lerp(detourSurface - detourSurfaceCut, height, detourShoulder);
                    float detourBlend = (1f - SS(detourWidth * 0.5f + detourShoulderWidth,
                        detourWidth * 0.5f + detourShoulderWidth + step * 2f, detourDistance))
                        * mainRouteClearance;
                    height = Mathf.Lerp(height, detourHeight, detourBlend);
                    height = AddHardTrackCliffs(height, x, z, detourX, detourWidth,
                        detourShoulderWidth, detourSlope, detourOutsideSign,
                        detourCliffStrength * mainRouteClearance * wadiRouteClearance, 0.42f, 38f);
                }

                if (canyonTrackActive)
                {
                    float canyonLateral = (x - canyonX) / Mathf.Sqrt(1f + canyonSlope * canyonSlope);
                    float canyonDistance = Mathf.Abs(canyonLateral);
                    if (canyonDistance < canyonWidth * 0.5f + canyonTrackShoulderWidth + step * 2f)
                    {
                        float canyonShoulder = SS(canyonWidth * 0.5f,
                            canyonWidth * 0.5f + canyonTrackShoulderWidth, canyonDistance);
                        // La route extrême garde son relief dans son propre couloir.
                        float roadBandLateral = Mathf.Clamp(canyonLateral,
                            -canyonWidth * 0.5f, canyonWidth * 0.5f);
                        float outwardBank = roadBandLateral * canyonBank * GetExtremeCrossfall(z);
                        float canyonSurface = canyonNaturalHeight + CanyonTrackElevation(z)
                            + RoadRoughness(x, z, canyonLateral) * wadiRoughnessMultiplier
                            + outwardBank;
                        float canyonDipZ = GetExtremeRouteZAtDistance(GetExtremeSectionDistance(3, 0.625f));
                        float canyonLaunchRamp = ExtremeJumpLaunchRamp(
                            z, canyonLateral, canyonWidth, canyonDipZ);
                        float canyonSurfaceCut = Mathf.Max(
                            ErosionJumpDip(z, canyonLateral, canyonWidth,
                                canyonDipZ, extremeWashoutJumpDepth),
                            ExtremeJumpGapCut(z, canyonLateral, canyonWidth, canyonDipZ));
                        canyonSurface += canyonLaunchRamp;
                        float bridgeGapZ = GetExtremeRouteZAtDistance(GetExtremeSectionDistance(6, 1f / 3f));
                        canyonSurfaceCut += SuspensionBridgeGapCut(z, canyonLateral,
                            canyonWidth, bridgeGapZ);
                        float canyonHeight = Mathf.Lerp(canyonSurface - canyonSurfaceCut, height, canyonShoulder);
                        float canyonBlend = (1f - SS(canyonWidth * 0.5f + canyonTrackShoulderWidth,
                            canyonWidth * 0.5f + canyonTrackShoulderWidth + step * 2f, canyonDistance))
                            * mainRouteClearance;
                        height = Mathf.Lerp(height, canyonHeight, canyonBlend);
                    }
                    if (hardCanyonActive)
                        height = AddHardTrackCliffs(height, x, z, canyonX, canyonWidth,
                            canyonTrackShoulderWidth, canyonSlope, canyonOutsideSign,
                            canyonCliffStrength * mainRouteClearance * wadiRouteClearance,
                            1f, 8f, true, extremeRavineDepth);
                }

                // L'oued moyen est indépendant des voies de crête et conserve
                // une chaussée continue sur toute sa longueur.
                if (wadiActive)
                {
                    float wadiLateral = (x - wadiX) / Mathf.Sqrt(1f + wadiSlope * wadiSlope);
                    float wadiDistance = Mathf.Abs(wadiLateral);
                    if (wadiDistance < wadiRouteWidth * 0.5f + wadiRouteShoulderWidth + step * 2f)
                    {
                        float wadiShoulder = SS(wadiRouteWidth * 0.5f,
                            wadiRouteWidth * 0.5f + wadiRouteShoulderWidth, wadiDistance);
                        float micro = (Mathf.PerlinNoise(x * 0.16f + seedOffset + 501f,
                            z * 0.16f + seedOffset + 227f) - 0.5f) * 2f * wadiMicroBumpHeight;
                        float longWave = (Mathf.PerlinNoise(z * 0.014f + seedOffset + 119f,
                            seedOffset + 811f) - 0.5f) * 2f * wadiRollingWaveHeight;
                        float bank = wadiLateral * Mathf.Sin(z * 0.0036f + seedOffset + 0.6f) * wadiCrossSlope;
                        float wadiSurface = wadiNaturalHeight + RoadRoughness(x, z, wadiLateral)
                            * wadiRoughnessMultiplier + micro + longWave + bank;
                        float wadiHeight = Mathf.Lerp(wadiSurface, height, wadiShoulder);
                        float wadiBlend = 1f - SS(wadiRouteWidth * 0.5f + wadiRouteShoulderWidth,
                            wadiRouteWidth * 0.5f + wadiRouteShoulderWidth + step * 2f, wadiDistance);
                        height = Mathf.Lerp(height, wadiHeight, wadiBlend);
                    }
                }

                h[zi, xi] = height;
            }
        }

        AddPotholes(h, res, step);

        for (int zi = 0; zi < res; zi++)
            for (int xi = 0; xi < res; xi++)
                h[zi, xi] = Mathf.Clamp(h[zi, xi], 0f, terrainHeight) / terrainHeight;

        data.SetHeights(0, 0, h);

        // Textures PBR procédurales : steppe calcaire, piste graveleuse et roche stratifiée.
        Texture2D groundAlbedo = dirtAlbedo;
        Texture2D groundNormal = dirtNormal;
        Texture2D trackAlbedo = wadiAlbedo;
        Texture2D trackNormal = wadiNormal;
        Texture2D cityAsphaltAlbedo = asphaltAlbedo;
        Texture2D cityAsphaltNormal = asphaltNormal;
        Texture2D rockAlbedo = null;
        Texture2D rockNormal = null;
        if (useProceduralSurfaceTextures)
        {
            if (groundAlbedo == null && groundNormal == null)
            {
                TerrainTextureFactory.Create(SurfaceKind.Steppe, seed + 21, out groundAlbedo, out groundNormal);
                generatedTextures.Add(groundAlbedo);
                generatedTextures.Add(groundNormal);
            }
            if (trackAlbedo == null && trackNormal == null)
            {
                TerrainTextureFactory.Create(SurfaceKind.Track, seed + 37, out trackAlbedo, out trackNormal);
                generatedTextures.Add(trackAlbedo);
                generatedTextures.Add(trackNormal);
            }
            if (cityAsphaltAlbedo == null && cityAsphaltNormal == null)
            {
                TerrainTextureFactory.Create(SurfaceKind.Asphalt, seed + 45, out cityAsphaltAlbedo, out cityAsphaltNormal);
                generatedTextures.Add(cityAsphaltAlbedo);
                generatedTextures.Add(cityAsphaltNormal);
            }
            TerrainTextureFactory.Create(SurfaceKind.Limestone, seed + 53, out rockAlbedo, out rockNormal);
            generatedTextures.Add(rockAlbedo);
            generatedTextures.Add(rockNormal);
        }

        TerrainLayer dirt = MakeLayer(new Color(0.53f, 0.52f, 0.46f), 1.15f, 3f,
            groundAlbedo, groundNormal, new Vector2(18f, 18f));
        asphaltLayer = MakeLayer(new Color(0.43f, 0.40f, 0.35f), 1.45f, 21f,
            cityAsphaltAlbedo, cityAsphaltNormal, new Vector2(6.5f, 6.5f));

        dirt.smoothness = 0.05f;
        dirt.metallic = 0f;
        asphaltLayer.smoothness = 0.10f;
        asphaltLayer.metallic = 0f;
        wadiLayer = MakeLayer(new Color(0.57f, 0.54f, 0.47f), 1.18f, 33f,
            trackAlbedo, trackNormal, new Vector2(6f, 6f));
        wadiLayer.smoothness = 0.05f;
        wadiLayer.metallic = 0f;
        TerrainLayer rock = MakeLayer(new Color(0.59f, 0.58f, 0.54f), 1.3f, 47f,
            rockAlbedo, rockNormal, new Vector2(9f, 9f));
        rock.smoothness = 0.05f;
        rock.metallic = 0f;

        data.alphamapResolution = alphamapResolution;
        data.terrainLayers = new[] { dirt, asphaltLayer, wadiLayer, rock };
        int ar = data.alphamapResolution;
        float[,,] maps = new float[ar, ar, 4];

        for (int ay = 0; ay < ar; ay++)
        {
            float v = (ay + 0.5f) / ar;
            float z = v * terrainSize;
            float rx = RoadX(z);
            float slope = RoadSlope(z);
            float norm = 1f / Mathf.Sqrt(1f + slope * slope);
            float halfRoad = GetRoadWidthAt(z) * 0.5f;
            bool detourActive = IsDetourActiveAt(z);
            bool wadiActive = IsWadiRouteActiveAt(z);
            bool canyonTrackActive = IsCanyonTrackActiveAt(z);

            for (int ax = 0; ax < ar; ax++)
            {
                float u = (ax + 0.5f) / ar;
                float x = u * terrainSize;
                float d = Mathf.Abs(x - rx) * norm;

                float mainRoadW = 1f - SS(halfRoad - 0.3f, halfRoad + 0.8f, d);
                float ruralTrackW = 0f;
                if (detourActive)
                {
                    float detourSlope = GetRuralDetourCenterX(z + 0.5f) - GetRuralDetourCenterX(z - 0.5f);
                    float detourDistance = Mathf.Abs(x - GetRuralDetourCenterX(z))
                                         / Mathf.Sqrt(1f + detourSlope * detourSlope);
                    ruralTrackW = 1f - SS(detourWidth * 0.5f - 0.35f,
                        detourWidth * 0.5f + 0.85f, detourDistance);
                }
                // En ville la RN13 est asphaltée ; entre les agglomérations, elle devient terre ou gravier.
                float townBlend = GetTownSurfaceBlend(z);
                float crossStreetW = 0f;
                if (townBlend > 0f)
                {
                    float firstCross = terrainSize * 0.35f + 70f;
                    float crossIndex = Mathf.Round((z - firstCross) / 100f);
                    float crossZ = firstCross + crossIndex * 100f;
                    float alongCross = Mathf.Abs(z - crossZ);
                    float acrossCross = Mathf.Abs(x - rx);
                    crossStreetW = townBlend
                        * (1f - SS(3.8f, 5.5f, alongCross))
                        * (1f - SS(68f, 82f, acrossCross));
                }
                // L'itinéraire facile reste une vraie chaussée asphaltée jusqu'au bout.
                float asphaltW = Mathf.Max(mainRoadW, crossStreetW);
                float mountainGravelW = 0f;
                float lowlandDustW = 0f;
                float wadiTrackW = 0f;
                if (wadiActive)
                {
                    float wadiSlope = GetWadiRouteCenterX(z + 0.5f) - GetWadiRouteCenterX(z - 0.5f);
                    float wadiDistance = Mathf.Abs(x - GetWadiRouteCenterX(z))
                                       / Mathf.Sqrt(1f + wadiSlope * wadiSlope);
                    wadiTrackW = 1f - SS(wadiRouteWidth * 0.5f - 0.35f,
                        wadiRouteWidth * 0.5f + 0.9f, wadiDistance);
                    wadiTrackW *= Mathf.Lerp(0.70f, 1f, WadiLoosePatchAtZ(z));
                }
                float canyonTrackW = 0f;
                if (canyonTrackActive)
                {
                    float canyonSlope = GetCanyonTrackCenterX(z + 0.5f) - GetCanyonTrackCenterX(z - 0.5f);
                    float canyonDistance = Mathf.Abs(x - GetCanyonTrackCenterX(z))
                                         / Mathf.Sqrt(1f + canyonSlope * canyonSlope);
                    float canyonWidth = GetExtremeTrackWidth(z);
                    canyonTrackW = 1f - SS(canyonWidth * 0.5f - 0.35f,
                        canyonWidth * 0.5f + 0.9f, canyonDistance);
                }
                float trackWeight = Mathf.Max(mainRoadW, ruralTrackW) + wadiTrackW + canyonTrackW;
                float totalTrackW = Mathf.Clamp01(trackWeight);
                if (trackWeight > 1f)
                {
                    wadiTrackW /= trackWeight;
                    canyonTrackW /= trackWeight;
                }
                float steep = data.GetSteepness(u, v);
                float rockW = Mathf.Max(SS(28f, 42f, steep) * (1f - totalTrackW), mountainGravelW);
                float sandyRoadW = Mathf.Clamp(wadiTrackW + canyonTrackW + lowlandDustW, 0f,
                    Mathf.Max(0f, 1f - asphaltW - rockW));
                float dirtW = Mathf.Max(0f, 1f - asphaltW - sandyRoadW - rockW);

                maps[ay, ax, 0] = dirtW;
                maps[ay, ax, 1] = asphaltW;
                maps[ay, ax, 2] = sandyRoadW;
                maps[ay, ax, 3] = rockW;
            }
        }
        data.SetAlphamaps(0, 0, maps);

        // Objet Terrain (avec son collider)
        GameObject go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain route";
        go.transform.SetParent(transform, false);
        terrain = go.GetComponent<Terrain>();

        Shader s = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        if (s != null) terrain.materialTemplate = new Material(s);

        BuildRoadSurfaceDetails();
        BuildHardRouteSafetyWalls();
    }

    void AddPotholes(float[,] h, int res, float step)
    {
        var rng = new System.Random(seed);
        int placedMainPotholes = 0;
        int mainAttempts = 0;
        int easyPotholes = Mathf.Max(0, easyRoadPotholeCount);
        while (placedMainPotholes < easyPotholes && mainAttempts++ < easyPotholes * 8)
        {
            float z = Mathf.Lerp(RouteForkStartZ + 18f, RouteEndZ - 12f, (float)rng.NextDouble());
            if (IsTownAt(z)) continue;
            float halfRoad = GetRoadWidthAt(z) * 0.5f;
            float x = RoadX(z) + Mathf.Lerp(-halfRoad + 0.8f, halfRoad - 0.8f, (float)rng.NextDouble());
            float r = Mathf.Lerp(potholeRadius.x, potholeRadius.y, (float)rng.NextDouble());
            float depth = Mathf.Min(0.055f,
                Mathf.Lerp(potholeDepth.x, potholeDepth.y, (float)rng.NextDouble()));
            AddPothole(h, res, step, x, z, r, depth, false);
            placedMainPotholes++;
        }

        // Quelques défauts discrets sur l'asphalte du centre-ville, pas des cratères.
        var townRng = new System.Random(seed + 7021);
        int townPotholes = 14;
        for (int i = 0; i < townPotholes; i++)
        {
            float z = Mathf.Lerp(terrainSize * 0.365f, terrainSize * 0.635f,
                (float)townRng.NextDouble());
            float halfRoad = GetRoadWidthAt(z) * 0.5f;
            float x = RoadX(z) + Mathf.Lerp(-halfRoad + 1.2f, halfRoad - 1.2f,
                (float)townRng.NextDouble());
            AddPothole(h, res, step, x, z,
                Mathf.Lerp(0.42f, 0.82f, (float)townRng.NextDouble()),
                Mathf.Lerp(0.025f, 0.065f, (float)townRng.NextDouble()), false);
        }

        if (enableWadiRoute && wadiPotholeCount > 0)
        {
            var wadiRng = new System.Random(seed + 4217);
            float halfWadi = wadiRouteWidth * 0.5f;
            float startZ = RouteForkEndZ + 25f;
            float endZ = RouteEndZ - 20f;
            int placedWadiPotholes = 0;
            int wadiAttempts = 0;
            while (placedWadiPotholes < wadiPotholeCount && wadiAttempts++ < wadiPotholeCount * 3)
            {
                float z = Mathf.Lerp(startZ, endZ, (float)wadiRng.NextDouble());
                float x = GetWadiRouteCenterX(z)
                        + Mathf.Lerp(-halfWadi + 0.65f, halfWadi - 0.65f, (float)wadiRng.NextDouble());
                float r = Mathf.Lerp(0.65f, 1.25f, (float)wadiRng.NextDouble());
                float depth = Mathf.Lerp(0.045f, 0.12f, (float)wadiRng.NextDouble());
                AddPothole(h, res, step, x, z, r, depth, true);
                placedWadiPotholes++;
            }
        }

        if (enableCanyonTrack && wadiPotholeCount > 0)
        {
            var canyonRng = new System.Random(seed + 8159);
            float startZ = RouteForkEndZ + 25f;
            float endZ = RouteEndZ - 20f;
            int placed = 0;
            int attempts = 0;
            while (placed < wadiPotholeCount && attempts++ < wadiPotholeCount * 3)
            {
                float z = Mathf.Lerp(startZ, endZ, (float)canyonRng.NextDouble());
                float halfCanyon = GetExtremeTrackWidth(z) * 0.5f;
                float x = GetCanyonTrackCenterX(z)
                        + Mathf.Lerp(-halfCanyon + 0.65f, halfCanyon - 0.65f, (float)canyonRng.NextDouble());
                AddPothole(h, res, step, x, z,
                    Mathf.Lerp(0.7f, 1.3f, (float)canyonRng.NextDouble()),
                    Mathf.Lerp(0.055f, 0.13f, (float)canyonRng.NextDouble()), true);
                placed++;
            }
        }

        if (!enableRuralDetour || detourPotholeCount <= 0) return;
        var detourRng = new System.Random(seed + 9187);
        float detourHalf = detourWidth * 0.5f;
        for (int i = 0; i < detourPotholeCount; i++)
        {
            float z = Mathf.Lerp(RouteForkEndZ + 25f, RouteEndZ - 20f,
                (float)detourRng.NextDouble());
            float x = GetRuralDetourCenterX(z)
                    + Mathf.Lerp(-detourHalf + 0.65f, detourHalf - 0.65f, (float)detourRng.NextDouble());
            float r = Mathf.Lerp(0.65f, 1.3f, (float)detourRng.NextDouble());
            float depth = Mathf.Lerp(0.06f, 0.15f, (float)detourRng.NextDouble());
            AddPothole(h, res, step, x, z, r, depth, true);
        }
    }

    void AddPothole(float[,] h, int res, float step, float x, float z, float radius, float depth, bool isWadi)
    {
        CarvePothole(h, res, step, x, z, radius, depth);
        potholeMarks.Add(new PotholeMark(x, z, radius, isWadi));
    }

    void BuildRoadSurfaceDetails()
    {
        if (potholeMarks.Count == 0) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material asphaltRepair = NewSurfaceMaterial(shader, "Aged asphalt repairs");
        Material asphaltRepairFaded = NewSurfaceMaterial(shader, "Faded asphalt repairs");
        Material potholeInterior = NewSurfaceMaterial(shader, "Dark pothole interiors");
        Material wadiHoleInterior = NewSurfaceMaterial(shader, "Dark wadi gravel washouts");
        asphaltRepair.color = new Color(0.86f, 0.85f, 0.82f);
        asphaltRepairFaded.color = new Color(1.06f, 1.03f, 0.98f);
        // Un centre presque noir ressemble à un trou de rendu à cette échelle.
        // Ces tons pierre/asphalte gardent la lecture du creux sans taches noires.
        potholeInterior.color = new Color(0.25f, 0.24f, 0.22f);
        wadiHoleInterior.color = new Color(0.39f, 0.36f, 0.31f);
        SetAsphaltMaps(asphaltRepair, asphaltAlbedo, asphaltNormal);
        SetAsphaltMaps(asphaltRepairFaded, asphaltAlbedo, asphaltNormal);
        SetSmoothness(asphaltRepair, 0.12f);
        SetSmoothness(asphaltRepairFaded, 0.2f);
        SetSmoothness(potholeInterior, 0.02f);
        SetSmoothness(wadiHoleInterior, 0.01f);

        GameObject details = new GameObject("Road surface wear and repairs");
        details.transform.SetParent(transform, false);

        var patchGroups = new List<Vector3>[2] { new List<Vector3>(), new List<Vector3>() };
        var patchUvs = new List<Vector2>[2] { new List<Vector2>(), new List<Vector2>() };
        var patchTriangles = new List<int>[2] { new List<int>(), new List<int>() };
        var darkRoad = new List<Vector3>();
        var darkRoadTriangles = new List<int>();
        var darkWadi = new List<Vector3>();
        var darkWadiTriangles = new List<int>();
        var chippedEdges = new List<Vector3>();
        var chippedTriangles = new List<int>();

        var rng = new System.Random(seed + 15931);
        AddRepairPatches(rng, 92, terrainSize * 0.02f, terrainSize * 0.98f,
            false, patchGroups, patchUvs, patchTriangles);

        for (int i = 0; i < potholeMarks.Count; i++)
        {
            PotholeMark hole = potholeMarks[i];
            List<Vector3> fill = hole.isWadi ? darkWadi : darkRoad;
            List<int> fillTriangles = hole.isWadi ? darkWadiTriangles : darkRoadTriangles;
            AddPotholeVisual(fill, fillTriangles, chippedEdges, chippedTriangles, hole, i);
        }

        for (int i = 0; i < patchGroups.Length; i++)
        {
            Material patchMaterial = i == 0 ? asphaltRepair : asphaltRepairFaded;
            AddSurfaceMesh(details.transform, i == 0 ? "Old asphalt patches" : "Faded asphalt patches",
                patchGroups[i], patchUvs[i], patchTriangles[i], patchMaterial);
        }
        AddSurfaceMesh(details.transform, "Pothole dark centers", darkRoad, null, darkRoadTriangles, potholeInterior);
        AddSurfaceMesh(details.transform, "Wadi gravel washout centers", darkWadi, null, darkWadiTriangles, wadiHoleInterior);
        AddSurfaceMesh(details.transform, "Broken pothole edges", chippedEdges, null, chippedTriangles, asphaltRepair);
    }

    void BuildHardRouteSafetyWalls()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return;

        Material stone = NewSurfaceMaterial(shader, "Mountain limestone retaining wall");
        stone.color = new Color(0.62f, 0.59f, 0.51f);
        SetAsphaltMaps(stone, dirtAlbedo, dirtNormal);
        SetSmoothness(stone, 0.06f);

        if (enableRuralDetour)
            BuildHardTrackSafetyWalls(stone, "Stone parapets — difficult mountain detour",
                GetRuralDetourCenterX, detourWidth,
                RouteForkEndZ + 25f, RouteEndZ - 25f);
        // La corniche extrême reste sans muret ni collision de sécurité.
        // La piste difficile conserve ses parapets.
    }

    void BuildHardTrackSafetyWalls(Material stone, string objectName,
        System.Func<float, float> centerline, float trackWidth, float startZ, float endZ)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        const float sampleStep = 3.5f;
        float previousZ = startZ;
        float previousCurvature = TrackCurvature(centerline, previousZ);
        float previousSign = previousCurvature >= 0f ? -1f : 1f;

        for (float z = startZ + sampleStep; z <= endZ + 0.01f; z += sampleStep)
        {
            float curvature = TrackCurvature(centerline, z);
            float outsideSign = curvature >= 0f ? -1f : 1f;
            bool curvedEnough = Mathf.Abs(curvature) > 0.045f;
            bool previousCurvedEnough = Mathf.Abs(previousCurvature) > 0.045f;
            if (curvedEnough && previousCurvedEnough && outsideSign == previousSign
                && HardWallSegmentClear(centerline, trackWidth, outsideSign, previousZ, z))
                AddHardWallSegment(vertices, uvs, triangles, previousZ, z, outsideSign,
                    centerline, trackWidth);

            previousZ = z;
            previousCurvature = curvature;
            previousSign = outsideSign;
        }

        if (vertices.Count == 0) return;

        Mesh mesh = new Mesh { name = objectName };
        generatedMeshes.Add(mesh);
        mesh.indexFormat = vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject wall = new GameObject(objectName);
        wall.transform.SetParent(transform, false);
        wall.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = wall.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = stone;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
        wall.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    bool HardWallSegmentClear(System.Func<float, float> centerline, float trackWidth,
        float outsideSign, float z0, float z1)
    {
        // Contrôle les deux extrémités et le milieu pour ne pas placer un muret
        // en travers de l'avenue ou de la piste moyenne lors d'un croisement.
        for (int sample = 0; sample < 3; sample++)
        {
            float z = Mathf.Lerp(z0, z1, sample * 0.5f);
            float trackSlope = centerline(z + 0.5f) - centerline(z - 0.5f);
            float wallOffset = outsideSign * (trackWidth * 0.5f + 0.4f);
            float wallNorm = 1f / Mathf.Sqrt(1f + trackSlope * trackSlope);
            float wallX = centerline(z) + wallOffset * wallNorm;
            float wallZ = z - trackSlope * wallOffset * wallNorm;

            float mainSlope = RoadSlope(wallZ);
            float mainDistance = Mathf.Abs(wallX - RoadX(wallZ)) / Mathf.Sqrt(1f + mainSlope * mainSlope);
            if (mainDistance < GetRoadWidthAt(wallZ) * 0.5f + shoulderWidth + 1f)
                return false;

            if (IsWadiRouteActiveAt(wallZ))
            {
                float wadiSlope = GetWadiRouteCenterX(wallZ + 0.5f) - GetWadiRouteCenterX(wallZ - 0.5f);
                float wadiDistance = Mathf.Abs(wallX - GetWadiRouteCenterX(wallZ))
                    / Mathf.Sqrt(1f + wadiSlope * wadiSlope);
                if (wadiDistance < wadiRouteWidth * 0.5f + wadiRouteShoulderWidth + 1f)
                    return false;
            }
        }
        return true;
    }

    float TrackCurvature(System.Func<float, float> centerline, float z)
    {
        return (centerline(z + 2f) - centerline(z)) - (centerline(z) - centerline(z - 2f));
    }

    void AddHardWallSegment(List<Vector3> vertices, List<Vector2> uvs,
        List<int> triangles, float z0, float z1, float outsideSign,
        System.Func<float, float> centerline, float trackWidth)
    {
        int first = vertices.Count;
        AddHardWallCrossSection(vertices, uvs, z0, outsideSign, centerline, trackWidth);
        AddHardWallCrossSection(vertices, uvs, z1, outsideSign, centerline, trackWidth);

        // Inner face, outer face, cap, and underside. Double-sided faces avoid gaps on sharp bends.
        AddDoubleSidedQuad(triangles, first, first + 2, first + 6, first + 4);
        AddDoubleSidedQuad(triangles, first + 1, first + 5, first + 7, first + 3);
        AddDoubleSidedQuad(triangles, first + 2, first + 3, first + 7, first + 6);
        AddDoubleSidedQuad(triangles, first, first + 4, first + 5, first + 1);
        AddDoubleSidedQuad(triangles, first, first + 1, first + 3, first + 2);
        AddDoubleSidedQuad(triangles, first + 4, first + 6, first + 7, first + 5);
    }

    void AddHardWallCrossSection(List<Vector3> vertices, List<Vector2> uvs,
        float z, float outsideSign, System.Func<float, float> centerline, float trackWidth)
    {
        float slope = centerline(z + 0.5f) - centerline(z - 0.5f);
        Vector3 right = new Vector3(1f, 0f, -slope).normalized;
        Vector3 center = new Vector3(centerline(z), 0f, z);
        float halfRoad = trackWidth * 0.5f;
        float innerOffset = halfRoad - 0.2f;
        float outerOffset = halfRoad + 0.4f;
        Vector3 inner = center + right * (outsideSign * innerOffset);
        Vector3 outer = center + right * (outsideSign * outerOffset);
        Vector3 innerBase = WorldSurfacePoint(inner.x, inner.z, 0.06f);
        Vector3 outerBase = WorldSurfacePoint(outer.x, outer.z, 0.06f);
        Vector3 localUp = transform.InverseTransformVector(Vector3.up);
        const float wallHeight = 0.68f;

        vertices.Add(innerBase);
        vertices.Add(outerBase);
        vertices.Add(innerBase + localUp * wallHeight);
        vertices.Add(outerBase + localUp * wallHeight);
        uvs.Add(new Vector2(z / 4f, 0f));
        uvs.Add(new Vector2(z / 4f, 0f));
        uvs.Add(new Vector2(z / 4f, 1f));
        uvs.Add(new Vector2(z / 4f, 1f));
    }

    static void AddDoubleSidedQuad(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(a); triangles.Add(c); triangles.Add(d);
        triangles.Add(c); triangles.Add(b); triangles.Add(a);
        triangles.Add(d); triangles.Add(c); triangles.Add(a);
    }

    void AddRepairPatches(System.Random rng, int count, float startZ, float endZ, bool detour,
        List<Vector3>[] groups, List<Vector2>[] uvs, List<int>[] triangles)
    {
        if (endZ <= startZ) return;
        int made = 0;
        int attempts = count * 8;
        for (int attempt = 0; attempt < attempts && made < count; attempt++)
        {
            float z = Mathf.Lerp(startZ, endZ, (float)rng.NextDouble());
            float lateral = detour
                ? Mathf.Lerp(-detourWidth * 0.28f, detourWidth * 0.28f, (float)rng.NextDouble())
                : Mathf.Lerp(-GetRoadWidthAt(z) * 0.34f, GetRoadWidthAt(z) * 0.34f, (float)rng.NextDouble());
            float centerX = detour ? GetRuralDetourCenterX(z) : RoadX(z);
            Vector3 forward = detour ? GetDetourDirection(z) : GetRoadDirection(z);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 center = new Vector3(centerX, 0f, z) + right * lateral;
            float length = Mathf.Lerp(2.4f, 5.2f, (float)rng.NextDouble());
            float width = Mathf.Lerp(0.8f, 1.9f, (float)rng.NextDouble());

            bool nearHole = false;
            for (int i = 0; i < potholeMarks.Count; i++)
            {
                PotholeMark hole = potholeMarks[i];
                if (hole.isWadi) continue;
                if (new Vector2(hole.x - center.x, hole.z - center.z).sqrMagnitude <
                    (hole.radius + length * 0.55f) * (hole.radius + length * 0.55f))
                {
                    nearHole = true;
                    break;
                }
            }
            if (nearHole) continue;

            int group = rng.NextDouble() < 0.28 ? 1 : 0;
            AddIrregularPatch(groups[group], uvs[group], triangles[group], center,
                forward, right, length, width, rng);
            made++;
        }
    }

    Vector3 GetDetourDirection(float z)
    {
        float dx = GetRuralDetourCenterX(z + 0.5f) - GetRuralDetourCenterX(z - 0.5f);
        return new Vector3(dx, 0f, 1f).normalized;
    }

    void AddIrregularPatch(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector3 center, Vector3 forward, Vector3 right, float length, float width, System.Random rng)
    {
        int first = vertices.Count;
        Vector3 centerLocal = WorldSurfacePoint(center.x, center.z, 0.04f);
        vertices.Add(centerLocal);
        uvs.Add(new Vector2(center.x / 6.5f, center.z / 6.5f));
        const int sides = 9;
        float rotation = (float)rng.NextDouble() * Mathf.PI * 2f;
        for (int i = 0; i < sides; i++)
        {
            float angle = rotation + i * Mathf.PI * 2f / sides;
            float rough = Mathf.Lerp(0.76f, 1.18f, (float)rng.NextDouble());
            float along = Mathf.Cos(angle) * length * 0.5f * rough;
            float across = Mathf.Sin(angle) * width * 0.5f * rough;
            Vector3 point = center + forward * along + right * across;
            vertices.Add(WorldSurfacePoint(point.x, point.z, 0.04f));
            uvs.Add(new Vector2(point.x / 6.5f, point.z / 6.5f));
        }
        for (int i = 0; i < sides; i++)
        {
            int a = first;
            int b = first + 1 + i;
            int c = first + 1 + (i + 1) % sides;
            triangles.Add(a); triangles.Add(c); triangles.Add(b);
        }
    }

    void AddPotholeVisual(List<Vector3> fillVertices, List<int> fillTriangles,
        List<Vector3> edgeVertices, List<int> edgeTriangles, PotholeMark hole, int index)
    {
        const int sides = 10;
        float phase = (index * 0.75487766f + seedOffset) * Mathf.PI * 2f;
        int center = fillVertices.Count;
        fillVertices.Add(WorldSurfacePoint(hole.x, hole.z, 0.035f));
        int ringStart = fillVertices.Count;
        float[] innerRadii = new float[sides];
        float[] outerRadii = new float[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle = phase + i * Mathf.PI * 2f / sides;
            float irregular = 0.82f + 0.18f * Mathf.Sin(index * 2.37f + i * 3.13f);
            innerRadii[i] = hole.radius * Mathf.Lerp(0.38f, 0.66f, (i % 3) / 2f) * irregular;
            outerRadii[i] = hole.radius * Mathf.Lerp(0.96f, 1.22f, (i % 4) / 3f) * irregular;
            float x = hole.x + Mathf.Cos(angle) * innerRadii[i];
            float z = hole.z + Mathf.Sin(angle) * innerRadii[i];
            fillVertices.Add(WorldSurfacePoint(x, z, 0.035f));
        }
        for (int i = 0; i < sides; i++)
        {
            fillTriangles.Add(center); fillTriangles.Add(ringStart + (i + 1) % sides); fillTriangles.Add(ringStart + i);
        }

        int edgeStart = edgeVertices.Count;
        for (int i = 0; i < sides; i++)
        {
            float angle = phase + i * Mathf.PI * 2f / sides;
            float innerR = innerRadii[i] * 1.13f;
            float outerR = outerRadii[i];
            float ix = hole.x + Mathf.Cos(angle) * innerR;
            float iz = hole.z + Mathf.Sin(angle) * innerR;
            float ox = hole.x + Mathf.Cos(angle) * outerR;
            float oz = hole.z + Mathf.Sin(angle) * outerR;
            edgeVertices.Add(WorldSurfacePoint(ix, iz, 0.04f));
            edgeVertices.Add(WorldSurfacePoint(ox, oz, 0.04f));
        }
        for (int i = 0; i < sides; i++)
        {
            int a = edgeStart + i * 2;
            int b = edgeStart + ((i + 1) % sides) * 2;
            edgeTriangles.Add(a); edgeTriangles.Add(b); edgeTriangles.Add(a + 1);
            edgeTriangles.Add(a + 1); edgeTriangles.Add(b); edgeTriangles.Add(b + 1);
        }
    }

    Vector3 WorldSurfacePoint(float x, float z, float lift)
    {
        float y = SampleGroundHeight(x, z) + lift;
        return transform.InverseTransformPoint(new Vector3(x, y, z));
    }

    void AddSurfaceMesh(Transform parent, string label, List<Vector3> vertices,
        List<Vector2> uvs, List<int> triangles, Material material)
    {
        if (vertices.Count == 0) return;
        Mesh mesh = new Mesh { name = label };
        generatedMeshes.Add(mesh);
        mesh.indexFormat = vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        if (uvs != null && uvs.Count == vertices.Count) mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GameObject meshObject = new GameObject(label);
        meshObject.transform.SetParent(parent, false);
        meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
    }

    static void SetAsphaltMaps(Material material, Texture albedo, Texture normal)
    {
        if (albedo != null)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", albedo);
        }
        if (normal != null && material.HasProperty("_BumpMap"))
        {
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
        }
        if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", 0.28f);
    }

    static void SetSmoothness(Material material, float value)
    {
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", value);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
    }

    Material NewSurfaceMaterial(Shader shader, string label)
    {
        Material material = new Material(shader) { name = label };
        generatedMaterials.Add(material);
        return material;
    }

    void OnDestroy()
    {
        for (int i = 0; i < generatedMaterials.Count; i++)
            if (generatedMaterials[i] != null) Destroy(generatedMaterials[i]);
        for (int i = 0; i < generatedMeshes.Count; i++)
            if (generatedMeshes[i] != null) Destroy(generatedMeshes[i]);
        for (int i = 0; i < generatedTextures.Count; i++)
            if (generatedTextures[i] != null) Destroy(generatedTextures[i]);
    }

    static void CarvePothole(float[,] h, int res, float step, float x, float z, float radius, float depth)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt((x - radius) / step));
        int x1 = Mathf.Min(res - 1, Mathf.CeilToInt((x + radius) / step));
        int z0 = Mathf.Max(0, Mathf.FloorToInt((z - radius) / step));
        int z1 = Mathf.Min(res - 1, Mathf.CeilToInt((z + radius) / step));

        for (int zi = z0; zi <= z1; zi++)
        {
            for (int xi = x0; xi <= x1; xi++)
            {
                float dx = xi * step - x;
                float dz = zi * step - z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < radius)
                {
                    float t = 1f - (dist / radius) * (dist / radius);
                    h[zi, xi] -= depth * t * t;
                }
            }
        }
    }

    // ---------- Textures générées par le code ----------

    TerrainLayer MakeLayer(Color baseColor, float contrast, float offset,
        Texture2D importedAlbedo, Texture2D importedNormal, Vector2 tileSize)
    {
        const int size = 512;
        Texture2D tex = importedAlbedo;
        Texture2D normalMap = importedNormal;
        bool makeAlbedo = tex == null;
        bool makeNormal = normalMap == null;

        if (makeAlbedo)
        {
            tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
        }
        if (makeNormal)
        {
            normalMap = new Texture2D(size, size, TextureFormat.RGB24, false, true);
            normalMap.wrapMode = TextureWrapMode.Repeat;
            normalMap.filterMode = FilterMode.Trilinear;
            normalMap.anisoLevel = 8;
        }

        float[,] heights = makeNormal ? new float[size, size] : null;
        if (makeAlbedo || makeNormal)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n1 = TileNoise(x, y, size, 0.04f, offset);
                    float n2 = TileNoise(x, y, size, 0.25f, offset + 10f);
                    float n = n1 * 0.6f + n2 * 0.4f;
                    if (makeNormal) heights[x, y] = n;
                    if (makeAlbedo)
                    {
                        float f = 1f + (n - 0.5f) * contrast;
                        tex.SetPixel(x, y, new Color(
                            Mathf.Clamp01(baseColor.r * f),
                            Mathf.Clamp01(baseColor.g * f),
                            Mathf.Clamp01(baseColor.b * f)));
                    }
                }
            }
        }

        if (makeNormal)
        {
            for (int y = 0; y < size; y++)
            {
                int up = (y + 1) % size;
                int down = (y + size - 1) % size;
                for (int x = 0; x < size; x++)
                {
                    int right = (x + 1) % size;
                    int left = (x + size - 1) % size;
                    Vector3 normal = new Vector3(
                        (heights[left, y] - heights[right, y]) * 2.2f,
                        (heights[x, down] - heights[x, up]) * 2.2f,
                        1f).normalized;
                    normalMap.SetPixel(x, y, new Color(
                        normal.x * 0.5f + 0.5f,
                        normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f));
                }
            }
        }

        if (makeAlbedo) tex.Apply(true);
        if (makeNormal) normalMap.Apply(true);
        return new TerrainLayer
        {
            diffuseTexture = tex,
            normalMapTexture = normalMap,
            normalScale = importedNormal != null ? 0.8f : 1f,
            tileSize = tileSize
        };
    }

    // Bruit qui se répète sans couture
    static float TileNoise(float x, float y, float w, float scale, float off)
    {
        float a = Mathf.PerlinNoise(x * scale + off, y * scale + off);
        float b = Mathf.PerlinNoise((x - w) * scale + off, y * scale + off);
        float c = Mathf.PerlinNoise(x * scale + off, (y - w) * scale + off);
        float d = Mathf.PerlinNoise((x - w) * scale + off, (y - w) * scale + off);
        return (a * (w - x) * (w - y) + b * x * (w - y) + c * (w - x) * y + d * x * y) / (w * w);
    }

    // ---------- Voiture ----------

    void PlaceCar()
    {
        if (car == null) return;

        // Un tronçon commun avant le premier embranchement (environ 30 % du parcours).
        float z = Mathf.Min(24f, terrainSize * 0.03f);
        float x = RoadX(z);
        Vector3 dir = new Vector3(RoadSlope(z), 0f, 1f).normalized;
        float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + 1.2f;

        car.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.LookRotation(dir, Vector3.up));

        var rb = car.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
    }

    /// <summary>Recommence une livraison au début de la route.</summary>
    public void ResetPlayerToStart() => PlaceCar();

    /// <summary>Replace le pickup sur la chaussée praticable la plus proche de sa position.</summary>
    public bool RecoverPlayerNearRoad(Transform target)
    {
        if (target == null || terrain == null) return false;

        Vector3 local = transform.InverseTransformPoint(target.position);
        float bestScore = float.PositiveInfinity;
        Vector3 bestPosition = Vector3.zero;
        Quaternion bestRotation = target.rotation;
        float[] zOffsets = { 0f, 6f, -6f, 12f, -12f, 20f, -20f, 30f, -30f };

        for (int i = 0; i < zOffsets.Length; i++)
        {
            float z = Mathf.Clamp(local.z + zOffsets[i], 12f, terrainSize - 12f);
            ConsiderRecoveryPoint(target, local, RoadX(z), z, GetRoadDirection(z),
                ref bestScore, ref bestPosition, ref bestRotation);

            if (IsDetourActiveAt(z))
            {
                float dx = GetRuralDetourCenterX(z + 0.5f) - GetRuralDetourCenterX(z - 0.5f);
                ConsiderRecoveryPoint(target, local, GetRuralDetourCenterX(z), z,
                    new Vector3(dx, 0f, 1f).normalized, ref bestScore, ref bestPosition, ref bestRotation);
            }
            if (IsWadiRouteActiveAt(z))
                ConsiderRecoveryPoint(target, local, GetWadiRouteCenterX(z), z, GetWadiRouteDirection(z),
                    ref bestScore, ref bestPosition, ref bestRotation);
            if (IsCanyonTrackActiveAt(z))
            {
                float dx = GetCanyonTrackCenterX(z + 0.5f) - GetCanyonTrackCenterX(z - 0.5f);
                ConsiderRecoveryPoint(target, local, GetCanyonTrackCenterX(z), z,
                    new Vector3(dx, 0f, 1f).normalized, ref bestScore, ref bestPosition, ref bestRotation);
            }
        }

        if (float.IsPositiveInfinity(bestScore)) return false;
        target.SetPositionAndRotation(bestPosition, bestRotation);

        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Physics.SyncTransforms();
        return true;
    }

    void ConsiderRecoveryPoint(Transform target, Vector3 originLocal, float x, float z, Vector3 direction,
        ref float bestScore, ref Vector3 bestPosition, ref Quaternion bestRotation)
    {
        Vector3 candidate = transform.TransformPoint(new Vector3(x, 0f, z));
        candidate.y = SampleGroundHeight(x, z) + 1.2f;
        Vector3 worldDirection = transform.TransformDirection(direction).normalized;
        Quaternion candidateRotation = Quaternion.LookRotation(worldDirection, Vector3.up);
        Collider[] overlaps = Physics.OverlapBox(candidate + Vector3.up * 0.5f,
            new Vector3(1.05f, 1.05f, 2.3f), candidateRotation,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider other = overlaps[i];
            if (other is TerrainCollider) continue;
            if (!other || other.transform == target || other.transform.IsChildOf(target)) continue;
            return;
        }

        float dx = x - originLocal.x;
        float dz = z - originLocal.z;
        float score = dx * dx + dz * dz;
        if (score >= bestScore) return;

        bestScore = score;
        bestPosition = candidate;
        bestRotation = candidateRotation;
    }
}

