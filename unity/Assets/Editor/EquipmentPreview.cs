using System;
using System.IO;
using System.Linq;
using RiftWard.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RiftWard.Editor
{
    /// Reproducible contact sheet for every weapon/gear rarity, plus geometry/animation-anchor checks.
    public static class EquipmentPreview
    {
        [MenuItem("RiftWard/Render Equipment Catalog")]
        public static void RenderCatalog()
        {
            string[] items = { "pistol", "rifle", "shotgun", "sniper", "saber", "armor", "module", "boots", "chip" };
            const int tile = 256;
            var scene = EditorSceneManager.NewPreviewScene();
            var stage = new GameObject("Equipment inspection stage");
            SceneManager.MoveGameObjectToScene(stage, scene);
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.scene = scene;
            camera.transform.position = new Vector3(0, 0.55f, -3.8f);
            camera.transform.LookAt(Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.065f);
            camera.fieldOfView = 32;
            camera.enabled = false;
            var rt = new RenderTexture(tile, tile, 24) { antiAliasing = 4 };
            camera.targetTexture = rt;
            var sheet = new Texture2D(tile * items.Length, tile * 5, TextureFormat.RGB24, false);
            var sample = new Texture2D(tile, tile, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            foreach (var spec in new[] { (new Vector3(-2, 3, -3), 4f, new Color(1, 0.92f, 0.8f)), (new Vector3(2, 1, -2), 2.5f, new Color(0.65f, 0.8f, 1)), (new Vector3(0, 2, 2), 4f, Color.white) })
            {
                var light = new GameObject("Studio light").AddComponent<Light>();
                light.transform.SetParent(stage.transform, false);
                light.transform.position = spec.Item1;
                light.type = LightType.Point; light.range = 10; light.intensity = spec.Item2; light.color = spec.Item3;
                light.renderMode = LightRenderMode.ForcePixel;
            }
            int validated = 0;
            try
            {
                for (int rarity = 0; rarity < 5; rarity++)
                    for (int item = 0; item < items.Length; item++)
                    {
                        var root = new GameObject(items[item]); root.transform.SetParent(stage.transform, false);
                        if (item < 5) Models.WeaponDisplay(root.transform, items[item], rarity);
                        else Models.GearModel(root.transform, items[item], rarity);
                        var filters = root.GetComponentsInChildren<MeshFilter>();
                        foreach (var filter in filters)
                        {
                            var mesh = filter.sharedMesh;
                            if (mesh == null || mesh.vertexCount == 0) throw new Exception("Missing geometry: " + items[item]);
                            if (mesh.name != "Equipment chamfer") continue;
                            var vertices = mesh.vertices; var indices = mesh.triangles;
                            if (indices.Length != 132) throw new Exception("Unexpected chamfer topology");
                            for (int n = 0; n < indices.Length; n += 3)
                            {
                                Vector3 a = vertices[indices[n]], b = vertices[indices[n + 1]], c = vertices[indices[n + 2]];
                                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) <= 0) throw new Exception("Inverted chamfer face");
                            }
                        }
                        if (item < 4 && root.GetComponentsInChildren<Transform>().All(t => t.name != "Muzzle")) throw new Exception("Missing muzzle");
                        if (item == 0 && root.GetComponentsInChildren<Transform>().All(t => t.name != "Slide")) throw new Exception("Missing slide pivot");
                        if (item < 4 && root.GetComponentsInChildren<Transform>().All(t => t.name != "Mag")) throw new Exception("Missing magazine pivot");
                        if ((item == 1 || item == 3) && root.GetComponentsInChildren<Transform>().All(t => t.name != "Bolt")) throw new Exception("Missing bolt pivot");
                        if (item == 2 && root.GetComponentsInChildren<Transform>().All(t => t.name != "Pump")) throw new Exception("Missing pump pivot");
                        if (rarity >= 3 && root.GetComponentsInChildren<Transform>().All(t => t.name != "EnergyHalo")) throw new Exception("Missing prestige finish");
                        root.transform.rotation = Quaternion.Euler(item == 8 ? 55 : 12, item < 5 ? 12 : 155, 0);
                        var renderers = root.GetComponentsInChildren<MeshRenderer>();
                        var bounds = renderers[0].bounds;
                        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                        float scale = 1.7f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                        root.transform.localScale = Vector3.one * scale;
                        root.transform.position = -bounds.center * scale;
                        camera.Render();
                        RenderTexture.active = rt;
                        sample.ReadPixels(new Rect(0, 0, tile, tile), 0, 0); sample.Apply();
                        sheet.SetPixels(item * tile, (4 - rarity) * tile, tile, tile, sample.GetPixels());
                        UnityEngine.Object.DestroyImmediate(root);
                        validated++;
                    }
                // First-person-only heat/ammo parts must still build at every quality and rarity.
                int quality = GameSettings.Quality;
                try
                {
                    for (int q = 0; q < 4; q++)
                        for (int rarity = 0; rarity < 5; rarity++)
                            for (int item = 0; item < 5; item++)
                            {
                                GameSettings.Quality = q;
                                var root = new GameObject("First-person check"); root.transform.SetParent(stage.transform, false);
                                var muzzle = Models.Viewmodel(items[item], root.transform, rarity, true, 30, 3, item < 4);
                                if (muzzle == null || float.IsNaN(muzzle.localPosition.z)) throw new Exception("Invalid muzzle");
                                if (item < 4 && root.transform.Find("Heat") == null) throw new Exception("Missing heat sleeve");
                                UnityEngine.Object.DestroyImmediate(root);
                            }
                }
                finally { GameSettings.Quality = quality; }
                sheet.Apply();
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/equipment-catalog.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                Debug.Log("EQUIPMENT_VALIDATED " + validated + " display models, 100 first-person variants; catalog=" + path);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(stage);
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(sample);
                UnityEngine.Object.DestroyImmediate(sheet);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
