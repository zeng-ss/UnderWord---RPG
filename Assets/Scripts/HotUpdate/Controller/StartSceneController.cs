using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class StartSceneController : MonoBehaviour
{
    public Button hostButton;
    public Button clientButton;
    
    private void Start()
    {
        hostButton = GameObject.Find("HostButton").GetComponent<Button>();
        clientButton = GameObject.Find("ClientButton").GetComponent<Button>();
        NetworkManager.Singleton.Shutdown();
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
        });
        clientButton.onClick.AddListener((() => NetworkManager.Singleton.StartClient()));
    }
}
