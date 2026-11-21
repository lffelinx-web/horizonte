using UnityEditor;
using UnityEngine;

namespace Horizonte.EditorTools
{
    /// <summary>
    /// Import settings for the Blender FBX buildings in Resources/Buildings: real-world metres, Blender's axes converted,
    /// no cameras, lights or animation. Each import logs the model's size so it can be checked against its footprint
    /// (one tile is 8 x 8 m).
    /// </summary>
    public sealed class ModelImportRules : AssetPostprocessor
    {
        const string Folder = "/Modelos/";

        void OnPreprocessModel()
        {
            if (!assetPath.Contains(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.useFileScale = true;
            importer.globalScale = 1;
            importer.bakeAxisConversion = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.addCollider = false;
            importer.isReadable = true;
            importer.importBlendShapes = false;
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.Contains(Folder)) return;
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            Debug.Log($"Horizonte: {root.name} mide {bounds.size.x:0.0} x {bounds.size.z:0.0} m de planta y {bounds.size.y:0.0} m de alto; base en y = {bounds.min.y:0.00}, centro en ({bounds.center.x:0.0}, {bounds.center.z:0.0}).");
        }
    }
}
