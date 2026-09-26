using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChestPush.LevelData;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace ChestPush.LevelEditor
{
    internal static class ProjectValidator
    {
        [MenuItem("Tools/ChestPush/Validate Project")]
        private static void ValidateFromMenu()
        {
            var issues = Validate(EditorSessionService.Session, true).issues;
            if (issues.Count == 0) UnityEngine.Debug.Log("ChestPush project data and Addressables references are valid.");
            else UnityEngine.Debug.LogError("ChestPush validation found " + issues.Count + " issue(s):\n" +
                string.Join("\n", issues.Select(issue => issue.ToString())));
        }

        public static ValidationResult Validate(EditorSession session, bool requirePlayable)
        {
            var result = new ValidationResult();
            var catalog = EditorSessionService.Catalog;
            result.issues.AddRange(LevelValidator.ValidateCatalog(catalog).issues);
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            CheckAddress(result, settings, LevelPaths.CatalogAssetPath, LevelPaths.CatalogAddress);
            CheckAddress(result, settings, LevelPaths.GraphAssetPath, LevelPaths.GraphAddress);
            CheckAddress(result, settings, LevelPaths.VictoryEffectAssetPath, LevelPaths.VictoryEffectAddress);
            if (catalog != null)
            {
                foreach (var entry in catalog.entries ?? new List<BlockEntry>())
                {
                    if (entry == null) continue;
                    string prefabPath = entry.prefab == null ? null : AssetDatabase.GUIDToAssetPath(entry.prefab.AssetGUID);
                    CheckAddress(result, settings, prefabPath, entry.address);
                }
                var player = catalog.Find("Player_Normal");
                if (player == null || player.category != BlockCategory.Player)
                    result.Add("PlayerPrefab", "catalog", "缺少 Player_Normal 玩家预制体");
            }

            LevelGraphDefinition graph = null;
            try { graph = LevelCodec.ReadGraph(session.graphJson); }
            catch (Exception exception) { result.Add("GraphJson", LevelPaths.GraphAssetPath, exception.Message); }
            if (graph != null) result.issues.AddRange(GraphValidator.Validate(graph, requirePlayable && session.levels.Count > 0).issues);

            var ids = new HashSet<string>();
            var addressById = new Dictionary<string, string>();
            foreach (var file in session.levels)
            {
                LevelDefinition level;
                try { level = LevelCodec.ReadLevel(file.json); }
                catch (Exception exception) { result.Add("LevelJson", file.path, exception.Message); continue; }
                result.issues.AddRange(LevelValidator.ValidateLevel(level, catalog, requirePlayable,
                    Path.GetFileNameWithoutExtension(file.path)).issues);
                if (!ids.Add(level.levelId)) result.Add("DuplicateLevelId", file.path, "稳定关卡 ID 重复");
                addressById[level.levelId] = LevelPaths.LevelAddressPrefix + Path.GetFileNameWithoutExtension(file.path);
                CheckAddress(result, settings, file.path, addressById[level.levelId]);
            }
            if (graph != null)
            {
                foreach (var node in graph.nodes ?? new List<LevelGraphNode>())
                {
                    if (node == null) continue;
                    if (!addressById.TryGetValue(node.levelId, out var address))
                        result.Add("LevelFileMissing", node.levelId, "目录节点没有对应的关卡文件");
                    else if (node.address != address)
                        result.Add("LevelAddress", node.levelId, "节点地址与关卡文件名不一致");
                }
                foreach (var id in addressById.Keys)
                    if (graph.nodes == null || graph.nodes.All(node => node == null || node.levelId != id))
                        result.Add("LevelNodeMissing", id, "关卡文件没有目录节点");
            }
            return result;
        }

        private static void CheckAddress(ValidationResult result, AddressableAssetSettings settings, string path, string expected)
        {
            if (string.IsNullOrEmpty(path)) { result.Add("AssetMissing", expected, "资源文件不存在"); return; }
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) { result.Add("AssetMissing", path, "资源文件不存在"); return; }
            var entry = settings == null ? null : settings.FindAssetEntry(guid);
            if (entry == null || entry.address != expected)
                result.Add("Addressable", path, $"Addressables 登记应为 {expected}");
        }
    }
}
