#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Traversify.Editor
{
    /// <summary>
    /// Manages scripting define symbols to ensure compatibility layers work correctly
    /// </summary>
    [InitializeOnLoad]
    public static class SymbolDefinitionManager
    {
        static SymbolDefinitionManager()
        {
            // Check if this is the first time the editor is being loaded after compilation
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // Schedule the check to run after the editor is fully initialized
            EditorApplication.delayCall += () => {
                AddRequiredSymbols();
            };
        }

        [MenuItem("Tools/Traversify/Fix Missing References")]
        public static void AddRequiredSymbols()
        {
            string currentSymbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(
                EditorUserBuildSettings.selectedBuildTargetGroup);

            List<string> symbolList = currentSymbols.Split(';').ToList();

            // Check for UI
            bool uiPresent = AssetDatabase.FindAssets("t:asmdef Unity.ugui").Length > 0;
            if (!uiPresent && !symbolList.Contains("UNITY_UI_INSTALLED"))
            {
                symbolList.Add("UNITY_UI_INSTALLED");
                Debug.Log("Added UNITY_UI_INSTALLED define symbol");
            }

            // Check for TextMeshPro
            bool tmpPresent = AssetDatabase.FindAssets("t:asmdef Unity.TextMeshPro").Length > 0;
            if (!tmpPresent && !symbolList.Contains("TMP_PRESENT"))
            {
                symbolList.Add("TMP_PRESENT");
                Debug.Log("Added TMP_PRESENT define symbol");
            }

            // Check for Newtonsoft.Json
            bool newtonsoftPresent = AssetDatabase.FindAssets("t:asmdef Unity.Nuget.Newtonsoft.Json").Length > 0;
            if (!newtonsoftPresent && !symbolList.Contains("NEWTONSOFT_JSON_INSTALLED"))
            {
                symbolList.Add("NEWTONSOFT_JSON_INSTALLED");
                Debug.Log("Added NEWTONSOFT_JSON_INSTALLED define symbol");
            }

            // Update the symbols
            string newSymbols = string.Join(";", symbolList.Distinct());
            if (newSymbols != currentSymbols)
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup, newSymbols);
                Debug.Log("Updated scripting define symbols to fix missing references");
            }
        }
    }
}
#endif
