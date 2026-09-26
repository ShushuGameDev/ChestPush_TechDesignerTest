using System;
using System.Collections.Generic;
using System.Linq;
using ChestPush.LevelData;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChestPush.LevelEditor
{
    [EditorTool("ChestPush Level Paint")]
    public sealed class LevelPaintTool : EditorTool
    {
        private const int MaxRectangleCells = 4096;

        private readonly struct PreviewBlock
        {
            public readonly GridPoint point;
            public readonly int baseY;
            public readonly PlacementSlot slot;
            public readonly BlockEntry entry;
            public readonly bool active;

            public PreviewBlock(GridPoint point, int baseY, PlacementSlot slot, BlockEntry entry, bool active)
            {
                this.point = point;
                this.baseY = baseY;
                this.slot = slot;
                this.entry = entry;
                this.active = active;
            }

            public Vector3 Center => new Vector3(point.x + 0.5f,
                baseY + (slot == PlacementSlot.Floor ? 0.5f : slot == PlacementSlot.Marker ? 1.04f : 1.5f),
                point.z + 0.5f);
        }

        public static string SelectedPlacementId { get; private set; }
        public static event Action SelectionChanged;
        private static GUIContent icon;
        private bool pointerInSceneView;
        private bool rectangleDragging;
        private GridPoint rectangleStart;
        private GridPoint rectangleEnd;

        public override GUIContent toolbarIcon => icon ?? (icon = new GUIContent("CP", "ChestPush Level Paint"));

        public static void NotifySelectionChanged() => SelectionChanged?.Invoke();

        internal static bool CanUseRectangle(EditorSession session) => session.erase ||
            session.selectedSlot == PlacementSlot.Floor || session.selectedSlot == PlacementSlot.Structure;

        public static bool SaveAndReturnToGraph()
        {
            if (!EditorSessionService.SaveNow()) return false;
            SelectedPlacementId = null;
            EditorSessionService.CloseLevel();
            ToolManager.RestorePreviousTool();
            LevelGraphWindow.OpenGraph();
            return true;
        }

        public static void FrameSelectedLevel()
        {
            var view = SceneView.lastActiveSceneView;
            var level = EditorSessionService.CurrentLevel;
            if (view == null || level == null) return;
            var layer = level.layers.Find(item => item.id == EditorSessionService.Session.selectedLayerId);
            if (layer == null) return;
            float minX = layer.floors.Count == 0 ? -4 : layer.floors.Min(cell => cell.x);
            float maxX = layer.floors.Count == 0 ? 4 : layer.floors.Max(cell => cell.x) + 1;
            float minZ = layer.floors.Count == 0 ? -4 : layer.floors.Min(cell => cell.z);
            float maxZ = layer.floors.Count == 0 ? 4 : layer.floors.Max(cell => cell.z) + 1;
            view.orthographic = true;
            view.rotation = Quaternion.Euler(45f, 45f, 0f);
            view.pivot = new Vector3((minX + maxX) * 0.5f, layer.baseY + 1f, (minZ + maxZ) * 0.5f);
            view.size = Mathf.Max(8f, maxX - minX, maxZ - minZ) * 1.3f;
            view.Focus();
            view.Repaint();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView)) return;
            if (sceneView.camera == null) return;
            var level = EditorSessionService.CurrentLevel;
            var catalog = EditorSessionService.Catalog;
            if (level == null || catalog == null) return;
            var layer = level.layers.Find(item => item.id == EditorSessionService.Session.selectedLayerId);
            if (layer == null) return;
            var current = Event.current;
            int controlId = GUIUtility.GetControlID("ChestPush.RectanglePaint".GetHashCode(), FocusType.Passive);
            if (current.type == EventType.KeyDown)
            {
                if (current.keyCode == KeyCode.Escape && rectangleDragging)
                {
                    CancelRectangleDrag();
                    current.Use();
                    sceneView.Repaint();
                    return;
                }
                if (current.keyCode == KeyCode.Q || current.keyCode == KeyCode.E)
                {
                    RotateCamera(sceneView, current.keyCode == KeyCode.Q ? -45f : 45f);
                    current.Use();
                    return;
                }
                if (current.keyCode == KeyCode.B)
                {
                    CancelRectangleDrag();
                    EditorSessionService.Session.rectangleTool = false;
                    LevelGraphWindow.RefreshPalette();
                    current.Use();
                    sceneView.Repaint();
                    return;
                }
                if (current.keyCode == KeyCode.R)
                {
                    if (CanUseRectangle(EditorSessionService.Session))
                    {
                        CancelRectangleDrag();
                        EditorSessionService.Session.rectangleTool = true;
                        LevelGraphWindow.RefreshPalette();
                    }
                    else sceneView.ShowNotification(new GUIContent("Rectangle paint supports Floor and Structure. Enable erase for other slots."));
                    current.Use();
                    sceneView.Repaint();
                    return;
                }
            }
            if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId);
            if (current.type == EventType.MouseLeaveWindow)
            {
                pointerInSceneView = false;
                sceneView.Repaint();
            }
            else if (current.type == EventType.MouseEnterWindow || current.type == EventType.MouseMove ||
                current.type == EventType.MouseDrag || current.type == EventType.MouseDown)
            {
                pointerInSceneView = true;
                if (current.type != EventType.MouseDown) sceneView.Repaint();
            }
            var ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0, layer.baseY + 1, 0));
            float floorDistance = -1f;
            bool hasHoveredCell = pointerInSceneView && plane.Raycast(ray, out floorDistance);
            GridPoint hoveredPoint = default;
            if (hasHoveredCell)
            {
                var hoveredPosition = ray.GetPoint(floorDistance);
                hoveredPoint = new GridPoint(layer.id, Mathf.FloorToInt(hoveredPosition.x), Mathf.FloorToInt(hoveredPosition.z));
            }
            var previousZTest = Handles.zTest;
            var previousColor = Handles.color;
            try
            {
                Handles.zTest = CompareFunction.LessEqual;
                DrawLevel(level, catalog, sceneView.camera, ray, hasHoveredCell ? floorDistance : -1f);
                DrawGrid(layer);
            }
            finally
            {
                Handles.zTest = previousZTest;
                Handles.color = previousColor;
            }
            if (rectangleDragging) DrawRectanglePreview(rectangleStart, rectangleEnd, layer.baseY,
                EditorSessionService.Session.rectangleOutline, EditorSessionService.Session.erase);
            if (hasHoveredCell) DrawHover(hoveredPoint, layer.baseY);
            if (rectangleDragging)
            {
                if (current.type == EventType.MouseDrag && current.button == 0)
                {
                    if (hasHoveredCell) rectangleEnd = hoveredPoint;
                    current.Use();
                    sceneView.Repaint();
                    return;
                }
                if (current.type == EventType.MouseUp && current.button == 0)
                {
                    if (hasHoveredCell) rectangleEnd = hoveredPoint;
                    rectangleDragging = false;
                    if (GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
                    if (TryBuildRectanglePoints(rectangleStart, rectangleEnd,
                        EditorSessionService.Session.rectangleOutline, out var points))
                        PaintRectangle(points);
                    else sceneView.ShowNotification(new GUIContent($"Rectangle is limited to {MaxRectangleCells} cells."));
                    current.Use();
                    sceneView.Repaint();
                    return;
                }
                return;
            }
            if (!hasHoveredCell || current.alt || current.type != EventType.MouseDown) return;
            if (current.button == 0)
            {
                if (EditorSessionService.Session.rectangleTool && CanUseRectangle(EditorSessionService.Session))
                {
                    rectangleStart = rectangleEnd = hoveredPoint;
                    rectangleDragging = true;
                    GUIUtility.hotControl = controlId;
                }
                else Paint(hoveredPoint);
                current.Use();
                sceneView.Repaint();
            }
            else if (current.button == 1)
            {
                SelectPlacement(hoveredPoint);
                current.Use();
            }
        }

        private static void RotateCamera(SceneView sceneView, float yawDegrees)
        {
            var targetRotation = Quaternion.AngleAxis(yawDegrees, Vector3.up) * sceneView.rotation;
            sceneView.LookAt(sceneView.pivot, targetRotation, sceneView.size, sceneView.orthographic, false);
        }

        private void CancelRectangleDrag()
        {
            if (!rectangleDragging) return;
            rectangleDragging = false;
            GUIUtility.hotControl = 0;
        }

        private static void DrawLevel(LevelDefinition level, BlockCatalog catalog, Camera camera, Ray pointerRay, float floorDistance)
        {
            if (Event.current.type != EventType.Repaint) return;
            var blocks = new List<PreviewBlock>();
            foreach (var layer in level.layers)
            {
                bool active = layer.id == EditorSessionService.Session.selectedLayerId;
                foreach (var floor in layer.floors)
                    blocks.Add(new PreviewBlock(new GridPoint(layer.id, floor.x, floor.z), layer.baseY,
                        PlacementSlot.Floor, catalog.Find(floor.blockId), active));
            }
            foreach (var placement in level.placements)
            {
                var layer = level.layers.Find(item => item.id == placement.layerId);
                if (layer == null) continue;
                blocks.Add(new PreviewBlock(placement.Point, layer.baseY, placement.slot,
                    catalog.Find(placement.blockId), layer.id == EditorSessionService.Session.selectedLayerId));
            }
            var cameraPosition = camera.transform.position;
            var cameraForward = camera.transform.forward;
            blocks.Sort((a, b) =>
            {
                int depth = Vector3.Dot(b.Center - cameraPosition, cameraForward)
                    .CompareTo(Vector3.Dot(a.Center - cameraPosition, cameraForward));
                if (depth != 0) return depth;
                int layer = a.point.layerId.CompareTo(b.point.layerId);
                if (layer != 0) return layer;
                int x = a.point.x.CompareTo(b.point.x);
                if (x != 0) return x;
                int z = a.point.z.CompareTo(b.point.z);
                return z != 0 ? z : a.slot.CompareTo(b.slot);
            });
            foreach (var block in blocks)
            {
                bool occludesHoveredCell = block.active && block.slot == PlacementSlot.Structure && floorDistance > 0f &&
                    new Bounds(block.Center, Vector3.one * 0.98f).IntersectRay(pointerRay, out float wallDistance) &&
                    wallDistance < floorDistance - 0.01f;
                DrawBlock(block.point, block.baseY, block.slot, block.entry, block.active, occludesHoveredCell);
            }
        }

        private static void DrawBlock(GridPoint point, int baseY, PlacementSlot slot, BlockEntry entry, bool active,
            bool occludesHoveredCell)
        {
            if (entry == null) return;
            Color color;
            switch (entry.kind)
            {
                case BlockKind.Breakable: color = new Color(0.95f, 0.43f, 0.22f); break;
                case BlockKind.Teleport: color = new Color(0.15f, 0.72f, 0.95f); break;
                case BlockKind.Spawn: color = new Color(0.29f, 0.92f, 0.46f); break;
                case BlockKind.Target: color = new Color(0.95f, 0.32f, 0.68f); break;
                case BlockKind.PressurePlate: color = new Color(0.9f, 0.2f, 0.2f); break;
                default: color = slot == PlacementSlot.Floor ? new Color(0.5f, 0.57f, 0.64f) :
                    slot == PlacementSlot.Box ? new Color(1f, 0.78f, 0.28f) : new Color(0.22f, 0.3f, 0.4f); break;
            }
            color.a = occludesHoveredCell ? 0.80f : active ? 1f : 0.20f;
            Handles.color = color;
            if (slot == PlacementSlot.Marker)
            {
                var center = new Vector3(point.x + 0.5f, baseY + 1.04f, point.z + 0.5f);
                Handles.DrawSolidRectangleWithOutline(new[]
                {
                    center + new Vector3(-0.4f, 0, -0.4f), center + new Vector3(-0.4f, 0, 0.4f),
                    center + new Vector3(0.4f, 0, 0.4f), center + new Vector3(0.4f, 0, -0.4f)
                }, color, color);
            }
            else
            {
                float y = slot == PlacementSlot.Floor ? baseY + 0.5f : baseY + 1.5f;
                Handles.CubeHandleCap(0, new Vector3(point.x + 0.5f, y, point.z + 0.5f), Quaternion.identity, 0.98f, EventType.Repaint);
            }
        }

        private static void DrawGrid(FloorLayer layer)
        {
            if (Event.current.type != EventType.Repaint) return;
            int minX = layer.floors.Count == 0 ? -7 : Mathf.Min(-2, layer.floors.Min(cell => cell.x) - 2);
            int maxX = layer.floors.Count == 0 ? 8 : Mathf.Max(3, layer.floors.Max(cell => cell.x) + 3);
            int minZ = layer.floors.Count == 0 ? -7 : Mathf.Min(-2, layer.floors.Min(cell => cell.z) - 2);
            int maxZ = layer.floors.Count == 0 ? 8 : Mathf.Max(3, layer.floors.Max(cell => cell.z) + 3);
            Handles.color = new Color(0.44f, 0.70f, 0.83f, 0.34f);
            float y = layer.baseY + 1.012f;
            for (int x = minX; x <= maxX; x++) Handles.DrawLine(new Vector3(x, y, minZ), new Vector3(x, y, maxZ));
            for (int z = minZ; z <= maxZ; z++) Handles.DrawLine(new Vector3(minX, y, z), new Vector3(maxX, y, z));
        }

        private static void DrawHover(GridPoint point, int baseY)
        {
            if (Event.current.type != EventType.Repaint) return;
            float y = baseY + 1.045f;
            Handles.color = Color.yellow;
            Handles.DrawAAPolyLine(4, new Vector3(point.x, y, point.z), new Vector3(point.x + 1, y, point.z),
                new Vector3(point.x + 1, y, point.z + 1), new Vector3(point.x, y, point.z + 1), new Vector3(point.x, y, point.z));
        }

        private static void DrawRectanglePreview(GridPoint start, GridPoint end, int baseY, bool outline, bool erase)
        {
            if (Event.current.type != EventType.Repaint) return;
            int minX = Mathf.Min(start.x, end.x);
            int maxX = Mathf.Max(start.x, end.x);
            int minZ = Mathf.Min(start.z, end.z);
            int maxZ = Mathf.Max(start.z, end.z);
            long cellCount = RectanglePointCount(start, end, outline);
            bool tooLarge = cellCount > MaxRectangleCells;
            Color face = tooLarge ? new Color(1f, 0.18f, 0.12f, 0.20f) :
                erase ? new Color(1f, 0.28f, 0.18f, 0.16f) : new Color(0.18f, 0.82f, 1f, 0.14f);
            Color border = tooLarge ? new Color(1f, 0.22f, 0.15f, 1f) :
                erase ? new Color(1f, 0.34f, 0.22f, 1f) : new Color(0.2f, 0.88f, 1f, 1f);
            float y = baseY + 1.055f;
            if (!outline || minX == maxX || minZ == maxZ)
                DrawPreviewArea(minX, maxX + 1, minZ, maxZ + 1, y, face, border);
            else
            {
                DrawPreviewArea(minX, maxX + 1, minZ, minZ + 1, y, face, border);
                DrawPreviewArea(minX, maxX + 1, maxZ, maxZ + 1, y, face, border);
                if (maxZ - minZ > 1)
                {
                    DrawPreviewArea(minX, minX + 1, minZ + 1, maxZ, y, face, border);
                    DrawPreviewArea(maxX, maxX + 1, minZ + 1, maxZ, y, face, border);
                }
            }
            Handles.color = border;
            Handles.Label(new Vector3((minX + maxX + 1) * 0.5f, y + 0.04f, (minZ + maxZ + 1) * 0.5f),
                tooLarge ? $"Too large · limit {MaxRectangleCells}" : $"{maxX - minX + 1} × {maxZ - minZ + 1} · {cellCount} cells");
        }

        private static void DrawPreviewArea(int minX, int maxX, int minZ, int maxZ, float y, Color face, Color border)
        {
            if (maxX <= minX || maxZ <= minZ) return;
            Handles.DrawSolidRectangleWithOutline(new[]
            {
                new Vector3(minX, y, minZ), new Vector3(minX, y, maxZ),
                new Vector3(maxX, y, maxZ), new Vector3(maxX, y, minZ)
            }, face, border);
        }

        private static long RectanglePointCount(GridPoint start, GridPoint end, bool outline)
        {
            long width = Math.Abs((long)end.x - start.x) + 1;
            long height = Math.Abs((long)end.z - start.z) + 1;
            if (!outline || width == 1 || height == 1) return width * height;
            return width * 2 + height * 2 - 4;
        }

        private static bool TryBuildRectanglePoints(GridPoint start, GridPoint end, bool outline, out List<GridPoint> points)
        {
            long count = RectanglePointCount(start, end, outline);
            if (start.layerId != end.layerId || count > MaxRectangleCells)
            {
                points = null;
                return false;
            }
            int minX = Mathf.Min(start.x, end.x);
            int maxX = Mathf.Max(start.x, end.x);
            int minZ = Mathf.Min(start.z, end.z);
            int maxZ = Mathf.Max(start.z, end.z);
            points = new List<GridPoint>((int)count);
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!outline || x == minX || x == maxX || z == minZ || z == maxZ)
                        points.Add(new GridPoint(start.layerId, x, z));
                }
            }
            return true;
        }

        private static void PaintRectangle(IReadOnlyCollection<GridPoint> points)
        {
            if (points == null || points.Count == 0) return;
            var session = EditorSessionService.Session;
            if (!session.erase && session.selectedSlot != PlacementSlot.Floor && session.selectedSlot != PlacementSlot.Structure)
                return;
            var entry = session.erase ? null : EditorSessionService.Catalog.Find(session.selectedBlockId);
            if (!session.erase && entry == null) return;
            bool removedSelection = false;
            bool saved = EditorSessionService.EditLevel(session.erase ? "Erase Rectangle" : "Paint Rectangle", level =>
                removedSelection = ApplyRectangle(level, points, session.selectedSlot, entry, session.erase));
            if (!saved || !removedSelection) return;
            SelectedPlacementId = null;
            SelectionChanged?.Invoke();
        }

        private static bool ApplyRectangle(LevelDefinition level, IReadOnlyCollection<GridPoint> points,
            PlacementSlot slot, BlockEntry entry, bool erase)
        {
            bool removedSelection = false;
            if (erase)
            {
                // Keep rectangle erasing identical to a single-cell erase. The selected
                // brush slot must not hide content in the rectangle from the erase tool.
                foreach (var point in points)
                {
                    string erasedId = EraseCell(level, point);
                    if (erasedId != null && erasedId == SelectedPlacementId)
                        removedSelection = true;
                }
                return removedSelection;
            }
            var targets = new HashSet<GridPoint>(points);
            int layerId = points.First().layerId;
            if (slot == PlacementSlot.Floor)
            {
                var layer = level.layers.Find(item => item.id == layerId);
                if (layer == null) return false;
                var existing = layer.floors.ToDictionary(item => new Vector2Int(item.x, item.z));
                foreach (var point in points)
                {
                    var key = new Vector2Int(point.x, point.z);
                    if (existing.TryGetValue(key, out var floor)) floor.blockId = entry.id;
                    else
                    {
                        var added = new FloorCell { x = point.x, z = point.z, blockId = entry.id };
                        layer.floors.Add(added);
                        existing.Add(key, added);
                    }
                }
                return false;
            }
            var preserved = new HashSet<GridPoint>();
            level.placements.RemoveAll(item =>
            {
                if (item.slot != PlacementSlot.Structure || !targets.Contains(item.Point)) return false;
                if (item.blockId == entry.id && preserved.Add(item.Point)) return false;
                if (item.instanceId == SelectedPlacementId) removedSelection = true;
                return true;
            });
            foreach (var point in points)
                if (!preserved.Contains(point)) level.placements.Add(NewPlacement(point, entry));
            return removedSelection;
        }

        private static void Paint(GridPoint point)
        {
            var session = EditorSessionService.Session;
            if (session.erase)
            {
                string erasedId = null;
                if (EditorSessionService.EditLevel("Erase Cell", level => erasedId = EraseCell(level, point)) &&
                    erasedId != null && erasedId == SelectedPlacementId)
                {
                    SelectedPlacementId = null;
                    SelectionChanged?.Invoke();
                }
                return;
            }
            var entry = EditorSessionService.Catalog.Find(session.selectedBlockId);
            if (entry == null) return;
            EditorSessionService.EditLevel("Paint Cell", level =>
            {
                var layer = level.layers.Find(item => item.id == point.layerId);
                if (session.selectedSlot == PlacementSlot.Floor)
                {
                    var floor = layer.floors.Find(item => item.x == point.x && item.z == point.z);
                    if (floor == null) layer.floors.Add(new FloorCell { x = point.x, z = point.z, blockId = entry.id });
                    else floor.blockId = entry.id;
                    return;
                }
                if (session.selectedSlot == PlacementSlot.Marker)
                {
                    if (entry.kind == BlockKind.Spawn)
                        level.placements.RemoveAll(item => item.slot == PlacementSlot.Marker &&
                            EditorSessionService.Catalog.Find(item.blockId)?.kind == BlockKind.Spawn);
                    else level.placements.RemoveAll(item => item.slot == PlacementSlot.Marker && item.Point.Equals(point) && item.blockId == entry.id);
                    level.placements.Add(NewPlacement(point, entry));
                    return;
                }
                level.placements.RemoveAll(item => item.slot == session.selectedSlot && item.Point.Equals(point));
                level.placements.Add(NewPlacement(point, entry));
            });
        }

        private static string EraseCell(LevelDefinition level, GridPoint point)
        {
            var placement = level.placements.LastOrDefault(item => item.Point.Equals(point) && item.slot == PlacementSlot.Box)
                ?? level.placements.LastOrDefault(item => item.Point.Equals(point) && item.slot == PlacementSlot.Structure)
                ?? level.placements.LastOrDefault(item => item.Point.Equals(point) && item.slot == PlacementSlot.Marker);
            if (placement != null)
            {
                level.placements.Remove(placement);
                return placement.instanceId;
            }
            var layer = level.layers.Find(item => item.id == point.layerId);
            layer?.floors.RemoveAll(item => item.x == point.x && item.z == point.z);
            return null;
        }

        private static Placement NewPlacement(GridPoint point, BlockEntry entry)
        {
            var placement = new Placement { layerId = point.layerId, x = point.x, z = point.z,
                slot = entry.category == BlockCategory.Structure ? PlacementSlot.Structure :
                    entry.category == BlockCategory.Marker ? PlacementSlot.Marker : PlacementSlot.Box,
                blockId = entry.id };
            if (entry.kind == BlockKind.Breakable) placement.breakModes = BreakMode.Interact | BreakMode.BoxImpact;
            if (entry.kind == BlockKind.PressurePlate) placement.switchId = "switch_" + placement.instanceId.Substring(0, 8);
            return placement;
        }

        private static void SelectPlacement(GridPoint point)
        {
            var level = EditorSessionService.CurrentLevel;
            var placement = level.placements.LastOrDefault(item => item.Point.Equals(point) && item.slot == EditorSessionService.Session.selectedSlot);
            SelectedPlacementId = placement?.instanceId;
            SelectionChanged?.Invoke();
        }
    }
}
