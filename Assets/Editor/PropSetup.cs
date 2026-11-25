using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Alesa.Editor
{
    /// <summary>
    /// Imports the ammo box and corpse models, builds their materials and prefabs, and puts them in
    /// the scene: the real ammo models in place of the lost placeholder children, and the corpse with
    /// a minion feeding on it in room C.
    /// </summary>
    /// <remarks>
    /// The models arrive at roughly 1/100 scale (an ammo box measures 5 mm across as imported), so the
    /// importer scale is set here rather than by scaling the transform, which would otherwise have to
    /// be repeated at every use and breaks lightmapping and physics.
    /// </remarks>
    public static class PropSetup
    {
        private const string SilverFbx = "Assets/asset/ammo-box/source/ammobox.fbx";
        private const string NormalFbx = "Assets/asset/9mm-ammo-box/source/ammoBox.fbx";
        private const string BodyFbx = "Assets/asset/dead body/model/dead body 6.fbx";

        private const string PrefabDir = "Assets/_Project/Prefabs/Props";

        // Target longest dimension in metres, used to work the importer scale out from the raw bounds.
        private const float SilverTarget = 0.20f;
        private const float NormalTarget = 0.16f;
        private const float BodyTarget = 1.85f;

        // Room C, where the player meets their first minion. The spawn zone is centred (106.5, -2.5),
        // 11 x 10, so this sits off to one side of it rather than in the doorway.
        private static readonly Vector3 BodyPosition = new Vector3(109.0f, 24.62f, -5.0f);
        private static readonly Vector3 FeedPosition = new Vector3(109.0f, 24.62f, -3.6f);

        [MenuItem("ALESA/Wire Props (ammo models, corpse)")]
        public static void Wire()
        {
            var log = new StringBuilder();

            var silver = BuildAmmo(SilverFbx, SilverTarget, "SilverAmmoBox",
                "Assets/asset/ammo-box/textures/ammoboxColor.png",
                "Assets/asset/ammo-box/textures/ammoboxNormal.png",
                "Assets/asset/ammo-box/textures/ammoboxRoughness.png", null, log);

            var normal = BuildAmmo(NormalFbx, NormalTarget, "NineMilAmmoBox",
                "Assets/asset/9mm-ammo-box/textures/ammoBox_AmmoBox_BaseColor.png",
                "Assets/asset/9mm-ammo-box/textures/ammoBox_AmmoBox_Normal.png",
                "Assets/asset/9mm-ammo-box/textures/ammoBox_AmmoBox_Roughness.png",
                "Assets/asset/9mm-ammo-box/textures/ammoBox_AmmoBox_Metallic.png", log);

            var body = BuildAmmo(BodyFbx, BodyTarget, "DeadBody",
                "Assets/asset/dead body/texture/diff.png",
                "Assets/asset/dead body/texture/normal.png",
                "Assets/asset/dead body/texture/s.png", null, log);

            AssetDatabase.SaveAssets();

            PlaceAmmoModels(silver, normal, log);
            PlaceCorpse(body, log);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log(log.ToString());
        }

        // ---- import, material, prefab ---------------------------------------------------------------

        private static GameObject BuildAmmo(string fbx, float targetSize, string name,
            string albedo, string normalMap, string roughness, string metallic, StringBuilder log)
        {
            var importer = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (importer == null) { log.AppendLine("MISSING model: " + fbx); return null; }

            // Measure at the current scale, then set the scale that lands it at the target size.
            var probe = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (probe == null) { log.AppendLine("MISSING model asset: " + fbx); return null; }
            // World size, not mesh size. Both ammo FBXs carry a node with localScale 100 inside the
            // file, so their meshes measure 0.2 m while the object they sit on is 20 m across.
            float longest = 0f;
            foreach (var mf in probe.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Vector3 s = mf.sharedMesh.bounds.size;
                Vector3 k = mf.transform.lossyScale;
                s = new Vector3(s.x * Mathf.Abs(k.x), s.y * Mathf.Abs(k.y), s.z * Mathf.Abs(k.z));
                longest = Mathf.Max(longest, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
            }
            if (longest <= 0.00001f) { log.AppendLine("no mesh bounds on " + fbx); return null; }

            float scale = targetSize / longest * importer.globalScale;
            bool reimport = false;
            if (!Mathf.Approximately(importer.globalScale, scale)) { importer.globalScale = scale; reimport = true; }
            if (importer.animationType != ModelImporterAnimationType.None)
            { importer.animationType = ModelImporterAnimationType.None; reimport = true; } // static props
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            { importer.materialImportMode = ModelImporterMaterialImportMode.None; reimport = true; }
            if (reimport) importer.SaveAndReimport();

            var material = BuildMaterial(name, albedo, normalMap, roughness, metallic, log);
            var prefab = BuildPrefab(fbx, name, material, log);
            log.AppendLine("  " + name + ": importer scale " + scale.ToString("F3")
                + " -> " + MeasureWorldSize(fbx).ToString("F2") + " m longest side");
            return prefab;
        }

        /// <summary>Longest world-space side of the imported model, read back after a reimport.</summary>
        private static float MeasureWorldSize(string fbx)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (go == null) return 0f;
            float longest = 0f;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Vector3 s = mf.sharedMesh.bounds.size;
                Vector3 k = mf.transform.lossyScale;
                s = new Vector3(s.x * Mathf.Abs(k.x), s.y * Mathf.Abs(k.y), s.z * Mathf.Abs(k.z));
                longest = Mathf.Max(longest, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
            }
            return longest;
        }

        private static Material BuildMaterial(string name, string albedo, string normalMap,
            string roughness, string metallic, StringBuilder log)
        {
            string path = PrefabDir + "/" + name + ".mat";
            EnsureFolder(PrefabDir);

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }

            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo);
            if (baseTex != null) material.SetTexture("_MainTex", baseTex);
            else log.AppendLine("  MISSING albedo: " + albedo);

            // A normal map read as a colour texture produces the familiar flat blue, lit wrongly.
            var normalTex = LoadAsNormalMap(normalMap);
            if (normalTex != null) { material.SetTexture("_BumpMap", normalTex); material.EnableKeyword("_NORMALMAP"); }

            var metallicTex = metallic != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(metallic) : null;
            if (metallicTex != null)
            {
                material.SetTexture("_MetallicGlossMap", metallicTex);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }

            // Standard wants smoothness, and these packs ship roughness, which is its inverse. There is
            // no invert on a Standard slot, so the map is not used and a constant is set to match the
            // rest of the hospital's dressing instead.
            material.SetFloat("_Metallic", metallicTex != null ? 1f : (name == "DeadBody" ? 0f : 0.25f));
            material.SetFloat("_Glossiness", name == "DeadBody" ? 0.35f : 0.30f);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D LoadAsNormalMap(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            if (importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static GameObject BuildPrefab(string fbx, string name, Material material, StringBuilder log)
        {
            EnsureFolder(PrefabDir);
            string path = PrefabDir + "/" + name + ".prefab";

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (source == null) return null;

            // Wrapped in a fresh root so the prefab's own transform is identity. The ammo FBXs carry a
            // node with scale 100, and without this that 100 lands on the prefab root — where setting a
            // scene instance back to scale 1, as any sane placement does, shrinks the box to a millimetre.
            var instance = new GameObject(name);
            try
            {
                var model = Object.Instantiate(source);
                model.name = "Model";
                model.transform.SetParent(instance.transform, false);

                foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = material;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
                log.AppendLine("  prefab " + path);
                return saved;
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
        }

        // ---- scene ----------------------------------------------------------------------------------

        private static void PlaceAmmoModels(GameObject silver, GameObject normal, StringBuilder log)
        {
            int replaced = 0;
            foreach (var pickup in Object.FindObjectsByType<InteractablePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool isSilver = pickup.name.IndexOf("Silver", System.StringComparison.OrdinalIgnoreCase) >= 0;
                var model = isSilver ? silver : normal;
                if (model == null) continue;
                if (pickup.name.IndexOf("Ammo", System.StringComparison.OrdinalIgnoreCase) < 0 && !isSilver) continue;

                // Clear the old placeholder children, broken prefab links included.
                for (int i = pickup.transform.childCount - 1; i >= 0; i--)
                {
                    var child = pickup.transform.GetChild(i);
                    if (child.name.IndexOf("Ammo Model", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Undo.DestroyObjectImmediate(child.gameObject);
                }

                var placed = (GameObject)PrefabUtility.InstantiatePrefab(model, pickup.transform);
                placed.name = "Ammo Model";
                placed.transform.localPosition = Vector3.zero;
                placed.transform.localRotation = Quaternion.identity;
                placed.transform.localScale = Vector3.one;

                // The grey prototype cube underneath would still be drawn through the new model.
                var box = pickup.GetComponent<MeshRenderer>();
                if (box != null) box.enabled = false;

                replaced++;
                log.AppendLine("  " + pickup.name + " -> " + model.name);
            }
            log.Insert(0, "[Props] " + replaced + " ammo pickups re-modelled.\n");
        }

        private static void PlaceCorpse(GameObject bodyPrefab, StringBuilder log)
        {
            if (bodyPrefab == null) { log.AppendLine("[Props] no corpse prefab; skipped."); return; }

            var parent = GameObject.Find("Hospital Props (scripted)");
            if (parent == null) parent = new GameObject("Hospital Props (scripted)");

            var existing = parent.transform.Find("Dead body - room C");
            GameObject body;
            if (existing != null) body = existing.gameObject;
            else
            {
                body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab, parent.transform);
                body.name = "Dead body - room C";
            }
            body.transform.position = DropToFloor(BodyPosition);
            body.transform.rotation = Quaternion.Euler(0f, 35f, 0f);

            var spotTransform = parent.transform.Find("Feeding spot - room C");
            GameObject spot = spotTransform != null ? spotTransform.gameObject : new GameObject("Feeding spot - room C");
            spot.transform.SetParent(parent.transform, false);
            spot.transform.position = DropToFloor(FeedPosition);

            var feeding = spot.GetComponent<MinionFeedingSpot>();
            if (feeding == null) feeding = spot.AddComponent<MinionFeedingSpot>();
            var so = new SerializedObject(feeding);
            so.FindProperty("body").objectReferenceValue = body.transform;
            so.FindProperty("claimRadius").floatValue = 12f;
            so.FindProperty("disturbRadius").floatValue = 8f;
            so.FindProperty("disturbSightRadius").floatValue = 16f;
            so.FindProperty("biteInterval").floatValue = 4.3f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feeding);

            log.AppendLine("[Props] corpse at " + body.transform.position.ToString("F2")
                + ", minion feeds at " + spot.transform.position.ToString("F2"));
        }

        /// <summary>Puts a prop on the floor rather than at the nominal height the coordinates assume.</summary>
        private static Vector3 DropToFloor(Vector3 wanted)
        {
            RaycastHit hit;
            if (Physics.Raycast(wanted + Vector3.up * 2f, Vector3.down, out hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point;
            UnityEngine.AI.NavMeshHit navHit;
            if (UnityEngine.AI.NavMesh.SamplePosition(wanted, out navHit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                return navHit.position;
            return wanted;
        }
    }
}
