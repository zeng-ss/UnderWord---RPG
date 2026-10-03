using System.Collections.Generic;
using TMPro;
using Unity.Netcode;

public class PlayerDataPanel : BasePanel
{
    public TMP_Text attackText;
    public TMP_Text healthText;
    public TMP_Text defenseText;
    public TMP_Text baoJiText;
    public TMP_Text exAttackText;
    private List<DepotDataRuntime> currentDepotsData = new();
    private PlayerCtrl player;

    private void OnEnable()
    {
        player = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerCtrl>();
        player.playerData.OnValueChanged += OnPlayerDataChanged;
        if (attackText.text == "？？？")
        {
            attackText.text = $"{GameManager.Instance.playerBaseData.attackValue}";
            healthText.text = $"{GameManager.Instance.playerBaseData.maxHealthValue}";
            defenseText.text = $"{GameManager.Instance.playerBaseData.defenseValue}";
            baoJiText.text = $"{GameManager.Instance.playerBaseData.baoJiValue}%";
            exAttackText.text = $"{GameManager.Instance.playerBaseData.exAttackValue}";
        }
    }

    // 更新 UI
    private void OnPlayerDataChanged(PlayerData previousValue, PlayerData newValue)
    {
        attackText.text = $"{player.playerData.Value.attackValue}";
        healthText.text = $"{player.playerData.Value.maxHealthValue}";
        defenseText.text = $"{player.playerData.Value.defenseValue}";
        baoJiText.text = $"{player.playerData.Value.baoJiValue}%";
        exAttackText.text = $"{player.playerData.Value.exAttackValue}";
    }

    // 更新信息
    public void UpdatePlayerData(List<DepotDataRuntime> depotsData)
    {
        if (!player) player = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerCtrl>();
        currentDepotsData = depotsData;
        // 客户端请求服务端计算，服务端计算完成再赋值，实现数据同步
        if (!GameManager.Instance.IsServer)
        {
            var newPlayerData = player.CalculatePlayerData(depotsData);
            player.CalculatePlayerDataServerRpc(newPlayerData);
        }
        // 服务端直接更新数据自动同步
        else { player.playerData.Value = player.CalculatePlayerData(depotsData); }
    }

    private void OnDestroy()
    {
        player.playerData.OnValueChanged -= OnPlayerDataChanged;
    }
}
