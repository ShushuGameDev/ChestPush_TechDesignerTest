using System.Collections.Generic;
using System.Linq;

namespace ChestPush.LevelData
{
    public static class GraphProgression
    {
        public static bool IsUnlocked(LevelGraphDefinition graph, IEnumerable<string> completedIds, string levelId)
        {
            if (graph == null || graph.nodes == null) return false;
            var node = graph.nodes.Find(item => item.levelId == levelId);
            if (node == null) return false;
            var completed = new HashSet<string>(completedIds ?? Enumerable.Empty<string>());
            if (completed.Contains(levelId) || graph.entryLevelIds.Contains(levelId)) return true;
            var predecessors = graph.nodes.Where(other => other.successors != null && other.successors.Contains(levelId)).ToArray();
            if (predecessors.Length == 0) return false;
            return node.unlockMode == UnlockMode.All ? predecessors.All(parent => completed.Contains(parent.levelId)) :
                predecessors.Any(parent => completed.Contains(parent.levelId));
        }
    }
}
