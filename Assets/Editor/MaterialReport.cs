using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Horizonte.EditorTools
{
    /// <summary>Lists what Unity imported for each model's materials: colour, textures and sub-assets. For checking imports.</summary>
    public static class MaterialReport
    {
        public static void Run()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Modelos" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var subs = AssetDatabase.LoadAllAssetsAtPath(path);
                Debug.Log($"REPORTE {path}: {subs.OfType<Texture>().Count()} texturas dentro");
                foreach (var m in subs.OfType<Material>().Take(40))
                {
                    var map = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    var bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                    Debug.Log($"REPORTE   {m.name} shader={m.shader.name} color={(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.clear)} map={(map ? map.name + " " + map.width : "-")} bump={(bump ? bump.name : "-")}");
                }
            }
        }
    }
}
