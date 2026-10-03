using System.Data;
using MySqlConnector;
using UnityEngine.UI;

public class RegisterPanel : BasePanel
{
    public Text account;
    public Text password;
    public Text pwsCheck;
    public Button registerBtn;
    public Button loginBtn;

    private void Start()
    {
        registerBtn.onClick.AddListener(RegisterCheck);
        loginBtn.onClick.AddListener(() =>
        {
            UIManager.Instance.OpenPanel<LoginPanel>();
            UIManager.Instance.ClosePanel<RegisterPanel>();
        });
    }

    /// <summary>
    /// 核对注册方法
    /// </summary>
    private void RegisterCheck()
    {
        //首先先判断用户存不在   如果存在就要换一个名字  查询语句 
        string query = $"SELECT COUNT(*) FROM user WHERE UserAccount='{account.text}'";
        //插入新的用户   插入语句 
        string inserQuery =
            $"INSERT INTO user (UserAccount,UserPassword,IsOnline) VALUES('{account.text.Trim()}','{password.text.Trim()}','0')";
        if (password.text.Trim() != pwsCheck.text.Trim() || password.text.Trim() == "")
        {
            UIManager.Instance.OpenPanel<TipPanel>(panel => { panel.ShowTip("密码输入不一致，请重新输入"); });
            return;
        }
        if (DBManager.Instance.OpenConnection())
        {
            DataTable dt = DBManager.Instance.SelectQuery(query);
            if (int.Parse(dt.Rows[0][0].ToString()) > 0)
            {
                UIManager.Instance.OpenPanel<TipPanel>(panel => { panel.ShowTip("用户名已存在"); });
            }
            else
            {
                try
                {
                    DBManager.Instance.NonQuery(inserQuery);
                    UIManager.Instance.OpenPanel<TipPanel>(panel => { panel.ShowTip("注册成功"); });
                }
                catch (MySqlException ex)
                {
                    print(ex.Message);
                    UIManager.Instance.OpenPanel<TipPanel>(panel => { panel.ShowTip("注册失败"); });
                }
            }
        }
        else
        {
            UIManager.Instance.OpenPanel<TipPanel>(panel => { panel.ShowTip("数据库连接失败"); });
        }
    }
}