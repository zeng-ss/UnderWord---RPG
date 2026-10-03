using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

public class MainSaveData
{
    public string playerName;         // 玩家设置的昵称
    public string playerAccountData;  // 玩家的登录账号
    public int dialogueIdData;        // 当前的对话下标
    public long saveRealTicks;        // 存档时的真实系统时间戳
    public List<TaskDataRuntime> saveTasksData = new();     // 已经完成的任务列表 id
    public List<DepotDataRuntime> haveDepotsData = new();   // 已经拥有的道具列表
    public Dictionary<int, int> materialNumsData = new();   // 已经拥有的材料数量 键 =>材料 id 值 => 材料数量
}

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;
    
    #region 数据

    public string curPlayerName;
    public string curPlayerAccount;
    // 玩家基础属性（所有玩家相同）
    public PlayerData playerBaseData;
    public DepotConfig depotConfig;
    [Header("当前解锁的任务列表")]
    public TaskDataConfigSO taskConfigSO;
    [SerializeField] private MaterialDataSO materialData;
    public Dictionary<ulong, PlayerConfig> AllPlayerConfigs = new();

    // 材料数据副本
    public Dictionary<int, MaterialDataRuntime> materialDataRuntime = new();
    // 已经拥有的道具列表
    [HideInInspector] public List<DepotDataRuntime> haveDepotList = new();
    // 已经拥有的材料数量 键 =>材料 id 值 => 材料数量
    public Dictionary<int, int> materialNumDict = new() { { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 } };
    // 任务数据
    [HideInInspector] public List<TaskDataRuntime> curTasksData = new();
    // 当前的对话下标
    [HideInInspector] public int dialogueId;
    // 存档数据
    private MainSaveData mainSaveData;
    
    #endregion
    
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        if (Instance == null) { Instance = this; gameObject.name = "GameManager"; }
        else { DestroyImmediate(gameObject); }
        // 如果是旧玩家，此赋值后面会被覆盖
        foreach (var item in taskConfigSO.taskDataList) { curTasksData.Add(new TaskDataRuntime(item)); }
    }
    
    public override void OnNetworkSpawn()
    {
        foreach (var runtime in materialData.materials) { materialDataRuntime.Add(runtime.id, new MaterialDataRuntime(runtime)); }
    }

    private List<AsyncOperationHandle<GameObject>> loadedPrefabs = new();

    public void Start() { StartCoroutine(PreloadNetworkPrefabs()); }
    
    private IEnumerator PreloadNetworkPrefabs()
    {
        var prefabList = NetworkManager.Singleton.NetworkConfig.Prefabs;
        foreach (var prefab in prefabList.Prefabs)
        {
            if (!prefab.Prefab)
            {
                Debug.LogWarning("NetworkPrefabsList 中包含空引用");
                continue;
            }
            var handle = Addressables.LoadAssetAsync<GameObject>(prefab.Prefab.name);
            yield return handle;
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                loadedPrefabs.Add(handle); // 保存句柄，防止被卸载
                Debug.Log($"✅ 预加载成功: {prefab.Prefab.name}");
            }
            else Debug.LogError($"❌ 预加载失败: {prefab.Prefab.name}");
        }
        yield return null;
        SceneManager.LoadScene("StartScene");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            UIManager.Instance.OpenPanel<ExitPanel>();
            if (SceneManager.GetActiveScene().name == "GameScene")
            {
                EventCenter.Instance.EventTrigger(GameEvent.光标出现);
            }
        }
    }

    /// <summary>
    /// 玩家数据初始化
    /// </summary>
    private void PlayerDataInit()
    {
        if (mainSaveData == null)
        {
            print("mainSaveData为空！");
            return;
        }
        curPlayerName = mainSaveData.playerName;
        dialogueId = mainSaveData.dialogueIdData;
        curTasksData = new List<TaskDataRuntime>(mainSaveData.saveTasksData);
        foreach (var item in mainSaveData.haveDepotsData) haveDepotList.Add(new DepotDataRuntime(item));
        materialNumDict = new Dictionary<int, int>(mainSaveData.materialNumsData);
    }

    #region 工具方法

    /// <summary>
    /// 检测鼠标是否在指定层的UI上
    /// </summary>
    public bool IsPointerOverSpecificUILayer(LayerMask uiLayerMask)
    {
        // 获取当前鼠标位置的事件数据
        PointerEventData eventData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };
        // 存储射线检测结果
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        // 遍历检测结果
        foreach (var result in results)
        {
            // 检查是否在指定层
            if (result.gameObject != null && ((1 << result.gameObject.layer) & uiLayerMask) != 0)
            {
                return true;
            }
        }
        return false;
    }

    #endregion
    
    #region 消息

    public ChatPanel chatPanel;
    public void SendMes(string message,  string sendTime)
    {
        if (chatPanel == null) chatPanel = FindObjectOfType<ChatPanel>();
        var messageData = new MessageData
        {
            senderClientId = NetworkManager.LocalClientId, 
            name = curPlayerName,
            message = message, 
            sendTime = sendTime
        };
        // 本地先更新
        chatPanel.AddChatItem(messageData, true);
        if (!IsServer)
        {
            AddMessageServerRpc(messageData);
        }
        else { AddMessageClientRpc(messageData); }
    }

    // 再广播其他客户端
    [ServerRpc(RequireOwnership = false)]
    private void AddMessageServerRpc(MessageData data) { AddMessageClientRpc(data); }
    
    [ClientRpc]
    private void AddMessageClientRpc(MessageData data)
    {
        if (chatPanel == null) chatPanel = FindObjectOfType<ChatPanel>();
        // 其他客户端添加信息
        if (data.senderClientId != NetworkManager.LocalClientId)
        {
            if (chatPanel == null) chatPanel = FindObjectOfType<ChatPanel>();
            chatPanel.AddChatItem(data, false);
        }
    }

    #endregion

    #region 存档

    [HideInInspector] public string currentArchivePath;
    
    /// <summary>
    /// 读档
    /// </summary>
    public void LoadFileData()
    {
        //克隆新的  拿到 archive 文件夹下面的所有子文件夹的名字  
        string parentPath = Application.persistentDataPath + "/Archive/EveryGame";
        if (!Directory.Exists(parentPath)) return;
        string[] allPath = Directory.GetDirectories(parentPath);
        foreach (var path in allPath)
        {
            string fullJsonPath = Path.Combine(path, "main.json"); // 完整的存档文件路径
            MainSaveData data = JsonMgr.Instance.LoadData<MainSaveData>("main", path);
            if (data.playerAccountData == curPlayerAccount)
            {
                print(path);
                UIManager.Instance.OpenPanel<StartPanel>(panel =>
                {
                    panel.nameText.text = $"欢迎你：{data.playerName}";
                });
                mainSaveData = data;
                // 先赋值存档数据
                currentArchivePath = fullJsonPath;
                // 强制重新初始化所有受存档影响的数据
                PlayerDataInit();
                break;
            }
        }   
        // 没找到就是新玩家
        if (mainSaveData == null) UIManager.Instance.OpenPanel<StartPanel>(panel =>
        {
            panel.inputPanel.SetActive(true);
        });
    }
    
    /// <summary>
    /// 保存存档信息
    /// 保存对应游戏存档到archive文件夹下面   Archive/名称/Main.json  
    /// </summary>
    public void SaveArchive()
    {
        mainSaveData = new MainSaveData
        {
            playerName = curPlayerName,
            playerAccountData = curPlayerAccount,
            dialogueIdData = dialogueId,
            saveTasksData = new List<TaskDataRuntime>(curTasksData),
            haveDepotsData = new List<DepotDataRuntime>(haveDepotList),
            materialNumsData = new Dictionary<int, int>(materialNumDict),
            saveRealTicks = DateTimeOffset.Now.ToUnixTimeSeconds()       // 存档时的真实系统时间
        };
        if (currentArchivePath != null && File.Exists(currentArchivePath))
        {
            // 存在当前存档路径：直接覆盖原文件
            string json = JsonConvert.SerializeObject(mainSaveData, Formatting.Indented, 
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All });
            File.WriteAllText(currentArchivePath, json); // 覆盖写入
            Debug.Log("已覆盖存档：" + currentArchivePath);
            return;
        }
        // 保存的地址
        JsonMgr.Instance.SaveData(mainSaveData, "main", "Archive/EveryGame/" + mainSaveData.playerName + "/");
    }

    #endregion

    #region 网络

    public void StartGame(Dictionary<ulong, PlayerConfig> playerInfos)
    {
        AllPlayerConfigs = new Dictionary<ulong, PlayerConfig>(playerInfos);
        // 同步给所有客户端
        UpdateAllPlayerInfos();
    }

    private void UpdateAllPlayerInfos()
    {
        // 逐个发送玩家信息
        foreach (var playerInfo in AllPlayerConfigs)
        {
            UpdatePlayerInfoClientRpc(playerInfo.Value);
        }
    }

    /// <summary>
    /// 客户端RPC：更新单个玩家信息
    /// </summary>
    [ClientRpc]
    private void UpdatePlayerInfoClientRpc(PlayerConfig playerInfo)
    {
        if (!IsServer)
        {
            // 添加到或更新玩家信息字典
            AllPlayerConfigs[playerInfo.id] = playerInfo;
        }
    }

    #endregion
    
    #region 其他脚本需要调用的网络请求方法

    /// <summary>
    /// 全局客户端请求服务端为当前玩家生成敌人（携带玩家 id）
    /// </summary>
    /// <param name="requestClientId">请求生成敌人的玩家ID</param>
    [ServerRpc(RequireOwnership = false)]
    public void RequestSpawnEnemyServerRpc(ulong requestSpawnId)
    {
        ResMgr.Instance.LoadAndInstantiateAsync("enemy",null,enemy =>
        {
            enemy.GetComponent<NetworkObject>().Spawn();
            enemy.GetComponent<EnemyCtrl>().requestSpawnId.Value = requestSpawnId;
        });
    }

    #endregion

    public override void OnDestroy()
    {
        // 游戏退出时释放所有预加载的预制体
        foreach (var handle in loadedPrefabs.Where(handle => handle.IsValid()))
        {
            Addressables.Release(handle);
        }
        loadedPrefabs.Clear();
    }
    
}
