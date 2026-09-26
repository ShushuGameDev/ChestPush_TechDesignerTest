using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ChestPush.GameFlow
{
    [Serializable]
    public sealed class ProgressData
    {
        public int schemaVersion = 1;
        public List<string> completedLevelIds = new List<string>();
        public string lastSelectedLevelId;
    }

    public static class ProgressStore
    {
        private static string PathName => Path.Combine(Application.persistentDataPath, "chestpush_progress.json");

        public static ProgressData Load()
        {
            try
            {
                if (!File.Exists(PathName)) return new ProgressData();
                var data = JsonUtility.FromJson<ProgressData>(File.ReadAllText(PathName));
                return data != null && data.schemaVersion == 1 && data.completedLevelIds != null ? data : new ProgressData();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Progress load failed: {exception.Message}");
                return new ProgressData();
            }
        }

        public static void Save(ProgressData data)
        {
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                var temporary = PathName + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                if (File.Exists(PathName)) File.Delete(PathName);
                File.Move(temporary, PathName);
            }
            catch (Exception exception) { Debug.LogError($"Progress save failed: {exception}"); }
        }
    }
}
