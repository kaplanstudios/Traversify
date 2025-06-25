#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Traversify.Editor
{
    public class RequiredPackagesWindow : EditorWindow
    {
        private Vector2 scrollPosition;
        private readonly List<PackageInfo> requiredPackages = new List<PackageInfo>()
        {
            new PackageInfo("com.unity.ugui", "1.0.0", "Unity UI", "Built-in UI system for Unity"),
            new PackageInfo("com.unity.textmeshpro", "3.0.6", "TextMeshPro", "Advanced text rendering for Unity"),
            new PackageInfo("com.unity.nuget.newtonsoft-json", "3.0.2", "Newtonsoft Json", "JSON serialization library")
        };

        [MenuItem("Tools/Traversify/Required Packages")]
        public static void ShowWindow()
        {
            var window = GetWindow<RequiredPackagesWindow>("Required Packages");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Required Packages for Traversify", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("These packages are required for Traversify to function properly.", MessageType.Info);
            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var package in requiredPackages)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(package.Name, EditorStyles.boldLabel);
                if (GUILayout.Button("Install", GUILayout.Width(100)))
                {
                    InstallPackage(package);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(package.Description);
                EditorGUILayout.LabelField($"ID: {package.Id}");
                EditorGUILayout.LabelField($"Version: {package.Version}");
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            if (GUILayout.Button("Install All Packages"))
            {
                InstallAllPackages();
            }
        }

        private void InstallPackage(PackageInfo package)
        {
            string packageWithVersion = $"{package.Id}@{package.Version}";
            UnityEditor.PackageManager.Client.Add(packageWithVersion);
            Debug.Log($"Installing package: {packageWithVersion}");
        }

        private void InstallAllPackages()
        {
            foreach (var package in requiredPackages)
            {
                InstallPackage(package);
            }
        }

        private class PackageInfo
        {
            public string Id { get; }
            public string Version { get; }
            public string Name { get; }
            public string Description { get; }

            public PackageInfo(string id, string version, string name, string description)
            {
                Id = id;
                Version = version;
                Name = name;
                Description = description;
            }
        }
    }
}
#endif
