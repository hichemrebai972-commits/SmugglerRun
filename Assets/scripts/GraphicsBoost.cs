using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Améliore le rendu du jeu (URP) : étalonnage cinéma, bloom, vignette, flou de mouvement,
/// soleil et ombres douces, reflets du ciel, brume de distance, antialiasing et terrain plus net.
/// À mettre sur un objet vide "GraphicsBoost". Tous les réglages sont modifiables dans l'Inspector.
/// </summary>
[DefaultExecutionOrder(-200)]
public class GraphicsBoost : MonoBehaviour
{
    [Header("Références (trouvées automatiquement si vides)")]
    public Camera cam;
    public Light sun;

    [Header("Post-traitement")]
    public bool enablePostProcessing = true;
    [Range(0f, 2f)] public float bloomIntensity = 0.5f;
    [Range(0f, 1f)] public float vignetteIntensity = 0.21f;
    [Range(0f, 1f)] public float motionBlurIntensity = 0.08f;
    [Range(-1f, 1f)] public float exposure = 0.04f;
    [Range(-30f, 40f)] public float contrast = 12f;
    [Range(-30f, 40f)] public float saturation = -6f;

    [Header("Soleil, ciel et reflets")]
    public bool setupSunAndAmbient = true;
    public bool useKasserineDawnHDRI = true;
    [Range(0.1f, 2f)] public float skyExposure = 0.85f;
    public Color sunColor = new Color(1f, 0.96f, 0.90f);
    public float sunIntensity = 1.8f;
    [Range(0.2f, 1f)] public float sunShadowStrength = 0.72f;
    [Range(0.5f, 2f)] public float ambientFill = 1.25f;
    [Tooltip("Hauteur du soleil (X) et direction (Y). Plus X est petit, plus le soleil est bas.")]
    public Vector3 sunAngles = new Vector3(23f, -40f, 0f);

    [Header("Brume de distance")]
    public bool setupHaze = true;
    public Color hazeColor = new Color(0.72f, 0.69f, 0.61f);
    [Tooltip("0.0004 = air très clair, 0.0012 = brume épaisse")]
    public float hazeDensity = 0.00038f;
    [Tooltip("Distance où la brume commence, en mètres.")]
    public float fogStartDistance = 110f;
    [Tooltip("Distance où la brume masque l'horizon, en mètres.")]
    public float fogEndDistance = 950f;

    [Header("Caméra")]
    public float farClip = 1500f;

    [Header("Terrain et textures")]
    public bool tuneTerrain = true;
    Material generatedSkybox;
    Material originalSkybox;

    void Awake()
    {
        originalSkybox = RenderSettings.skybox;
        if (cam == null) cam = Camera.main;
        FindSun();

        if (setupSunAndAmbient) SetupSunAndAmbient();
        if (setupHaze) SetupHaze();
        if (enablePostProcessing) SetupPostProcessing();
        SetupCamera();

        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
    }

    void Start()
    {
        if (tuneTerrain) StartCoroutine(TuneTerrainNextFrame());
    }

    void FindSun()
    {
        if (sun != null) return;
        sun = RenderSettings.sun;
        if (sun != null) return;
        foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type == LightType.Directional) { sun = l; break; }
        }
    }

    void SetupSunAndAmbient()
    {
        if (useKasserineDawnHDRI)
        {
            Texture2D panorama = Resources.Load<Texture2D>("wasteland_clouds_puresky_2k");
            Shader skyShader = Shader.Find("Skybox/Panoramic");
            if (panorama != null && skyShader != null)
            {
                generatedSkybox = new Material(skyShader) { name = "Ciel aride à l'aube - Poly Haven CC0" };
                generatedSkybox.SetTexture("_MainTex", panorama);
                generatedSkybox.SetFloat("_Exposure", skyExposure);
                generatedSkybox.SetFloat("_Rotation", 0f);
                RenderSettings.skybox = generatedSkybox;
            }
        }

        if (sun != null)
        {
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            sun.transform.rotation = Quaternion.Euler(sunAngles);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = sunShadowStrength;
            RenderSettings.sun = sun;
        }

        // Lumière ambiante et reflets pris dans le ciel (important pour la peinture du pickup)
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = ambientFill;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 1f;
        DynamicGI.UpdateEnvironment();
    }

    void SetupHaze()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogDensity = hazeDensity;
        RenderSettings.fogColor = hazeColor;
        RenderSettings.fogStartDistance = fogStartDistance;
        RenderSettings.fogEndDistance = fogEndDistance;
    }

    void SetupPostProcessing()
    {
        GameObject volumeObject = new GameObject("Post-traitement (GraphicsBoost)");
        volumeObject.transform.SetParent(transform, false);

        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        volume.weight = 1f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();

        Tonemapping tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.ACES);

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(bloomIntensity);
        bloom.scatter.Override(0.7f);
        bloom.highQualityFiltering.Override(true);

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(vignetteIntensity);
        vignette.smoothness.Override(0.45f);

        ColorAdjustments colorAdjustments = profile.Add<ColorAdjustments>(true);
        colorAdjustments.postExposure.Override(exposure);
        colorAdjustments.contrast.Override(contrast);
        colorAdjustments.saturation.Override(saturation);
        colorAdjustments.colorFilter.Override(new Color(1.055f, 1.015f, 0.95f, 1f));

        if (motionBlurIntensity > 0f)
        {
            MotionBlur motionBlur = profile.Add<MotionBlur>(true);
            motionBlur.quality.Override(MotionBlurQuality.Medium);
            motionBlur.intensity.Override(motionBlurIntensity);
            motionBlur.clamp.Override(0.05f);
        }

        volume.sharedProfile = profile;
    }

    void SetupCamera()
    {
        if (cam == null) return;

        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        data.dithering = true;
        data.stopNaN = true;

        cam.allowHDR = true;
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, farClip);
    }

    IEnumerator TuneTerrainNextFrame()
    {
        // On attend une image pour que le terrain généré par RoughRoadTerrain existe
        yield return null;

        foreach (Terrain terrain in FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            terrain.heightmapPixelError = 2f;
            terrain.basemapDistance = 2000f;
            terrain.drawInstanced = true;
        }
    }

    void OnDestroy()
    {
        if (generatedSkybox != null)
        {
            if (RenderSettings.skybox == generatedSkybox) RenderSettings.skybox = originalSkybox;
            Destroy(generatedSkybox);
        }
    }
}
