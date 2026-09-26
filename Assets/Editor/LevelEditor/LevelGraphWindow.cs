using System;
using System.Collections.Generic;
using System.Linq;
using ChestPush.LevelData;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChestPush.LevelEditor
{
    public sealed class LevelGraphWindow : EditorWindow
    {
        private const float NodeWidth = 185;
        private const float NodeHeight = 72;
        private const float PortSize = 16;
        private const float GridSpacing = 64;
        private const float MinZoom = 0.35f;
        private const float MaxZoom = 2f;
        private const float ZoomStep = 0.1f;
        private string selectedId;
        private string linkSourceId;
        private string hoveredLinkTargetId;
        private int linkPointerId = -1;
        private Vector2 linkPointerCanvasPosition;
        private VisualElement linkOutputPort;
        private TextField newName;
        private Label errorLabel;
        private Label saveLabel;
        private VisualElement graphRoot;
        private VisualElement paletteRoot;
        private VisualElement graphViewport;
        private VisualElement canvas;
        private VisualElement inspector;
        private Label zoomLabel;
        private IMGUIContainer edgeLayer;
        private readonly Dictionary<string, VisualElement> nodeElements = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, VisualElement> inputPorts = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, VisualElement> outputPorts = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, PortPlacement> inputPlacements = new Dictionary<string, PortPlacement>();
        private readonly Dictionary<string, PortPlacement> outputPlacements = new Dictionary<string, PortPlacement>();
        private readonly HashSet<string> selectedIds = new HashSet<string>();
        private readonly Dictionary<string, Vector2> groupDragStartPositions = new Dictionary<string, Vector2>();
        [SerializeField] private bool sceneEditMode;
        [SerializeField] private float graphZoom = 1f;
        [SerializeField] private Vector2 graphPan;
        private bool isPanning;
        private int panPointerId = -1;
        private Vector2 panStartPointerPosition;
        private Vector2 panStartOffset;
        private bool isBoxSelecting;
        private bool boxSelectionMoved;
        private Vector2 boxSelectionStart;
        private Vector2 boxSelectionCurrent;
        private bool boxSelectionAdditive;
        private VisualElement selectionBox;
        private bool isMovingNodeGroup;
        private Vector2 nodeGroupDragStart;
        private static LevelGraphWindow activeWindow;

        [MenuItem("Tools/ChestPush/Level Editor")]
        public static void Open() => GetWindow<LevelGraphWindow>("ChestPush Levels").Focus();

        public static void OpenGraph()
        {
            var window = GetWindow<LevelGraphWindow>("ChestPush Levels");
            window.sceneEditMode = false;
            window.Refresh();
            window.Focus();
        }

        internal static void RefreshPalette() => activeWindow?.Refresh();

        private void OnEnable()
        {
            activeWindow = this;
            EditorSessionService.Changed += Refresh;
            LevelPaintTool.SelectionChanged += Refresh;
        }

        private void OnDisable()
        {
            if (activeWindow == this) activeWindow = null;
            EditorSessionService.Changed -= Refresh;
            LevelPaintTool.SelectionChanged -= Refresh;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;
            graphRoot = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
            root.Add(graphRoot);
            paletteRoot = new VisualElement { style = { flexGrow = 1 } };
            root.Add(paletteRoot);
            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, height = 40, paddingLeft = 8, paddingTop = 6 } };
            graphRoot.Add(toolbar);
            toolbar.Add(new Label("New level:") { style = { width = 80, unityTextAlign = TextAnchor.MiddleLeft } });
            newName = new TextField { value = "easy_00_guide", style = { width = 185 } };
            toolbar.Add(newName);
            toolbar.Add(Button("Create", () =>
            {
                var graph = EditorSessionService.Graph;
                var viewportSize = graphViewport.contentRect.size;
                var graphCenter = (viewportSize * 0.5f - graphPan) / graphZoom;
                var desiredPosition = graphCenter - new Vector2(NodeWidth, NodeHeight) * 0.5f;
                var position = FindAvailableNodePosition(graph.nodes, desiredPosition);
                if (EditorSessionService.CreateLevel(newName.value, position))
                {
                    selectedId = EditorSessionService.CurrentLevel.levelId;
                    selectedIds.Clear();
                    selectedIds.Add(selectedId);
                    Refresh();
                }
                else ShowError();
            }));
            toolbar.Add(Button("Validate", ValidateAll));
            toolbar.Add(Button("Focus (F)", FrameSelectedNode));
            toolbar.Add(new Label("Drag empty space to select · Ctrl+wheel zoom · Middle drag pan")
                { style = { flexGrow = 1, marginLeft = 8, unityTextAlign = TextAnchor.MiddleLeft, color = new Color(0.68f, 0.75f, 0.84f) } });
            zoomLabel = new Label { style = { width = 58, unityTextAlign = TextAnchor.MiddleRight } };
            toolbar.Add(zoomLabel);
            var body = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Row } };
            graphRoot.Add(body);
            graphViewport = new VisualElement { style = { flexGrow = 1, overflow = Overflow.Hidden, backgroundColor = new Color(0.10f, 0.12f, 0.16f) } };
            graphViewport.focusable = true;
            body.Add(graphViewport);
            edgeLayer = new IMGUIContainer(() => DrawEdges(EditorSessionService.Graph));
            edgeLayer.style.position = Position.Absolute;
            edgeLayer.style.left = 0;
            edgeLayer.style.right = 0;
            edgeLayer.style.top = 0;
            edgeLayer.style.bottom = 0;
            edgeLayer.pickingMode = PickingMode.Ignore;
            graphViewport.Add(edgeLayer);
            canvas = new VisualElement();
            canvas.style.position = Position.Absolute;
            canvas.style.left = 0;
            canvas.style.right = 0;
            canvas.style.top = 0;
            canvas.style.bottom = 0;
            canvas.style.overflow = Overflow.Visible;
            canvas.style.transformOrigin = new TransformOrigin(Length.Percent(0), Length.Percent(0), 0);
            graphViewport.Add(canvas);
            ApplyGraphView();
            graphViewport.RegisterCallback<WheelEvent>(OnGraphWheel, TrickleDown.TrickleDown);
            graphViewport.RegisterCallback<PointerDownEvent>(OnGraphPointerDown, TrickleDown.TrickleDown);
            graphViewport.RegisterCallback<PointerMoveEvent>(OnGraphPointerMove, TrickleDown.TrickleDown);
            graphViewport.RegisterCallback<PointerUpEvent>(OnGraphPointerUp, TrickleDown.TrickleDown);
            graphViewport.RegisterCallback<PointerCaptureOutEvent>(OnGraphPointerCaptureOut);
            selectionBox = new VisualElement { name = "selection-box", pickingMode = PickingMode.Ignore };
            selectionBox.style.position = Position.Absolute;
            selectionBox.style.display = DisplayStyle.None;
            selectionBox.style.borderLeftWidth = selectionBox.style.borderRightWidth = 1;
            selectionBox.style.borderTopWidth = selectionBox.style.borderBottomWidth = 1;
            selectionBox.style.borderLeftColor = selectionBox.style.borderRightColor = new Color(0.40f, 0.76f, 1f, 0.9f);
            selectionBox.style.borderTopColor = selectionBox.style.borderBottomColor = new Color(0.40f, 0.76f, 1f, 0.9f);
            selectionBox.style.backgroundColor = new Color(0.30f, 0.65f, 1f, 0.16f);
            canvas.Add(selectionBox);
            graphRoot.RegisterCallback<KeyDownEvent>(OnGraphKeyDown);
            inspector = new VisualElement { style = { width = 290, paddingLeft = 10, paddingRight = 10, paddingTop = 10, backgroundColor = new Color(0.17f, 0.20f, 0.26f) } };
            body.Add(inspector);
            errorLabel = new Label { style = { minHeight = 28, color = new Color(1f, 0.55f, 0.45f), paddingLeft = 8 } };
            graphRoot.Add(errorLabel);
            saveLabel = new Label { style = { minHeight = 22, color = new Color(0.42f, 0.8f, 0.54f), paddingLeft = 8 } };
            graphRoot.Add(saveLabel);
            Refresh();
        }

        private void Refresh()
        {
            if (canvas == null || inspector == null || paletteRoot == null) return;
            CancelLinkDrag(false);
            if (sceneEditMode && EditorSessionService.CurrentLevel == null) sceneEditMode = false;
            graphRoot.style.display = sceneEditMode ? DisplayStyle.None : DisplayStyle.Flex;
            paletteRoot.style.display = sceneEditMode ? DisplayStyle.Flex : DisplayStyle.None;
            if (sceneEditMode)
            {
                LevelPalettePanel.Build(paletteRoot);
                return;
            }
            canvas.Clear();
            nodeElements.Clear();
            inputPorts.Clear();
            outputPorts.Clear();
            inputPlacements.Clear();
            outputPlacements.Clear();
            var graph = EditorSessionService.Graph;
            if (selectedId != null && graph.nodes.All(item => item.levelId != selectedId)) selectedId = null;
            selectedIds.RemoveWhere(id => graph.nodes.All(item => item.levelId != id));
            if (selectedId != null && selectedIds.Count == 0) selectedIds.Add(selectedId);
            foreach (var node in graph.nodes) DrawNode(node);
            UpdatePortPlacements(graph);
            canvas.Add(selectionBox);
            UpdateNodeSelectionAppearance();
            edgeLayer?.MarkDirtyRepaint();
            DrawInspector(graph);
            errorLabel.text = EditorSessionService.LastError ?? "";
            saveLabel.text = EditorSessionService.SaveStatus;
        }

        private void DrawEdges(LevelGraphDefinition graph)
        {
            Handles.BeginGUI();
            DrawGrid();
            Handles.color = new Color(0.56f, 0.82f, 1f);
            foreach (var source in graph.nodes)
            {
                foreach (var id in source.successors ?? Enumerable.Empty<string>())
                {
                    var target = graph.nodes.Find(node => node.levelId == id);
                    if (target == null) continue;
                    DrawAdaptiveEdge(GetOutputPlacement(source), GetInputPlacement(target), Handles.color, 3);
                }
            }
            var dragSource = graph.nodes.Find(node => node.levelId == linkSourceId);
            if (dragSource != null)
            {
                var target = graph.nodes.Find(node => node.levelId == hoveredLinkTargetId);
                var end = target == null
                    ? new PortPlacement(linkPointerCanvasPosition, OppositeSide(GetOutputPlacement(dragSource).Side))
                    : GetInputPlacement(target);
                DrawAdaptiveEdge(GetOutputPlacement(dragSource), end,
                    target == null ? new Color(0.55f, 0.75f, 0.92f, 0.85f) : new Color(0.42f, 1f, 0.62f), 3);
            }
            Handles.EndGUI();
        }

        private void DrawGrid()
        {
            Rect viewport = edgeLayer.contentRect;
            if (viewport.width <= 0 || viewport.height <= 0) return;
            float spacing = GridSpacing;
            while (spacing * graphZoom < 32) spacing *= 2;
            float left = -graphPan.x / graphZoom;
            float right = (viewport.width - graphPan.x) / graphZoom;
            float top = -graphPan.y / graphZoom;
            float bottom = (viewport.height - graphPan.y) / graphZoom;
            int firstColumn = Mathf.FloorToInt(left / spacing);
            int lastColumn = Mathf.CeilToInt(right / spacing);
            int firstRow = Mathf.FloorToInt(top / spacing);
            int lastRow = Mathf.CeilToInt(bottom / spacing);
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                float x = graphPan.x + column * spacing * graphZoom;
                Handles.color = column == 0 ? new Color(0.32f, 0.43f, 0.55f, 0.65f) :
                    (column % 4 == 0 ? new Color(0.25f, 0.31f, 0.39f, 0.6f) : new Color(0.20f, 0.24f, 0.3f, 0.42f));
                Handles.DrawLine(new Vector3(x, 0), new Vector3(x, viewport.height));
            }
            for (int row = firstRow; row <= lastRow; row++)
            {
                float y = graphPan.y + row * spacing * graphZoom;
                Handles.color = row == 0 ? new Color(0.32f, 0.43f, 0.55f, 0.65f) :
                    (row % 4 == 0 ? new Color(0.25f, 0.31f, 0.39f, 0.6f) : new Color(0.20f, 0.24f, 0.3f, 0.42f));
                Handles.DrawLine(new Vector3(0, y), new Vector3(viewport.width, y));
            }
        }

        private void DrawAdaptiveEdge(PortPlacement start, PortPlacement end, Color color, float width)
        {
            Vector2 delta = end.Position - start.Position;
            float distance = delta.magnitude;
            if (distance < 0.01f) return;

            Vector2 startNormal = SideNormal(start.Side);
            Vector2 endNormal = SideNormal(end.Side);
            float startForward = Vector2.Dot(delta, startNormal);
            float endForward = Vector2.Dot(-delta, endNormal);

            float baseLength = Mathf.Clamp(distance * 0.28f, 24f, 130f);
            Vector2 startControl = start.Position + startNormal * CalculateTangentLength(baseLength, startForward);
            Vector2 endControl = end.Position + endNormal * CalculateTangentLength(baseLength, endForward);

            if (TryGetQuadraticCorner(start, end, out Vector2 corner, out float cornerClearance))
            {
                // A quadratic makes a gentle 90-degree turn. Fade it into the cubic for long spans.
                float quadraticWeight = (1f - SmoothRange(420f, 620f, distance)) *
                                        SmoothRange(6f, 28f, cornerClearance);
                Vector2 quadraticStart = start.Position + (corner - start.Position) * (2f / 3f);
                Vector2 quadraticEnd = end.Position + (corner - end.Position) * (2f / 3f);
                startControl = Vector2.Lerp(startControl, quadraticStart, quadraticWeight);
                endControl = Vector2.Lerp(endControl, quadraticEnd, quadraticWeight);
            }

            if (startForward > 0 && endForward > 0)
            {
                float alignment = Mathf.Min(startForward, endForward) / distance;
                float straightWeight = Mathf.Max(SmoothRange(0.94f, 0.995f, alignment),
                    1f - SmoothRange(32f, 64f, distance));
                Vector2 straightStart = Vector2.Lerp(start.Position, end.Position, 1f / 3f);
                Vector2 straightEnd = Vector2.Lerp(start.Position, end.Position, 2f / 3f);
                startControl = Vector2.Lerp(startControl, straightStart, straightWeight);
                endControl = Vector2.Lerp(endControl, straightEnd, straightWeight);
            }

            Vector3 screenStart = GraphToViewport(start.Position);
            Vector3 screenEnd = GraphToViewport(end.Position);
            Vector3 screenStartControl = GraphToViewport(startControl);
            Vector3 screenEndControl = GraphToViewport(endControl);
            Handles.DrawBezier(screenStart, screenEnd, screenStartControl, screenEndControl, color, null, width);
            DrawDirectionArrow(screenStart, screenStartControl, screenEndControl, screenEnd, color);
        }

        private static void DrawDirectionArrow(Vector2 start, Vector2 startControl,
            Vector2 endControl, Vector2 end, Color color)
        {
            const int samples = 32;
            float length = 0;
            Vector2 previous = start;
            for (int i = 1; i <= samples; i++)
            {
                Vector2 point = CubicPoint(start, startControl, endControl, end, i / (float)samples);
                length += Vector2.Distance(previous, point);
                previous = point;
            }
            if (length < 12f) return;

            float arrowLength = Mathf.Clamp(length * 0.25f, 5f, 11f);
            float inset = Mathf.Min(16f, length * 0.28f);
            float walked = 0;
            previous = end;
            for (int i = samples - 1; i >= 0; i--)
            {
                float t = i / (float)samples;
                Vector2 point = CubicPoint(start, startControl, endControl, end, t);
                float segment = Vector2.Distance(previous, point);
                if (walked + segment >= inset)
                {
                    float tipT = (i + 1f - (inset - walked) / segment) / samples;
                    Vector2 tip = CubicPoint(start, startControl, endControl, end, tipT);
                    Vector2 tangent = CubicTangent(start, startControl, endControl, end, tipT);
                    if (tangent.sqrMagnitude < 0.0001f) tangent = end - start;
                    Vector2 direction = tangent.normalized;
                    Vector2 baseCenter = tip - direction * arrowLength;
                    Vector2 wing = new Vector2(-direction.y, direction.x) * (arrowLength * 0.58f);
                    Handles.color = color;
                    Handles.DrawAAConvexPolygon(tip, baseCenter + wing, baseCenter - wing);
                    return;
                }
                walked += segment;
                previous = point;
            }
        }

        private static Vector2 CubicPoint(Vector2 start, Vector2 startControl,
            Vector2 endControl, Vector2 end, float t)
        {
            float remaining = 1f - t;
            return remaining * remaining * remaining * start +
                   3f * remaining * remaining * t * startControl +
                   3f * remaining * t * t * endControl +
                   t * t * t * end;
        }

        private static Vector2 CubicTangent(Vector2 start, Vector2 startControl,
            Vector2 endControl, Vector2 end, float t)
        {
            float remaining = 1f - t;
            return 3f * remaining * remaining * (startControl - start) +
                   6f * remaining * t * (endControl - startControl) +
                   3f * t * t * (end - endControl);
        }

        private static bool TryGetQuadraticCorner(PortPlacement start, PortPlacement end,
            out Vector2 corner, out float clearance)
        {
            Vector2 startNormal = SideNormal(start.Side);
            Vector2 endNormal = SideNormal(end.Side);
            if (!Mathf.Approximately(Vector2.Dot(startNormal, endNormal), 0))
            {
                corner = default;
                clearance = 0;
                return false;
            }

            bool startHorizontal = start.Side == PortSide.Left || start.Side == PortSide.Right;
            corner = startHorizontal
                ? new Vector2(end.Position.x, start.Position.y)
                : new Vector2(start.Position.x, end.Position.y);
            clearance = Mathf.Min(Vector2.Dot(corner - start.Position, startNormal),
                Vector2.Dot(corner - end.Position, endNormal));
            return clearance > 0;
        }

        private static float CalculateTangentLength(float baseLength, float forwardDistance)
        {
            float forwardLength = Mathf.Min(baseLength, Mathf.Max(24f, forwardDistance * 0.45f));
            float reverseLength = Mathf.Min(180f, baseLength + Mathf.Min(50f, Mathf.Max(0, -forwardDistance) * 0.2f));
            return Mathf.Lerp(forwardLength, reverseLength, 1f - SmoothRange(-24f, 24f, forwardDistance));
        }

        private static float SmoothRange(float min, float max, float value)
        {
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(min, max, value));
        }

        private Vector3 GraphToViewport(Vector3 graphPosition)
        {
            return new Vector3(graphPan.x + graphPosition.x * graphZoom,
                graphPan.y + graphPosition.y * graphZoom, graphPosition.z);
        }

        private void OnGraphWheel(WheelEvent evt)
        {
            if (!evt.ctrlKey || Mathf.Approximately(evt.delta.y, 0)) return;
            Vector2 viewportPosition = graphViewport.WorldToLocal(evt.mousePosition);
            Vector2 graphPosition = (viewportPosition - graphPan) / graphZoom;
            float direction = evt.delta.y < 0 ? 1 : -1;
            float nextZoom = Mathf.Clamp(graphZoom + direction * ZoomStep, MinZoom, MaxZoom);
            if (!Mathf.Approximately(nextZoom, graphZoom))
            {
                graphZoom = nextZoom;
                graphPan = viewportPosition - graphPosition * graphZoom;
                ApplyGraphView();
            }
            evt.PreventDefault();
            evt.StopImmediatePropagation();
        }

        private void OnGraphPointerDown(PointerDownEvent evt)
        {
            if (evt.button == 0) graphViewport.Focus();
            if (evt.button == 0 && (evt.target == graphViewport || evt.target == canvas))
            {
                isBoxSelecting = true;
                boxSelectionMoved = false;
                boxSelectionAdditive = evt.shiftKey || evt.ctrlKey || evt.commandKey;
                boxSelectionStart = canvas.WorldToLocal((Vector2)evt.position);
                boxSelectionCurrent = boxSelectionStart;
                graphViewport.CapturePointer(evt.pointerId);
                selectionBox.style.display = DisplayStyle.Flex;
                UpdateSelectionBox();
                evt.PreventDefault();
                evt.StopImmediatePropagation();
                return;
            }
            if (evt.button != 2 || isPanning) return;
            isPanning = true;
            panPointerId = evt.pointerId;
            panStartPointerPosition = evt.position;
            panStartOffset = graphPan;
            graphViewport.CapturePointer(evt.pointerId);
            graphViewport.Focus();
            evt.PreventDefault();
            evt.StopImmediatePropagation();
        }

        private void OnGraphPointerMove(PointerMoveEvent evt)
        {
            if (isBoxSelecting && graphViewport.HasPointerCapture(evt.pointerId))
            {
                boxSelectionCurrent = canvas.WorldToLocal((Vector2)evt.position);
                boxSelectionMoved |= (boxSelectionCurrent - boxSelectionStart).sqrMagnitude > 16;
                UpdateSelectionBox();
                evt.PreventDefault();
                evt.StopImmediatePropagation();
                return;
            }
            if (!isPanning || evt.pointerId != panPointerId ||
                !graphViewport.HasPointerCapture(evt.pointerId)) return;
            Vector2 pointerDelta = (Vector2)evt.position - panStartPointerPosition;
            graphPan = panStartOffset + pointerDelta;
            ApplyGraphView();
            evt.PreventDefault();
            evt.StopImmediatePropagation();
        }

        private void OnGraphPointerUp(PointerUpEvent evt)
        {
            if (isBoxSelecting && graphViewport.HasPointerCapture(evt.pointerId))
            {
                if (boxSelectionMoved)
                {
                    if (!boxSelectionAdditive) selectedIds.Clear();
                    Rect selectionRect = MakeRect(boxSelectionStart, boxSelectionCurrent);
                    foreach (var node in EditorSessionService.Graph.nodes)
                    {
                        var nodeRect = new Rect(node.editorX, node.editorY, NodeWidth, NodeHeight);
                        if (selectionRect.Overlaps(nodeRect)) selectedIds.Add(node.levelId);
                    }
                    selectedId = selectedIds.Count == 1 ? selectedIds.First() : null;
                    Refresh();
                }
                else if (!boxSelectionAdditive)
                {
                    selectedIds.Clear();
                    selectedId = null;
                    Refresh();
                }
                isBoxSelecting = false;
                selectionBox.style.display = DisplayStyle.None;
                graphViewport.ReleasePointer(evt.pointerId);
                evt.PreventDefault();
                evt.StopImmediatePropagation();
                return;
            }
            if (!isPanning || evt.pointerId != panPointerId) return;
            if (graphViewport.HasPointerCapture(evt.pointerId))
                graphViewport.ReleasePointer(evt.pointerId);
            EndGraphPan();
            evt.PreventDefault();
            evt.StopImmediatePropagation();
        }

        private void OnGraphPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (isBoxSelecting)
            {
                isBoxSelecting = false;
                if (selectionBox != null) selectionBox.style.display = DisplayStyle.None;
            }
            if (isPanning && evt.pointerId == panPointerId) EndGraphPan();
        }

        private void UpdateSelectionBox()
        {
            Rect rect = MakeRect(boxSelectionStart, boxSelectionCurrent);
            selectionBox.style.left = rect.xMin;
            selectionBox.style.top = rect.yMin;
            selectionBox.style.width = rect.width;
            selectionBox.style.height = rect.height;
        }

        private static Rect MakeRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private void EndGraphPan()
        {
            isPanning = false;
            panPointerId = -1;
        }

        private void OnGraphKeyDown(KeyDownEvent evt)
        {
            if (!(evt.target is VisualElement target) ||
                (target != graphViewport && !graphViewport.Contains(target))) return;
            if (evt.keyCode == KeyCode.Escape && linkSourceId != null)
            {
                CancelLinkDrag();
                evt.StopPropagation();
                return;
            }
            if (evt.keyCode != KeyCode.F || evt.ctrlKey || evt.altKey || evt.shiftKey) return;
            FrameSelectedNode();
            evt.StopPropagation();
        }

        private void FrameSelectedNode()
        {
            var graph = EditorSessionService.Graph;
            var selection = graph.nodes.Where(item => selectedIds.Contains(item.levelId) || item.levelId == selectedId).ToList();
            if (selection.Count == 0)
            {
                if (errorLabel != null) errorLabel.text = "Select a level node before focusing.";
                return;
            }
            Vector2 viewportSize = graphViewport.contentRect.size;
            if (viewportSize.x <= 1 || viewportSize.y <= 1)
            {
                graphViewport.schedule.Execute(FrameSelectedNode);
                return;
            }
            float left = selection.Min(item => item.editorX);
            float top = selection.Min(item => item.editorY);
            float right = selection.Max(item => item.editorX + NodeWidth);
            float bottom = selection.Max(item => item.editorY + NodeHeight);
            Vector2 selectionCenter = new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f);
            graphPan = viewportSize / 2 - selectionCenter * graphZoom;
            ApplyGraphView();
            graphViewport.Focus();
            if (errorLabel != null) errorLabel.text = "";
        }

        private void ApplyGraphView()
        {
            graphZoom = Mathf.Clamp(graphZoom, MinZoom, MaxZoom);
            if (canvas != null)
            {
                canvas.transform.position = new Vector3(graphPan.x, graphPan.y, 0);
                canvas.transform.scale = new Vector3(graphZoom, graphZoom, 1);
            }
            if (zoomLabel != null) zoomLabel.text = $"{Mathf.RoundToInt(graphZoom * 100)}%";
            edgeLayer?.MarkDirtyRepaint();
        }

        private PortPlacement GetOutputPlacement(LevelGraphNode node)
        {
            return outputPlacements.TryGetValue(node.levelId, out var placement)
                ? placement : new PortPlacement(new Vector2(node.editorX + NodeWidth, node.editorY + NodeHeight / 2), PortSide.Right);
        }

        private PortPlacement GetInputPlacement(LevelGraphNode node)
        {
            return inputPlacements.TryGetValue(node.levelId, out var placement)
                ? placement : new PortPlacement(new Vector2(node.editorX, node.editorY + NodeHeight / 2), PortSide.Left);
        }

        private enum PortSide { Left, Right, Top, Bottom }
        private static readonly PortSide[] InputSidePriority =
            { PortSide.Left, PortSide.Top, PortSide.Bottom, PortSide.Right };
        private static readonly PortSide[] OutputSidePriority =
            { PortSide.Right, PortSide.Bottom, PortSide.Top, PortSide.Left };

        private readonly struct PortPlacement
        {
            public readonly Vector2 Position;
            public readonly PortSide Side;

            public PortPlacement(Vector2 position, PortSide side)
            {
                Position = position;
                Side = side;
            }
        }

        private static Vector2 SideNormal(PortSide side)
        {
            switch (side)
            {
                case PortSide.Left: return Vector2.left;
                case PortSide.Right: return Vector2.right;
                case PortSide.Top: return Vector2.down;
                default: return Vector2.up;
            }
        }

        private static PortSide OppositeSide(PortSide side)
        {
            switch (side)
            {
                case PortSide.Left: return PortSide.Right;
                case PortSide.Right: return PortSide.Left;
                case PortSide.Top: return PortSide.Bottom;
                default: return PortSide.Top;
            }
        }

        private Rect GetNodeRect(LevelGraphNode node)
        {
            if (nodeElements.TryGetValue(node.levelId, out var element))
                return new Rect(element.style.left.value.value, element.style.top.value.value, NodeWidth, NodeHeight);
            return new Rect(node.editorX, node.editorY, NodeWidth, NodeHeight);
        }

        private void UpdatePortPlacements(LevelGraphDefinition graph)
        {
            var centers = new Dictionary<string, Vector2>();
            var incoming = new Dictionary<string, List<Vector2>>();
            var outgoing = new Dictionary<string, List<Vector2>>();
            foreach (var node in graph.nodes)
            {
                centers[node.levelId] = GetNodeRect(node).center;
                incoming[node.levelId] = new List<Vector2>();
                outgoing[node.levelId] = new List<Vector2>();
            }
            foreach (var source in graph.nodes)
            {
                foreach (var targetId in source.successors ?? Enumerable.Empty<string>())
                {
                    if (!centers.ContainsKey(targetId)) continue;
                    outgoing[source.levelId].Add(centers[targetId]);
                    incoming[targetId].Add(centers[source.levelId]);
                }
            }

            foreach (var node in graph.nodes)
            {
                Rect rect = GetNodeRect(node);
                var input = ChoosePortPlacement(rect, incoming[node.levelId], false);
                var output = ChoosePortPlacement(rect, outgoing[node.levelId], true);
                SeparateOverlappingPorts(rect, ref input, ref output);
                inputPlacements[node.levelId] = input;
                outputPlacements[node.levelId] = output;
                PlacePort(inputPorts[node.levelId], rect, input);
                PlacePort(outputPorts[node.levelId], rect, output);
            }
        }

        private static PortPlacement ChoosePortPlacement(Rect rect, List<Vector2> neighbors, bool output)
        {
            if (neighbors.Count == 0)
                return new PortPlacement(output
                    ? new Vector2(rect.xMax, rect.center.y)
                    : new Vector2(rect.xMin, rect.center.y), output ? PortSide.Right : PortSide.Left);

            Vector2 average = Vector2.zero;
            foreach (var center in neighbors) average += center;
            average /= neighbors.Count;
            PortSide[] sides = output ? OutputSidePriority : InputSidePriority;
            PortPlacement best = default;
            float bestScore = float.PositiveInfinity;
            foreach (var side in sides)
            {
                var candidate = new PortPlacement(PointOnSide(rect, side, average), side);
                float score = 0;
                foreach (var center in neighbors) score += Vector2.Distance(candidate.Position, center);
                if (score >= bestScore - 0.01f) continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        private static Vector2 PointOnSide(Rect rect, PortSide side, Vector2 toward)
        {
            const float margin = PortSize / 2 + 4;
            switch (side)
            {
                case PortSide.Left:
                    return new Vector2(rect.xMin, Mathf.Clamp(toward.y, rect.yMin + margin, rect.yMax - margin));
                case PortSide.Right:
                    return new Vector2(rect.xMax, Mathf.Clamp(toward.y, rect.yMin + margin, rect.yMax - margin));
                case PortSide.Top:
                    return new Vector2(Mathf.Clamp(toward.x, rect.xMin + margin, rect.xMax - margin), rect.yMin);
                default:
                    return new Vector2(Mathf.Clamp(toward.x, rect.xMin + margin, rect.xMax - margin), rect.yMax);
            }
        }

        private static void SeparateOverlappingPorts(Rect rect, ref PortPlacement input, ref PortPlacement output)
        {
            if (input.Side != output.Side || Vector2.Distance(input.Position, output.Position) >= PortSize + 4) return;
            const float halfGap = (PortSize + 4) / 2f;
            const float margin = PortSize / 2 + 4;
            bool vertical = input.Side == PortSide.Left || input.Side == PortSide.Right;
            float min = (vertical ? rect.yMin : rect.xMin) + margin;
            float max = (vertical ? rect.yMax : rect.xMax) - margin;
            float inputAxis = vertical ? input.Position.y : input.Position.x;
            float outputAxis = vertical ? output.Position.y : output.Position.x;
            float middle = Mathf.Clamp((inputAxis + outputAxis) * 0.5f, min + halfGap, max - halfGap);
            input = new PortPlacement(vertical
                ? new Vector2(input.Position.x, middle - halfGap)
                : new Vector2(middle - halfGap, input.Position.y), input.Side);
            output = new PortPlacement(vertical
                ? new Vector2(output.Position.x, middle + halfGap)
                : new Vector2(middle + halfGap, output.Position.y), output.Side);
        }

        private static void PlacePort(VisualElement port, Rect rect, PortPlacement placement)
        {
            port.style.left = placement.Position.x - rect.xMin - PortSize / 2;
            port.style.top = placement.Position.y - rect.yMin - PortSize / 2;
        }

        private void DrawNode(LevelGraphNode node)
        {
            var element = new VisualElement();
            element.style.position = Position.Absolute;
            element.style.left = node.editorX;
            element.style.top = node.editorY;
            element.style.width = NodeWidth;
            element.style.height = NodeHeight;
            element.style.paddingLeft = 9;
            element.style.paddingRight = 9;
            element.style.paddingTop = 8;
            element.style.overflow = Overflow.Visible;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = 8;
            element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = 8;
            element.style.backgroundColor = selectedIds.Contains(node.levelId) || selectedId == node.levelId
                ? new Color(0.23f, 0.52f, 0.73f) : new Color(0.25f, 0.31f, 0.39f);
            element.Add(new Label(node.address.Substring(node.address.LastIndexOf('/') + 1)) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            element.Add(new Label($"#{node.selectOrder}   {node.unlockMode}"));
            canvas.Add(element);
            nodeElements[node.levelId] = element;

            var inputPort = CreatePort(false, "Drop a connection here");
            element.Add(inputPort);
            inputPorts[node.levelId] = inputPort;

            var outputPort = CreatePort(true, "Drag to another node to connect");
            element.Add(outputPort);
            outputPorts[node.levelId] = outputPort;
            RegisterLinkDrag(outputPort, node.levelId);

            Vector2 startPointer = Vector2.zero;
            bool moved = false;
            bool deselectedByModifier = false;
            bool modifierSelectionGesture = false;
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                modifierSelectionGesture = evt.shiftKey || evt.ctrlKey || evt.commandKey;
                if (modifierSelectionGesture)
                {
                    if (!selectedIds.Add(node.levelId))
                    {
                        selectedIds.Remove(node.levelId);
                        deselectedByModifier = true;
                    }
                    else deselectedByModifier = false;
                    selectedId = selectedIds.Count == 1 ? selectedIds.First() : null;
                    UpdateNodeSelectionAppearance();
                }
                else if (!selectedIds.Contains(node.levelId))
                {
                    deselectedByModifier = false;
                    selectedIds.Clear();
                    selectedIds.Add(node.levelId);
                    selectedId = node.levelId;
                    UpdateNodeSelectionAppearance();
                }
                else deselectedByModifier = false;
                if (deselectedByModifier)
                {
                    evt.StopPropagation();
                    return;
                }
                startPointer = evt.position;
                groupDragStartPositions.Clear();
                foreach (var selected in EditorSessionService.Graph.nodes.Where(item => selectedIds.Contains(item.levelId)))
                    groupDragStartPositions[selected.levelId] = new Vector2(selected.editorX, selected.editorY);
                if (!groupDragStartPositions.ContainsKey(node.levelId))
                {
                    selectedIds.Clear();
                    selectedIds.Add(node.levelId);
                    selectedId = node.levelId;
                    groupDragStartPositions[node.levelId] = new Vector2(node.editorX, node.editorY);
                }
                nodeGroupDragStart = canvas.WorldToLocal((Vector2)evt.position);
                isMovingNodeGroup = false;
                moved = false;
                element.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            element.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!element.HasPointerCapture(evt.pointerId)) return;
                var pointerDelta = (Vector2)evt.position - startPointer;
                if (pointerDelta.sqrMagnitude > 9) moved = true;
                if (!moved) return;
                isMovingNodeGroup = true;
                var delta = canvas.WorldToLocal((Vector2)evt.position) - nodeGroupDragStart;
                foreach (var pair in groupDragStartPositions)
                {
                    if (!nodeElements.TryGetValue(pair.Key, out var movingElement)) continue;
                    movingElement.style.left = pair.Value.x + delta.x;
                    movingElement.style.top = pair.Value.y + delta.y;
                }
                UpdatePortPlacements(EditorSessionService.Graph);
                edgeLayer?.MarkDirtyRepaint();
            });
            element.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!element.HasPointerCapture(evt.pointerId)) return;
                element.ReleasePointer(evt.pointerId);
                if (moved && isMovingNodeGroup)
                {
                    if (EditorSessionService.Graph.nodes.All(item => !groupDragStartPositions.ContainsKey(item.levelId)))
                    {
                        Refresh();
                        evt.StopPropagation();
                        return;
                    }
                    if (!EditorSessionService.EditGraph("Move Level Node", g =>
                    {
                        var delta = canvas.WorldToLocal((Vector2)evt.position) - nodeGroupDragStart;
                        foreach (var pair in groupDragStartPositions)
                        {
                            var edit = g.nodes.Find(item => item.levelId == pair.Key);
                            if (edit == null) continue;
                            edit.editorX = pair.Value.x + delta.x;
                            edit.editorY = pair.Value.y + delta.y;
                        }
                    })) ShowError();
                }
                else if (!moved)
                {
                    if (!modifierSelectionGesture)
                    {
                        selectedIds.Clear();
                        selectedIds.Add(node.levelId);
                    }
                    selectedId = selectedIds.Count == 1 ? selectedIds.First() : null;
                    Refresh();
                    graphViewport.Focus();
                }
                isMovingNodeGroup = false;
                groupDragStartPositions.Clear();
                evt.StopPropagation();
            });
        }

        private static VisualElement CreatePort(bool output, string tooltip)
        {
            var port = new VisualElement { name = output ? "output-port" : "input-port", tooltip = tooltip };
            port.style.position = Position.Absolute;
            port.style.width = PortSize;
            port.style.height = PortSize;
            port.style.borderTopLeftRadius = PortSize / 2;
            port.style.borderTopRightRadius = PortSize / 2;
            port.style.borderBottomLeftRadius = PortSize / 2;
            port.style.borderBottomRightRadius = PortSize / 2;
            port.style.borderLeftWidth = 2;
            port.style.borderRightWidth = 2;
            port.style.borderTopWidth = 2;
            port.style.borderBottomWidth = 2;
            port.style.borderLeftColor = new Color(0.72f, 0.88f, 1f);
            port.style.borderRightColor = new Color(0.72f, 0.88f, 1f);
            port.style.borderTopColor = new Color(0.72f, 0.88f, 1f);
            port.style.borderBottomColor = new Color(0.72f, 0.88f, 1f);
            port.style.backgroundColor = output ? new Color(0.35f, 0.72f, 1f) : new Color(0.12f, 0.16f, 0.22f);
            return port;
        }

        private void RegisterLinkDrag(VisualElement outputPort, string sourceId)
        {
            outputPort.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                linkSourceId = sourceId;
                linkPointerId = evt.pointerId;
                linkOutputPort = outputPort;
                linkPointerCanvasPosition = canvas.WorldToLocal((Vector2)evt.position);
                selectedId = sourceId;
                graphViewport.Focus();
                outputPort.CapturePointer(evt.pointerId);
                errorLabel.text = "Drag onto another node's input port or body. Release on empty space to cancel.";
                edgeLayer?.MarkDirtyRepaint();
                evt.StopImmediatePropagation();
            });
            outputPort.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (linkSourceId != sourceId || !outputPort.HasPointerCapture(evt.pointerId)) return;
                linkPointerCanvasPosition = canvas.WorldToLocal((Vector2)evt.position);
                SetHoveredLinkTarget(FindLinkTarget((Vector2)evt.position, sourceId));
                edgeLayer?.MarkDirtyRepaint();
                evt.StopImmediatePropagation();
            });
            outputPort.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (linkSourceId != sourceId || !outputPort.HasPointerCapture(evt.pointerId)) return;
                string targetId = FindLinkTarget((Vector2)evt.position, sourceId);
                linkSourceId = null;
                hoveredLinkTargetId = null;
                linkPointerId = -1;
                linkOutputPort = null;
                outputPort.ReleasePointer(evt.pointerId);
                if (targetId == null)
                {
                    RestoreInputPortColors();
                    errorLabel.text = "";
                    edgeLayer?.MarkDirtyRepaint();
                }
                else
                {
                    EditorSessionService.ConnectLevels(sourceId, targetId);
                    Refresh();
                }
                evt.StopImmediatePropagation();
            });
            outputPort.RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                if (linkSourceId == sourceId && linkPointerId == evt.pointerId) CancelLinkDrag();
            });
        }

        private string FindLinkTarget(Vector2 panelPosition, string sourceId)
        {
            foreach (var pair in inputPorts)
            {
                if (pair.Key == sourceId) continue;
                var hitArea = pair.Value.worldBound;
                hitArea.xMin -= 7;
                hitArea.xMax += 7;
                hitArea.yMin -= 7;
                hitArea.yMax += 7;
                if (hitArea.Contains(panelPosition)) return pair.Key;
            }
            foreach (var pair in nodeElements)
            {
                if (pair.Key != sourceId && pair.Value.worldBound.Contains(panelPosition)) return pair.Key;
            }
            return null;
        }

        private void SetHoveredLinkTarget(string targetId)
        {
            if (hoveredLinkTargetId == targetId) return;
            RestoreInputPortColors();
            hoveredLinkTargetId = targetId;
            if (targetId != null && inputPorts.TryGetValue(targetId, out var port))
                port.style.backgroundColor = new Color(0.42f, 1f, 0.62f);
        }

        private void RestoreInputPortColors()
        {
            foreach (var port in inputPorts.Values) port.style.backgroundColor = new Color(0.12f, 0.16f, 0.22f);
        }

        private void CancelLinkDrag(bool repaint = true)
        {
            var capturedPort = linkOutputPort;
            int capturedPointerId = linkPointerId;
            linkSourceId = null;
            hoveredLinkTargetId = null;
            linkPointerId = -1;
            linkOutputPort = null;
            if (capturedPort != null && capturedPointerId >= 0 && capturedPort.HasPointerCapture(capturedPointerId))
                capturedPort.ReleasePointer(capturedPointerId);
            RestoreInputPortColors();
            if (!repaint) return;
            if (errorLabel != null) errorLabel.text = "";
            edgeLayer?.MarkDirtyRepaint();
        }

        private void DrawInspector(LevelGraphDefinition graph)
        {
            inspector.Clear();
            if (selectedIds.Count > 1)
            {
                inspector.SetEnabled(false);
                inspector.Add(new Label($"{selectedIds.Count} nodes selected") { style = { fontSize = 19, marginBottom = 12 } });
                inspector.Add(new Label("Move the selected nodes together by dragging any selected node.")
                    { style = { whiteSpace = WhiteSpace.Normal } });
                return;
            }
            inspector.SetEnabled(true);
            var node = graph.nodes.Find(item => item.levelId == selectedId);
            inspector.Add(new Label(node == null ? "Select a level node" : "Level settings") { style = { fontSize = 19, marginBottom = 12 } });
            if (node == null) return;
            var file = EditorSessionService.FindById(node.levelId);
            string stem = node.address.Substring(node.address.LastIndexOf('/') + 1);
            var rename = new TextField("File name") { value = stem };
            inspector.Add(rename);
            inspector.Add(Button("Rename", () => { if (!EditorSessionService.RenameLevel(node.levelId, rename.value)) ShowError(); }));
            inspector.Add(new Label("Stable ID: " + node.levelId) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 12 } });
            var unlock = new EnumField("Unlock", node.unlockMode);
            unlock.RegisterValueChangedCallback(evt => EditorSessionService.EditGraph("Change Unlock Rule", g =>
                g.nodes.Find(item => item.levelId == node.levelId).unlockMode = (UnlockMode)evt.newValue));
            inspector.Add(unlock);
            bool isEntry = graph.entryLevelIds.Contains(node.levelId);
            var entry = new Toggle("Entry level") { value = isEntry };
            entry.RegisterValueChangedCallback(evt => EditorSessionService.EditGraph("Set Entry Level", g =>
            {
                if (evt.newValue && !g.entryLevelIds.Contains(node.levelId)) g.entryLevelIds.Add(node.levelId);
                if (!evt.newValue) g.entryLevelIds.Remove(node.levelId);
            }));
            inspector.Add(entry);
            inspector.Add(Button("Selection order up", () => SwapOrder(node.levelId, -1)));
            inspector.Add(Button("Selection order down", () => SwapOrder(node.levelId, 1)));
            inspector.Add(new Label("Drag the right edge port onto another node to connect.")
                { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8, marginBottom = 8, color = new Color(0.72f, 0.82f, 0.92f) } });
            inspector.Add(Button("Edit 3D level", () =>
            {
                if (file == null) return;
                EditorSessionService.Select(file.path);
                sceneEditMode = true;
                Refresh();
                ToolManager.SetActiveTool<LevelPaintTool>();
                LevelPaintTool.FrameSelectedLevel();
            }));
            inspector.Add(new Label("Next levels (in order)") { style = { marginTop = 15, unityFontStyleAndWeight = FontStyle.Bold } });
            for (int i = 0; i < node.successors.Count; i++)
            {
                int index = i;
                var successor = graph.nodes.Find(item => item.levelId == node.successors[index]);
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                row.Add(new Label(successor == null ? "Missing" : successor.address.Split('/').Last()) { style = { flexGrow = 1 } });
                row.Add(Button("↑", () => MoveSuccessor(node.levelId, index, -1)));
                row.Add(Button("↓", () => MoveSuccessor(node.levelId, index, 1)));
                row.Add(Button("×", () => EditorSessionService.EditGraph("Disconnect Levels", g =>
                    g.nodes.Find(item => item.levelId == node.levelId).successors.RemoveAt(index))));
                inspector.Add(row);
            }
            inspector.Add(Button("Delete level", () =>
            {
                if (!EditorUtility.DisplayDialog("Delete level", "Delete this level JSON and graph node? Ctrl+Z restores it in this editor session.", "Delete", "Cancel")) return;
                EditorSessionService.DeleteLevel(node.levelId);
                selectedId = null;
                Refresh();
            }));
        }

        private void SwapOrder(string levelId, int offset)
        {
            var ordered = EditorSessionService.Graph.nodes.OrderBy(item => item.selectOrder).ToList();
            int index = ordered.FindIndex(item => item.levelId == levelId);
            int next = index + offset;
            if (index < 0 || next < 0 || next >= ordered.Count) return;
            EditorSessionService.EditGraph("Reorder Level Selection", graph =>
            {
                var a = graph.nodes.Find(item => item.levelId == ordered[index].levelId);
                var b = graph.nodes.Find(item => item.levelId == ordered[next].levelId);
                (a.selectOrder, b.selectOrder) = (b.selectOrder, a.selectOrder);
            });
        }

        private void MoveSuccessor(string levelId, int index, int offset)
        {
            EditorSessionService.EditGraph("Reorder Next Levels", graph =>
            {
                var list = graph.nodes.Find(item => item.levelId == levelId).successors;
                int next = index + offset;
                if (next < 0 || next >= list.Count) return;
                (list[index], list[next]) = (list[next], list[index]);
            });
        }

        private void ValidateAll()
        {
            var issues = ProjectValidator.Validate(EditorSessionService.Session, true).issues;
            errorLabel.text = issues.Count == 0 ? "All levels and references are valid." : issues[0].ToString() + $"  (+{issues.Count - 1} more)";
        }

        private void ShowError() { errorLabel.text = EditorSessionService.LastError; }

        private void UpdateNodeSelectionAppearance()
        {
            foreach (var pair in nodeElements)
                pair.Value.style.backgroundColor = selectedIds.Contains(pair.Key) || selectedId == pair.Key
                    ? new Color(0.23f, 0.52f, 0.73f) : new Color(0.25f, 0.31f, 0.39f);
            DrawInspector(EditorSessionService.Graph);
        }

        private static Vector2 FindAvailableNodePosition(List<LevelGraphNode> nodes, Vector2 desired)
        {
            const float gapX = 24f;
            const float gapY = 24f;
            float stepX = NodeWidth + gapX;
            float stepY = NodeHeight + gapY;
            for (int ring = 0; ring < 128; ring++)
            {
                for (int y = -ring; y <= ring; y++)
                {
                    for (int x = -ring; x <= ring; x++)
                    {
                        if (ring > 0 && Mathf.Abs(x) != ring && Mathf.Abs(y) != ring) continue;
                        Vector2 candidate = desired + new Vector2(x * stepX, y * stepY);
                        var candidateRect = new Rect(candidate.x - gapX * 0.5f, candidate.y - gapY * 0.5f,
                            NodeWidth + gapX, NodeHeight + gapY);
                        bool overlaps = nodes.Any(node => candidateRect.Overlaps(
                            new Rect(node.editorX, node.editorY, NodeWidth, NodeHeight)));
                        if (!overlaps) return candidate;
                    }
                }
            }
            return desired + new Vector2(nodes.Count * stepX, nodes.Count * stepY);
        }

        private static Button Button(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.style.marginRight = 5;
            button.style.marginBottom = 5;
            return button;
        }
    }
}
