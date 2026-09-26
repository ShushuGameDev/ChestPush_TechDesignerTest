using System;
using System.Collections.Generic;
using System.Linq;
using ChestPush.LevelData;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChestPush.GameFlow
{
    public sealed class SceneMenuUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup mainMenu;
        [SerializeField] private CanvasGroup chooseLevel;
        [SerializeField] private Button startButton;
        [SerializeField] private Button selectButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private RectTransform gridField;
        [SerializeField] private RectTransform hardLevel;

        private readonly List<GameObject> generatedButtons = new List<GameObject>();
        private readonly List<GameObject> levelButtons = new List<GameObject>();
        private List<LevelGraphNode> levels = new List<LevelGraphNode>();
        private Func<LevelGraphNode, bool> isUnlocked;
        private Action<LevelGraphNode> openLevel;
        private Action start;
        private Action select;
        private Action quit;
        private Action back;
        private string selectedDifficulty;
        private Texture2D buttonTexture;

        private void Awake()
        {
            ResolveReferences();
            buttonTexture = Resources.Load<Texture2D>("Texture/Light");
            if (buttonTexture == null) Debug.LogError("Resources/Texture/Light.png is missing.");
            Hide();
        }

        public void Bind(Action startAction, Action selectAction, Action quitAction, Action backAction)
        {
            start = startAction;
            select = selectAction;
            quit = quitAction;
            back = backAction;
            ResolveReferences();
            WireHomeButtons();
        }

        private void WireHomeButtons()
        {
            if (startButton == null || selectButton == null || quitButton == null) return;
            startButton.onClick.RemoveAllListeners();
            selectButton.onClick.RemoveAllListeners();
            quitButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(() => start());
            selectButton.onClick.AddListener(() => select());
            quitButton.onClick.AddListener(() => quit());
        }

        public bool ShowHome(bool canStart, bool canSelect)
        {
            ResolveReferences();
            WireHomeButtons();
            if (startButton == null || selectButton == null)
            {
                Debug.LogError("MainMenu buttons are missing; cannot show the home menu.");
                return false;
            }
            startButton.interactable = canStart;
            selectButton.interactable = canSelect;
            SetVisible(mainMenu, true);
            SetVisible(chooseLevel, false);
            return true;
        }

        public bool ShowLevelSelect(IEnumerable<LevelGraphNode> nodes, Func<LevelGraphNode, bool> unlocked,
            Action<LevelGraphNode> open)
        {
            ResolveReferences();
            if (selectButton == null || gridField == null || hardLevel == null || chooseLevel == null)
            {
                Debug.LogError("ChooseLevel references are missing; cannot show level selection.");
                return false;
            }
            levels = nodes.OrderBy(node => node.selectOrder).ToList();
            isUnlocked = unlocked;
            openLevel = open;
            var difficulties = levels.Select(DifficultyKey).Distinct()
                .OrderBy(DifficultyOrder).ThenBy(key => key, StringComparer.Ordinal).ToList();
            if (!difficulties.Contains(selectedDifficulty)) selectedDifficulty = difficulties.FirstOrDefault();

            ClearGenerated();
            foreach (var difficulty in difficulties)
            {
                var key = difficulty;
                var button = MakeButton(hardLevel, DifficultyLabel(key));
                button.gameObject.name = "Difficulty " + key;
                button.onClick.AddListener(() => SelectDifficulty(key));
                var image = button.GetComponent<RawImage>();
                if (image != null) image.color = key == selectedDifficulty ? Color.white : new Color(0.7f, 0.7f, 0.7f);
            }

            var backButton = MakeButton(chooseLevel.transform, "返回");
            var rect = backButton.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-16, -12);
            rect.sizeDelta = new Vector2(120, 48);
            backButton.onClick.AddListener(() => back());

            FillLevelButtons();
            SetVisible(mainMenu, false);
            SetVisible(chooseLevel, true);
            return true;
        }

        public void Hide()
        {
            ResolveReferences();
            SetVisible(mainMenu, false);
            SetVisible(chooseLevel, false);
        }

        private void SelectDifficulty(string key)
        {
            selectedDifficulty = key;
            foreach (Transform child in hardLevel)
            {
                if (!child.gameObject.activeSelf) continue;
                var image = child.GetComponent<RawImage>();
                if (image != null) image.color = child.name == "Difficulty " + key
                    ? Color.white : new Color(0.7f, 0.7f, 0.7f);
            }
            FillLevelButtons();
        }

        private void FillLevelButtons()
        {
            foreach (var child in levelButtons)
            {
                if (child == null) continue;
                generatedButtons.Remove(child);
                child.SetActive(false);
                Destroy(child);
            }
            levelButtons.Clear();

            int number = 0;
            foreach (var node in levels.Where(node => DifficultyKey(node) == selectedDifficulty))
            {
                number++;
                var target = node;
                bool unlocked = isUnlocked(node);
                var button = MakeButton(gridField, unlocked ? $"第{number}关" : $"第{number}关\n未解锁");
                levelButtons.Add(button.gameObject);
                button.interactable = unlocked;
                button.onClick.AddListener(() => openLevel(target));
            }
        }

        private Button MakeButton(Transform parent, string label)
        {
            var instance = Instantiate(selectButton.gameObject, parent, false);
            instance.name = "Menu Button " + label;
            generatedButtons.Add(instance);
            var button = instance.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.interactable = true;
            var image = instance.GetComponent<RawImage>();
            if (image != null)
            {
                if (buttonTexture != null) image.texture = buttonTexture;
                image.color = Color.white;
            }
            var text = instance.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.color = Color.black;
            }
            return button;
        }

        private void ClearGenerated()
        {
            foreach (var item in generatedButtons)
                if (item != null)
                {
                    item.SetActive(false);
                    Destroy(item);
                }
            generatedButtons.Clear();
            levelButtons.Clear();
        }

        private void ResolveReferences()
        {
            if (mainMenu == null) mainMenu = transform.Find("MainMenu")?.GetComponent<CanvasGroup>();
            if (chooseLevel == null) chooseLevel = transform.Find("ChooseLevel")?.GetComponent<CanvasGroup>();
            if (mainMenu != null)
            {
                if (startButton == null) startButton = mainMenu.transform.Find("Button")?.GetComponent<Button>();
                if (selectButton == null) selectButton = mainMenu.transform.Find("Button (1)")?.GetComponent<Button>();
                if (quitButton == null) quitButton = mainMenu.transform.Find("Button (2)")?.GetComponent<Button>();
            }
            if (chooseLevel != null)
            {
                if (gridField == null) gridField = chooseLevel.transform.Find("GridField") as RectTransform;
                if (hardLevel == null) hardLevel = chooseLevel.transform.Find("HardLevel") as RectTransform;
            }
        }

        private static string DifficultyKey(LevelGraphNode node)
        {
            string name = node.address ?? string.Empty;
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            int underscore = name.IndexOf('_');
            string key = (underscore >= 0 ? name.Substring(0, underscore) : name).ToLowerInvariant();
            if (key == "guide") return "tutorial";
            if (key == "normal") return "medium";
            return key;
        }

        private static int DifficultyOrder(string key)
        {
            switch (key)
            {
                case "tutorial": return 0;
                case "easy": return 1;
                case "medium": return 2;
                case "hard": return 3;
                default: return 4;
            }
        }

        private static string DifficultyLabel(string key)
        {
            switch (key)
            {
                case "tutorial": return "教程";
                case "easy": return "简单";
                case "medium": return "中等";
                case "hard": return "困难";
                default: return key;
            }
        }

        private static void SetVisible(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1 : 0;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }
    }
}
