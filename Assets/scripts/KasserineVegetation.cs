using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Pose sur le terrain une végétation inspirée de Kasserine, construite par le code (aucun asset à télécharger) :
/// pins d'Alep sur les contreforts, oliviers en rangées dans la plaine, touffes d'alfa sur la steppe,
/// figuiers de Barbarie en bord de route et rochers calcaires.
/// À mettre sur un objet vide "Vegetation". Le script trouve tout seul le composant Route.
/// </summary>
public class KasserineVegetation : MonoBehaviour
{
    public RoughRoadTerrain route;
    public int seed = 4242;

    [Header("Quantités")]
    [Range(0f, 2f)] public float density = 0.85f;
    public int pineCount = 1050;
    public int olivePlots = 12;
    public int alfaCount = 5400;
    public int pricklyPearClusters = 150;
    public int rockCount = 1450;

    [Header("Distance d'affichage (mètres)")]
    public float chunkSize = 200f;
    public float treeDrawDistance = 750f;
    public float rockDrawDistance = 520f;
    public float pearDrawDistance = 320f;
    public float alfaDrawDistance = 170f;

    [Header("Collisions")]
    public bool treeColliders = true;

    enum Kind { PineTrunk, PineLeaf, OliveTrunk, OliveLeaf, Alfa, Pear, Rock, Count }

    struct ColliderDef
    {
        public Vector3 center;
        public float radius;
        public float height;
        public bool sphere;
    }

    class Chunk
    {
        public List<CombineInstance>[] lists = new List<CombineInstance>[(int)Kind.Count];
        public List<ColliderDef> colliders = new List<ColliderDef>();
    }

    static readonly float[] CityStart = { 0.35f };
    static readonly float[] CityEnd = { 0.65f };

    readonly Mesh[][] meshes = new Mesh[(int)Kind.Count][];
    readonly Material[] materials = new Material[(int)Kind.Count];
    readonly Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
    readonly List<Mesh> generatedMeshes = new List<Mesh>();
    readonly List<Texture2D> generatedTextures = new List<Texture2D>();
    float size;
    System.Random rng;

    void Start()
    {
        if (route == null) route = FindFirstObjectByType<RoughRoadTerrain>();
        if (route == null) { Debug.LogWarning("KasserineVegetation : aucun RoughRoadTerrain trouvé."); return; }

        size = route.terrainSize;
        rng = new System.Random(seed);

        BuildPrototypes();
        ScatterPines();
        ScatterOliveGroves();
        ScatterAlfa();
        ScatterPricklyPears();
        ScatterRocks();
        BuildChunkObjects();
    }

    // ---------- Règles du terrain ----------

