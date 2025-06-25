#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Traversify.Editor
{
    /// <summary>
    /// Utility for managing required packages
    /// </summary>
    public static class PackageUtility
    {
        [MenuItem("Tools/Traversify/Install Required Packages")]
        public static void InstallRequiredPackages()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Install Required Packages",
                "This will add the following packages to your project:\n\n" +
                "- com.unity.mathematics (1.2.6)\n" +
                "- com.unity.barracuda (3.0.0)\n\n" +
                "Do you want to continue?",
                "Install", "Cancel");

            if (!confirm) return;

            AddPackage("com.unity.mathematics", "1.2.6");
            AddPackage("com.unity.barracuda", "3.0.0");

            Debug.Log("Package installation initiated. Check Package Manager for status.");
        }

        private static void AddPackage(string packageId, string version)
        {
            string packageWithVersion = $"{packageId}@{version}";
            UnityEditor.PackageManager.Client.Add(packageWithVersion);
            Debug.Log($"Requested package: {packageWithVersion}");
        }
    }
}
#endif
