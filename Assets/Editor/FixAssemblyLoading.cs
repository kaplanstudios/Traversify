using System.IO;
using UnityEditor;
using UnityEngine;  // Added for Debug

public class FixAssemblyLoading : MonoBehaviour
{
    [MenuItem("Tools/Fix Assembly Loading Issues")]
    static void FixAssemblies()
    {
        string[] dllPaths = {
            "Assets/Battlehub/RTEditor/ThirdParty/UnityWeld/UnityWeld.dll",
            "Assets/Battlehub/RTEditor/ThirdParty/UnityWeld/Editor/UnityWeld_Editor.dll",
            "Assets/Battlehub/StorageData/Generated/StorageTypeModel.dll"
        };
        
        int fixedCount = 0;
        int deletedCount = 0;
        
        foreach (string dllPath in dllPaths)
        {
            if (!File.Exists(dllPath)) 
            {
                Debug.Log($"File not found: {dllPath}");
                continue;
            }
            
            var importer = AssetImporter.GetAtPath(dllPath) as PluginImporter;
            if (importer != null)
            {
                // Unity version compatible approach
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(true);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, true);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, true);
                
                // Apply changes
                AssetDatabase.ImportAsset(dllPath, ImportAssetOptions.ForceUpdate);
                fixedCount++;
                Debug.Log($"Updated settings for: {dllPath}");
            }
            else
            {
                // If we can't fix it, delete it
                File.Delete(dllPath);
                if (File.Exists(dllPath + ".meta"))
                {
                    File.Delete(dllPath + ".meta");
                }
                deletedCount++;
                Debug.LogWarning($"Deleted problematic DLL: {dllPath}");
            }
        }
        
        AssetDatabase.Refresh();
        Debug.Log($"Assembly fix complete. Fixed: {fixedCount}, Deleted: {deletedCount}");
        
        if (deletedCount > 0)
        {
            Debug.LogWarning("Some DLLs were deleted. If they were important, you may need to reimport the Battlehub asset.");
        }
    }
    
    [MenuItem("Tools/Delete Problem DLLs")]
    static void DeleteProblemDLLs()
    {
        string[] dllsToDelete = {
            "Assets/Battlehub/RTEditor/ThirdParty/UnityWeld/UnityWeld.dll",
            "Assets/Battlehub/RTEditor/ThirdParty/UnityWeld/Editor/UnityWeld_Editor.dll",
            "Assets/Battlehub/StorageData/Generated/StorageTypeModel.dll"
        };
        
        int deletedCount = 0;
        
        foreach (string dllPath in dllsToDelete)
        {
            if (File.Exists(dllPath))
            {
                File.Delete(dllPath);
                deletedCount++;
                Debug.Log($"Deleted: {dllPath}");
                
                // Also delete meta file
                string metaPath = dllPath + ".meta";
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                    Debug.Log($"Deleted: {metaPath}");
                }
            }
        }
        
        AssetDatabase.Refresh();
        Debug.Log($"Deleted {deletedCount} DLL files. Unity should now compile successfully.");
    }
}