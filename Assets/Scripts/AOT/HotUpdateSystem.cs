using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HybridCLR;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

// TODO：资源热更新流程总结：
//    1. 初始化 → 2. 检查 catalog 更新 → 3. 更新 catalog → 4. 计算下载大小 → 5. 下载资源 → 6. 进入游戏
// TODO：关键概念解释：
//    Catalog（目录）：Addressables的索引文件，记录所有资源的位置、依赖关系和版本信息
//    ResourceLocator（资源定位器）：用于定位和加载具体资源
//    AsyncOperationHandle（异步操作句柄）：管理异步操作的状态、进度和结果，必须正确释放
//    断点续传：通过PlayerPrefs记录更新状态，失败时清理缓存重新下载

public class HotUpdateSystem : MonoBehaviour
{
    private DllConfig dllConfig; // 存储 DLL配置信息的对象
    private IHotUpdateWindow hotUpdateWindow; // 热更新窗口接口引用，用于显示下载进度
    private const string dllConfigKey = "DllConfig"; // DLL配置文件的 Addressables Key
    private const string gameLanuchKey = "GameLanuch"; // 游戏启动器的 Addressables Key
    private const string hotUpdateWindowKey = "HotUpdateWindow"; // 热更新窗口的 Addressable Key

    private HashSet<string> loadedDlls = new(); // 记录已经加载过的 DLL，避免重复加载
    private string catalogPath => $"{persistentDataPath}/catalog_0.1.json"; // catalog文件路径，指向持久化存储的 catalog文件

    private string persistentDataPath =>
        $"{Application.persistentDataPath}/com.unity.addressables"; // 持久化数据路径，Addressables的缓存目录

    private void Start()
    {
        // 构建 Addressables 缓存目录路径
        // 格式：C:\Users\用户名\AppData\LocalLow\公司名\应用名\com.unity.addressables
        StartCoroutine(HotUpdate()); // 开始热更新流程
    }

    /// <summary>
    /// 热更新主流程协程
    /// 这是整个热更新系统的核心入口，控制所有热更新步骤的顺序
    /// </summary>
    private IEnumerator HotUpdate()
    {
        // 第一步：断点续传处理
        // 从 PlayerPrefs读取上次热更新状态
        int hotUpdateState = PlayerPrefs.GetInt("HotUpdateSucceed", 0);
        if (hotUpdateState == 0) // 0代表上一次热更失败
        {
            print("断点续传 - 检测到上次热更新失败，清理缓存");
            //Addressables.LoadContentCatalogAsync(persistentDataPath);  // 可选的：加载本地的catalog
            // 清理缓存目录，确保重新下载
            if (Directory.Exists(persistentDataPath))
            {
                Directory.Delete(persistentDataPath, true); // 递归删除整个目录
            }
        }

        // 设置当前热更新状态为"进行中"
        PlayerPrefs.SetInt("HotUpdateSucceed", 0);

        //第二步：初始化 Addressables系统 
        yield return Addressables.InitializeAsync(); // 初始化 Addressables，必须调用

        // 第三步：检测 catalog更新 
        yield return CheckForCatalogUpdates();

        // 第四步：标记热更新完成
        PlayerPrefs.SetInt("HotUpdateSucceed", 1); // 更新成功，设置为1

        // 第五步：加载程序集和开始游戏
        // 无论是否热更，都需要进入此处
        // 加载 AOT 程序集元数据（为 HybridCLR准备）
        LoadMetadataForAOTAssemblies();
        // 加载热更程序集
        LoadHotUpdateAssembly();
        if (hotUpdateWindow == null) // 如果有窗口就按照窗口的进度来，如果没有窗口就直接开始游戏
        {
            GameLanuch(); // 直接启动游戏
            // 这样开始游戏也可以通过什么东西（组件）来实现开始逻辑    Addressables.InstantiateAsync("xxx").WaitForCompletion();
        }
    }

