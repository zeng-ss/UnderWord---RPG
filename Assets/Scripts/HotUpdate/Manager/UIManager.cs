using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class UIManager : UnitySingleTonMono<UIManager>
{
    // 面板管理字典（保留实例）
    public Dictionary<string, BasePanel> UIPanelDict = new ();
    // 面板资源句柄字典（永久保留）
    private Dictionary<string, AsyncOperationHandle<GameObject>> panelAssetHandles = new ();
    // 面板原始Transform配置（保存预制体的宽高/锚点）
    private Dictionary<string, RectTransformData> panelOriginalTransformData = new ();
    // 后处理面板追踪
    private HashSet<string> postProcessingPanels = new ();
    // 加载中面板追踪
    private Dictionary<string, AsyncOperationHandle<GameObject>> panelLoadHandles = new ();

    // Canvas相关
    [HideInInspector] public RectTransform canvas;
    private Canvas canvasComponent;
    private CanvasScaler canvasScaler;

    // 面板容器
    private GameObject currentPanel;

    // 相机引用
    private Camera mainCamera;
    private Camera cameraOne;
    private bool isCanvasInitialized;

    // 保存RectTransform原始数据的结构体
    private struct RectTransformData
    {
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 sizeDelta;
        public Vector2 pivot;
        public Vector3 anchoredPosition3D;
        public Vector3 localScale;
    }

    // 唯一的openPanel方法
    public void OpenPanel<T>(Action<T> onLoadComplete = null, bool needPostProcessing = false) where T : BasePanel
    {
        if (!isCanvasInitialized)
        {
            Debug.LogError("Canvas未初始化完成，无法打开面板！");
            onLoadComplete?.Invoke(null);
            return;
        }
        UpdateCameraReferences();
        string panelName = typeof(T).Name;
        // 面板已存在：恢复原始Transform+修复资源
        if (UIPanelDict.ContainsKey(panelName))
        {
            T panel = UIPanelDict[panelName] as T;
            // 1. 恢复预制体原始Transform（核心：解决宽高为0）
            if (panelOriginalTransformData.ContainsKey(panelName))
            {
                RestorePanelTransform(panel.transform, panelName);
            }
            // 3. 显示面板
            panel.Show();
            // 4. 切换Canvas模式
            if (needPostProcessing)
            {
                postProcessingPanels.Add(panelName);
                StartCoroutine(DelaySetCanvasMode(true, 0.1f));
            }
            else if (postProcessingPanels.Count == 0)
            {
                StartCoroutine(DelaySetCanvasMode(false, 0.1f));
            }
            // 5. 执行回调
            onLoadComplete?.Invoke(panel);
            return;
        }
        // 面板未加载：异步加载
        if (panelLoadHandles.ContainsKey(panelName))
        {
            Debug.LogWarning($"面板 {panelName} 正在加载中，请勿重复调用");
            onLoadComplete?.Invoke(null);
            return;
        }
        AsyncOperationHandle<GameObject> loadHandle = Addressables.LoadAssetAsync<GameObject>(panelName);
        panelLoadHandles.Add(panelName, loadHandle);
        StartCoroutine(FinishLoadPanelCoroutine<T>(loadHandle, panelName, needPostProcessing, onLoadComplete));
    }

    /// <summary>
    /// 加载面板协程（保存原始Transform+不释放资源）
    /// </summary>
    private IEnumerator FinishLoadPanelCoroutine<T>(AsyncOperationHandle<GameObject> loadHandle, string panelName, 
        bool needPostProcessing, Action<T> onLoadComplete = null) where T : BasePanel
    {
        yield return loadHandle;
        panelLoadHandles.Remove(panelName);

        if (loadHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"加载面板失败: {panelName}，错误：{loadHandle.OperationException}");
            onLoadComplete?.Invoke(null);
            yield break;
        }

        // 1. 永久保留资源句柄
        panelAssetHandles[panelName] = loadHandle;

        // 2. 获取预制体原始Transform数据（核心：保存宽高/锚点）
        GameObject prefabObj = loadHandle.Result;
        RectTransform prefabRect = prefabObj.GetComponent<RectTransform>();
        if (prefabRect != null)
        {
            panelOriginalTransformData[panelName] = new RectTransformData
            {
                anchorMin = prefabRect.anchorMin,
                anchorMax = prefabRect.anchorMax,
                sizeDelta = prefabRect.sizeDelta,
                pivot = prefabRect.pivot,
                anchoredPosition3D = prefabRect.anchoredPosition3D,
                localScale = prefabRect.localScale
            };
        }

        // 3. 实例化面板（保留预制体Transform）
        GameObject panelObj = Instantiate(prefabObj, currentPanel.transform, false);
        panelObj.name = panelName;

        // 5. 获取面板组件
        BasePanel panel = panelObj.GetComponent<T>();
        if (panel == null)
        {
            Debug.LogError($"面板 {panelName} 缺少BasePanel组件");
            Destroy(panelObj);
            onLoadComplete?.Invoke(null);
            yield break;
        }

        // 6. 加入字典并显示
        UIPanelDict.Add(panelName, panel);
        panel.Show();

        // 7. 切换Canvas模式
        if (needPostProcessing)
        {
            postProcessingPanels.Add(panelName);
            StartCoroutine(DelaySetCanvasMode(true, 0.1f));
        }

        // 8. 执行回调
        onLoadComplete?.Invoke(panel as T);
    }

    /// <summary>
    /// 恢复面板到预制体原始Transform（解决宽高为0）
    /// </summary>
    private void RestorePanelTransform(Transform panelTransform, string panelName)
    {
        if (!panelOriginalTransformData.ContainsKey(panelName)) return;

        RectTransform panelRect = panelTransform.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            RectTransformData originalData = panelOriginalTransformData[panelName];
            // 仅重置父物体，保留预制体原始属性
            panelRect.SetParent(currentPanel.transform, false);
            // 恢复预制体原始配置（核心：不再强制设为0）
            panelRect.anchorMin = originalData.anchorMin;
            panelRect.anchorMax = originalData.anchorMax;
            panelRect.sizeDelta = originalData.sizeDelta;
            panelRect.pivot = originalData.pivot;
            panelRect.anchoredPosition3D = originalData.anchoredPosition3D;
            panelRect.localScale = originalData.localScale;
            panelRect.localRotation = Quaternion.identity;
        }
        else
        {
            panelTransform.SetParent(currentPanel.transform);
            panelTransform.localPosition = Vector3.zero;
            panelTransform.localRotation = Quaternion.identity;
            panelTransform.localScale = Vector3.one;
        }
    }

    /// <summary>
    /// 关闭面板
    /// </summary>
    public void ClosePanel<T>()
    {
        string panelName = typeof(T).Name;
        if (UIPanelDict.ContainsKey(panelName))
        {
            BasePanel panel = UIPanelDict[panelName];
            panel.Hide();
            // 隐藏时仅移到Canvas根节点，不修改Transform
            panel.transform.SetParent(canvas.transform, false);
            // 切换Canvas模式
            if (postProcessingPanels.Contains(panelName))
            {
                postProcessingPanels.Remove(panelName);
                if (postProcessingPanels.Count == 0)
                {
                    StartCoroutine(DelaySetCanvasMode(false, 0.1f));
                }
            }
        }
    }

    /// <summary>
    /// 彻底销毁面板（释放资源+清除Transform数据）
    /// </summary>
    public void DestroyPanel<T>()
    {
        string panelName = typeof(T).Name;
        if (UIPanelDict.ContainsKey(panelName))
        {
            // 1. 销毁面板对象
            Destroy(UIPanelDict[panelName].gameObject);
            // 2. 移除字典
            UIPanelDict.Remove(panelName);
            // 3. 释放资源
            if (panelAssetHandles.ContainsKey(panelName))
            {
                Addressables.Release(panelAssetHandles[panelName]);
                panelAssetHandles.Remove(panelName);
            }
            // 4. 清除Transform数据
            if (panelOriginalTransformData.ContainsKey(panelName))
            {
                panelOriginalTransformData.Remove(panelName);
            }
            // 5. 移除后处理标记
            postProcessingPanels.Remove(panelName);
        }
    }
    
    private IEnumerator DelaySetCanvasMode(bool needPostProcessing, float delay)
    {
        yield return new WaitForSeconds(delay);
        SetCanvasMode(needPostProcessing);
    }

    private void UpdateCameraReferences()
    {
        mainCamera = Camera.main ?? GameObject.Find("Main Camera")?.GetComponent<Camera>();
        cameraOne = GameObject.Find("CameraOne")?.GetComponent<Camera>();
    }

    private void SetCanvasMode(bool needPostProcessing)
    {
        if (canvasComponent == null) return;
        if (needPostProcessing && cameraOne != null)
        {
            canvasComponent.renderMode = RenderMode.ScreenSpaceCamera;
            canvasComponent.worldCamera = cameraOne;
            canvasComponent.planeDistance = 0.32f;
            cameraOne.depth = mainCamera != null ? mainCamera.depth + 10 : 10;
        }
        else
        {
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasComponent.worldCamera = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        mainCamera = null;
        cameraOne = null;
        UpdateCameraReferences();
        StartCoroutine(DelaySetCanvasMode(false, 0.5f));
    }

    public override void Awake()
    {
        base.Awake();
        GameObject canvasObj = Resources.Load<GameObject>("Canvas") ?? new GameObject("Canvas");
        if (canvasObj.name != "Canvas")
        {
            canvasObj.name = "Canvas";
            canvasComponent = canvasObj.AddComponent<Canvas>();
            canvasScaler = canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
        }
        else
        {
            canvasObj = Instantiate(canvasObj);
            canvasObj.name = "Canvas";
            canvasComponent = canvasObj.GetComponent<Canvas>();
            canvasScaler = canvasObj.GetComponent<CanvasScaler>();
        }

        canvas = canvasObj.transform as RectTransform;
        DontDestroyOnLoad(canvasObj);

        // 面板容器初始化
        currentPanel = new GameObject("currentShowPanel");
        RectTransform rect = currentPanel.AddComponent<RectTransform>();
        rect.SetParent(canvas, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localPosition = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        // EventSystem初始化
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject eventSystemObj = Resources.Load<GameObject>("EventSystem") ?? new GameObject("EventSystem");
            if (eventSystemObj.name != "EventSystem")
            {
                eventSystemObj.name = "EventSystem";
                eventSystemObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystemObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
            else
            {
                eventSystemObj = Instantiate(eventSystemObj);
            }
            DontDestroyOnLoad(eventSystemObj);
        }
        isCanvasInitialized = true;
    }

    public T GetPanel<T>() where T : BasePanel
    {
        string panelName = typeof(T).Name;
        return UIPanelDict.ContainsKey(panelName) ? UIPanelDict[panelName] as T : null;
    }

    public void ForceOverlayMode() { postProcessingPanels.Clear(); StartCoroutine(DelaySetCanvasMode(false, 0.1f)); }
    public bool HasPostProcessingPanels() { return postProcessingPanels.Count > 0; }
    public void ClearAllPanel()
    {
        foreach (var panel in UIPanelDict.Values) panel.Hide();
        postProcessingPanels.Clear();
        StartCoroutine(DelaySetCanvasMode(false, 0.1f));
    }

    public void TogglePanel<T>(KeyCode keyCode) where T : BasePanel
    {
        if (Input.GetKeyDown(keyCode))
        {
            T panel = GetPanel<T>();
            if (panel == null || !panel.gameObject.activeInHierarchy)
            {
                OpenPanel<T>();
                EventCenter.Instance.EventTrigger(GameEvent.光标出现);
            }
            else
            {
                ClosePanel<T>();
                EventCenter.Instance.EventTrigger(GameEvent.光标消失);
            }
        }
    }

    private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    private void OnDestroy()
    {
        // 游戏退出时释放所有资源
        foreach (var handle in panelAssetHandles.Values)
        {
            Addressables.Release(handle);
        }
        panelAssetHandles.Clear();
        panelLoadHandles.Clear();
        panelOriginalTransformData.Clear();
    }
}