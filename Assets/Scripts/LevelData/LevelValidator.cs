using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ChestPush.LevelData
{
    public sealed class ValidationIssue
    {
        public string code;
        public string location;
        public string message;

        public ValidationIssue(string code, string location, string message)
        {
            this.code = code;
            this.location = location;
            this.message = message;
        }

        public override string ToString() => $"{location}: {message} ({code})";
    }

    public sealed class ValidationResult
    {
        public readonly List<ValidationIssue> issues = new List<ValidationIssue>();
        public bool IsValid => issues.Count == 0;
        public void Add(string code, string location, string message) => issues.Add(new ValidationIssue(code, location, message));
    }

    public static class LevelValidator
    {
        private static readonly Regex FileName = new Regex(@"^[a-z]+_[0-9]{2,}_[a-z0-9]+$", RegexOptions.Compiled);
        private static readonly Regex BlockId = new Regex(@"^[A-Z][A-Za-z0-9]*_[A-Z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)?$", RegexOptions.Compiled);

        public static ValidationResult ValidateCatalog(BlockCatalog catalog)
        {
            var result = new ValidationResult();
            if (catalog == null || catalog.entries == null)
            {
                result.Add("CatalogMissing", "catalog", "方块总表不存在");
                return result;
            }
            var ids = new HashSet<string>();
            var addresses = new HashSet<string>();
            foreach (var entry in catalog.entries)
            {
                if (entry == null) { result.Add("EntryNull", "catalog", "总表存在空项"); continue; }
                if (string.IsNullOrWhiteSpace(entry.id) || !BlockId.IsMatch(entry.id))
                    result.Add("BlockId", entry.id ?? "catalog", "方块 ID 格式无效");
                if (!ids.Add(entry.id ?? "")) result.Add("DuplicateBlockId", entry.id, "方块 ID 重复");
                string prefix = entry.category == BlockCategory.Structure ? "Wall_" : entry.category + "_";
                if (entry.id != null && !entry.id.StartsWith(prefix, StringComparison.Ordinal))
                    result.Add("BlockPrefix", entry.id, $"{entry.category} 类型的 ID 应以 {prefix} 开头");
                if (string.IsNullOrWhiteSpace(entry.address)) result.Add("AddressMissing", entry.id, "Addressables 地址为空");
                else if (!addresses.Add(entry.address)) result.Add("DuplicateAddress", entry.id, "Addressables 地址重复");
                if (entry.prefab == null || !entry.prefab.RuntimeKeyIsValid()) result.Add("PrefabMissing", entry.id, "Prefab 引用无效");
                if (entry.kind == BlockKind.Teleport && !entry.passable) result.Add("TeleportPassable", entry.id, "传送格必须可通行");
            }
            return result;
        }

        public static ValidationResult ValidateLevel(LevelDefinition level, BlockCatalog catalog, bool requirePlayable = true, string fileStem = null)
        {
            var result = new ValidationResult();
            if (level == null) { result.Add("LevelMissing", "level", "关卡数据为空"); return result; }
            if (level.schemaVersion != 1) result.Add("Version", "level", "不支持的关卡格式版本");
            if (!Guid.TryParseExact(level.levelId, "N", out _)) result.Add("LevelId", "level", "关卡 ID 必须是稳定 GUID");
            if (fileStem != null && !FileName.IsMatch(fileStem)) result.Add("FileName", fileStem, "文件名应为 难度_编号_特征");
            if (level.layers == null || level.layers.Count == 0) { result.Add("NoLayer", "layers", "至少需要一个地基层"); return result; }
            var layerIds = new HashSet<int>();
            var floorCells = new HashSet<GridPoint>();
            int? lastY = null;
            foreach (var layer in level.layers.OrderBy(item => item == null ? int.MinValue : item.baseY))
            {
                if (layer == null) { result.Add("LayerNull", "layers", "存在空地基层"); continue; }
                if (!layerIds.Add(layer.id)) result.Add("LayerId", $"layer:{layer.id}", "地基层 ID 重复");
                if (lastY.HasValue && layer.baseY - lastY.Value < 2) result.Add("LayerHeight", $"layer:{layer.id}", "与前一层的高度差必须至少为 2");
                lastY = layer.baseY;
                if (layer.floors == null) { result.Add("FloorsNull", $"layer:{layer.id}", "地基列表为空"); continue; }
                foreach (var floor in layer.floors)
                {
                    if (floor == null) { result.Add("FloorNull", $"layer:{layer.id}", "存在空地基记录"); continue; }
                    var point = new GridPoint(layer.id, floor.x, floor.z);
                    if (!floorCells.Add(point)) result.Add("DuplicateFloor", point.ToString(), "地基格重复");
                    CheckBlock(result, catalog, floor.blockId, BlockCategory.Floor, point.ToString());
                }
            }

            var instances = new HashSet<string>();
            var structures = new HashSet<GridPoint>();
            var boxes = new HashSet<GridPoint>();
            var targets = new HashSet<GridPoint>();
            var spawnPoints = new List<GridPoint>();
            var pressureIds = new HashSet<string>();
            var walls = new List<Placement>();
            var teleports = new List<Placement>();
            int spawnCount = 0;
            int boxCount = 0;
            foreach (var placement in level.placements ?? new List<Placement>())
            {
                if (placement == null) { result.Add("PlacementNull", "placements", "存在空放置记录"); continue; }
                var point = placement.Point;
                string location = $"{point}/{placement.instanceId}";
                if (!Guid.TryParseExact(placement.instanceId, "N", out _) || !instances.Add(placement.instanceId))
                    result.Add("InstanceId", location, "实例 ID 无效或重复");
                if (!layerIds.Contains(point.layerId)) result.Add("MissingLayer", location, "引用的地基层不存在");
                if (!floorCells.Contains(point)) result.Add("MissingFloor", location, "此格没有地基");
                BlockCategory category = placement.slot == PlacementSlot.Structure ? BlockCategory.Structure :
                    placement.slot == PlacementSlot.Marker ? BlockCategory.Marker : BlockCategory.Box;
                if (placement.slot == PlacementSlot.Floor) result.Add("FloorSlot", location, "地基应写在 layers 中");
                var entry = CheckBlock(result, catalog, placement.blockId, category, location);
                if (placement.slot == PlacementSlot.Structure && !structures.Add(point)) result.Add("StructureOverlap", location, "格子内容重复");
                if (placement.slot == PlacementSlot.Box) { boxCount++; if (!boxes.Add(point)) result.Add("BoxOverlap", location, "箱子重复"); }
                if (entry == null) continue;
                if (entry.kind == BlockKind.Spawn) { spawnCount++; spawnPoints.Add(point); }
                if (entry.kind == BlockKind.Target && !targets.Add(point)) result.Add("TargetOverlap", location, "目标点重复");
                if (entry.kind == BlockKind.PressurePlate)
                {
                    if (string.IsNullOrWhiteSpace(placement.switchId) || !pressureIds.Add(placement.switchId))
                        result.Add("SwitchId", location, "压板关联 ID 为空或重复");
                }
                if (entry.kind == BlockKind.Breakable) walls.Add(placement);
                if (entry.kind == BlockKind.Teleport) teleports.Add(placement);
            }
            if (level.placements == null) result.Add("PlacementsNull", "placements", "放置列表为空");
            foreach (var box in boxes)
                if (IsBlockedStructure(box)) result.Add("BoxBlocked", box.ToString(), "箱子不能与不可通行的墙体重叠");
            foreach (var spawn in spawnPoints)
            {
                if (IsBlockedStructure(spawn)) result.Add("SpawnBlocked", spawn.ToString(), "出生点不能位于不可通行的墙体中");
                if (boxes.Contains(spawn)) result.Add("SpawnBoxOverlap", spawn.ToString(), "出生点不能与箱子重叠");
            }
            foreach (var target in targets)
            {
                if (boxes.Contains(target)) result.Add("TargetBoxOverlap", target.ToString(), "目标点不能与箱子同格");
                if (IsBlockedStructure(target)) result.Add("TargetBlocked", target.ToString(), "目标点不能位于不可通行的墙体中");
            }
            foreach (var wall in walls)
            {
                if (wall.breakModes == BreakMode.None) result.Add("BreakMode", wall.Point.ToString(), "可破坏墙未配置破坏方式");
                if ((wall.breakModes & BreakMode.Switch) == 0) continue;
                if (wall.switchIds == null || wall.switchIds.Count == 0 || wall.switchIds.Any(id => !pressureIds.Contains(id)))
                    result.Add("SwitchLink", wall.Point.ToString(), "墙体引用不存在的压板");
            }
            foreach (var teleport in teleports)
            {
                if (teleport.allowedActors == ActorMask.None) result.Add("TeleportActors", teleport.Point.ToString(), "传送格没有允许的对象");
                if ((requirePlayable || teleport.destination.HasValue) && (!teleport.destination.HasValue || !floorCells.Contains(teleport.destination.Value)))
                    result.Add("TeleportDestination", teleport.Point.ToString(), "传送出口没有地基");
                else if (teleport.destination.HasValue && IsBlockedStructure(teleport.destination.Value))
                    result.Add("TeleportBlocked", teleport.Point.ToString(), "传送出口被固定障碍占据");
            }
            if (requirePlayable)
            {
                if (spawnCount != 1) result.Add("SpawnCount", "level", "可玩关卡需要且仅需要一个出生点");
                if (boxCount == 0 || boxCount != targets.Count) result.Add("TargetCount", "level", "箱子与目标点数量必须相同且大于零");
            }
            return result;

            bool IsBlockedStructure(GridPoint point) => level.placements != null && level.placements.Any(p =>
                p != null && p.slot == PlacementSlot.Structure && p.Point.Equals(point) &&
                catalog != null && catalog.Find(p.blockId) != null && !catalog.Find(p.blockId).passable);
        }

        private static BlockEntry CheckBlock(ValidationResult result, BlockCatalog catalog, string id, BlockCategory expected, string location)
        {
            var entry = catalog == null ? null : catalog.Find(id);
            if (entry == null) result.Add("UnknownBlock", location, $"未知方块 ID: {id}");
            else if (entry.category != expected) result.Add("BlockCategory", location, $"{id} 不属于 {expected} 类别");
            return entry;
        }
    }

    public static class GraphValidator
    {
        public static ValidationResult Validate(LevelGraphDefinition graph, bool requirePlayable = true)
        {
            var result = new ValidationResult();
            if (graph == null) { result.Add("GraphMissing", "graph", "目录不存在"); return result; }
            if (graph.schemaVersion != 1) result.Add("Version", "graph", "不支持的目录格式版本");
            var nodes = new Dictionary<string, LevelGraphNode>();
            var orders = new HashSet<int>();
            foreach (var node in graph.nodes ?? new List<LevelGraphNode>())
            {
                if (node == null) { result.Add("NodeNull", "nodes", "存在空节点"); continue; }
                if (string.IsNullOrWhiteSpace(node.levelId) || nodes.ContainsKey(node.levelId)) result.Add("NodeId", node.levelId ?? "nodes", "关卡 ID 为空或重复");
                else nodes.Add(node.levelId, node);
                if (!orders.Add(node.selectOrder)) result.Add("SelectOrder", node.levelId, "选关顺序重复");
                if (string.IsNullOrWhiteSpace(node.address)) result.Add("Address", node.levelId, "关卡 Addressables 地址为空");
            }
            if (graph.nodes == null) result.Add("NodesNull", "nodes", "节点列表为空");
            var entries = graph.entryLevelIds ?? new List<string>();
            if (requirePlayable && entries.Count == 0) result.Add("EntryMissing", "graph", "需要至少一个入口关卡");
            foreach (var id in entries) if (!nodes.ContainsKey(id)) result.Add("EntryInvalid", id, "入口关卡不存在");
            foreach (var node in nodes.Values)
            {
                var edges = new HashSet<string>();
                foreach (var next in node.successors ?? new List<string>())
                {
                    if (!nodes.ContainsKey(next)) result.Add("SuccessorMissing", node.levelId, $"后继关卡 {next} 不存在");
                    if (!edges.Add(next)) result.Add("SuccessorDuplicate", node.levelId, "后继连线重复");
                    if (next == node.levelId) result.Add("SelfLoop", node.levelId, "关卡不能指向自身");
                }
            }
            var visited = new HashSet<string>();
            var active = new HashSet<string>();
            foreach (var id in nodes.Keys) Visit(id);
            void Visit(string id)
            {
                if (active.Contains(id)) { result.Add("Cycle", id, "关卡目录存在循环"); return; }
                if (!visited.Add(id)) return;
                active.Add(id);
                foreach (var next in nodes[id].successors ?? new List<string>()) if (nodes.ContainsKey(next)) Visit(next);
                active.Remove(id);
            }
            if (requirePlayable)
            {
                var reachable = new HashSet<string>();
                var queue = new Queue<string>(entries.Where(nodes.ContainsKey));
                while (queue.Count > 0)
                {
                    var id = queue.Dequeue();
                    if (!reachable.Add(id)) continue;
                    foreach (var next in nodes[id].successors ?? new List<string>()) if (nodes.ContainsKey(next)) queue.Enqueue(next);
                }
                foreach (var id in nodes.Keys) if (!reachable.Contains(id)) result.Add("Unreachable", id, "关卡无法从入口到达");
            }
            return result;
        }
    }
}
