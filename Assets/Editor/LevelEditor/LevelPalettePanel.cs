using System;
using System.Linq;
using ChestPush.LevelData;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChestPush.LevelEditor
{
    internal static class LevelPalettePanel
    {
        public static void Build(VisualElement root)
        {
            var previousOffset = root.Q<ScrollView>()?.scrollOffset ?? Vector2.zero;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 8;
            var level = EditorSessionService.CurrentLevel;
            var catalog = EditorSessionService.Catalog;
            if (level == null || catalog == null)
            {
                root.Add(new Label("在 Tools > ChestPush > Level Editor 中选中关卡，再点击 Edit 3D level。")
                { style = { whiteSpace = WhiteSpace.Normal } });
                return;
            }
            root.Add(new Label("关卡编辑") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 16 } });
            root.Add(new Label(string.IsNullOrEmpty(EditorSessionService.LastError)
                ? EditorSessionService.SaveStatus : "本次修改未保存")
            { style = { color = string.IsNullOrEmpty(EditorSessionService.LastError)
                ? new Color(0.35f, 0.75f, 0.5f) : Color.red, marginTop = 5, marginBottom = 5 } });
            root.Add(new Label("机关属性请先点击 Apply，再返回目录。")
            { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 6 } });
            root.Add(Button("保存并返回目录", () =>
            {
                if (!LevelPaintTool.SaveAndReturnToGraph()) LevelGraphWindow.RefreshPalette();
            }));
            var scroll = new ScrollView { style = { flexGrow = 1 } };
            root.Add(scroll);
            scroll.schedule.Execute(() => scroll.scrollOffset = previousOffset);
            var body = scroll.contentContainer;
            body.Add(new Label("Level Layers") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            foreach (var layer in level.layers.OrderBy(item => item.baseY))
            {
                int id = layer.id;
                body.Add(Button($"{(EditorSessionService.Session.selectedLayerId == id ? "▶ " : "")}{id}  y={layer.baseY}", () =>
                {
                    EditorSessionService.Session.selectedLayerId = id;
                    SceneView.RepaintAll();
                    LevelGraphWindow.RefreshPalette();
                }));
            }
            body.Add(Button("Add layer (+2m)", () =>
            {
                int id = level.layers.Max(item => item.id) + 1;
                int y = level.layers.Max(item => item.baseY) + 2;
                if (EditorSessionService.EditLevel("Add Floor Layer", data => data.layers.Add(new FloorLayer { id = id, baseY = y })))
                    EditorSessionService.Session.selectedLayerId = id;
                LevelGraphWindow.RefreshPalette();
            }));
            var activeLayer = level.layers.Find(item => item.id == EditorSessionService.Session.selectedLayerId);
            if (activeLayer != null)
            {
                var height = new IntegerField("Base Y") { value = activeLayer.baseY };
                body.Add(height);
                body.Add(Button("Apply height", () =>
                {
                    EditorSessionService.EditLevel("Change Layer Height", data =>
                        data.layers.Find(item => item.id == activeLayer.id).baseY = height.value);
                    LevelGraphWindow.RefreshPalette();
                }));
                if (level.layers.Count > 1) body.Add(Button("Delete selected layer", () =>
                {
                    if (EditorSessionService.EditLevel("Delete Floor Layer", data =>
                    {
                        data.layers.RemoveAll(item => item.id == activeLayer.id);
                        data.placements.RemoveAll(item => item.layerId == activeLayer.id);
                    })) EditorSessionService.Session.selectedLayerId = EditorSessionService.CurrentLevel.layers[0].id;
                    LevelGraphWindow.RefreshPalette();
                }));
            }
            body.Add(new Label("Brush slot") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 12 } });
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            body.Add(row);
            foreach (PlacementSlot slot in Enum.GetValues(typeof(PlacementSlot)))
            {
                var target = slot;
                row.Add(Button($"{(EditorSessionService.Session.selectedSlot == slot ? "▶" : "")}{slot}", () =>
                {
                    EditorSessionService.Session.selectedSlot = target;
                    if (!LevelPaintTool.CanUseRectangle(EditorSessionService.Session))
                        EditorSessionService.Session.rectangleTool = false;
                    var first = catalog.entries.FirstOrDefault(entry => entry.category != BlockCategory.Player && SlotFor(entry.category) == target);
                    if (first != null) EditorSessionService.Session.selectedBlockId = first.id;
                    LevelGraphWindow.RefreshPalette();
                }));
            }
            var erase = new Toggle("Erase brush") { value = EditorSessionService.Session.erase };
            erase.RegisterValueChangedCallback(evt =>
            {
                EditorSessionService.Session.erase = evt.newValue;
                if (!LevelPaintTool.CanUseRectangle(EditorSessionService.Session))
                    EditorSessionService.Session.rectangleTool = false;
                LevelGraphWindow.RefreshPalette();
            });
            body.Add(erase);
            body.Add(new Label("Paint tool") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } });
            var toolRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            body.Add(toolRow);
            toolRow.Add(Button($"{(EditorSessionService.Session.rectangleTool ? "" : "▶ ")}Brush (B)", () =>
            {
                EditorSessionService.Session.rectangleTool = false;
                LevelGraphWindow.RefreshPalette();
            }));
            var rectangle = Button($"{(EditorSessionService.Session.rectangleTool ? "▶ " : "")}Rectangle (R)", () =>
            {
                EditorSessionService.Session.rectangleTool = true;
                LevelGraphWindow.RefreshPalette();
            });
            rectangle.SetEnabled(LevelPaintTool.CanUseRectangle(EditorSessionService.Session));
            toolRow.Add(rectangle);
            if (EditorSessionService.Session.rectangleTool)
            {
                var shapeRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                body.Add(shapeRow);
                shapeRow.Add(Button($"{(EditorSessionService.Session.rectangleOutline ? "" : "▶ ")}Fill", () =>
                {
                    EditorSessionService.Session.rectangleOutline = false;
                    LevelGraphWindow.RefreshPalette();
                }));
                shapeRow.Add(Button($"{(EditorSessionService.Session.rectangleOutline ? "▶ " : "")}Outline", () =>
                {
                    EditorSessionService.Session.rectangleOutline = true;
                    LevelGraphWindow.RefreshPalette();
                }));
            }
            body.Add(new Label("Blocks") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } });
            var grid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            body.Add(grid);
            foreach (var entry in catalog.entries.Where(item => item.category != BlockCategory.Player &&
                SlotFor(item.category) == EditorSessionService.Session.selectedSlot))
            {
                var block = entry;
                var card = Button("", () =>
                {
                    EditorSessionService.Session.selectedBlockId = block.id;
                    EditorSessionService.Session.erase = false;
                    if (!LevelPaintTool.CanUseRectangle(EditorSessionService.Session))
                        EditorSessionService.Session.rectangleTool = false;
                    LevelGraphWindow.RefreshPalette();
                });
                card.tooltip = block.id;
                card.style.width = 116;
                card.style.height = 132;
                card.style.marginRight = 6;
                card.style.paddingTop = 5;
                card.style.paddingBottom = 5;
                card.style.alignItems = Align.Center;
                card.style.flexDirection = FlexDirection.Column;
                card.style.backgroundColor = EditorSessionService.Session.selectedBlockId == block.id
                    ? new Color(0.22f, 0.48f, 0.68f) : new Color(0.22f, 0.25f, 0.30f);
                var preview = new Image { scaleMode = ScaleMode.ScaleToFit, style = { width = 88, height = 88 } };
                SetPreview(preview, block);
                card.Add(preview);
                card.Add(new Label(block.id) { style = { width = 104, unityTextAlign = TextAnchor.MiddleCenter,
                    whiteSpace = WhiteSpace.Normal, marginTop = 4 } });
                grid.Add(card);
            }
            body.Add(new Label(EditorSessionService.Session.rectangleTool
                ? "Left drag: rectangle    Esc: cancel    Right click: configure"
                : "Left click: paint    Right click: configure") { style = { marginTop = 8 } });
            DrawSelectedProperties(body, level, catalog);
            if (!string.IsNullOrEmpty(EditorSessionService.LastError))
                body.Add(new Label(EditorSessionService.LastError) { style = { color = Color.red, whiteSpace = WhiteSpace.Normal } });
        }

        private static void SetPreview(Image image, BlockEntry entry)
        {
            var path = AssetDatabase.GUIDToAssetPath(entry.prefab?.AssetGUID);
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;
            var thumbnail = AssetPreview.GetMiniThumbnail(prefab);
            image.image = AssetPreview.GetAssetPreview(prefab) ?? thumbnail;
            if (image.image != thumbnail) return;
            int attempts = 0;
            image.schedule.Execute(() =>
            {
                attempts++;
                var preview = AssetPreview.GetAssetPreview(prefab);
                if (preview != null) image.image = preview;
            }).Every(250).Until(() => image.image != thumbnail || attempts >= 20);
        }

        private static void DrawSelectedProperties(VisualElement body, LevelDefinition level, BlockCatalog catalog)
        {
            var selected = level.placements.Find(item => item.instanceId == LevelPaintTool.SelectedPlacementId);
            if (selected == null) return;
            var entry = catalog.Find(selected.blockId);
            if (entry == null) return;
            body.Add(new Label($"Selected: {entry.id} ({selected.layerId}:{selected.x},{selected.z})")
            { style = { marginTop = 15, unityFontStyleAndWeight = FontStyle.Bold } });
            if (entry.kind == BlockKind.Breakable)
            {
                var modes = new EnumFlagsField("Break modes", selected.breakModes);
                var ids = new TextField("Switch IDs") { value = string.Join(",", selected.switchIds ?? Enumerable.Empty<string>()) };
                body.Add(modes);
                body.Add(ids);
                body.Add(Button("Apply wall", () =>
                {
                    EditorSessionService.EditLevel("Configure Breakable Wall", data =>
                    {
                        var wall = data.placements.Find(item => item.instanceId == selected.instanceId);
                        wall.breakModes = (BreakMode)modes.value;
                        wall.switchIds = ids.value.Split(',').Select(text => text.Trim()).Where(text => text.Length > 0).ToList();
                    });
                }));
            }
            else if (entry.kind == BlockKind.Teleport)
            {
                var allowed = new EnumFlagsField("Allowed", selected.allowedActors);
                var destination = selected.destination ?? selected.Point;
                var layer = new IntegerField("Exit layer") { value = destination.layerId };
                var x = new IntegerField("Exit X") { value = destination.x };
                var z = new IntegerField("Exit Z") { value = destination.z };
                body.Add(allowed); body.Add(layer); body.Add(x); body.Add(z);
                body.Add(Button("Apply teleport", () =>
                {
                    EditorSessionService.EditLevel("Configure Teleport", data =>
                    {
                        var portal = data.placements.Find(item => item.instanceId == selected.instanceId);
                        portal.allowedActors = (ActorMask)allowed.value;
                        portal.destination = new GridPoint(layer.value, x.value, z.value);
                    });
                }));
            }
            else if (entry.kind == BlockKind.PressurePlate)
            {
                var id = new TextField("Switch ID") { value = selected.switchId };
                body.Add(id);
                body.Add(Button("Apply plate", () => EditorSessionService.EditLevel("Configure Pressure Plate", data =>
                    data.placements.Find(item => item.instanceId == selected.instanceId).switchId = id.value)));
            }
        }

        private static PlacementSlot SlotFor(BlockCategory category)
        {
            switch (category)
            {
                case BlockCategory.Floor: return PlacementSlot.Floor;
                case BlockCategory.Structure: return PlacementSlot.Structure;
                case BlockCategory.Marker: return PlacementSlot.Marker;
                default: return PlacementSlot.Box;
            }
        }

        private static Button Button(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.style.marginBottom = 4;
            return button;
        }
    }
}
