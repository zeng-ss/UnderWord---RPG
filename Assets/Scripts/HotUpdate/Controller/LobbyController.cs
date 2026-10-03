using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class PlayerConfig : INetworkSerializable
{
    public ulong id;          // 玩家的唯一网络ID
    public string name;
    public bool isReady;      // 玩家是否已准备
    public string headImageName;
    
    /// <summary>
    /// 网络序列化方法，自动将数据打包/解包进行网络传输
    /// </summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref id);
        serializer.SerializeValue(ref name);
        serializer.SerializeValue(ref isReady);
        serializer.SerializeValue(ref headImageName);
    }
}

public class LobbyController : NetworkBehaviour
{
    #region UI组件
    
    private GameObject roomItemcontent;
    private Button startBtn;
    private Toggle toggle;
    private TMP_Text readyTipText;
    
    #endregion
    
    // 数据存储
    private Dictionary<ulong, PlayerRoomItem> playerRoomItemDict = new();
    private Dictionary<ulong, PlayerConfig> playerConfigDict = new();
    private readonly object _dictLock = new();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // 获取UI组件
        roomItemcontent = GameObject.Find("RoomItemContent");
        toggle = GameObject.Find("Toggle").GetComponent<Toggle>();
        UIManager.Instance.OpenPanel<ChatPanel>();
        startBtn = GameObject.Find("StartBtn").GetComponent<Button>();
        readyTipText = GameObject.Find("ReadyTipText").GetComponent<TMP_Text>();

        // 添加按钮监听
        startBtn.onClick.AddListener(OnStartGame);
        toggle.onValueChanged.AddListener(OnToggleReady);

