using System.Collections.Generic;
using System.Data;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 登录面板  每次打开登录面板  重新连接 数据库  关掉面板关闭数据库连接   
/// </summary>
public class LoginPanel : BasePanel
{
    public Text StateText;
    public Dropdown dropdown;
    private List<Dropdown.OptionData> optionDatas = new();
    private ServerData currentServerData;
    private Dictionary<string, ServerData> serverDataDict = new();
    public Button loginBtn;
    public Button registerBtn;
    public Text account;
    public Text password;

    /// <summary>
    /// 激活面板
    /// </summary>
    private void OnEnable()
    {
        if (!DBManager.Instance.OpenConnection()) //链接失败
        {
            UIManager.Instance.OpenPanel<TipPanel>(panel =>
            {
                panel.ShowTip("数据库连接失败");
            });
            return;
        }
        //更新选服信息  
        UpdateServerData();
        //给下拉列表添加值更改的监听方法 
        dropdown.onValueChanged.AddListener(value =>
        {
            currentServerData = serverDataDict[optionDatas[value].text];
            StateText.text = currentServerData.State;
            StateText.color = currentServerData.State == "爆满" ? Color.red : Color.green;
        });
    }

    /// <summary>
    /// 更新选服信息的方法  
    /// </summary>
    private void UpdateServerData() //到这里说明数据库连接成功 
    {
        optionDatas.Clear();
        serverDataDict.Clear();
        //需要一条查询语句  
        string query = "SELECT * FROM server;";
        DataTable dt = new DataTable();
        dt = DBManager.Instance.SelectQuery(query); //查询对应的选服信息  
        foreach (DataRow data in dt.Rows) //每一行的信息  
        {
            //拿到每一行的信息生成一个serverData的数据对象   还要把这个存到我们的字典中 
            ServerData serverdata = new ServerData
            {
                ServerId = int.Parse(data[0].ToString()),
                ServerIp = data[1].ToString(),
                State = data[2].ToString(),
                ServerName = data[3].ToString()
            };
            // 添加到 optionDatas列表中  
            Dropdown.OptionData optionData = new Dropdown.OptionData(serverdata.ServerName);
            optionDatas.Add(optionData);
            serverDataDict.Add(serverdata.ServerName, serverdata);
        }
        // 添加到我们的 dropdown
        dropdown.options = optionDatas;
        // 给我们当前选中的数据对象赋值  
        currentServerData = serverDataDict[optionDatas[0].text];
        StateText.text = currentServerData.State;
        StateText.color = currentServerData.State == "爆满" ? Color.red : Color.green;
    }

    private void Start()
    {
        loginBtn.onClick.AddListener(LoginCheck);
        registerBtn.onClick.AddListener(() =>
        {
            UIManager.Instance.OpenPanel<RegisterPanel>();
            UIManager.Instance.ClosePanel<LoginPanel>();
        });
    }

/// <summary>
    /// 核心登录校验：只判断账号密码+是否在线
    /// </summary>
    private void LoginCheck()
    {
        string inputAccount = account.text.Trim();
        string inputPwd = password.text.Trim();

        if (string.IsNullOrEmpty(inputAccount) || string.IsNullOrEmpty(inputPwd))
        {
            UIManager.Instance.OpenPanel<TipPanel>(panel =>
            {
                panel.ShowTip("账号或密码不能为空");
            });
            return;
        }

        switch (currentServerData.State)
        {
            case "爆满":
                UIManager.Instance.OpenPanel<TipPanel>(panel =>
                {
                    panel.ShowTip("当前服务器爆满请选择其他服务器");
                });
                break;
            default:
                // ========== 新增：登录时先清理超时5分钟的在线账号 ==========
                string clearTimeoutSql = @"
                UPDATE user 
                SET IsOnline = 0 
                WHERE IsOnline = 1 
                AND TIMESTAMPDIFF(MINUTE, LastActiveTime, NOW()) > 3;"; // 5分钟超时可调整
                DBManager.Instance.ExecuteNonQuery(clearTimeoutSql);
                Debug.Log("已清理超时在线账号");
                // 1. 查询账号+在线状态
                string query = $"SELECT UserPassword, IsOnline FROM user WHERE UserAccount = '{inputAccount}' LIMIT 1";
                DataTable dt = DBManager.Instance.SelectQuery(query);
                
                if (dt.Rows.Count > 0)
                {
                    string dbPwd = dt.Rows[0]["UserPassword"].ToString();
                    int isOnline = int.Parse(dt.Rows[0]["IsOnline"].ToString());

                    // 2. 关键校验：是否已在线
                    if (isOnline == 1)
                    {
                        UIManager.Instance.OpenPanel<TipPanel>(panel =>
                        {
                            panel.ShowTip("该账号已在其他设备登录");
                        });
                        return;
                    }

                    // 3. 密码正确 → 标记为在线
                    if (dbPwd == inputPwd)
                    {
                        // 更新LastActiveTime为当前时间，用于后续超时判断
                        string updateSql = $"UPDATE user SET IsOnline = 1, LastActiveTime = NOW() WHERE UserAccount = '{inputAccount}'";
                        if (DBManager.Instance.ExecuteNonQuery(updateSql) > 0)
                        {
                            GameManager.Instance.curPlayerAccount = inputAccount;
                            GameManager.Instance.LoadFileData();
                            UIManager.Instance.ClosePanel<LoginPanel>();
                            UIManager.Instance.OpenPanel<TipPanel>(panel =>
                            {
                                panel.ShowTip("登录成功");
                            });
                        }
                        else
                        {
                            UIManager.Instance.OpenPanel<TipPanel>(panel =>
                            {
                                panel.ShowTip("登录失败，请重试");
                            });
                        }
                    }
                    else
                    {
                        UIManager.Instance.OpenPanel<TipPanel>(panel =>
                        {
                            panel.ShowTip("密码错误");
                        });
                    }
                }
                else
                {
                    UIManager.Instance.OpenPanel<TipPanel>(panel =>
                    {
                        panel.ShowTip("账号不存在");
                    });
                }
                break;
        }
    }
    
}