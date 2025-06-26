using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using Unity.EditorCoroutines.Editor;
using System.Linq; // Add LINQ for FirstOrDefault

public class TraversifyPackageReinstaller : EditorWindow
{
    private List<PackageInfo> packagesToInstall = new List<PackageInfo>()
    {
        new PackageInfo("com.unity.textmeshpro", "3.0.6", "TextMeshPro", "Advanced text rendering"),
        new PackageInfo("com.unity.ugui", "1.0.0", "Unity UI", "UI Toolkit"),
        new PackageInfo("com.unity.editorcoroutines", "1.0.0", "Editor Coroutines", "Coroutines for Editor scripts"),
        new PackageInfo("com.unity.ai.inference", "1.0.0-exp.2", "AI Inference", "AI tools for Unity"),
        new PackageInfo("com.unity.nuget.newtonsoft-json", "3.2.1", "Newtonsoft Json", "JSON library for .NET")
    };

    private Dictionary<string, bool> installStatus = new Dictionary<string, bool>();
    private Dictionary<string, string> packageMessages = new Dictionary<string, string>();
    private bool isInstallingPackages = false;
    private AddRequest currentRequest;
    private int currentPackageIndex = 0;
    private bool forceRemoveFirst = true;
    private bool showDetailedLogs = false;
    private Vector2 scrollPosition;

    [MenuItem("Tools/Traversify/Package Reinstaller")]
    public static void ShowWindow()
    {
        GetWindow<TraversifyPackageReinstaller>("Traversify Package Reinstaller");
    }

    private void OnEnable()
    {
        ResetInstallStatus();
    }

