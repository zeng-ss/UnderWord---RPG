using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// 构建工具类
/// 提供三个菜单项用于不同阶段的构建：
/// 1. GenerateDllFiles: 生成DLL文本文件
/// 2. NewClient: 构建完整客户端
/// 3. UpdateClient: 构建增量更新包
/// </summary>
public static class Build
{
    // 构建输出路径：项目根目录/Builds/项目名.exe
    private static string buildPath => Path.Combine(new DirectoryInfo(Application.dataPath).Parent.FullName, $"Builds/{Application.productName}.exe");
    
    /// <summary>
    /// 生成DLL文本文件
    /// 将HybridCLR生成的DLL文件转换为Unity可用的TextAsset (.bytes文件)
    /// 并将它们添加到Addressables分组中
    /// </summary>
    [MenuItem("Build/GenerateDllFiles")]
    public static void GenerateDllFiles()
    {
        Debug.Log("开始生成dll文本文件！");
        string environmentDir = Environment.CurrentDirectory;  // 当前工作目录
        // HybridCLR生成的 AOT DLL目录
        string aotDllDir = Path.Combine(environmentDir, SettingsUtil.GetAssembliesPostIl2CppStripDir(EditorUserBuildSettings.activeBuildTarget));
        // HybridCLR生成的热更 DLL目录
        string hotUpdateDllDir = Path.Combine(environmentDir, SettingsUtil.GetHotUpdateDllsOutputDirByTarget(EditorUserBuildSettings.activeBuildTarget));
        
        // 输出目录
        string aotTextDir = Path.Combine(environmentDir,"Assets/DllBytes/AOT");
        string hotUpdateTextDir = Path.Combine(environmentDir,"Assets/DllBytes/HotUpdate");
        string priorityHotUpdateTextDir = Path.Combine(environmentDir,"Assets/DllBytes/PriorityHotUpdate");
        
        // 加载 DLL配置文件
        DllConfig dllConfig = AssetDatabase.LoadAssetAtPath<DllConfig>("Assets/Config/DllConfig.asset");
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        
        // ====== 处理 AOT DLL ======
        AddressableAssetGroup aotGroup = settings.FindGroup("AOT");
        foreach (string dllName in dllConfig.aot)
        {
            // 查找 DLL文件路径
            string dllPath = Path.Combine(aotDllDir, $"{dllName}");
            // 如果 AOT目录没有，尝试热更目录（某些 DLL可能同时是 AOT和热更）
            if (!File.Exists(dllPath)) dllPath = Path.Combine(hotUpdateDllDir, $"{dllName}");
            
            string dllBytesPath = Path.Combine(aotTextDir, $"{dllName}.bytes");
            
            // 复制 DLL文件并重命名为.bytes扩展名
            File.Copy(dllPath, dllBytesPath, true);
            AssetDatabase.Refresh();  // 刷新 AssetDatabase以识别新文件
            
            // 将.bytes文件添加到 Addressables的 AOT分组
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(
                AssetDatabase.AssetPathToGUID($"Assets/DllBytes/AOT/{dllName}.bytes"), aotGroup);
            entry.SetAddress($"{dllName}");  // 设置 Addressables地址（key）
        }
        
        // ====== 处理普通热更DLL ======
        AddressableAssetGroup hotUpdateGroup = settings.FindGroup("HotUpdate");
        foreach (string dllName in dllConfig.hotUpdate)
        {
            string dllPath = Path.Combine(hotUpdateDllDir, $"{dllName}");
            string dllBytesPath = Path.Combine(hotUpdateTextDir, $"{dllName}.bytes");
            
            File.Copy(dllPath, dllBytesPath, true);
            AssetDatabase.Refresh();
            
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(
                AssetDatabase.AssetPathToGUID($"Assets/DllBytes/HotUpdate/{dllName}.bytes"), hotUpdateGroup);
            entry.SetAddress($"{dllName}");
        }
        
        // ====== 处理优先热更DLL ======
        AddressableAssetGroup priorityHotUpdateGroup = settings.FindGroup("PriorityHotUpdate");
        foreach (string dllName in dllConfig.priorityHotUpdate)
        {
            string dllPath = Path.Combine(hotUpdateDllDir, $"{dllName}");
            string dllBytesPath = Path.Combine(priorityHotUpdateTextDir, $"{dllName}.bytes");
            
            File.Copy(dllPath, dllBytesPath, true);
            AssetDatabase.Refresh();
            
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(
                AssetDatabase.AssetPathToGUID($"Assets/DllBytes/PriorityHotUpdate/{dllName}.bytes"), priorityHotUpdateGroup);
            entry.SetAddress($"{dllName}");
        }
        
        // 保存设置
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("成功生成dll文本文件！");
    }

    /// <summary>
    /// 构建新客户端
    /// 完整构建流程，包含：
    /// 1. 生成HybridCLR文件
    /// 2. 生成DLL文本文件
    /// 3. 构建可执行文件
    /// </summary>
    [MenuItem("Build/NewClient")]
    public static void NewClient()
    {
        // 1. 生成 HybridCLR所需的 AOT泛型引用等
        PrebuildCommand.GenerateAll();
        
        // 2. 将 DLL转换为 TextAsset并添加到 Addressables
        GenerateDllFiles();
        
        // 3. 获取构建场景列表
        string[] scenes = new string[EditorSceneManager.sceneCountInBuildSettings];
        for (int i = 0; i < EditorSceneManager.sceneCountInBuildSettings; i++)
        {
            scenes[i] = SceneUtility.GetScenePathByBuildIndex(i);
            Debug.Log($"添加场景{scenes[i]}");
        }

        // 4. 配置构建选项
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions()
        {
            scenes = scenes,  // 包含的场景
            target = EditorUserBuildSettings.activeBuildTarget,  // 目标平台
            locationPathName = buildPath,  // 输出路径
            options = BuildOptions.Development | BuildOptions.AllowDebugging,  // 开发模式，允许调试
        };
        
        // 5. 执行构建
        BuildPipeline.BuildPlayer(buildPlayerOptions);
        
        // 6. 输出构建路径
        Debug.unityLogger.Log(buildPath);
    }

    /// <summary>
    /// 更新客户端（增量更新）
    /// 只构建发生变化的资源，用于热更新
    /// 需要先有content_state.bin文件（由NewClient生成）
    /// </summary>
    [MenuItem("Build/UpdateClient")]
    public static void UpdateClient()
    {
        // 1. 生成 HybridCLR文件
        PrebuildCommand.GenerateAll();
        
        // 2. 生成 DLL文本文件
        GenerateDllFiles();
        
        // 3. 获取 content_state.bin路径（记录上次构建状态）
        string path = ContentUpdateScript.GetContentStateDataPath(false);
        
        // 4. 获取 Addressables设置
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        
        // 5. 执行增量更新构建
        ContentUpdateScript.BuildContentUpdate(settings, path);
        
        Debug.Log("完成客户端更新构建");
    }
}