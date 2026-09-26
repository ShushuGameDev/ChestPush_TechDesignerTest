using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChestPush.LevelData;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace ChestPush.LevelEditor
{
    [Serializable]
    internal sealed class LevelFileRecord
    {
        public string path;
        [TextArea] public string json;
    }

    internal sealed class EditorSession : ScriptableObject
    {
        [TextArea] public string graphJson;
        public List<LevelFileRecord> levels = new List<LevelFileRecord>();
        public string selectedPath;
        public int selectedLayerId;
        public PlacementSlot selectedSlot = PlacementSlot.Floor;
        public string selectedBlockId = "Floor_Normal";
        public bool erase;
        public bool rectangleTool;
        public bool rectangleOutline;
    }

    [InitializeOnLoad]
    internal static class EditorSessionService
    {
        private static EditorSession session;
        public static event Action Changed;
        public static string LastError { get; private set; }
        public static DateTime? LastSavedAt { get; private set; }
        public static string SaveStatus => LastSavedAt.HasValue
            ? $"已自动保存到 JSON · {LastSavedAt.Value:HH:mm:ss}"
            : "已从 JSON 加载 · 修改后自动保存";

        static EditorSessionService() { Undo.undoRedoPerformed += OnUndoRedo; }

        public static EditorSession Session
        {
            get
            {
                if (session == null) Load();
                return session;
            }
        }

        public static BlockCatalog Catalog => AssetDatabase.LoadAssetAtPath<BlockCatalog>(LevelPaths.CatalogAssetPath);
        public static LevelGraphDefinition Graph => LevelCodec.ReadGraph(Session.graphJson);
        public static LevelDefinition CurrentLevel => CurrentFile == null ? null : LevelCodec.ReadLevel(CurrentFile.json);
        public static LevelFileRecord CurrentFile => Session.levels.Find(file => file.path == Session.selectedPath);

        private static void Load()
        {
            session = ScriptableObject.CreateInstance<EditorSession>();
            session.hideFlags = HideFlags.HideAndDontSave;
            session.graphJson = File.Exists(LevelPaths.GraphAssetPath) ? File.ReadAllText(LevelPaths.GraphAssetPath) : LevelCodec.WriteGraph(new LevelGraphDefinition());
            foreach (var path in Directory.GetFiles("Assets/Levels", "*.json").Select(path => path.Replace('\\', '/')))
            {
                if (path == LevelPaths.GraphAssetPath) continue;
                session.levels.Add(new LevelFileRecord { path = path, json = File.ReadAllText(path) });
            }
            if (session.levels.Count > 0) session.selectedPath = session.levels[0].path;
        }

        public static void Select(string path)
        {
            if (Session.levels.All(file => file.path != path)) return;
            LastError = null;
            Session.selectedPath = path;
            Session.selectedLayerId = CurrentLevel.layers[0].id;
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        public static bool SaveNow()
        {
            LastError = null;
            if (CurrentFile == null) { LastError = "当前没有正在编辑的关卡"; return false; }
            try
            {
                Sync();
                return true;
            }
            catch (Exception exception)
            {
                LastError = "保存失败：" + exception.Message;
                Debug.LogError(LastError);
                Changed?.Invoke();
                return false;
            }
        }

        public static void CloseLevel()
        {
            Session.selectedPath = null;
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        public static bool CreateLevel(string fileStem, Vector2 graphPosition)
        {
            LastError = null;
            if (string.IsNullOrWhiteSpace(fileStem)) { LastError = "文件名不能为空"; return false; }
            string path = $"Assets/Levels/{fileStem}.json";
            if (Session.levels.Any(file => file.path == path) || File.Exists(path)) { LastError = "文件已存在"; return false; }
            var level = new LevelDefinition();
            var levelCheck = LevelValidator.ValidateLevel(level, Catalog, false, fileStem);
            if (!levelCheck.IsValid) { LastError = levelCheck.issues[0].ToString(); return false; }
            var graph = Graph;
            var node = new LevelGraphNode
            {
                levelId = level.levelId, address = LevelPaths.LevelAddressPrefix + fileStem,
                selectOrder = graph.nodes.Count == 0 ? 0 : graph.nodes.Max(item => item.selectOrder) + 1,
                editorX = graphPosition.x, editorY = graphPosition.y
            };
            graph.nodes.Add(node);
            if (graph.entryLevelIds.Count == 0) graph.entryLevelIds.Add(level.levelId);
            Undo.RecordObject(Session, "Create Level");
            Session.levels.Add(new LevelFileRecord { path = path, json = LevelCodec.WriteLevel(level) });
            Session.graphJson = LevelCodec.WriteGraph(graph);
            Session.selectedPath = path;
            Sync();
            return true;
        }

        public static bool RenameLevel(string levelId, string newStem)
        {
            LastError = null;
            var file = FindById(levelId);
            if (file == null) { LastError = "关卡不存在"; return false; }
            string path = $"Assets/Levels/{newStem}.json";
            if (Session.levels.Any(item => item != file && item.path == path) ||
                (path != file.path && File.Exists(path))) { LastError = "文件名已存在"; return false; }
            var check = LevelValidator.ValidateLevel(LevelCodec.ReadLevel(file.json), Catalog, false, newStem);
            if (!check.IsValid) { LastError = check.issues[0].ToString(); Changed?.Invoke(); return false; }
            var graph = Graph;
            graph.nodes.Find(node => node.levelId == levelId).address = LevelPaths.LevelAddressPrefix + newStem;
            Undo.RecordObject(Session, "Rename Level");
            file.path = path;
            Session.selectedPath = path;
            Session.graphJson = LevelCodec.WriteGraph(graph);
            Sync();
            return true;
        }

        public static bool DeleteLevel(string levelId)
        {
            var file = FindById(levelId);
            if (file == null) return false;
            var graph = Graph;
            graph.nodes.RemoveAll(node => node.levelId == levelId);
            graph.entryLevelIds.RemoveAll(id => id == levelId);
            foreach (var node in graph.nodes) node.successors.RemoveAll(id => id == levelId);
            if (graph.entryLevelIds.Count == 0 && graph.nodes.Count > 0) graph.entryLevelIds.Add(graph.nodes[0].levelId);
            Undo.RecordObject(Session, "Delete Level");
            Session.levels.Remove(file);
            Session.selectedPath = Session.levels.Count > 0 ? Session.levels[0].path : null;
            Session.graphJson = LevelCodec.WriteGraph(graph);
            Sync();
            return true;
        }

        public static bool EditGraph(string undoLabel, Action<LevelGraphDefinition> edit)
        {
            LastError = null;
            var graph = Graph;
            edit(graph);
            var check = GraphValidator.Validate(graph, false);
            if (!check.IsValid) { LastError = check.issues[0].ToString(); Changed?.Invoke(); return false; }
            Undo.RecordObject(Session, undoLabel);
            Session.graphJson = LevelCodec.WriteGraph(graph);
            Sync();
            return true;
        }

        public static bool ConnectLevels(string sourceId, string targetId)
        {
            LastError = null;
            var graph = Graph;
            var source = graph.nodes.Find(item => item.levelId == sourceId);
            var target = graph.nodes.Find(item => item.levelId == targetId);
            if (source == null || target == null)
            {
                LastError = "连接的源节点或目标节点已不存在，请重新选择";
                return false;
            }
            if (sourceId == targetId)
            {
                LastError = "关卡不能连接到自身";
                return false;
            }
            if (source.successors != null && source.successors.Contains(targetId)) return true;
            return EditGraph("Connect Levels", current =>
            {
                var from = current.nodes.Find(item => item.levelId == sourceId);
                if (from.successors == null) from.successors = new List<string>();
                from.successors.Add(targetId);
            });
        }

        public static bool EditLevel(string undoLabel, Action<LevelDefinition> edit)
        {
            LastError = null;
            var file = CurrentFile;
            if (file == null) return false;
            var level = LevelCodec.ReadLevel(file.json);
            edit(level);
            var check = LevelValidator.ValidateLevel(level, Catalog, false, Path.GetFileNameWithoutExtension(file.path));
            if (!check.IsValid) { LastError = check.issues[0].ToString(); Changed?.Invoke(); return false; }
            Undo.RecordObject(Session, undoLabel);
            file.json = LevelCodec.WriteLevel(level);
            Sync();
            return true;
        }

        public static LevelFileRecord FindById(string levelId)
        {
            foreach (var file in Session.levels)
            {
                try { if (LevelCodec.ReadLevel(file.json).levelId == levelId) return file; }
                catch (Exception) { }
            }
            return null;
        }

        private static void OnUndoRedo()
        {
            if (session == null) return;
            Sync();
        }

        private static void Sync()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var wanted = new HashSet<string>(Session.levels.Select(file => file.path));
            foreach (var diskPath in Directory.GetFiles("Assets/Levels", "*.json").Select(path => path.Replace('\\', '/')))
            {
                if (diskPath == LevelPaths.GraphAssetPath || wanted.Contains(diskPath)) continue;
                BaseAssetsInstaller.Unregister(settings, diskPath);
                AssetDatabase.DeleteAsset(diskPath);
            }
            foreach (var file in Session.levels)
            {
                WriteIfChanged(file.path, file.json);
                AssetDatabase.ImportAsset(file.path);
                BaseAssetsInstaller.Register(settings, file.path, LevelPaths.LevelAddressPrefix + Path.GetFileNameWithoutExtension(file.path));
            }
            WriteIfChanged(LevelPaths.GraphAssetPath, Session.graphJson);
            AssetDatabase.ImportAsset(LevelPaths.GraphAssetPath);
            BaseAssetsInstaller.Register(settings, LevelPaths.GraphAssetPath, LevelPaths.GraphAddress);
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(Session);
            LastError = null;
            LastSavedAt = DateTime.Now;
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        private static void WriteIfChanged(string path, string value)
        {
            if (File.Exists(path) && File.ReadAllText(path) == value) return;
            var temp = path + ".tmp";
            File.WriteAllText(temp, value);
            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }

            try
            {
                File.Replace(temp, path, null);
            }
            catch (IOException)
            {
                // File.Replace can fail intermittently on Windows when Unity or another
                // process briefly holds the destination. Keep the already-written temp
                // file and fall back to an overwrite copy; if this also fails, Sync reports
                // the error and the temp file remains available for recovery.
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
        }
    }
}