    /// <summary>
    /// 检测catalog目录更新
    /// catalog是Addressables的目录文件，记录所有资源的位置、依赖关系和版本信息
    /// 检查是否有新的资源版本需要下载
    /// </summary>
    /// <returns></returns>
    private IEnumerator CheckForCatalogUpdates()
    {
        // 检查是否有新的 catalog版本可用
        // false参数表示只检查不自动更新
        AsyncOperationHandle<List<string>> checkForCatalogUpdatesHandle =
            Addressables.CheckForCatalogUpdates(false); // false: 不自动更新，只检查
        yield return checkForCatalogUpdatesHandle; // 等待检查完成

        if (checkForCatalogUpdatesHandle.Status != AsyncOperationStatus.Succeeded)
        {
            // 检查失败，抛出异常
            Debug.LogError($"CheckForCatalogUpdates 失败: {checkForCatalogUpdatesHandle.OperationException}");
        }
        else
        {
            print("CheckForCatalogUpdates成功 - 已检查到catalog更新状态");
            // 拿到需要更新的 catalog列表
            List<string> catalogResult = checkForCatalogUpdatesHandle.Result;
            if (catalogResult.Count > 0) // 有 catalog需要更新
            {
                // foreach (var result in catalogResult) { print(result); }  // 打印需要更新的 catalog名称
                yield return UpdateCatalogs(catalogResult); // 更新 catalog
            }
            else print("无需更新 - catalog已是最新版本");
        }

        // 释放 Addressables 操作句柄，避免内存泄漏
        Addressables.Release(checkForCatalogUpdatesHandle);
    }

    /// <summary>
    /// 更新catalog目录
    /// 当检测到有catalog需要更新时，下载最新的catalog文件
    /// </summary>
    /// <param name="catalogResult">需要更新的catalog名称列表</param>
    /// <returns></returns>
    private IEnumerator UpdateCatalogs(List<string> catalogResult)
    {
        // 更新指定的 catalog
        // false参数表示不自动下载资源，只更新catalog
        AsyncOperationHandle<List<IResourceLocator>> updateCatalogHandle =
            Addressables.UpdateCatalogs(catalogResult, false); // false: 不自动下载资源
        yield return updateCatalogHandle; // 等待更新完成

        if (updateCatalogHandle.Status != AsyncOperationStatus.Succeeded)
        {
            // 更新失败，抛出异常
            Debug.LogError($"UpdateCatalogs 失败: {updateCatalogHandle.OperationException}");
        }
        else
        {
            print("UpdateCatalogs 成功 - catalog已更新");
            // 获取更新后的资源定位器列表
            List<IResourceLocator> locatorList = updateCatalogHandle.Result;

            if (locatorList.Count > 0)
            {
                // 收集所有资源的 Key（标识符）
                List<object> keys = new List<object>(1000); // 预分配容量
                foreach (var result in locatorList)
                {
                    // print(result.LocatorId);  // 打印定位器 ID
                    keys.AddRange(result.Keys); // 添加所有资源 Key
                }

                // 计算需要下载的资源大小
                yield return GetDownloadSize(keys);
            }
        }

        // 释放Addressables操作句柄
        Addressables.Release(updateCatalogHandle);
    }

    /// <summary>
    /// 获取需要下载的资源总大小
    /// 根据收集的资源Key，计算总共需要下载多少数据
    /// </summary>
    /// <param name="keys">所有需要下载的资源Key集合</param>
    /// <returns></returns>
    private IEnumerator GetDownloadSize(IEnumerable<object> keys)
    {
        // 计算指定资源的总下载大小
        AsyncOperationHandle<long> sizeHandle = Addressables.GetDownloadSizeAsync(keys);
        yield return sizeHandle; // 等待计算完成

        if (sizeHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"GetDownloadSize 失败: {sizeHandle.OperationException}");
        }
        else
        {
            long downloadSize = sizeHandle.Result; // 获取总大小（字节）
            if (downloadSize > 0) // 有资源需要下载
            {
                // 先执行优先热更（下载并加载必要的 DLL和 UI）
                yield return PriorityHotUpdate();
                // 显示热更新窗口，并传入总下载大小和完成回调
                hotUpdateWindow.Show(downloadSize, GameLanuch);
                hotUpdateWindow.GetVersion(GetVersion(), GetVersionInfo());
                // 开始下载资源 
                yield return DownloadDependencies(keys, downloadSize);
            }
            else print("无需更新 - 没有需要下载的资源");
        }

