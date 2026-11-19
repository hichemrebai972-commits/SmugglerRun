using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Crée un matériau de particules transparent avec une texture ronde et douce.
/// Corrige la poussière et la fumée qui s'affichent comme des carrés pleins.
/// Utilisation : renderer.material = SoftParticleMaterial.Create();
/// </summary>
public static class SoftParticleMaterial
{
    static Texture2D softTexture;

    public static Material Create()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
        {
            Debug.LogError("SoftParticleMaterial : aucun shader transparent n'est disponible.");
            return null;
        }

        Material material = new Material(shader);
        Texture2D texture = GetTexture();

        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        else material.mainTexture = texture;

        // Mode transparent (sans ça, les particules sont opaques et on voit des carrés)
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;

        return material;
    }

    static Texture2D GetTexture()
    {
        if (softTexture != null) return softTexture;

        const int size = 64;
        softTexture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        softTexture.wrapMode = TextureWrapMode.Clamp;

        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - d);
                alpha = alpha * alpha * (3f - 2f * alpha); // bord très doux
                softTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        softTexture.Apply(true);
        return softTexture;
    }
}
