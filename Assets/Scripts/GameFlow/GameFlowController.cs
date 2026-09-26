using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ChestPush.GridRules;
using ChestPush.LevelData;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ChestPush.GameFlow
{
    public sealed class GameFlowController : MonoBehaviour
    {
        private enum Screen { Loading, Home, Select, Playing, Complete }

        private DemoUI ui;
        private SceneMenuUI sceneMenu;
        private readonly LevelPresenter presenter = new LevelPresenter();
        private AsyncOperationHandle<BlockCatalog> catalogHandle;
        private AsyncOperationHandle<TextAsset> graphHandle;
        private AsyncOperationHandle<TextAsset> levelHandle;
        private AsyncOperationHandle<GameObject> victoryEffectHandle;
        private BlockCatalog catalog;
        private LevelGraphDefinition graph;
        private ProgressData progress;
        private GridWorld world;
        private LevelGraphNode currentNode;
        private Screen screen;
        private bool busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateController()
        {
            if (FindObjectOfType<GameFlowController>() == null)
                new GameObject("ChestPush Game Flow").AddComponent<GameFlowController>();
        }

        private void Start()
        {
            DontDestroyOnLoad(gameObject);
            ui = new DemoUI();
            sceneMenu = FindObjectOfType<SceneMenuUI>();
            if (sceneMenu == null) Debug.LogError("MainScene needs a SceneMenuUI on MenuCanvas.");
            else
            {
                sceneMenu.Bind(StartFirstLevel, ShowLevelSelect, QuitGame, ShowHome);
                sceneMenu.Hide();
            }
            progress = ProgressStore.Load();
            screen = Screen.Loading;
            ui.ShowMenu("ChestPush", "Loading level catalog...", Array.Empty<(string, Action, bool)>());
            StartCoroutine(LoadCore());
        }

        private IEnumerator LoadCore()
        {
            catalogHandle = Addressables.LoadAssetAsync<BlockCatalog>(LevelPaths.CatalogAddress);
            yield return catalogHandle;
            if (catalogHandle.Status != AsyncOperationStatus.Succeeded) { ShowError("Block catalog could not be loaded."); yield break; }
            catalog = catalogHandle.Result;
            graphHandle = Addressables.LoadAssetAsync<TextAsset>(LevelPaths.GraphAddress);
            yield return graphHandle;
            if (graphHandle.Status != AsyncOperationStatus.Succeeded) { ShowError("Level graph could not be loaded."); yield break; }
            try { graph = LevelCodec.ReadGraph(graphHandle.Result.text); }
            catch (Exception exception) { ShowError("Invalid level graph: " + exception.Message); yield break; }
            var catalogCheck = LevelValidator.ValidateCatalog(catalog);
            if (!catalogCheck.IsValid) { ShowError(catalogCheck.issues[0].ToString()); yield break; }
            var graphCheck = GraphValidator.Validate(graph, graph.nodes != null && graph.nodes.Count > 0);
            if (!graphCheck.IsValid) { ShowError(graphCheck.issues[0].ToString()); yield break; }
            victoryEffectHandle = Addressables.LoadAssetAsync<GameObject>(LevelPaths.VictoryEffectAddress);
            yield return victoryEffectHandle;
            if (victoryEffectHandle.Status != AsyncOperationStatus.Succeeded)
            {
                ShowError("Victory effect could not be loaded.");
                yield break;
            }
            ShowHome();
        }

        private void Update()
        {
            if (screen == Screen.Select && Input.GetKeyDown(KeyCode.Escape))
            {
                ShowHome();
                return;
            }
            if (screen != Screen.Playing || busy || world == null) return;
            if (Input.GetKeyDown(KeyCode.Q)) StartCoroutine(RotateCamera(-45f));
            else if (Input.GetKeyDown(KeyCode.E)) StartCoroutine(RotateCamera(45f));
            else if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) Move(presenter.ToWorldDirection(GridDirection.North));
            else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) Move(presenter.ToWorldDirection(GridDirection.East));
            else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) Move(presenter.ToWorldDirection(GridDirection.South));
            else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) Move(presenter.ToWorldDirection(GridDirection.West));
            else if (Input.GetKeyDown(KeyCode.Space)) Interact();
            else if (Input.GetKeyDown(KeyCode.Z)) Undo();
            else if (Input.GetKeyDown(KeyCode.R)) Restart();
            else if (Input.GetKeyDown(KeyCode.Escape)) ShowLevelSelect();
        }

        private void LateUpdate()
        {
            if (screen != Screen.Playing || ui == null) return;
            if (!presenter.TryGetDirectionHintPosition(GridDirection.North, out var w) ||
                !presenter.TryGetDirectionHintPosition(GridDirection.West, out var a) ||
                !presenter.TryGetDirectionHintPosition(GridDirection.South, out var s) ||
                !presenter.TryGetDirectionHintPosition(GridDirection.East, out var d)) return;
            ui.SetDirectionHints(Camera.main, w, a, s, d);
        }

        private IEnumerator RotateCamera(float yawDegrees)
        {
            busy = true;
            yield return presenter.RotateCamera(yawDegrees);
            busy = false;
        }

        private void ShowHome()
        {
            screen = Screen.Home;
            ui.Hide();
            var first = FirstLevel();
            if (sceneMenu != null && sceneMenu.ShowHome(first != null, graph.nodes.Count > 0)) return;
            var options = new List<(string, Action, bool)>();
            if (first != null) options.Add(("从头开始", () => StartCoroutine(OpenLevel(first)), true));
            options.Add(("选关", ShowLevelSelect, graph.nodes.Count > 0));
            options.Add(("退出", QuitGame, true));
            ui.ShowMenu("ChestPush", "", options);
        }

        private void ShowLevelSelect()
        {
            screen = Screen.Select;
            ui.Hide();
            if (sceneMenu != null && sceneMenu.ShowLevelSelect(graph.nodes, IsUnlocked,
                    node => StartCoroutine(OpenLevel(node)))) return;
            var options = new List<(string, Action, bool)>();
            foreach (var node in graph.nodes.OrderBy(item => item.selectOrder))
            {
                var target = node;
                bool unlocked = IsUnlocked(node);
                options.Add(($"{Name(node)}{(unlocked ? "" : "  [LOCKED]")}",
                    () => StartCoroutine(OpenLevel(target)), unlocked));
            }
            options.Add(("返回", ShowHome, true));
            ui.ShowMenu("选关", "", options);
        }

        private LevelGraphNode FirstLevel()
        {
            foreach (var id in graph.entryLevelIds)
            {
                var node = graph.nodes.FirstOrDefault(item => item.levelId == id);
                if (IsUnlocked(node)) return node;
            }
            return graph.nodes.Where(IsUnlocked).OrderBy(node => node.selectOrder).FirstOrDefault();
        }

        private void StartFirstLevel()
        {
            var node = FirstLevel();
            if (node != null) StartCoroutine(OpenLevel(node));
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private IEnumerator OpenLevel(LevelGraphNode node)
        {
            if (busy || !IsUnlocked(node)) yield break;
            busy = true;
            screen = Screen.Loading;
            sceneMenu?.Hide();
            ui.ShowMenu("Loading", Name(node), Array.Empty<(string, Action, bool)>());
            presenter.Clear();
            if (levelHandle.IsValid()) Addressables.Release(levelHandle);
            levelHandle = Addressables.LoadAssetAsync<TextAsset>(node.address);
            yield return levelHandle;
            if (levelHandle.Status != AsyncOperationStatus.Succeeded) { busy = false; ShowError("Level could not be loaded: " + node.address); yield break; }
            LevelDefinition level;
            try { level = LevelCodec.ReadLevel(levelHandle.Result.text); }
            catch (Exception exception) { busy = false; ShowError("Invalid level JSON: " + exception.Message); yield break; }
            if (level.levelId != node.levelId) { busy = false; ShowError("Level ID does not match graph node."); yield break; }
            var validation = LevelValidator.ValidateLevel(level, catalog);
            if (!validation.IsValid) { busy = false; ShowError(validation.issues[0].ToString()); yield break; }
            try { world = new GridWorld(level, catalog); }
            catch (Exception exception) { busy = false; ShowError(exception.Message); yield break; }
            currentNode = node;
            progress.lastSelectedLevelId = node.levelId;
            ProgressStore.Save(progress);
            yield return presenter.Build(level, catalog, world.Capture());
            busy = false;
            screen = Screen.Playing;
            ui.ShowHud(Name(node), world.ActionCount, Undo, Restart, ShowLevelSelect);
        }

        private bool IsUnlocked(LevelGraphNode node)
        {
            return node != null && GraphProgression.IsUnlocked(graph, progress.completedLevelIds, node.levelId);
        }

        private void Move(GridDirection direction)
        {
            var before = world.Capture();
            if (!world.TryMove(direction)) return;
            StartCoroutine(AnimateTurn(before));
        }

        private void Interact()
        {
            var before = world.Capture();
            if (!world.TryInteract()) return;
            StartCoroutine(AnimateTurn(before));
        }

        private IEnumerator AnimateTurn(WorldSnapshot before)
        {
            busy = true;
            yield return presenter.Animate(before, world.Capture());
            busy = false;
            if (screen != Screen.Playing) yield break;
            ui.SetHud(Name(currentNode), world.ActionCount);
            if (world.Completed) yield return CompleteLevel();
        }

        private void Undo()
        {
            if (busy || world == null || !world.Undo()) return;
            presenter.Apply(world.Capture());
            ui.SetHud(Name(currentNode), world.ActionCount);
        }

        private void Restart()
        {
            if (busy || world == null) return;
            world.Restart();
            presenter.Apply(world.Capture());
            ui.SetHud(Name(currentNode), world.ActionCount);
        }

        private IEnumerator CompleteLevel()
        {
            screen = Screen.Complete;
            if (!progress.completedLevelIds.Contains(currentNode.levelId)) progress.completedLevelIds.Add(currentNode.levelId);
            ProgressStore.Save(progress);
            ui.Hide();
            yield return presenter.PlayVictoryEffect(victoryEffectHandle.Result, world.Capture());
            yield return new WaitForSeconds(0.35f);
            var options = new List<(string, Action, bool)>();
            foreach (var id in currentNode.successors ?? new List<string>())
            {
                var next = graph.nodes.FirstOrDefault(node => node.levelId == id);
                if (next == null || !IsUnlocked(next)) continue;
                var target = next;
                options.Add(("Next: " + Name(next), () => StartCoroutine(OpenLevel(target)), true));
            }
            options.Add(("Level Select", ShowLevelSelect, true));
            options.Add(("Replay", () => StartCoroutine(OpenLevel(currentNode)), true));
            ui.ShowMenu("Level Complete", "Choose the next level.", options);
        }

        private void ShowError(string message)
        {
            Debug.LogError(message);
            screen = Screen.Home;
            sceneMenu?.Hide();
            ui.ShowMenu("ChestPush Error", message, new[] { ("Back", (Action)ShowHome, graph != null) });
        }

        private static string Name(LevelGraphNode node)
        {
            if (node == null) return "Level";
            int slash = node.address == null ? -1 : node.address.LastIndexOf('/');
            return slash < 0 ? node.address : node.address.Substring(slash + 1);
        }

        private void OnDestroy()
        {
            presenter.Clear();
            ui?.Dispose();
            if (levelHandle.IsValid()) Addressables.Release(levelHandle);
            if (victoryEffectHandle.IsValid()) Addressables.Release(victoryEffectHandle);
            if (graphHandle.IsValid()) Addressables.Release(graphHandle);
            if (catalogHandle.IsValid()) Addressables.Release(catalogHandle);
        }
    }
}