        // 释放Addressables操作句柄
        Addressables.Release(sizeHandle);
    }

    /// <summary>
    /// 下载具体资源
    /// 实际执行资源下载，并更新进度显示
    /// </summary>
    /// <param name="keys">资源Key集合</param>
    /// <param name="downloadSize">总下载大小（字节）</param>
    /// <returns></returns>
    private IEnumerator DownloadDependencies(IEnumerable<object> keys, long downloadSize)
    {
        // 开始下载资源依赖
        // MergeMode.Union表示合并所有资源的依赖
        // false表示异步下载
        AsyncOperationHandle downloadHandle =
            Addressables.DownloadDependenciesAsync(keys, Addressables.MergeMode.Union, false);

        // ====== 循环监控下载进度 ======
        while (!downloadHandle.IsDone) // 下载未完成时循环
        {
            if (downloadHandle.Status == AsyncOperationStatus.Failed)
            {
                Debug.LogError($"DownloadDependencies 失败: {downloadHandle.OperationException}");
                break; // 下载失败，跳出循环
            }
            // 获取当前下载进度
            // float percentage = downloadHandle.GetDownloadStatus().Percent;  // 下载进度（0 ~ 1）
            // long currentDownloadSize = (long)(downloadSize * percentage);  // 当前已下载量（字节）

            // 更新 UI显示：传入下载进度百分比
            hotUpdateWindow.UpdateDownloadProgress(downloadHandle.GetDownloadStatus().Percent);
            yield return null; // 等待下一帧继续检查
        }

        // 确保进度显示为100%
        hotUpdateWindow?.UpdateDownloadProgress(1);
        print("热更完成! - 所有资源已下载完成");
    }

    // 优先热更
    /// <summary>
    /// 优先热更流程
    /// 在显示下载窗口前，先下载必要的核心资源：
    /// 1. DLL配置文件
    /// 2. 优先热更的DLL程序集
    /// 3. 热更新窗口UI
    /// 这样能确保后续流程能正常执行
    /// </summary>
    private IEnumerator PriorityHotUpdate()
    {
        // 1. 先下载 dll配置文件
        // true表示强制重新下载
        yield return Addressables.DownloadDependenciesAsync(dllConfigKey, true);

        // 2. 加载 DLL配置
        // WaitForCompletion()是同步等待方法（注意：在协程中使用要小心死锁）
        dllConfig = Addressables.LoadAssetAsync<DllConfig>(dllConfigKey).WaitForCompletion();

        // 3. 下载优先热更程序集（异步，不等待）
        // 先开始下载，后面再加载
        Addressables.DownloadDependenciesAsync(dllConfig.priorityHotUpdate, Addressables.MergeMode.Union, true);

#if !UNITY_EDITOR
        // 4. 加载优先热更程序集（只在非编辑器环境执行）
        // 编辑器环境下直接使用源代码，不需要加载 DLL
        foreach (string dllName in dllConfig.priorityHotUpdate)
        {
            // 同步加载 DLL文本文件
            TextAsset dllText = Addressables.LoadAssetAsync<TextAsset>(dllName).WaitForCompletion();
            LoadDll(dllText);  // 将 DLL加载到运行时
        }
        
        // 5. 重新加载目录（因为DLL已更新，需要刷新资源索引）
        ReloadContentCatalog();
#endif
        // 6. 下载并加载热更新窗口
        // 因为热更新窗口本身也是 Addressable资源，需要先下载才能显示
        // 6.1 先下载热更新窗口所需的资源依赖
        yield return Addressables.DownloadDependenciesAsync(hotUpdateWindowKey, true);
        // 6.2 实例化热更新窗口 GameObject
        hotUpdateWindow = Addressables.InstantiateAsync(hotUpdateWindowKey).WaitForCompletion() // 等待实例化完成
            .GetComponent<IHotUpdateWindow>(); // 获取接口组件
    }

    /// <summary>
    /// 加载DLL到运行时
    /// 将TextAsset中的DLL字节数组加载到Assembly
    /// </summary>
    /// <param name="dllTextAsset">包含DLL字节的TextAsset</param>
    private void LoadDll(TextAsset dllTextAsset)
    {
        if (!loadedDlls.Contains(dllTextAsset.name))
        {
            // 使用 Assembly.Load将字节数组加载为程序集
            Assembly.Load(dllTextAsset.bytes);
            loadedDlls.Add(dllTextAsset.name);
        }

        // 释放 TextAsset资源
        Addressables.Release(dllTextAsset);
    }

    /// <summary>
    /// 重新加载catalog文件 当DLL更新后，需要重新加载catalog以识别新的资源
    /// </summary>
    private void ReloadContentCatalog()
    {
        Addressables.LoadContentCatalogAsync(catalogPath).WaitForCompletion(); // 加载本地的 catalog
    }

    /// <summary>
    /// 加载AOT程序集元数据
    /// 为HybridCLR准备AOT元数据，使得热更代码可以引用AOT代码
    /// 只在非编辑器环境执行
    /// </summary>
    private void LoadMetadataForAOTAssemblies()
    {
#if UNITY_EDITOR
        return; // 编辑器环境下不需要
#endif
        // 如果 dllConfig还没加载（没有执行优先热更的情况），先加载它
        if (dllConfig == null) // 避免没有加载过优先热更
        {
            dllConfig = Addressables.LoadAssetAsync<DllConfig>(dllConfigKey).WaitForCompletion();
        }

        // 遍历所有 AOT DLL，加载它们的元数据
        foreach (string dllName in dllConfig.aot)
        {
            TextAsset textAsset = Addressables.LoadAssetAsync<TextAsset>(dllName).WaitForCompletion();
            // 为 HybridCLR加载 AOT程序集的元数据
            LoadImageErrorCode err =
                RuntimeApi.LoadMetadataForAOTAssembly(textAsset.bytes, HomologousImageMode.SuperSet);
            Debug.Log($"LoadMetadataForAOTAssembly:{textAsset.name}. ret:{err}");
            // 注意：这里没有释放 textAsset，可能需要添加 Addressables.Release(textAsset);
        }
    }

    /// <summary>
    /// 加载热更程序集
    /// 加载所有热更DLL到运行时，包括优先热更和普通热更
    /// 只在非编辑器环境执行
    /// </summary>
    private void LoadHotUpdateAssembly()
    {
#if UNITY_EDITOR
        return; // 编辑器环境下直接使用源代码
#endif
        // 如果 dllConfig还没加载，先加载它
        if (dllConfig == null) // 避免没有加载过优先热更
        {
            dllConfig = Addressables.LoadAssetAsync<DllConfig>(dllConfigKey).WaitForCompletion();
        }

        // 加载优先热更程序集（如果 PriorityHotUpdate中已经加载过，这里会跳过）
        foreach (string dllName in dllConfig.priorityHotUpdate)
        {
            TextAsset dllText = Addressables.LoadAssetAsync<TextAsset>(dllName).WaitForCompletion();
            LoadDll(dllText);
        }

        // 加载普通热更程序集
        foreach (string dllName in dllConfig.hotUpdate)
        {
            TextAsset dllText = Addressables.LoadAssetAsync<TextAsset>(dllName).WaitForCompletion();
            LoadDll(dllText);
        }

        // 重新加载 catalog（因为加载了新 DLL）
        ReloadContentCatalog();
    }

    /// <summary>
    /// 跳转到游戏场景
    /// 热更新完成后，启动游戏主逻辑
    /// </summary>
    private void GameLanuch()
    {
        StartCoroutine(GameLanuchCoroutine());
    }

    private IEnumerator GameLanuchCoroutine()
    {
        // 清理热更新窗口资源
        if (hotUpdateWindow != null)
        {
            Addressables.Release(((Component)hotUpdateWindow).gameObject);
        }

        if (dllConfig != null)
        {
            Addressables.Release(dllConfig);
        }

        yield return Addressables.InstantiateAsync(gameLanuchKey);
    }

    /// <summary>
    /// 获取版本信息
    /// </summary>
    private string GetVersionInfo()
    {
        // 下载版本信息，true 使用完资源之后自动释放
        Addressables.DownloadDependenciesAsync("VersionInfo", true).WaitForCompletion();
        TextAsset versionInfo = Addressables.LoadAssetAsync<TextAsset>("VersionInfo").WaitForCompletion();
        string info = versionInfo.text;
        Addressables.Release(versionInfo);
        return info;
    }

    /// <summary>
    /// 获取版本号信息
    /// </summary>
    private string GetVersion()
    {
        // 下载版本信息，true 使用完资源之后自动释放
        Addressables.DownloadDependenciesAsync("Version", true).WaitForCompletion();
        TextAsset textAsset = Addressables.LoadAssetAsync<TextAsset>("Version").WaitForCompletion();
        string info = textAsset.text;
        Addressables.Release(textAsset);
        return info;
    }
}