using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 通用 Addressables 资源加载管理器
/// 泛型句柄类型不匹配问题
/// </summary>
public class ResMgr : UnitySingleTonMono<ResMgr>
{
    // 缓存改为泛型字典
    private Dictionary<string, AsyncOperationHandle> handleCache = new();
    private Dictionary<string, object> assetCache = new();
    private List<GameObject> instanceCache = new();

    #region 加载方法
    
    /// <summary>
    /// 异步加载资源（通用）
    /// </summary>
    /// <typeparam name="T">资源类型（GameObject/Texture2D/AudioClip等）</typeparam>
    /// <param name="address">Addressables Key</param>
    /// <param name="onComplete">加载完成回调</param>
    public void LoadAssetAsync<T>(string address, Action<T> onComplete) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(address))
        {
            Debug.LogError("ResMgr: 加载地址不能为空！");
            onComplete?.Invoke(null);
            return;
        }

        // 缓存中已有资源：直接返回
        if (assetCache.TryGetValue(address, out object cacheObj) && cacheObj is T tObj)
        {
            onComplete?.Invoke(tObj);
            return;
        }

        // 重新获取泛型句柄，避免类型不匹配 
        if (handleCache.TryGetValue(address, out AsyncOperationHandle handle))
        {
            // 将无泛型句柄转换为泛型句柄
            AsyncOperationHandle<T> genericHandle = handle.Convert<T>();
            if (genericHandle.IsDone)
            {
                OnLoadComplete<T>(genericHandle, address, onComplete);
            }
            else
            {
                genericHandle.Completed += op => OnLoadComplete<T>(op, address, onComplete);
            }
            return;
        }

        // 开始异步加载
        AsyncOperationHandle<T> loadHandle = Addressables.LoadAssetAsync<T>(address);
        handleCache.Add(address, loadHandle);
        loadHandle.Completed += (op) => OnLoadComplete<T>(op, address, onComplete);
    }

    /// <summary>
    /// 异步加载并实例化预制体（最常用）
    /// </summary>
    /// <param name="address">Addressables Key</param>
    /// <param name="parent">父物体</param>
    /// <param name="onComplete">实例化完成回调</param>
    public void LoadAndInstantiateAsync(string address, Transform parent = null, Action<GameObject> onComplete = null)
    {
        LoadAssetAsync<GameObject>(address, prefab =>
        {
            if (prefab == null)
            {
                onComplete?.Invoke(null);
                return;
            }
            // 实例化预制体
            GameObject instance = Instantiate(prefab, parent);
            instance.name = prefab.name; // 去掉(Clone)后缀
            instanceCache.Add(instance); // 加入实例缓存
            onComplete?.Invoke(instance);
        });
    }

    /// <summary>
    /// 批量异步加载资源
    /// </summary>
    /// <typeparam name="T">资源类型</typeparam>
    /// <param name="addressList">Addressables Key列表</param>
    /// <param name="onComplete">全部加载完成回调</param>
    public void LoadBatchAssetsAsync<T>(List<string> addressList, Action<Dictionary<string, T>> onComplete) where T : UnityEngine.Object
    {
        if (addressList == null || addressList.Count == 0)
        {
            onComplete?.Invoke(new Dictionary<string, T>());
            return;
        }

        Dictionary<string, T> resultDict = new Dictionary<string, T>();
        int loadedCount = 0;

        foreach (string address in addressList)
        {
            LoadAssetAsync<T>(address, asset =>
            {
                loadedCount++;
                if (asset != null)
                {
                    resultDict.Add(address, asset);
                }
                // 所有资源加载完成
                if (loadedCount == addressList.Count) onComplete?.Invoke(resultDict);
            });
        }
    }
    
    #endregion

    #region 资源释放方法
    
    /// <summary>
    /// 释放单个资源
    /// </summary>
    /// <param name="address">Addressables Key</param>
    public void ReleaseAsset(string address)
    {
        // 释放加载句柄
        if (handleCache.TryGetValue(address, out AsyncOperationHandle handle))
        {
            Addressables.Release(handle);
            handleCache.Remove(address);
        }
        // 移除资源缓存
        if (assetCache.ContainsKey(address)) assetCache.Remove(address);
    }

    /// <summary>
    /// 释放实例化的对象
    /// </summary>
    /// <param name="instance">实例化的对象</param>
    public void ReleaseInstance(GameObject instance)
    {
        if (instance == null) return;
        Addressables.ReleaseInstance(instance);
        instanceCache.Remove(instance);
        Destroy(instance);
    }

    /// <summary>
    /// 释放所有资源（场景切换/面板关闭时调用）
    /// </summary>
    public void ReleaseAll()
    {
        // 释放所有加载句柄
        foreach (var handle in handleCache.Values)
        {
            Addressables.Release(handle);
        }
        handleCache.Clear();
        // 释放所有实例
        foreach (var instance in instanceCache)
        {
            if (instance != null)
            {
                Addressables.ReleaseInstance(instance);
                Destroy(instance);
            }
        }
        instanceCache.Clear();
        // 清空资源缓存
        assetCache.Clear();
    }
    
    #endregion

    #region 私有辅助方法
    
    /// <summary>
    /// 加载完成回调处理（泛型版本）
    /// </summary>
    private void OnLoadComplete<T>(AsyncOperationHandle<T> op, string address, Action<T> onComplete) where T : UnityEngine.Object
    {
        if (op.Status == AsyncOperationStatus.Succeeded)
        {
            T result = op.Result;
            assetCache[address] = result; // 加入资源缓存
            onComplete?.Invoke(result);
        }
        else
        {
            Debug.LogError($"ResMgr: 加载失败 {address} - {op.OperationException}");
            onComplete?.Invoke(null);
        }
        // 移除句柄缓存（已完成加载）
        if (handleCache.ContainsKey(address)) handleCache.Remove(address);
    }

    /// <summary>
    /// 单例销毁时释放所有资源
    /// </summary>
    protected void OnDestroy()
    {
        ReleaseAll();
    }
    
    #endregion
}