    private void ResetInstallStatus()
    {
        installStatus.Clear();
        packageMessages.Clear();
        foreach (var package in packagesToInstall)
        {
            installStatus[package.id] = false;
            packageMessages[package.id] = "Pending installation";
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        GUILayout.Label("Traversify Package Reinstaller", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);
        EditorGUILayout.HelpBox("This tool will force reinstall all required packages for Traversify.", MessageType.Info);
        EditorGUILayout.Space(10);

        // Package list with status
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        foreach (var package in packagesToInstall)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            
            // Package name and version
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(package.displayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"ID: {package.id}   Version: {package.version}");
            EditorGUILayout.LabelField(package.description, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            
            // Status indicator
            GUILayout.FlexibleSpace();
            if (installStatus[package.id])
            {
                GUI.color = Color.green;
                GUILayout.Label("✓", EditorStyles.boldLabel, GUILayout.Width(20));
            }
            else
            {
                GUI.color = Color.yellow;
                GUILayout.Label("○", EditorStyles.boldLabel, GUILayout.Width(20));
            }
            GUI.color = Color.white;
            
            EditorGUILayout.EndHorizontal();
            
            // Message
            if (!string.IsNullOrEmpty(packageMessages[package.id]))
            {
                EditorGUILayout.LabelField(packageMessages[package.id], EditorStyles.miniLabel);
            }
            
            EditorGUILayout.Space(5);
        }
        
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(10);
        
        // Options
        forceRemoveFirst = EditorGUILayout.ToggleLeft("Force remove packages before reinstalling", forceRemoveFirst);
        showDetailedLogs = EditorGUILayout.ToggleLeft("Show detailed logs in console", showDetailedLogs);
        
        EditorGUILayout.Space(10);
        
        // Install button
        GUI.enabled = !isInstallingPackages;
        if (GUILayout.Button("Reinstall All Packages", GUILayout.Height(40)))
        {
            ResetInstallStatus();
            EditorCoroutineUtility.StartCoroutine(InstallAllPackages(), this);
        }
        GUI.enabled = true;
        
        if (isInstallingPackages)
        {
            Repaint(); // Keep repainting while installing
            
            // Progress bar for current installation
            float progress = (float)currentPackageIndex / packagesToInstall.Count;
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), progress, 
                $"Installing {(currentPackageIndex < packagesToInstall.Count ? packagesToInstall[currentPackageIndex].displayName : "Complete")}");
        }
    }

    private IEnumerator InstallAllPackages()
    {
        if (isInstallingPackages)
            yield break;

        isInstallingPackages = true;
        currentPackageIndex = 0;
        
        Debug.Log("[Traversify Package Reinstaller] Starting package reinstallation...");
        
        foreach (var package in packagesToInstall)
        {
            LogMessage($"Processing {package.displayName} ({package.id})");
            packageMessages[package.id] = "Processing...";
            
            // Force remove first if enabled
            if (forceRemoveFirst)
            {
                LogMessage($"Removing existing {package.id} package");
                packageMessages[package.id] = "Removing existing package...";
                yield return RemovePackage(package.id);
            }
            
            // Then install
            LogMessage($"Installing {package.id} {package.version}");
            packageMessages[package.id] = "Installing...";
            yield return InstallPackage(package.id, package.version);
            
            currentPackageIndex++;
        }
        
        isInstallingPackages = false;
        AssetDatabase.Refresh();
        
        // Verify installations after completion
        VerifyInstallations();
        
        EditorUtility.DisplayDialog("Package Reinstallation Complete", 
            "All packages have been reinstalled. Please restart Unity to ensure all changes take effect.", 
            "OK");
            
        Debug.Log("[Traversify Package Reinstaller] Package reinstallation completed!");
    }
    
    private IEnumerator RemovePackage(string packageId)
    {
        RemoveRequest removeRequest = null;
        
        // Start the request outside the try block
        try {
            removeRequest = Client.Remove(packageId);
        }
        catch (Exception e) {
            LogMessage($"Exception starting removal of {packageId}: {e.Message}", true);
            // Don't yield here - that would cause CS1631
            yield break;
        }
        
        // Wait for completion (outside try-catch)
        while (!removeRequest.IsCompleted)
        {
            yield return new WaitForSeconds(0.1f);
        }
        
        // Handle the result
        try {
            if (removeRequest.Status == StatusCode.Success)
            {
                LogMessage($"Successfully removed {packageId}");
            }
            else if (removeRequest.Status == StatusCode.Failure)
            {
                // Ignore failure since package might not be installed
                LogMessage($"Could not remove {packageId}: {removeRequest.Error?.message}", true);
            }
        }
        catch (Exception e) {
            LogMessage($"Exception processing removal result for {packageId}: {e.Message}", true);
        }
        
        // Wait outside of any try-catch
        yield return new WaitForSeconds(1f); // Give Unity some time
    }
    
    private IEnumerator InstallPackage(string packageId, string version)
    {
        string fullPackageId = packageId;
        if (!string.IsNullOrEmpty(version))
        {
            fullPackageId = $"{packageId}@{version}";
        }
        
        AddRequest addRequest = null;
        
        // Start the request outside the try block
        try {
            LogMessage($"Adding package: {fullPackageId}");
            addRequest = Client.Add(fullPackageId);
            currentRequest = addRequest;
        }
        catch (Exception e) {
            LogMessage($"Exception starting installation of {packageId}: {e.Message}", true);
            packageMessages[packageId] = $"Installation failed: {e.Message}";
            installStatus[packageId] = false;
            // Don't yield here - that would cause CS1631
            yield break;
        }
        
        // Wait for completion (outside try-catch)
        while (!addRequest.IsCompleted)
        {
            yield return new WaitForSeconds(0.5f);
        }
        
        // Handle the result
        try {
            if (addRequest.Status == StatusCode.Success)
            {
                LogMessage($"Successfully installed {packageId}");
                packageMessages[packageId] = "Successfully installed";
                installStatus[packageId] = true;
            }
            else
            {
                string errorMsg = addRequest.Error?.message ?? "Unknown error";
                LogMessage($"Failed to install {packageId}: {errorMsg}", true);
                packageMessages[packageId] = $"Installation failed: {errorMsg}";
                installStatus[packageId] = false;
            }
        }
        catch (Exception e) {
            LogMessage($"Exception processing installation result for {packageId}: {e.Message}", true);
            packageMessages[packageId] = $"Installation failed: {e.Message}";
            installStatus[packageId] = false;
        }
        
        // Wait outside of any try-catch
        yield return new WaitForSeconds(1f); // Give Unity some time
    }
    
    private void VerifyInstallations()
    {
        // Check if TextMeshPro's assemblies exist regardless of package manager status
        if (DoesTextMeshProExist())
        {
            installStatus["com.unity.textmeshpro"] = true;
            packageMessages["com.unity.textmeshpro"] = "Verified by assembly check";
            LogMessage("TextMeshPro verified by assembly check");
        }
    }
    
    private bool DoesTextMeshProExist()
    {
        try
        {
            // Check for TMP assembly
            var tmpAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Unity.TextMeshPro");
            
            if (tmpAssembly != null)
            {
                LogMessage("TextMeshPro assembly found");
                return true;
            }
            
            // Check for TMP types
            var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            if (tmpType != null)
            {
                LogMessage("TextMeshPro type found");
                return true;
            }
            
            // Check for TMP assets
            if (AssetDatabase.FindAssets("t:asmdef Unity.TextMeshPro").Length > 0)
            {
                LogMessage("TextMeshPro assets found in project");
                return true;
            }
            
            // Check if TMP folder exists
            if (Directory.Exists(Path.Combine(Application.dataPath, "TextMesh Pro")))
            {
                LogMessage("TextMeshPro folder exists in project");
                return true;
            }
            
            LogMessage("TextMeshPro not found in project", true);
            return false;
        }
        catch (Exception e)
        {
            LogMessage($"Error checking for TextMeshPro: {e.Message}", true);
            return false;
        }
    }
    
    private void LogMessage(string message, bool isWarning = false)
    {
        if (!showDetailedLogs && !isWarning)
            return;
            
        if (isWarning)
            Debug.LogWarning($"[Traversify Package Reinstaller] {message}");
        else
            Debug.Log($"[Traversify Package Reinstaller] {message}");
    }

    public class PackageInfo
    {
        public string id;
        public string version;
        public string displayName;
        public string description;
        
        public PackageInfo(string id, string version, string displayName, string description)
        {
            this.id = id;
            this.version = version;
            this.displayName = displayName;
            this.description = description;
        }
    }
}
