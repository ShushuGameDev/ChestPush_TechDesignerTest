using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChestPush.GameFlow
{
    public sealed class DemoUI
    {
        private sealed class DirectionHint
        {
            public readonly RectTransform root;
            public readonly RectTransform arrow;

            public DirectionHint(RectTransform root, RectTransform arrow)
            {
                this.root = root;
                this.arrow = arrow;
            }
        }

        private readonly Canvas canvas;
        private readonly Font font;
        private GameObject content;
        private GameObject directionHintLayer;
        private Text hudText;
        private DirectionHint wHint;
        private DirectionHint aHint;
        private DirectionHint sHint;
        private DirectionHint dHint;

        public DemoUI()
        {
            font = Resources.Load<Font>("Font/SourceHanSansSC-Regular");
            if (font == null)
            {
                Debug.LogWarning("SourceHanSansSC-Regular could not be loaded from Resources/Font; using Unity's legacy runtime font.");
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            var canvasObject = new GameObject("ChestPush UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public void Dispose()
        {
            if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
        }

        public void Hide() => Clear();

        public void ShowMenu(string title, string message, IEnumerable<(string label, Action action, bool enabled)> buttons)
        {
            Clear();
            content = Panel("Menu Background", canvas.transform, new Color(0.055f, 0.08f, 0.13f, 0.92f));
            Stretch(content.GetComponent<RectTransform>());
            var card = Panel("Menu Card", content.transform, new Color(0.13f, 0.18f, 0.26f, 0.96f));
            var rect = card.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(520, 570);
            rect.anchoredPosition = Vector2.zero;
            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(30, 30, 28, 28);
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = false;
            AddText(card.transform, title, 32, 58, FontStyle.Bold);
            if (!string.IsNullOrEmpty(message)) AddText(card.transform, message, 17, 64, FontStyle.Normal);
            var viewport = new GameObject("Choices", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            viewport.transform.SetParent(card.transform, false);
            viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.1f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            viewport.AddComponent<LayoutElement>().flexibleHeight = 1;
            var choices = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            choices.transform.SetParent(viewport.transform, false);
            var choicesRect = choices.GetComponent<RectTransform>();
            choicesRect.anchorMin = new Vector2(0, 1);
            choicesRect.anchorMax = new Vector2(1, 1);
            choicesRect.pivot = new Vector2(0.5f, 1);
            choicesRect.sizeDelta = new Vector2(0, 0);
            var choicesLayout = choices.GetComponent<VerticalLayoutGroup>();
            choicesLayout.spacing = 8;
            choicesLayout.childForceExpandHeight = false;
            choicesLayout.childControlHeight = false;
            choices.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.content = choicesRect;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.horizontal = false;
            foreach (var option in buttons) AddButton(choices.transform, option.label, option.action, option.enabled);
        }

        public void ShowHud(string levelName, int actionCount, Action undo, Action restart, Action select)
        {
            Clear();
            content = Panel("HUD", canvas.transform, new Color(0.07f, 0.1f, 0.15f, 0.82f));
            var rect = content.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(0, 76);
            var layout = content.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 13, 13);
            layout.spacing = 10;
            layout.childForceExpandWidth = false;
            layout.childControlWidth = false;
            hudText = AddText(content.transform, $"{levelName}    Moves: {actionCount}", 20, 48, FontStyle.Bold);
            hudText.GetComponent<LayoutElement>().flexibleWidth = 1;
            AddButton(content.transform, "Undo (Z)", undo, true, 130);
            AddButton(content.transform, "Restart (R)", restart, true, 130);
            AddButton(content.transform, "Levels (Esc)", select, true, 145);
            directionHintLayer = new GameObject("Direction Hints", typeof(RectTransform));
            directionHintLayer.transform.SetParent(canvas.transform, false);
            Stretch(directionHintLayer.GetComponent<RectTransform>());
            wHint = AddDirectionHint(directionHintLayer.transform, "W");
            aHint = AddDirectionHint(directionHintLayer.transform, "A");
            sHint = AddDirectionHint(directionHintLayer.transform, "S");
            dHint = AddDirectionHint(directionHintLayer.transform, "D");
        }

        public void SetHud(string levelName, int actionCount)
        {
            if (hudText != null) hudText.text = $"{levelName}    Moves: {actionCount}";
        }

        public void SetDirectionHints(Camera camera, Vector3 w, Vector3 a, Vector3 s, Vector3 d)
        {
            if (directionHintLayer == null) return;
            var centerScreenPosition = camera == null ? Vector3.zero :
                camera.WorldToScreenPoint((w + a + s + d) * 0.25f);
            SetDirectionHint(wHint, camera, w, centerScreenPosition);
            SetDirectionHint(aHint, camera, a, centerScreenPosition);
            SetDirectionHint(sHint, camera, s, centerScreenPosition);
            SetDirectionHint(dHint, camera, d, centerScreenPosition);
        }

        private GameObject Panel(string name, Transform parent, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            return panel;
        }

        private Text AddText(Transform parent, string value, int size, float height, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.text = value;
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return label;
        }

        private void AddButton(Transform parent, string label, Action action, bool enabled, float width = -1)
        {
            var go = new GameObject("Button " + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = enabled ? new Color(0.18f, 0.45f, 0.67f) : new Color(0.25f, 0.28f, 0.34f);
            var button = go.GetComponent<Button>();
            button.interactable = enabled;
            if (action != null) button.onClick.AddListener(() => action());
            var element = go.GetComponent<LayoutElement>();
            element.preferredHeight = 46;
            if (width > 0) element.preferredWidth = width;
            var text = AddText(go.transform, label, 18, 46, FontStyle.Normal);
            text.alignment = TextAnchor.MiddleCenter;
            Stretch(text.GetComponent<RectTransform>());
        }

        private DirectionHint AddDirectionHint(Transform parent, string key)
        {
            var go = new GameObject("Move " + key, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(54f, 54f);

            var arrowObject = new GameObject("Arrow", typeof(RectTransform));
            arrowObject.transform.SetParent(go.transform, false);
            var arrow = arrowObject.GetComponent<RectTransform>();
            arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 0.5f);
            arrow.sizeDelta = new Vector2(50f, 50f);
            var arrowColor = new Color(0.68f, 0.76f, 0.82f, 0.30f);
            AddArrowPart(arrow, "Shaft", new Vector2(7f, 28f), new Vector2(0f, -7f), 0f, arrowColor);
            AddArrowPart(arrow, "Head Left", new Vector2(7f, 22f), new Vector2(-7f, 10f), -45f, arrowColor);
            AddArrowPart(arrow, "Head Right", new Vector2(7f, 22f), new Vector2(7f, 10f), 45f, arrowColor);

            var text = AddText(go.transform, key, 20, 54, FontStyle.Bold);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.95f, 0.97f, 1f, 0.94f);
            text.raycastTarget = false;
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.03f, 0.05f, 0.08f, 0.72f);
            outline.effectDistance = new Vector2(1f, -1f);
            Stretch(text.GetComponent<RectTransform>());
            return new DirectionHint(rect, arrow);
        }

        private static void AddArrowPart(Transform parent, string name, Vector2 size, Vector2 position,
            float rotation, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private void SetDirectionHint(DirectionHint hint, Camera camera, Vector3 worldPosition,
            Vector3 centerScreenPosition)
        {
            if (hint == null) return;
            if (camera == null)
            {
                hint.root.gameObject.SetActive(false);
                return;
            }
            var screenPosition = camera.WorldToScreenPoint(worldPosition);
            bool visible = screenPosition.z > 0f && screenPosition.x >= 0f && screenPosition.x <= Screen.width &&
                screenPosition.y >= 0f && screenPosition.y <= Screen.height;
            hint.root.gameObject.SetActive(visible);
            if (!visible) return;
            var layerRect = directionHintLayer.GetComponent<RectTransform>();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(layerRect, screenPosition, null, out var localPosition))
                hint.root.anchoredPosition = localPosition;
            var screenDirection = screenPosition - centerScreenPosition;
            float angle = Mathf.Atan2(screenDirection.y, screenDirection.x) * Mathf.Rad2Deg - 90f;
            hint.arrow.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void Clear()
        {
            if (content != null) UnityEngine.Object.Destroy(content);
            if (directionHintLayer != null) UnityEngine.Object.Destroy(directionHintLayer);
            content = null;
            directionHintLayer = null;
            hudText = null;
            wHint = null;
            aHint = null;
            sHint = null;
            dHint = null;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
