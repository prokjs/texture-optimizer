using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Prokjs.TextureOptimizer
{
/// <summary>Conservative editor-only policy without any VRChat SDK or world runtime dependency.</summary>
internal static class WorldOptimizerPolicy
{
    private static readonly Regex GraphicsWords = new Regex(@"(^|[^a-z])(sign(s|age)?|graphic(s)?|artwork|graffiti|mural(s)?|sticker(s)?|poster(s)?|flyer(s)?|decal(s)?|logo|label(s)?|lettering|text|menu(s)?|route_info|printed_paper|notice(s)?|advert(isement|ising)?)([^a-z]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool IsGraphics(Renderer renderer)
    {
        if (renderer == null) return false;
        if (renderer.GetComponent<TextMesh>() != null || renderer.GetComponentInParent<Canvas>(true) != null) return true;
        foreach (Component component in renderer.GetComponents<Component>())
            if (component != null && (component.GetType().Namespace ?? "").StartsWith("TMPro", StringComparison.Ordinal)) return true;
        if (GraphicsWords.IsMatch(renderer.name)) return true;
        for (Transform t = renderer.transform.parent; t != null; t = t.parent)
        {
            string name = t.name;
            if (GraphicsWords.IsMatch(name) && name.IndexOf("Mount", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("Facade", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("Details", StringComparison.OrdinalIgnoreCase) < 0) return true;
        }
        foreach (Material material in renderer.sharedMaterials)
        {
            if (material == null) continue;
            if (GraphicsWords.IsMatch(material.name)) return true;
            string shader = material.shader == null ? "" : material.shader.name;
            if (shader.StartsWith("TextMeshPro/", StringComparison.OrdinalIgnoreCase) || shader.IndexOf("GUI/Text", StringComparison.OrdinalIgnoreCase) >= 0 || shader.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (material.HasProperty("_MainTex") && material.mainTexture != null && GraphicsWords.IsMatch(material.mainTexture.name)) return true;
        }
        return false;
    }

    internal static bool IsGenerated(Renderer renderer)
    {
        if (renderer == null) return false;
        for (Transform t = renderer.transform; t != null; t = t.parent)
            if (t.name.StartsWith("LodOptimizer_LOD", StringComparison.Ordinal)) return true;
        var filter = renderer.GetComponent<MeshFilter>();
        string path = filter == null || filter.sharedMesh == null ? "" : AssetDatabase.GetAssetPath(filter.sharedMesh);
        return path.StartsWith("Assets/prokjs/WorldOptimizerGenerated/", StringComparison.Ordinal);
    }

    internal static bool HasDynamicAncestor(Transform transform)
    {
        for (Transform t = transform; t != null; t = t.parent)
        {
            if (t.GetComponent<Rigidbody>() != null || t.GetComponent<CharacterController>() != null || t.GetComponent<Animator>() != null || t.GetComponent<Animation>() != null || t.GetComponent<Cloth>() != null || t.GetComponent<SkinnedMeshRenderer>() != null || t.GetComponent<ParticleSystem>() != null) return true;
            foreach (Component component in t.GetComponents<Component>())
            {
                if (component == null) return true;
                for (Type type = component.GetType(); type != null; type = type.BaseType)
                {
                    string name = type.FullName ?? "";
                    if (type.Name == "UdonSharpBehaviour" || name.StartsWith("VRC.Udon.", StringComparison.Ordinal) || name.StartsWith("VRC.SDK", StringComparison.Ordinal) && (type.Name == "VRC_Pickup" || type.Name == "VRCPickup" || type.Name == "VRCObjectSync" || type.Name == "VRCStation")) return true;
                }
            }
        }
        return false;
    }
}
}
