using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 局内消息数据
/// </summary>
public class MessageData : INetworkSerializable
{
    public ulong senderClientId; // 发送者的客户端 id
    public string name;      // 发送者
    public string message;   // 消息内容
    public string sendTime;  // 发送时间（时间戳）

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref senderClientId);
        serializer.SerializeValue(ref name);
        serializer.SerializeValue(ref message);
        serializer.SerializeValue(ref sendTime);
    }
}
public class GameController : NetworkBehaviour
{
    private bool isLockMouse;

    private void Start()
    {
        EventCenter.Instance.AddEventListener(GameEvent.光标出现,NoLock);
        EventCenter.Instance.AddEventListener(GameEvent.光标消失,Lock);
    }

    private void NoLock() { isLockMouse = false; }
    private void Lock() { isLockMouse = true; }
    
    private bool noLock;

    private void Update()
    {
        if (!noLock)
        {
            Cursor.lockState = isLockMouse ? CursorLockMode.Locked : CursorLockMode.None;
        }
        if (Input.GetKeyDown(KeyCode.K))
        {
            if (!noLock)
            {
                noLock = true;
                Cursor.lockState = CursorLockMode.None;
            }
            else noLock = false;
        }
        UIManager.Instance.TogglePanel<ChatPanel>(KeyCode.C);
        UIManager.Instance.TogglePanel<DepotPanel>(KeyCode.V);
        if (Input.GetKeyDown(KeyCode.Alpha5) && UIManager.Instance.GetPanel<ImprovePanel>())
        {
            UIManager.Instance.GetPanel<ImprovePanel>().UpdateFillBar(100f);
        }
        if (Input.GetKeyDown(KeyCode.B) && UIManager.Instance.GetPanel<DepotPanel>())
        {
            var panel = UIManager.Instance.GetPanel<PlayerDataPanel>();
            // 检查是否有动画正在播放
            if (panel && panel.isAnimating) return;
            if (!panel || !panel.gameObject.activeInHierarchy)
            {
                // 面板不存在或未激活，打开面板
                UIManager.Instance.OpenPanel<PlayerDataPanel>(dataPanel =>
                {
                    dataPanel.UpdatePlayerData(UIManager.Instance.GetPanel<DepotPanel>().equipedDepotList);
                });
            }
            else { UIManager.Instance.ClosePanel<PlayerDataPanel>(); }
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        EventCenter.Instance.RemoveEventListener(GameEvent.光标出现,NoLock);
        EventCenter.Instance.RemoveEventListener(GameEvent.光标消失,Lock);
    }
}
