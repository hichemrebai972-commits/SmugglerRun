using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Enregistre les meilleurs passages locaux et affiche un véhicule fantôme sur la route correspondante.</summary>
[DisallowMultipleComponent]
public sealed class BestRunGhost : MonoBehaviour
{
    const float SamplePeriod = 0.1f;
    const string FolderName = "SmugglerRunRuns";

    [Serializable]
    public sealed class GhostClip
    {
        public int version = 1;
        public List<GhostFrame> frames = new List<GhostFrame>();
    }

    [Serializable]
    public struct GhostFrame
    {
        public float time;
        public Vector3 position;
        public Quaternion rotation;
        public int route;
    }

    readonly List<GhostFrame> recording = new List<GhostFrame>();
    readonly List<GhostFrame> playback = new List<GhostFrame>();
    readonly List<Mesh> bakedMeshes = new List<Mesh>();
    GameObject ghostRoot;
    Material ghostMaterial;
    float recordClock;
    float recordingStartedAt;
    float playbackStartedAt;
    int currentRoute;
    bool visible = true;

    public bool HasGhost => playback.Count > 1 && ghostRoot != null;
    public bool IsVisible => visible;

    void Awake()
    {
        visible = PlayerPrefs.GetInt("SmugglerRun_GhostVisible", 1) != 0;
        BuildGhostVisual();
        BeginRun();
        SetVisible(visible);
    }

    void Update()
    {
        if (Time.deltaTime > 0f)
        {
            recordClock += Time.deltaTime;
            if (recordClock >= SamplePeriod)
            {
                recordClock %= SamplePeriod;
                recording.Add(new GhostFrame
                {
                    time = Time.time - recordingStartedAt,
                    position = transform.position,
                    rotation = transform.rotation,
                    route = currentRoute
                });
            }
        }

        if (!HasGhost || !visible) return;
        float playbackTime = Time.time - playbackStartedAt;
        if (playbackTime < playback[0].time)
        {
            ghostRoot.SetActive(false);
            return;
        }

        int upper = 1;
        while (upper < playback.Count - 1 && playback[upper].time < playbackTime) upper++;
        GhostFrame a = playback[upper - 1];
        GhostFrame b = playback[upper];
        float span = Mathf.Max(0.001f, b.time - a.time);
        float blend = Mathf.Clamp01((playbackTime - a.time) / span);
        ghostRoot.transform.SetPositionAndRotation(
            Vector3.Lerp(a.position, b.position, blend),
            Quaternion.Slerp(a.rotation, b.rotation, blend));
        ghostRoot.transform.localScale = transform.lossyScale;
        ghostRoot.SetActive(playbackTime <= playback[playback.Count - 1].time + 1f);
    }

    public void BeginRun()
    {
        recording.Clear();
        recordClock = 0f;
        recordingStartedAt = Time.time;
        recording.Add(new GhostFrame
        {
            time = 0f,
            position = transform.position,
            rotation = transform.rotation,
            route = currentRoute
        });
    }

    public void SetCurrentRoute(int routeTier)
    {
        routeTier = Mathf.Clamp(routeTier, 0, 3);
        if (currentRoute == routeTier && playback.Count > 0) return;
        currentRoute = routeTier;
        LoadRoute(routeTier);
    }

    public void SaveRoute(int routeTier)
    {
        routeTier = Mathf.Clamp(routeTier, 0, 3);
        GhostClip clip = new GhostClip();
        for (int i = 0; i < recording.Count; i++)
            if (recording[i].route == routeTier) clip.frames.Add(recording[i]);
        if (clip.frames.Count < 2) return;

        try
        {
            string directory = Path.Combine(Application.persistentDataPath, FolderName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "route_" + routeTier + ".json"), JsonUtility.ToJson(clip));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Impossible d'enregistrer le fantôme local : " + exception.Message);
        }
    }

    public void SetVisible(bool show)
    {
        visible = show;
        PlayerPrefs.SetInt("SmugglerRun_GhostVisible", visible ? 1 : 0);
        PlayerPrefs.Save();
        if (ghostRoot) ghostRoot.SetActive(visible && HasGhost);
    }

    void LoadRoute(int routeTier)
    {
        playback.Clear();
        if (ghostRoot) ghostRoot.SetActive(false);
        string path = Path.Combine(Application.persistentDataPath, FolderName, "route_" + routeTier + ".json");
        if (!File.Exists(path)) return;

        try
        {
            GhostClip clip = JsonUtility.FromJson<GhostClip>(File.ReadAllText(path));
            if (clip == null || clip.version != 1 || clip.frames == null) return;
            for (int i = 0; i < clip.frames.Count; i++)
                if (clip.frames[i].route == routeTier) playback.Add(clip.frames[i]);
            if (playback.Count > 1)
            {
                playbackStartedAt = Time.time - playback[0].time;
                if (ghostRoot) ghostRoot.SetActive(visible);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Fantôme local illisible, l'enregistrement sera ignoré : " + exception.Message);
        }
    }

    void BuildGhostVisual()
    {
        ghostRoot = new GameObject("Meilleur passage — fantôme");
        ghostRoot.hideFlags = HideFlags.DontSave;
        ghostRoot.transform.SetPositionAndRotation(transform.position, transform.rotation);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) return;

        ghostMaterial = new Material(shader) { name = "Fantôme cyan translucide" };
        ghostMaterial.color = new Color(0.23f, 0.88f, 1f, 0.38f);
        if (ghostMaterial.HasProperty("_Surface")) ghostMaterial.SetFloat("_Surface", 1f);
        if (ghostMaterial.HasProperty("_Blend")) ghostMaterial.SetFloat("_Blend", 0f);
        if (ghostMaterial.HasProperty("_ZWrite")) ghostMaterial.SetFloat("_ZWrite", 0f);
        if (ghostMaterial.HasProperty("_SrcBlend")) ghostMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (ghostMaterial.HasProperty("_DstBlend")) ghostMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        ghostMaterial.SetOverrideTag("RenderType", "Transparent");
        ghostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ghostMaterial.DisableKeyword("_ALPHATEST_ON");
        ghostMaterial.renderQueue = (int)RenderQueue.Transparent;

        Renderer[] sourceRenderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            Renderer source = sourceRenderers[i];
            if (!source) continue;
            Mesh mesh = null;
            MeshFilter filter = source.GetComponent<MeshFilter>();
            if (filter && filter.sharedMesh) mesh = filter.sharedMesh;
            else if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh)
            {
                mesh = new Mesh { name = "Fantôme " + source.name };
                skinned.BakeMesh(mesh);
                bakedMeshes.Add(mesh);
            }
            if (!mesh) continue;

            GameObject part = new GameObject("Fantôme • " + source.name, typeof(MeshFilter), typeof(MeshRenderer));
            part.hideFlags = HideFlags.DontSave;
            Transform partTransform = part.transform;
            partTransform.SetParent(ghostRoot.transform, false);
            Matrix4x4 relative = transform.worldToLocalMatrix * source.transform.localToWorldMatrix;
            partTransform.localPosition = relative.GetColumn(3);
            partTransform.localRotation = Quaternion.LookRotation(relative.GetColumn(2), relative.GetColumn(1));
            partTransform.localScale = new Vector3(relative.GetColumn(0).magnitude,
                relative.GetColumn(1).magnitude, relative.GetColumn(2).magnitude);
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ghostMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        ghostRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (ghostRoot) Destroy(ghostRoot);
        if (ghostMaterial) Destroy(ghostMaterial);
        for (int i = 0; i < bakedMeshes.Count; i++)
            if (bakedMeshes[i]) Destroy(bakedMeshes[i]);
    }
}
