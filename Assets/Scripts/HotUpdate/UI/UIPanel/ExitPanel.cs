using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class ExitPanel : BasePanel
{
    public Button yesBtn;
    public Button noBtn;
    private Vector3 startPos = new(0, 840, 0);

    private void OnEnable()
    {
        transform.localPosition = startPos;
        transform.DOLocalMove(Vector3.zero, 0.5f);
        yesBtn.onClick.AddListener(() =>
        {
            // 调用 GameManager 存档方法  
            GameManager.Instance.SaveArchive();
            DBManager.Instance.ExecuteNonQuery($"UPDATE user SET IsOnline = 0 WHERE UserAccount = '{GameManager.Instance.curPlayerAccount}'");
            DBManager.Instance.CloseConnection();
            Application.Quit();
        });
        noBtn.onClick.AddListener(() =>
        {
            transform.DOLocalMove(startPos, 0.5f)
                .OnComplete(() => { UIManager.Instance.ClosePanel<ExitPanel>(); });
        });
    }
    
    private void OnDisable()
    {
        yesBtn.onClick.RemoveAllListeners();
        noBtn.onClick.RemoveAllListeners();
    }
}
