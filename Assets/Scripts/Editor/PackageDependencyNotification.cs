#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Traversify.Editor
{
    [InitializeOnLoad]
    public class PackageDependencyNotification
    {
        private const string HideNotificationKey = "Traversify.HidePackageNotification";

        static PackageDependencyNotification()
        {
            EditorApplication.delayCall += () => {
                if (!EditorPrefs.GetBool(HideNotificationKey, false))
                {
                    ShowNotification();
                }
            };
        }

        private static void ShowNotification()
        {
            bool result = EditorUtility.DisplayDialog(
                "Missing Dependencies Detected",
                "Traversify requires some packages that may not be installed in your project:\n\n" +
                "- Unity UI (com.unity.ugui)\n" +
                "- TextMeshPro (com.unity.textmeshpro)\n" +
                "- Newtonsoft Json (com.unity.nuget.newtonsoft-json)\n\n" +
                "Would you like to open the Required Packages window to install them?\n\n" +
                "(You can access this window later via Tools > Traversify > Required Packages)",
                "Open Package Window", "Not Now");

            if (result)
            {
                RequiredPackagesWindow.ShowWindow();
            }
            else
            {
                bool dontShowAgain = EditorUtility.DisplayDialog(
                    "Hide Notification",
                    "Would you like to hide this notification in the future?\n\n" +
                    "You can always access the Required Packages window via\n" +
                    "Tools > Traversify > Required Packages",
                    "Hide Notification", "Show Next Time");

                EditorPrefs.SetBool(HideNotificationKey, dontShowAgain);
            }
        }

        [MenuItem("Tools/Traversify/Reset Package Notifications")]
        public static void ResetNotification()
        {
            EditorPrefs.DeleteKey(HideNotificationKey);
            Debug.Log("Package notification preferences have been reset.");
        }
    }
}
#endif
