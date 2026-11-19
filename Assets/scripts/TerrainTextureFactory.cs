using UnityEngine;

public enum SurfaceKind
{
    Steppe,       // terre sèche de steppe, cailloux et fissures
    SteppeDark,   // variante plus sombre (casse la répétition)
    Track,        // piste de terre compactée et gravier calcaire
    Asphalt,      // asphalte usé : granulats, fissures, taches
    Limestone     // roche calcaire à strates
}

/// <summary>
/// Fabrique des textures qui se répètent sans couture (albédo + normal map),
/// pour remplacer les motifs en spirale. Si tu as de vraies textures PBR, branche-les dans Route : elles sont prioritaires.
/// </summary>
public static class TerrainTextureFactory
{
    const int Size = 512;

    public static void Create(SurfaceKind kind, int seed, out Texture2D albedo, out Texture2D normal)
    {
        int n = Size;
        var pixels = new Color32[n * n];
        var heights = new float[n * n];

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                float v = (y + 0.5f) / n;
                Color c;
                float h;
                switch (kind)
                {
                    case SurfaceKind.Steppe: c = Steppe(u, v, seed, false, out h); break;
                    case SurfaceKind.SteppeDark: c = Steppe(u, v, seed, true, out h); break;
                    case SurfaceKind.Track: c = Track(u, v, seed, out h); break;
                    case SurfaceKind.Asphalt: c = Asphalt(u, v, seed, out h); break;
                    default: c = Limestone(u, v, seed, out h); break;
                }
                pixels[y * n + x] = new Color32(ToByte(c.r), ToByte(c.g), ToByte(c.b), 255);
                heights[y * n + x] = h;
            }
        }

        albedo = new Texture2D(n, n, TextureFormat.RGBA32, true);
        albedo.wrapMode = TextureWrapMode.Repeat;
        albedo.filterMode = FilterMode.Trilinear;
        albedo.anisoLevel = 8;
        albedo.SetPixels32(pixels);
        albedo.Apply(true);

        float strength = kind == SurfaceKind.Track ? 3.4f
                       : kind == SurfaceKind.Limestone ? 4.2f
                       : kind == SurfaceKind.Asphalt ? 1.3f : 2.2f;
        var normalPixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            int yUp = (y + 1) % n, yDown = (y - 1 + n) % n;
            for (int x = 0; x < n; x++)
            {
                int xRight = (x + 1) % n, xLeft = (x - 1 + n) % n;
                float hx = heights[y * n + xRight] - heights[y * n + xLeft];
                float hy = heights[yUp * n + x] - heights[yDown * n + x];
                Vector3 nrm = new Vector3(-hx * strength, -hy * strength, 1f).normalized;
                normalPixels[y * n + x] = new Color32(
                    ToByte(nrm.x * 0.5f + 0.5f), ToByte(nrm.y * 0.5f + 0.5f), ToByte(nrm.z * 0.5f + 0.5f), 255);
            }
        }

        normal = new Texture2D(n, n, TextureFormat.RGBA32, true, true);
        normal.wrapMode = TextureWrapMode.Repeat;
        normal.filterMode = FilterMode.Trilinear;
        normal.anisoLevel = 8;
        normal.SetPixels32(normalPixels);
        normal.Apply(true);
    }

    // ---------- Surfaces ----------

    static Color Steppe(float u, float v, int seed, bool dark, out float h)
    {
        float macro = Fbm(u, v, 3, 4, seed);
        float blot = Fbm(u, v, 10, 3, seed + 5);
        float grit = ValueNoise(u * 160f, v * 160f, 160, 160, seed + 9);

        Color a = dark ? new Color(0.52f, 0.44f, 0.33f) : new Color(0.62f, 0.56f, 0.44f);
        Color b = dark ? new Color(0.42f, 0.34f, 0.26f) : new Color(0.52f, 0.46f, 0.36f);
        Color c = Color.Lerp(a, b, Mathf.SmoothStep(0.3f, 0.7f, macro));
        c = Scale(c, 0.88f + blot * 0.24f + (grit - 0.5f) * 0.14f);
        h = macro * 0.25f + grit * 0.05f;

        // Cailloux
        Worley(u, v, 36, seed + 3, out float f1, out float f2, out uint id);
        float rnd = (id & 0xFF) / 255f;
        float pebbleRadius = 0.14f + ((id >> 8) & 0xFF) / 255f * 0.12f;
        if (rnd > 0.55f && f1 < pebbleRadius)
        {
            float t = 1f - f1 / pebbleRadius;
            float tone = 0.55f + ((id >> 16) & 0xFF) / 255f * 0.35f;
            Color pebble = new Color(tone, tone * 0.96f, tone * 0.86f);
            c = Color.Lerp(c, pebble, Mathf.SmoothStep(0f, 0.35f, t));
            h += t * 0.6f;
        }

        // Fissures de terre sèche
        Worley(u, v, 8, seed + 11, out float g1, out float g2, out uint gid);
        float crack = 1f - Mathf.SmoothStep(0f, 0.05f, g2 - g1);
        float dry = Mathf.SmoothStep(0.45f, 0.75f, blot);
        c = Scale(c, 1f - crack * 0.35f * dry);
        h -= crack * 0.5f * dry;
        return c;
    }

    static Color Track(float u, float v, int seed, out float h)
    {
        Worley(u, v, 56, seed, out float f1, out float f2, out uint id);
        float r1 = (id & 0xFF) / 255f;
        float r2 = ((id >> 8) & 0xFF) / 255f;

        Color[] palette =
        {
            new Color(0.74f, 0.70f, 0.60f), new Color(0.62f, 0.57f, 0.48f),
            new Color(0.80f, 0.77f, 0.70f), new Color(0.55f, 0.50f, 0.42f),
            new Color(0.68f, 0.62f, 0.52f)
        };
        Color stone = Scale(palette[(int)(r1 * palette.Length) % palette.Length], 0.85f + r2 * 0.3f);

        float fine = ValueNoise(u * 200f, v * 200f, 200, 200, seed + 4);
        float macro = Fbm(u, v, 4, 4, seed + 2);
        Color earth = Scale(new Color(0.50f, 0.44f, 0.35f), 0.85f + fine * 0.3f);

        float stoneRadius = 0.62f;
        float inside = 1f - Mathf.SmoothStep(stoneRadius * 0.8f, stoneRadius, f1);
        Color c = Color.Lerp(earth, stone, inside);
        c = Scale(c, 0.85f + macro * 0.3f);

        float dome = Mathf.Sqrt(Mathf.Clamp01(1f - (f1 / stoneRadius) * (f1 / stoneRadius)));
        h = inside * dome * 0.8f + fine * 0.05f;
        return c;
    }

    static Color Asphalt(float u, float v, int seed, out float h)
    {
        float macro = Fbm(u, v, 4, 3, seed);
        float grain = ValueNoise(u * 220f, v * 220f, 220, 220, seed + 1);
        Worley(u, v, 128, seed + 2, out float f1, out float f2, out uint id);
        float speck = ((id & 0xFF) / 255f - 0.5f);

        // Bitume gris usé et poussiéreux, sans noir brillant uniforme.
        float value = 0.235f + macro * 0.065f + (grain - 0.5f) * 0.09f;
        if (f1 < 0.18f) value += speck * 0.13f;

        Worley(u, v, 5, seed + 7, out float g1, out float g2, out uint gid);
        float crack = 1f - Mathf.SmoothStep(0f, 0.025f, g2 - g1);
        float stain = Mathf.SmoothStep(0.62f, 0.8f, Fbm(u, v, 6, 3, seed + 9));
        value *= 1f - crack * 0.42f;
        value *= 1f - stain * 0.18f;

        h = grain * 0.2f + (f1 < 0.18f ? 0.15f : 0f) - crack * 0.6f;
        return new Color(value, value * 0.99f, value * 0.97f);
    }

    static Color Limestone(float u, float v, int seed, out float h)
    {
        float strata = ValueNoise(u * 4f, v * 48f, 4, 48, seed);
        float macro = Fbm(u, v, 3, 4, seed + 3);
        float pit = ValueNoise(u * 90f, v * 90f, 90, 90, seed + 4);

        Color warm = new Color(0.74f, 0.68f, 0.57f);
        Color gray = new Color(0.62f, 0.61f, 0.57f);
        Color c = Scale(Color.Lerp(gray, warm, macro), 0.85f + strata * 0.22f + (pit - 0.5f) * 0.12f);

        Worley(u, v, 6, seed + 6, out float g1, out float g2, out uint gid);
        float crack = 1f - Mathf.SmoothStep(0f, 0.04f, g2 - g1);
        c = Scale(c, 1f - crack * 0.5f);

        h = strata * 0.35f + pit * 0.15f - crack * 0.8f + macro * 0.2f;
        return c;
    }

    // ---------- Bruits qui se répètent sans couture ----------

    static float ValueNoise(float x, float y, int periodX, int periodY, int seed)
    {
        int xi = Mathf.FloorToInt(x);
        int yi = Mathf.FloorToInt(y);
        float fx = x - xi;
        float fy = y - yi;
        float sx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
        float sy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
        int x0 = Wrap(xi, periodX), x1 = Wrap(xi + 1, periodX);
        int y0 = Wrap(yi, periodY), y1 = Wrap(yi + 1, periodY);
        float a = Hash01(x0, y0, seed);
        float b = Hash01(x1, y0, seed);
        float c = Hash01(x0, y1, seed);
        float d = Hash01(x1, y1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
    }

    static float Fbm(float u, float v, int period, int octaves, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        int p = period;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise(u * p, v * p, p, p, seed + i * 17);
            norm += amp;
            amp *= 0.5f;
            p *= 2;
        }
        return sum / norm;
    }

    static void Worley(float u, float v, int cells, int seed, out float f1, out float f2, out uint id)
    {
        float x = u * cells;
        float y = v * cells;
        int ix = Mathf.FloorToInt(x);
        int iy = Mathf.FloorToInt(y);
        float fx = x - ix;
        float fy = y - iy;
        f1 = 9f; f2 = 9f; id = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = Wrap(ix + dx, cells);
                int cy = Wrap(iy + dy, cells);
                uint hash = HashU(cx, cy, seed);
                float jx = (hash & 0xFFFF) / 65536f;
                float jy = ((hash >> 16) & 0xFFFF) / 65536f;
                float ddx = dx + jx - fx;
                float ddy = dy + jy - fy;
                float dist = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                if (dist < f1) { f2 = f1; f1 = dist; id = hash; }
                else if (dist < f2) f2 = dist;
            }
        }
    }

    static int Wrap(int i, int period)
    {
        i %= period;
        return i < 0 ? i + period : i;
    }

    static uint HashU(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h;
        }
    }

    static float Hash01(int x, int y, int seed)
    {
        return (HashU(x, y, seed) & 0xFFFFFF) / 16777216f;
    }

    static Color Scale(Color c, float f)
    {
        return new Color(c.r * f, c.g * f, c.b * f, 1f);
    }

    static byte ToByte(float value)
    {
        return (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
    }
}