        if (IsServer)
        {
            NetworkManager.OnClientConnectedCallback += OnConnectClient;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnect;
            // 服务端添加自己
            PlayerConfig serverPlayerConfig = new PlayerConfig
            {
                id = NetworkManager.Singleton.LocalClientId,
                name = GameManager.Instance.curPlayerName,
                isReady = false,
                headImageName = "icon_question"
            };
            AddPlayer(serverPlayerConfig);
        }
        else
        {
            // 客户端请求所有玩家信息，并同步自己的名称
            RequestAllPlayersServerRpc();
            SyncPlayerNameToServerRpc(GameManager.Instance.curPlayerName);
        }
        startBtn.gameObject.SetActive(false);
        readyTipText.gameObject.SetActive(true);
    }

    /// <summary>
    /// 添加玩家到大厅（线程安全）
    /// </summary>
    private void AddPlayer(PlayerConfig playerConfig)
    {
        UIManager.Instance.ClosePanel<LoadPanel>();
        lock (_dictLock)
        {
            // 安全检查：如果key已存在，先移除旧的
            if (playerConfigDict.ContainsKey(playerConfig.id))
            {
                playerConfigDict[playerConfig.id] = playerConfig;
                if (playerRoomItemDict.ContainsKey(playerConfig.id))
                {
                    playerRoomItemDict[playerConfig.id].Init(playerConfig);
                }
                return;
            }
            // 添加新玩家
            playerConfigDict.Add(playerConfig.id, playerConfig);
            // 实例化玩家Item
            ResMgr.Instance.LoadAndInstantiateAsync("PlayerRoomItem",roomItemcontent.transform,obj =>
            {
                PlayerRoomItem roomItem = obj.GetComponent<PlayerRoomItem>();
                // 安全检查：避免重复添加
                if (!playerRoomItemDict.ContainsKey(playerConfig.id))
                {
                    playerRoomItemDict.Add(playerConfig.id, roomItem);
                    roomItem.Init(playerConfig);
                }
                else Destroy(obj);
            });
        }
    }

    private void RemovePlayer(ulong playerId)
    {
        lock (_dictLock)
        {
            if (playerRoomItemDict.ContainsKey(playerId))
            {
                Destroy(playerRoomItemDict[playerId].gameObject);
                playerRoomItemDict.Remove(playerId);
            }
            if (playerConfigDict.ContainsKey(playerId))
            {
                playerConfigDict.Remove(playerId);
            }
        }
    }

    private void OnConnectClient(ulong clientId)
    {
        // 创建临时配置，等待客户端同步真实名称
        PlayerConfig playerConfig = new PlayerConfig
        {
            id = clientId,
            name = $"Player_{clientId}", // 临时名称
            isReady = false,
            headImageName = "icon_question"
        };
        AddPlayer(playerConfig);
        UpdateAllPlayerInfos();
    }

    private void OnClientDisconnect(ulong clientId)
    {
        RemovePlayer(clientId);
        UpdateAllPlayerInfos();
    }

    /// <summary>
    /// 客户端请求同步所有玩家信息
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestAllPlayersServerRpc()
    {
        List<PlayerConfig> tempConfigs = new List<PlayerConfig>();
        lock (_dictLock)
        {
            tempConfigs.AddRange(playerConfigDict.Values);
        }
        
        foreach (var playerConfig in tempConfigs)
        {
            UpdatePlayerInfoClientRpc(playerConfig);
        }
    }

    /// <summary>
    /// 客户端同步名称到服务端
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void SyncPlayerNameToServerRpc(string playerName)
    {
        ulong clientId = NetworkManager.Singleton.LocalClientId;
        lock (_dictLock)
        {
            if (playerConfigDict.ContainsKey(clientId))
            {
                // 更新现有玩家名称
                playerConfigDict[clientId].name = playerName;
            }
            else
            {
                // 如果不存在，创建新配置
                PlayerConfig newConfig = new PlayerConfig
                {
                    id = clientId,
                    name = playerName,
                    isReady = false,
                    headImageName = "icon_question"
                };
                AddPlayer(newConfig);
            }
            
            // 立即更新UI
            if (playerRoomItemDict.ContainsKey(clientId))
            {
                playerRoomItemDict[clientId].UpdateRoomItem(playerConfigDict[clientId]);
            }
            
            // 广播给所有客户端
            UpdateAllPlayerInfos();
        }
    }

    /// <summary>
    /// 更新所有客户端的玩家信息
    /// </summary>
    private void UpdateAllPlayerInfos()
    {
        bool canGo = true;
        List<PlayerConfig> playerConfigs = new List<PlayerConfig>();
        lock (_dictLock)
        {
            playerConfigs.AddRange(playerConfigDict.Values);
        }
        foreach (var playerConfig in playerConfigs)
        {
            if (!playerConfig.isReady) canGo = false;
            UpdatePlayerInfoClientRpc(playerConfig);
        }
        startBtn.gameObject.SetActive(canGo && IsServer);
    }

    /// <summary>
    /// 更新客户端玩家信息
    /// </summary>
    [ClientRpc]
    private void UpdatePlayerInfoClientRpc(PlayerConfig playerConfig)
    {
        lock (_dictLock)
        {
            ulong localId = NetworkManager.Singleton.LocalClientId;
            // 如果是自己，确保使用正确的本地名称
            if (playerConfig.id == localId)
            {
                playerConfig.name = GameManager.Instance.curPlayerName;
            }
            if (playerConfigDict.ContainsKey(playerConfig.id))
            {
                // 更新现有玩家
                playerConfigDict[playerConfig.id] = playerConfig;
                if (playerRoomItemDict.ContainsKey(playerConfig.id))
                {
                    playerRoomItemDict[playerConfig.id].UpdateRoomItem(playerConfig);
                }
            }
            // 添加新玩家
            else AddPlayer(playerConfig);
        }
        if (playerConfig.id == NetworkManager.LocalClientId)
        {
            lock (_dictLock)
            {
                playerConfigDict[playerConfig.id].name = GameManager.Instance.curPlayerName;
                if (!playerRoomItemDict.ContainsKey(playerConfig.id)) return;
                playerRoomItemDict[playerConfig.id].UpdateRoomItem(playerConfigDict[playerConfig.id]);
            }
        }
    }

    #region Toggle相关
    private void OnToggleReady(bool isOn)
    {
        ulong localId = NetworkManager.Singleton.LocalClientId;
        lock (_dictLock)
        {
            if (!playerConfigDict.ContainsKey(localId))
            {
                Debug.LogError("找不到本地玩家配置");
                return;
            }
            PlayerConfig currentPlayerConfig = playerConfigDict[localId];
            currentPlayerConfig.isReady = isOn;
            playerConfigDict[localId] = currentPlayerConfig;
            
            readyTipText.gameObject.SetActive(!isOn);
            if (playerRoomItemDict.ContainsKey(localId))
            {
                playerRoomItemDict[localId].UpdateRoomItem(currentPlayerConfig);
            }
            if (IsServer)
            {
                UpdateAllPlayerInfos();
            }
            else UpdatePlayerConfigServerRpc(currentPlayerConfig);
        }
    }
    #endregion
    
    [ServerRpc(RequireOwnership = false)]
    private void UpdatePlayerConfigServerRpc(PlayerConfig playerConfig)
    {
        lock (_dictLock)
        {
            playerConfigDict[playerConfig.id] = playerConfig;
            UpdateAllPlayerInfos();
        }
    }
    
    #region 开始游戏相关
    
    private void OnStartGame()
    {
        Debug.Log("开始游戏");
        GameManager.Instance.StartGame(playerConfigDict);
        SceneMgr.Instance.LoadScene("GameScene");
    }

    #endregion
    
    public override void OnDestroy()
    {
        // 清理事件监听
        if (!IsServer) return;
        NetworkManager.OnClientConnectedCallback -= OnConnectClient;
        NetworkManager.OnClientDisconnectCallback -= OnClientDisconnect;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        // 取消所有事件监听
        startBtn.onClick.RemoveAllListeners();
        toggle.onValueChanged.RemoveAllListeners();
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnConnectClient;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnect;
        }
        // 清理数据
        lock (_dictLock) // 加锁清理
        {
            playerConfigDict.Clear();
            playerRoomItemDict.Clear();
        }
    }
}