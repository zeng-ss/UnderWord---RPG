using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class StartPanel : BasePanel
{
    public TMP_Text nameText;
    public Button hostButton;
    public Button clientButton;
    [Header("退出游戏按钮")]
    public Button exitBtn;
    [Header("昵称")]
    public GameObject inputPanel;
    public TMP_InputField inputField;
    public Button confirmBtn;
    private float heartBeatInterval = 30f; // xx秒心跳一次
    private float heartBeatTimer;
    
    private void OnEnable()
    {
        hostButton.onClick.AddListener(() =>
        {
            hostButton.interactable = false;
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.Shutdown();
            }
            if (NetworkManager.Singleton.StartHost())
            {
                SceneMgr.Instance.LoadScene("LobbyScene");
            }
            else
            {
                hostButton.interactable = true; // 启动失败则重新启用按钮
            }
            UIManager.Instance.ClosePanel<StartPanel>();
        });
        clientButton.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartClient();
            UIManager.Instance.ClosePanel<StartPanel>();
        });
        exitBtn.onClick.AddListener(ExitGame);
        confirmBtn.onClick.AddListener(SaveName);
    }
    
    private void Update()
    {
        // 只有登录成功后才执行心跳
        if (!string.IsNullOrEmpty(GameManager.Instance.curPlayerAccount))
        {
            heartBeatTimer += Time.deltaTime;
            if (heartBeatTimer >= heartBeatInterval)
            {
                // 心跳：更新最后活跃时间
                string heartBeatSql = $"UPDATE user SET LastActiveTime = NOW() WHERE UserAccount = '{GameManager.Instance.curPlayerAccount}'";
                DBManager.Instance.ExecuteNonQuery(heartBeatSql);
                heartBeatTimer = 0;
                Debug.Log("心跳成功，更新最后活跃时间");
            }
        }
    }

    private void SaveName()
    {
        string inputName = inputField.text.Trim();
        if (string.IsNullOrEmpty(inputName))
        {
            UIManager.Instance.OpenPanel<TipPanel>(panel =>
            {
                panel.ShowTip("昵称不合法，请重新输入！");
            });
        }
        else
        {
            GameManager.Instance.curPlayerName = inputField.text.Trim();
            inputPanel.gameObject.SetActive(false);
            // 立即保存一次，确保昵称被记录
            GameManager.Instance.SaveArchive();
            nameText.text = $"欢迎你：{inputName}";
        }
    }

    private void ExitGame()
    {
        // 调用 GameManager 存档方法  
        GameManager.Instance.SaveArchive();
        DBManager.Instance.ExecuteNonQuery($"UPDATE user SET IsOnline = 0 WHERE UserAccount = '{GameManager.Instance.curPlayerAccount}'");
        DBManager.Instance.CloseConnection();
        Application.Quit();
    }
}
