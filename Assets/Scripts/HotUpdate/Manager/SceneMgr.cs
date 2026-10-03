using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

public class SceneMgr : NetworkBehaviour
{
    // 网络变量标记游戏是否已开始
    public NetworkVariable<bool> isGameStarted = new();
    
    // 加载进度相关
    private bool isLoading;
    private float loadingProgress;
    private string loadingSceneName = "";
    
    private static SceneMgr _instance;
    public static SceneMgr Instance => _instance;

    public virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject); // 安全销毁，不会立即执行
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // 监听网络变量变化
        isGameStarted.OnValueChanged += OnGameStartedChanged;
        if (IsServer)
        {
            NetworkManager.SceneManager.OnLoadEventCompleted += OnServerLoadComplete;
            NetworkManager.SceneManager.OnLoad += OnSceneLoad;
            // 监听新客户端连接（核心：所有客户端连接都触发）
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
        // 移除晚加入客户端的事件监听逻辑（改为服务端生成后客户端直接初始化）
    }

    private void OnGameStartedChanged(bool oldValue, bool newValue) 
    { 
        if (newValue) { EventCenter.Instance.EventTrigger(GameEvent.游戏开始); } 
    }
    
    /// <summary>
    /// 加载场景（带进度显示）
    /// </summary>
    public void LoadScene(string scenename)
    {
        // 只有服务器可以加载场景
        if (!IsServer) return;
        // 打开加载面板
        UIManager.Instance.OpenPanel<LoadPanel>();
        // 重置进度
        isLoading = true;
        loadingSceneName = scenename;
        // 加载场景前重置游戏状态
        isGameStarted.Value = false;
        // 开始加载场景
        print("Starting LoadScene");
        NetworkManager.SceneManager.LoadScene(scenename, LoadSceneMode.Single);
        StartLoadSceneClientRpc();
    }

    [ClientRpc]
    // 打开加载面板
    private void StartLoadSceneClientRpc() { UIManager.Instance.OpenPanel<LoadPanel>(); }
    
    private void OnSceneLoad(ulong clientId, string sceneName, LoadSceneMode loadSceneMode, AsyncOperation asyncOperation)
    {
        // 如果不是服务器触发的加载（比如客户端自己加载），不需要处理
        if (!IsServer && clientId != NetworkManager.LocalClientId) return;
        loadingProgress = 0f;
        // 开启协程更新加载进度
        StartCoroutine(TrackLoadingProgress(asyncOperation));
    }

    private IEnumerator TrackLoadingProgress(AsyncOperation asyncOperation)
    {
        // 不允许自动激活场景
        asyncOperation.allowSceneActivation = false;
        float displayProgress = 0f;
        float realProgress;
        float minLoadTime = 1.5f; // 最少显示1.5秒
        float elapsedTime = 0f;
    
        // 第一阶段：真实加载到90%
        while (asyncOperation.progress < 0.9f)
        {
            elapsedTime += Time.deltaTime;
            // 获取真实进度 (0-0.9 映射到 0-1)
            realProgress = asyncOperation.progress / 0.9f;
            // 计算显示进度：取真实进度和时间进度的最小值，确保不会太快
            float timeProgress = Mathf.Clamp01(elapsedTime / minLoadTime);
            displayProgress = Mathf.Min(realProgress, timeProgress);
            // 发送进度事件
            EventCenter.Instance.EventTrigger(GameEvent.进度条加载, displayProgress);
            SyncProgressClientRpc(displayProgress);
            yield return null;
        }
        // 第二阶段：平滑过渡到 100%
        float startProgress = displayProgress; // 【修复】记录起始值
        float smoothElapsed = 0f;
        float smoothDuration = 1f;
        
        while (smoothElapsed < smoothDuration)
        {
            smoothElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(smoothElapsed / smoothDuration);
            float easeT = t * (2 - t);
            displayProgress = Mathf.Lerp(startProgress, 1f, easeT);
            // 服务器更新本地进度
            loadingProgress = displayProgress;
            // 发送进度事件（本地）
            EventCenter.Instance.EventTrigger(GameEvent.进度条加载, displayProgress);
            // 同步进度给所有客户端
            SyncProgressClientRpc(displayProgress);
            yield return null;
        }
        // 确保显示 100%
        EventCenter.Instance.EventTrigger(GameEvent.进度条加载, 1f);
        SyncProgressClientRpc(1f);
        // 等待一小段时间，让玩家看到 100%
        yield return new WaitForSeconds(0.3f);
        // 激活场景
        asyncOperation.allowSceneActivation = true;
        // 等待场景真正切换完成
        while (!asyncOperation.isDone) { yield return null; }
    }

    /// <summary>
    /// 同步进度给所有客户端
    /// </summary>
    [ClientRpc]
    private void SyncProgressClientRpc(float progress)
    {
        loadingProgress = progress;
        EventCenter.Instance.EventTrigger(GameEvent.进度条加载, progress);
    }
    
    private void OnServerLoadComplete(string scenename, LoadSceneMode loadscenemode, List<ulong> clientscompleted, List<ulong> clientstimedout)
    {
        // 确保消息面板引用正确
        switch (scenename)
        {
            case "LobbyScene":
                Addressables.InstantiateAsync("LobbyController").WaitForCompletion().GetComponent<NetworkObject>().Spawn();
                break;
            case "GameScene":
                // 核心修改1：服务端为所有已连接客户端生成玩家
                foreach (var client in NetworkManager.Singleton.ConnectedClients)
                {
                    // 跳过服务器自己（Host端的话包含LocalClientId）
                    if (client.Key == NetworkManager.ServerClientId) continue;
                    SpawnPlayerForClient(client.Key);
                    SpawnOtherForClientRpc(client.Key);
                }
                GameObject player = Addressables.InstantiateAsync("Character").WaitForCompletion();
                NetworkObject playerNO = player.GetComponent<NetworkObject>();
        
                // SpawnAsPlayerObject 分配给 Host端
                playerNO.SpawnAsPlayerObject(NetworkManager.LocalClientId, destroyWithScene: true);
                Addressables.InstantiateAsync("NPC");
                Addressables.InstantiateAsync("GameController").WaitForCompletion().GetComponent<NetworkObject>().Spawn();
                isGameStarted.Value = true;
                break;
        }
        isLoading = false;
        loadingProgress = 1f;
        CloseLoadPanelClientRpc();
    }

    [ClientRpc]
    private void CloseLoadPanelClientRpc() { UIManager.Instance.ClosePanel<LoadPanel>(); }

    // 新客户端连接时，服务端立即为其生成玩家（无论游戏是否开始）
    private void OnClientConnected(ulong clientId) 
    {
        // 跳过服务器自己
        if (clientId == NetworkManager.ServerClientId) return;
        // 如果当前场景是GameScene，直接为新客户端生成玩家
        if (SceneManager.GetActiveScene().name == "GameScene")
        {
            SpawnPlayerForClient(clientId);
            SpawnOtherForClientRpc(clientId);
        }
    }

    [ClientRpc]
    private void SpawnOtherForClientRpc(ulong clientId)
    {
        if (clientId != NetworkManager.LocalClientId) return;
        Addressables.InstantiateAsync("NPC");
    }

    /// <summary>
    /// 服务端为指定客户端生成玩家对象
    /// </summary>
    /// <param name="clientId">目标客户端ID</param>
    private void SpawnPlayerForClient(ulong clientId)
    {
        // 异步加载改为同步（服务端可安全使用WaitForCompletion）
        GameObject player = Addressables.InstantiateAsync("Character").WaitForCompletion();
        NetworkObject playerNO = player.GetComponent<NetworkObject>();
        // SpawnAsPlayerObject 分配给指定客户端，归客户端控制
        playerNO.SpawnAsPlayerObject(clientId, destroyWithScene: true);
        //Debug.Log($"服务端为客户端 {clientId} 生成玩家对象，NetworkObjectId: {playerNO.NetworkObjectId}");
    }
    
    /// <summary>
    /// 获取当前加载进度
    /// </summary>
    public float GetLoadingProgress() { return loadingProgress; }

    /// <summary>
    /// 是否正在加载
    /// </summary>
    public bool IsLoading() { return isLoading; }

    /// <summary>
    /// 获取正在加载的场景名称
    /// </summary>
    public string GetLoadingSceneName() { return loadingSceneName; }
    
    public override void OnDestroy()
    {
        base.OnDestroy();
        if (_instance == this)
        {
            _instance = null; // 清理静态引用
        }
        isGameStarted.OnValueChanged -= OnGameStartedChanged;
        if (IsServer)
        {
            NetworkManager.SceneManager.OnLoadEventCompleted -= OnServerLoadComplete;
            NetworkManager.SceneManager.OnLoad -= OnSceneLoad;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }
}