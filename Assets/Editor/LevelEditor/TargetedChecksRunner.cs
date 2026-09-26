using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ChestPush.LevelEditor
{
    [InitializeOnLoad]
    public static class TargetedChecksRunner
    {
        private const string RequestPath = "Library/ChestPushRunTests.request";
        private static TestRunnerApi runner;

        static TargetedChecksRunner()
        {
            if (File.Exists(RequestPath)) EditorApplication.delayCall += RunRequested;
        }

        [MenuItem("Tools/ChestPush/Run Targeted EditMode Checks")]
        public static void Run()
        {
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new ResultCallback());
            runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "ChestPush.Tests" }
            }) { runSynchronously = true });
        }

        private static void RunRequested()
        {
            if (!File.Exists(RequestPath)) return;
            File.Delete(RequestPath);
            Run();
        }

        private sealed class ResultCallback : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var failed = Flatten(result).Where(item => item.TestStatus == TestStatus.Failed && !item.HasChildren)
                    .Select(item => item.FullName + ": " + item.Message);
                string text = $"Passed: {result.PassCount}, Failed: {result.FailCount}, Skipped: {result.SkipCount}\n" +
                    string.Join("\n", failed);
                File.WriteAllText("Library/ChestPushTestResult.txt", text);
                Debug.Log("ChestPush targeted checks: " + text);
                if (runner != null) ScriptableObject.DestroyImmediate(runner);
                runner = null;
            }

            private static System.Collections.Generic.IEnumerable<ITestResultAdaptor> Flatten(ITestResultAdaptor result)
            {
                yield return result;
                foreach (var child in result.Children)
                    foreach (var nested in Flatten(child)) yield return nested;
            }
        }
    }
}