    static float SS(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    float Rand(float min, float max) { return Mathf.Lerp(min, max, (float)rng.NextDouble()); }

    float Ground(float x, float z) { return route.SampleGroundHeight(x, z); }

    bool OnRoad(float x, float z, float margin)
    {
        return !route.IsOutsideRoad(new Vector3(x, Ground(x, z), z), margin);
    }

    float SlopeDegrees(float x, float z)
    {
        float dx = Ground(x + 2f, z) - Ground(x - 2f, z);
        float dz = Ground(x, z + 2f) - Ground(x, z - 2f);
        return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) / 4f) * Mathf.Rad2Deg;
    }

    float MainDistance(float x, float z)
    {
        return Mathf.Abs(x - route.GetRoadCenterX(z));
    }

    bool InCity(float z)
    {
        for (int i = 0; i < CityStart.Length; i++)
            if (z >= size * CityStart[i] && z <= size * CityEnd[i]) return true;
        return false;
    }

    float MountainFactor(float z)
    {
        float t = z / size;
        return SS(0.015f, 0.06f, t)
             * (1f - SS(route.mountainZoneEnd - 0.015f, route.mountainZoneEnd + 0.07f, t));
    }

    // ---------- Pose ----------

    void ScatterPines()
    {
        int target = Mathf.RoundToInt(pineCount * density);
        int placed = 0;
        for (int attempts = 0; placed < target && attempts < target * 25; attempts++)
        {
            float x = Rand(8f, size - 8f);
            float z = Rand(8f, size - 8f);
            float d = MainDistance(x, z);
            float p = 0.03f + MountainFactor(z) * SS(25f, 130f, d) * 0.97f;
            if (rng.NextDouble() > p) continue;
            if (InCity(z) && d < 60f) continue;
            if (OnRoad(x, z, 9f)) continue;
            if (SlopeDegrees(x, z) > 32f) continue;

            PlaceTree(Kind.PineTrunk, Kind.PineLeaf, x, z, Rand(0.8f, 1.4f), 4.2f);
            placed++;
        }
    }

    void ScatterOliveGroves()
    {
        int plots = Mathf.RoundToInt(olivePlots * density);
        int made = 0;
        for (int tries = 0; made < plots && tries < plots * 40; tries++)
        {
            float z = Rand(60f, size - 80f);
            if (InCity(z) || z / size < route.forestZoneEnd) continue;

            int side = rng.Next(2) == 0 ? -1 : 1;
            float cx = route.GetRoadCenterX(z) + side * Rand(40f, 190f);
            if (cx < 120f || cx > size - 120f) continue;

            float width = Rand(70f, 120f);
            float length = Rand(60f, 100f);
            const float spacing = 11f;
            int trees = 0;
            for (float gx = -width * 0.5f; gx <= width * 0.5f; gx += spacing)
            {
                for (float gz = -length * 0.5f; gz <= length * 0.5f; gz += spacing)
                {
                    float x = cx + gx + Rand(-0.6f, 0.6f);
                    float zz = z + gz + Rand(-0.6f, 0.6f);
                    if (x < 10f || x > size - 10f || zz < 10f || zz > size - 10f) continue;
                    if (InCity(zz) && MainDistance(x, zz) < 60f) continue;
                    if (OnRoad(x, zz, 10f)) continue;
                    if (SlopeDegrees(x, zz) > 14f) continue;
                    PlaceTree(Kind.OliveTrunk, Kind.OliveLeaf, x, zz, Rand(0.85f, 1.2f), 2.2f);
                    trees++;
                }
            }
            if (trees > 6) made++;
        }
    }

    void ScatterAlfa()
    {
        int target = Mathf.RoundToInt(alfaCount * density);
        int placed = 0;
        for (int attempts = 0; placed < target && attempts < target * 6; attempts++)
        {
            float x = Rand(6f, size - 6f);
            float z = Rand(6f, size - 6f);
            float d = MainDistance(x, z);
            if (InCity(z) && d < 45f) continue;
            if (rng.NextDouble() > 0.35f + 0.65f * SS(6f, 40f, d)) continue;
            if (OnRoad(x, z, 3.5f)) continue;
            if (SlopeDegrees(x, z) > 32f) continue;

            float y = Ground(x, z);
            AddInstance(Kind.Alfa, rng.Next(meshes[(int)Kind.Alfa].Length),
                new Vector3(x, y - 0.02f, z), Rand(0f, 360f), Vector3.one * Rand(0.7f, 1.5f), 0f);
            placed++;
        }
    }

    void ScatterPricklyPears()
    {
        int target = Mathf.RoundToInt(pricklyPearClusters * density);
        for (int i = 0; i < target; i++)
        {
            float z = Rand(40f, size - 40f);
            if (InCity(z)) continue;
            int side = rng.Next(2) == 0 ? -1 : 1;
            float x = route.GetRoadCenterX(z) + side * (route.roadWidth * 0.5f + Rand(10f, 30f));
            if (x < 8f || x > size - 8f) continue;

            int plants = rng.Next(3, 6);
            for (int k = 0; k < plants; k++)
            {
                float px = x + Rand(-2.5f, 2.5f);
                float pz = z + Rand(-2.5f, 2.5f);
                if (OnRoad(px, pz, 6f) || SlopeDegrees(px, pz) > 25f) continue;
                AddInstance(Kind.Pear, rng.Next(meshes[(int)Kind.Pear].Length),
                    new Vector3(px, Ground(px, pz) - 0.05f, pz), Rand(0f, 360f), Vector3.one * Rand(0.9f, 1.5f), 0f);
            }
        }
    }

    void ScatterRocks()
    {
        int target = Mathf.RoundToInt(rockCount * density);
        int placed = 0;
        for (int attempts = 0; placed < target && attempts < target * 8; attempts++)
        {
            float x = Rand(6f, size - 6f);
            float z = Rand(6f, size - 6f);
            float d = MainDistance(x, z);
            if (InCity(z) && d < 45f) continue;
            float slope = SlopeDegrees(x, z);
            float roadside = 1f - SS(35f, 115f, d);
            float p = 0.12f + 0.5f * SS(8f, 30f, slope)
                    + 0.35f * MountainFactor(z) + roadside * 0.38f;
            p = Mathf.Clamp01(p);
            if (rng.NextDouble() > p) continue;
            if (OnRoad(x, z, 2.5f)) continue;

            bool boulder = rng.NextDouble() < 0.10;
            float s = boulder ? Rand(2f, 4.5f) : Rand(0.4f, 1.6f);
            Vector3 scale = new Vector3(s * Rand(0.9f, 1.3f), s * Rand(0.8f, 1.1f), s * Rand(0.9f, 1.3f));
            float y = Ground(x, z) - 0.22f * s;
            AddInstance(Kind.Rock, rng.Next(meshes[(int)Kind.Rock].Length),
                new Vector3(x, y, z), Rand(0f, 360f), scale, Rand(-12f, 12f));

            if (s > 1.3f)
            {
                ColliderDef def = new ColliderDef
                {
                    center = new Vector3(x, y + s * 0.45f, z),
                    radius = s * 0.72f,
                    sphere = true
                };
                GetChunk(x, z).colliders.Add(def);
            }
            placed++;
        }
    }

    void PlaceTree(Kind trunk, Kind leaf, float x, float z, float scale, float colliderHeight)
    {
        float y = Ground(x, z) - 0.12f;
        int variants = meshes[(int)trunk].Length;
        int variant = rng.Next(variants);
        float yaw = Rand(0f, 360f);
        float tilt = Rand(-3f, 3f);
        Vector3 pos = new Vector3(x, y, z);
        AddInstance(trunk, variant, pos, yaw, Vector3.one * scale, tilt);
        AddInstance(leaf, variant, pos, yaw, Vector3.one * scale, tilt);

        if (treeColliders)
        {
            ColliderDef def = new ColliderDef
            {
                center = pos + Vector3.up * (colliderHeight * 0.5f * scale),
                radius = 0.35f * scale,
                height = colliderHeight * scale,
                sphere = false
            };
            GetChunk(x, z).colliders.Add(def);
        }
    }

    Chunk GetChunk(float x, float z)
    {
        int cx = Mathf.FloorToInt(x / chunkSize);
        int cz = Mathf.FloorToInt(z / chunkSize);
        long key = ((long)cx << 32) | (uint)cz;
        if (!chunks.TryGetValue(key, out Chunk chunk))
        {
            chunk = new Chunk();
            chunks[key] = chunk;
        }
        return chunk;
    }

    void AddInstance(Kind kind, int variant, Vector3 position, float yaw, Vector3 scale, float tilt)
    {
        Chunk chunk = GetChunk(position.x, position.z);
        int index = (int)kind;
        if (chunk.lists[index] == null) chunk.lists[index] = new List<CombineInstance>();
        chunk.lists[index].Add(new CombineInstance
        {
            mesh = meshes[index][variant],
            transform = Matrix4x4.TRS(position, Quaternion.Euler(tilt, yaw, tilt * 0.6f), scale)
        });
    }

    // ---------- Objets de la scène ----------

    void BuildChunkObjects()
    {
        foreach (KeyValuePair<long, Chunk> pair in chunks)
        {
            int cx = (int)(pair.Key >> 32);
            int cz = (int)(pair.Key & 0xFFFFFFFF);
            Vector3 center = new Vector3((cx + 0.5f) * chunkSize, 0f, (cz + 0.5f) * chunkSize);
            float radius = chunkSize * 0.71f;

            GameObject chunkObject = new GameObject("Végétation " + cx + "," + cz);
            chunkObject.transform.SetParent(transform, false);

            for (int k = 0; k < (int)Kind.Count; k++)
            {
                List<CombineInstance> list = pair.Value.lists[k];
                if (list == null || list.Count == 0) continue;

                Mesh combined = new Mesh { name = ((Kind)k).ToString() + " chunk" };
                combined.indexFormat = IndexFormat.UInt32;
                combined.CombineMeshes(list.ToArray(), true, true);
                combined.RecalculateBounds();
                generatedMeshes.Add(combined);

                GameObject part = new GameObject(((Kind)k).ToString());
                part.transform.SetParent(chunkObject.transform, false);
                part.AddComponent<MeshFilter>().sharedMesh = combined;
                MeshRenderer renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[k];
                renderer.shadowCastingMode = (Kind)k == Kind.Alfa ? ShadowCastingMode.Off : ShadowCastingMode.On;

                VegetationDistanceCuller culler = part.AddComponent<VegetationDistanceCuller>();
                culler.center = center;
                culler.chunkRadius = radius;
                culler.distance = DrawDistance((Kind)k);
            }

            if (pair.Value.colliders.Count > 0)
            {
                GameObject colliderObject = new GameObject("Collisions");
                colliderObject.transform.SetParent(chunkObject.transform, false);
                foreach (ColliderDef def in pair.Value.colliders)
                {
                    if (def.sphere)
                    {
                        SphereCollider sphere = colliderObject.AddComponent<SphereCollider>();
                        sphere.center = def.center;
                        sphere.radius = def.radius;
                    }
                    else
                    {
                        CapsuleCollider capsule = colliderObject.AddComponent<CapsuleCollider>();
                        capsule.center = def.center;
                        capsule.radius = def.radius;
                        capsule.height = Mathf.Max(def.height, def.radius * 2f);
                        capsule.direction = 1;
                    }
                }
            }
        }
    }

    float DrawDistance(Kind kind)
    {
        switch (kind)
        {
            case Kind.Alfa: return alfaDrawDistance;
            case Kind.Pear: return pearDrawDistance;
            case Kind.Rock: return rockDrawDistance;
            default: return treeDrawDistance;
        }
    }

    // ---------- Fabrication des modèles ----------

    void BuildPrototypes()
    {
        Texture2D needles = MakeSpeckleTexture(new Color(0.16f, 0.30f, 0.12f), new Color(0.30f, 0.43f, 0.20f), 11);
        Texture2D oliveLeaves = MakeSpeckleTexture(new Color(0.34f, 0.42f, 0.30f), new Color(0.55f, 0.60f, 0.46f), 23);
        Texture2D bark = MakeBarkTexture(31);
        Texture2D pearSkin = MakeSpeckleTexture(new Color(0.20f, 0.38f, 0.17f), new Color(0.34f, 0.52f, 0.24f), 47);
        Texture2D alfaTexture = MakeAlfaTexture(59);
        TerrainTextureFactory.Create(SurfaceKind.Limestone, seed + 71, out Texture2D rockAlbedo, out Texture2D rockNormal);
        generatedTextures.Add(needles);
        generatedTextures.Add(oliveLeaves);
        generatedTextures.Add(bark);
        generatedTextures.Add(pearSkin);
        generatedTextures.Add(alfaTexture);
        generatedTextures.Add(rockAlbedo);
        generatedTextures.Add(rockNormal);

        materials[(int)Kind.PineTrunk] = MakeMaterial(bark, Color.white, 0.05f, false, false);
        materials[(int)Kind.PineLeaf] = MakeMaterial(needles, Color.white, 0.1f, false, false);
        materials[(int)Kind.OliveTrunk] = MakeMaterial(bark, new Color(0.85f, 0.82f, 0.78f), 0.05f, false, false);
        materials[(int)Kind.OliveLeaf] = MakeMaterial(oliveLeaves, Color.white, 0.12f, false, false);
        materials[(int)Kind.Alfa] = MakeMaterial(alfaTexture, Color.white, 0.05f, true, true);
        materials[(int)Kind.Pear] = MakeMaterial(pearSkin, Color.white, 0.2f, false, false);
        materials[(int)Kind.Rock] = MakeMaterial(rockAlbedo, Color.white, 0.08f, false, false);
        if (materials[(int)Kind.Rock].HasProperty("_BumpMap"))
        {
            materials[(int)Kind.Rock].SetTexture("_BumpMap", rockNormal);
            materials[(int)Kind.Rock].EnableKeyword("_NORMALMAP");
        }

        meshes[(int)Kind.PineTrunk] = new Mesh[3];
        meshes[(int)Kind.PineLeaf] = new Mesh[3];
        for (int i = 0; i < 3; i++) BuildPine(i);

        meshes[(int)Kind.OliveTrunk] = new Mesh[3];
        meshes[(int)Kind.OliveLeaf] = new Mesh[3];
        for (int i = 0; i < 3; i++) BuildOlive(i);

        meshes[(int)Kind.Alfa] = new Mesh[2];
        for (int i = 0; i < 2; i++)
        {
            MeshBuilder b = new MeshBuilder();
            b.AddCrossedQuads(i == 0 ? 1.1f : 0.85f, i == 0 ? 0.8f : 0.65f, 3);
            meshes[(int)Kind.Alfa][i] = b.ToMesh("Alfa " + i);
        }

        meshes[(int)Kind.Pear] = new Mesh[2];
        for (int i = 0; i < 2; i++) meshes[(int)Kind.Pear][i] = BuildPricklyPear(i);

        meshes[(int)Kind.Rock] = new Mesh[3];
        for (int i = 0; i < 3; i++)
        {
            MeshBuilder b = new MeshBuilder();
            b.AddBlob(Vector3.zero, new Vector3(1f, 0.65f, 0.85f), 8, 12, 0.33f, 100 + i * 13, Quaternion.identity, 1.4f);
            meshes[(int)Kind.Rock][i] = b.ToMesh("Rock " + i);
        }
    }

    void BuildPine(int variant)
    {
        float height = 6f + variant * 1.4f;
        Vector3 bend = new Vector3(Rand(-0.9f, 0.9f), 0f, Rand(-0.9f, 0.9f));

        MeshBuilder trunk = new MeshBuilder();
        trunk.AddTrunk(Vector3.zero, height, 0.30f, 0.10f, 8, bend, 6);
        meshes[(int)Kind.PineTrunk][variant] = trunk.ToMesh("Pine trunk " + variant);

        // Pin d'Alep : cime claire et irrégulière en forme de parasol
        MeshBuilder crown = new MeshBuilder();
        int blobs = 7 + variant;
        for (int i = 0; i < blobs; i++)
        {
            float t = Rand(0.62f, 0.98f);
            float angle = Rand(0f, Mathf.PI * 2f);
            float reach = Rand(0.4f, 2.1f) * (1.1f - (t - 0.62f));
            Vector3 center = new Vector3(Mathf.Cos(angle) * reach, height * t, Mathf.Sin(angle) * reach) + bend * (t * t);
            Vector3 radii = new Vector3(Rand(1.4f, 2.4f), Rand(0.6f, 1.0f), Rand(1.4f, 2.4f));
            crown.AddBlob(center, radii, 6, 9, 0.28f, 200 + variant * 31 + i, Quaternion.Euler(0f, Rand(0f, 360f), 0f), 2f);
        }
        meshes[(int)Kind.PineLeaf][variant] = crown.ToMesh("Pine crown " + variant);
    }

    void BuildOlive(int variant)
    {
        MeshBuilder trunk = new MeshBuilder();
        float height = 1.5f + variant * 0.3f;
        trunk.AddTrunk(Vector3.zero, height, 0.28f, 0.18f, 7, new Vector3(Rand(-0.3f, 0.3f), 0f, Rand(-0.3f, 0.3f)), 4);
        trunk.AddTrunk(new Vector3(0f, height * 0.7f, 0f), 1.2f, 0.16f, 0.07f, 6, new Vector3(Rand(0.4f, 0.8f), 0f, Rand(-0.4f, 0.4f)), 3);
        trunk.AddTrunk(new Vector3(0f, height * 0.75f, 0f), 1.1f, 0.14f, 0.06f, 6, new Vector3(Rand(-0.8f, -0.4f), 0f, Rand(-0.4f, 0.4f)), 3);
        meshes[(int)Kind.OliveTrunk][variant] = trunk.ToMesh("Olive trunk " + variant);

        MeshBuilder crown = new MeshBuilder();
        int blobs = 5 + variant;
        for (int i = 0; i < blobs; i++)
        {
            float angle = Rand(0f, Mathf.PI * 2f);
            float reach = Rand(0.2f, 1.1f);
            Vector3 center = new Vector3(Mathf.Cos(angle) * reach, Rand(2.0f, 3.1f), Mathf.Sin(angle) * reach);
            Vector3 radii = new Vector3(Rand(1.0f, 1.7f), Rand(0.7f, 1.1f), Rand(1.0f, 1.7f));
            crown.AddBlob(center, radii, 6, 9, 0.3f, 300 + variant * 17 + i, Quaternion.Euler(0f, Rand(0f, 360f), 0f), 2f);
        }
        meshes[(int)Kind.OliveLeaf][variant] = crown.ToMesh("Olive crown " + variant);
    }

    Mesh BuildPricklyPear(int variant)
    {
        MeshBuilder b = new MeshBuilder();
        int pads = 5 + variant * 2;
        Vector3 anchor = Vector3.zero;
        for (int i = 0; i < pads; i++)
        {
            float lean = Rand(-25f, 25f);
            Quaternion rotation = Quaternion.Euler(lean, Rand(0f, 360f), Rand(-25f, 25f));
            Vector3 center = anchor + new Vector3(Rand(-0.25f, 0.25f), 0.3f + i * 0.12f, Rand(-0.25f, 0.25f));
            if (i == 0) center = new Vector3(0f, 0.3f, 0f);
            b.AddBlob(center, new Vector3(0.32f, 0.40f, 0.055f), 6, 8, 0.1f, 400 + variant * 29 + i, rotation, 1f);
        }
        return b.ToMesh("Prickly pear " + variant);
    }

    // ---------- Textures et matériaux ----------

    static Material MakeMaterial(Texture texture, Color tint, float smoothness, bool alphaClip, bool doubleSided)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader);
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        else material.mainTexture = texture;
        material.color = tint;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (alphaClip)
        {
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 1f);
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.45f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }
        if (doubleSided && material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        return material;
    }

    static Texture2D MakeSpeckleTexture(Color a, Color b, int textureSeed)
    {
        const int n = 128;
        var random = new System.Random(textureSeed);
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float noise = Mathf.PerlinNoise(x * 0.11f + textureSeed, y * 0.11f + textureSeed);
                float speck = 0.78f + (float)random.NextDouble() * 0.44f;
                Color c = Color.Lerp(a, b, noise) * speck;
                pixels[y * n + x] = new Color32(
                    (byte)Mathf.Clamp(c.r * 255f, 0f, 255f), (byte)Mathf.Clamp(c.g * 255f, 0f, 255f),
                    (byte)Mathf.Clamp(c.b * 255f, 0f, 255f), 255);
            }
        }
        return MakeTexture(pixels, n, TextureWrapMode.Repeat);
    }

    static Texture2D MakeBarkTexture(int textureSeed)
    {
        const int n = 128;
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float streak = Mathf.PerlinNoise(x * 0.35f + textureSeed, y * 0.04f + textureSeed);
                float detail = Mathf.PerlinNoise(x * 0.9f, y * 0.12f + textureSeed);
                float v = 0.18f + streak * 0.22f + detail * 0.08f;
                pixels[y * n + x] = new Color32(
                    (byte)(v * 255f * 1.05f), (byte)(v * 255f * 0.92f), (byte)(v * 255f * 0.80f), 255);
            }
        }
        return MakeTexture(pixels, n, TextureWrapMode.Repeat);
    }

    static Texture2D MakeAlfaTexture(int textureSeed)
    {
        const int n = 128;
        var random = new System.Random(textureSeed);
        var pixels = new Color32[n * n];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(190, 170, 100, 0);

        Color straw = new Color(0.76f, 0.68f, 0.40f);
        Color green = new Color(0.52f, 0.58f, 0.28f);
        for (int blade = 0; blade < 46; blade++)
        {
            float baseX = 0.12f + (float)random.NextDouble() * 0.76f;
            float lean = ((float)random.NextDouble() - 0.5f) * 0.55f;
            float curve = ((float)random.NextDouble() - 0.5f) * 0.5f;
            float height = 0.55f + (float)random.NextDouble() * 0.45f;
            float width = 0.012f + 0.010f * (float)random.NextDouble();
            Color color = Color.Lerp(straw, green, (float)random.NextDouble() * 0.6f);

            for (int step = 0; step <= 60; step++)
            {
                float t = step / 60f;
                float px = (baseX + lean * t + curve * t * t) * n;
                float py = height * t * n;
                float radius = Mathf.Max(0.8f, (width * (1f - t) + 0.0015f) * n);
                float shade = 0.7f + 0.3f * t;
                int r = Mathf.CeilToInt(radius);
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dy * dy > radius * radius) continue;
                        int ix = Mathf.RoundToInt(px) + dx;
                        int iy = Mathf.RoundToInt(py) + dy;
                        if (ix < 0 || ix >= n || iy < 0 || iy >= n) continue;
                        pixels[iy * n + ix] = new Color32(
                            (byte)(color.r * shade * 255f), (byte)(color.g * shade * 255f),
                            (byte)(color.b * shade * 255f), 255);
                    }
                }
            }
        }
        return MakeTexture(pixels, n, TextureWrapMode.Clamp);
    }

    static Texture2D MakeTexture(Color32[] pixels, int n, TextureWrapMode wrap)
    {
        var texture = new Texture2D(n, n, TextureFormat.RGBA32, true);
        texture.wrapMode = wrap;
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = 4;
        texture.SetPixels32(pixels);
        texture.Apply(true);
        return texture;
    }

    void OnDestroy()
    {
        foreach (Material material in materials)
            if (material != null) Destroy(material);
        foreach (Mesh mesh in generatedMeshes)
            if (mesh != null) Destroy(mesh);
        foreach (Mesh[] variants in meshes)
            if (variants != null)
                foreach (Mesh mesh in variants)
                    if (mesh != null) Destroy(mesh);
        foreach (Texture2D texture in generatedTextures)
            if (texture != null) Destroy(texture);
    }

    // ---------- Constructeur de maillages ----------

    class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> triangles = new List<int>();

        public void AddBlob(Vector3 center, Vector3 radii, int lat, int lon, float noiseAmount,
            int noiseSeed, Quaternion rotation, float uvScale)
        {
            int start = vertices.Count;
            for (int i = 0; i <= lat; i++)
            {
                float a = Mathf.PI * i / lat;
                for (int j = 0; j <= lon; j++)
                {
                    float b = 2f * Mathf.PI * j / lon;
                    Vector3 dir = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b));
                    float noise = 1f + noiseAmount * (Mathf.PerlinNoise(
                        noiseSeed * 0.37f + dir.x * 2.3f + 5f,
                        noiseSeed * 0.61f + dir.z * 2.3f + dir.y * 1.7f + 5f) - 0.5f) * 2f;
                    Vector3 p = Vector3.Scale(dir, radii) * noise;
                    vertices.Add(center + rotation * p);
                    normals.Add((rotation * new Vector3(dir.x / radii.x, dir.y / radii.y, dir.z / radii.z)).normalized);
                    uvs.Add(new Vector2((float)j / lon * uvScale, (float)i / lat * uvScale));
                }
            }
            int stride = lon + 1;
            for (int i = 0; i < lat; i++)
            {
                for (int j = 0; j < lon; j++)
                {
                    int a = start + i * stride + j;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }
        }

        public void AddTrunk(Vector3 basePosition, float height, float radiusBottom, float radiusTop,
            int sides, Vector3 bendTop, int rings)
        {
            int start = vertices.Count;
            for (int i = 0; i <= rings; i++)
            {
                float t = (float)i / rings;
                Vector3 c = basePosition + Vector3.up * (height * t) + bendTop * (t * t);
                float r = Mathf.Lerp(radiusBottom, radiusTop, t) * (1f + 0.08f * Mathf.Sin(t * 9f + height));
                for (int j = 0; j <= sides; j++)
                {
                    float angle = 2f * Mathf.PI * j / sides;
                    Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    vertices.Add(c + dir * r);
                    normals.Add(dir);
                    uvs.Add(new Vector2((float)j / sides * 2f, t * height * 0.5f));
                }
            }
            int stride = sides + 1;
            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = start + i * stride + j;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
        }

        public void AddCrossedQuads(float width, float height, int planes)
        {
            for (int p = 0; p < planes; p++)
            {
                float angle = Mathf.PI * p / planes;
                Vector3 half = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (width * 0.5f);
                int s = vertices.Count;
                vertices.Add(-half);
                vertices.Add(half);
                vertices.Add(half + Vector3.up * height);
                vertices.Add(-half + Vector3.up * height);
                for (int i = 0; i < 4; i++) normals.Add(Vector3.up);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                triangles.Add(s); triangles.Add(s + 2); triangles.Add(s + 1);
                triangles.Add(s); triangles.Add(s + 3); triangles.Add(s + 2);
            }
        }

        public Mesh ToMesh(string meshName)
        {
            Mesh mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

/// <summary>Cache les objets lointains de la végétation pour garder une bonne fluidité.</summary>
public class VegetationDistanceCuller : MonoBehaviour
{
    public Vector3 center;
    public float chunkRadius;
    public float distance;

    Renderer targetRenderer;
    Transform cameraTransform;
    float timer;

    void Start()
    {
        targetRenderer = GetComponent<Renderer>();
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f || targetRenderer == null) return;
        timer = 0.3f;

        if (cameraTransform == null)
        {
            Camera main = Camera.main;
            if (main == null) return;
            cameraTransform = main.transform;
        }

        Vector3 cam = cameraTransform.position;
        float dx = cam.x - center.x;
        float dz = cam.z - center.z;
        float limit = distance + chunkRadius;
        targetRenderer.enabled = dx * dx + dz * dz < limit * limit;
    }
}
