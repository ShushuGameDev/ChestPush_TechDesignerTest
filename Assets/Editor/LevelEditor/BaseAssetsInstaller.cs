using System.IO;
using ChestPush.LevelData;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace ChestPush.LevelEditor
{
    [InitializeOnLoad]
    public static class BaseAssetsInstaller
    {
        static BaseAssetsInstaller()
        {
            EditorApplication.delayCall += InstallWhenMissing;
        }

        [MenuItem("Tools/ChestPush/Create Base Assets")]
        public static void Install()
        {
            Directory.CreateDirectory("Assets/Data/Materials");
            Directory.CreateDirectory("Assets/Prefabs");
            Directory.CreateDirectory("Assets/Levels");
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null || settings.DefaultGroup == null)
            {
                Debug.LogError("Addressables settings could not be created.");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<BlockCatalog>(LevelPaths.CatalogAssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<BlockCatalog>();
                AssetDatabase.CreateAsset(catalog, LevelPaths.CatalogAssetPath);
            }
            Add(catalog, settings, "Floor_Normal", BlockCategory.Floor, BlockKind.Normal, true, new Color(0.42f, 0.49f, 0.58f), false);
            Add(catalog, settings, "Wall_Normal", BlockCategory.Structure, BlockKind.Normal, false, new Color(0.17f, 0.23f, 0.31f), false);
            Add(catalog, settings, "Wall_Breakable", BlockCategory.Structure, BlockKind.Breakable, false, new Color(0.86f, 0.40f, 0.18f), false);
            Add(catalog, settings, "Wall_Teleport", BlockCategory.Structure, BlockKind.Teleport, true, new Color(0.16f, 0.70f, 0.84f), false);
            Add(catalog, settings, "Box_Normal", BlockCategory.Box, BlockKind.Normal, false, new Color(0.93f, 0.72f, 0.23f), false);
            Add(catalog, settings, "Marker_Spawn", BlockCategory.Marker, BlockKind.Spawn, true, new Color(0.26f, 0.78f, 0.43f), true);
            Add(catalog, settings, "Marker_Target", BlockCategory.Marker, BlockKind.Target, true, new Color(0.90f, 0.32f, 0.62f), true);
            Add(catalog, settings, "Marker_PressurePlate", BlockCategory.Marker, BlockKind.PressurePlate, true, new Color(0.78f, 0.18f, 0.25f), true);
            Add(catalog, settings, "Player_Normal", BlockCategory.Player, BlockKind.Normal, false, new Color(0.93f, 0.96f, 1f), false);
            EditorUtility.SetDirty(catalog);
            Register(settings, LevelPaths.CatalogAssetPath, LevelPaths.CatalogAddress);

            if (!File.Exists(LevelPaths.GraphAssetPath)) File.WriteAllText(LevelPaths.GraphAssetPath, LevelCodec.WriteGraph(new LevelGraphDefinition()));
            AssetDatabase.ImportAsset(LevelPaths.GraphAssetPath);
            Register(settings, LevelPaths.GraphAssetPath, LevelPaths.GraphAddress);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("ChestPush base assets and Addressables entries are ready.");
        }

        private static void InstallWhenMissing()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<BlockCatalog>(LevelPaths.CatalogAssetPath) == null) Install();
        }

        private static void Add(BlockCatalog catalog, AddressableAssetSettings settings, string id, BlockCategory category,
            BlockKind kind, bool passable, Color color, bool marker)
        {
            string materialPath = $"Assets/Data/Materials/{id}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { color = color };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            string prefabPath = $"Assets/Prefabs/{id}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = id;
                if (marker) cube.transform.localScale = new Vector3(0.82f, 0.08f, 0.82f);
                cube.GetComponent<MeshRenderer>().sharedMaterial = material;
                if (kind == BlockKind.Teleport || marker) cube.GetComponent<BoxCollider>().isTrigger = true;
                PrefabUtility.SaveAsPrefabAsset(cube, prefabPath);
                Object.DestroyImmediate(cube);
            }
            string address = $"ChestPush/Blocks/{id}";
            Register(settings, prefabPath, address);
            var existing = catalog.Find(id);
            if (existing == null)
            {
                var guid = AssetDatabase.AssetPathToGUID(prefabPath);
                catalog.entries.Add(new BlockEntry
                {
                    id = id, category = category, kind = kind, passable = passable,
                    address = address, prefab = new UnityEngine.AddressableAssets.AssetReferenceGameObject(guid)
                });
            }
        }

        internal static void Register(AddressableAssetSettings settings, string path, string address)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return;
            var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            entry.address = address;
            EditorUtility.SetDirty(settings);
        }

        internal static void Unregister(AddressableAssetSettings settings, string path)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return;
            settings.RemoveAssetEntry(guid);
            EditorUtility.SetDirty(settings);
        }
    }
}
