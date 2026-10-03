using System;
using System.Data;
using MySqlConnector;
using UnityEngine;

/// <summary>
/// 服务器数据对象 
/// </summary>
public class ServerData  
{
    public int ServerId;
    public string ServerIp;
    public string State;
    public string ServerName;
}
/// <summary>
/// 数据库管理器   连接数据库的方法 
/// </summary>
public class DBManager : UnitySingleTonMono<DBManager>
{
    //连接字符串  告诉数据库的信息  服务器地址  数据库的名字  数据库的用户名 密码   编码格式  
    private string ConnectionString;//连接字符串 
    private MySqlConnection conn;
    private string severIp = "127.0.0.1"; //数据库地址
    private string database = "mygamedb"; //数据库地址
    private string username = "root";
    private string password = "123456";

    public override void Awake()
    {
        base.Awake();
        Init();
    }

    private void Init()
    {
        //创建这样的连接字符串
        ConnectionString = $"Server={severIp};Database={database};Uid={username};Pwd={password};charset=utf8mb4;Allow User Variables=True;SslMode=None;";
        //测试链接 
        // print(OpenConnection());
        UIManager.Instance.OpenPanel<LoginPanel>();
    }
    /// <summary>
    /// 创建数据库连接 
    /// </summary>
    public bool OpenConnection()
    {
        try
        {
            conn = new MySqlConnection(ConnectionString);
            conn.Open();
            print($"连接成功！服务器版本：{conn.ServerVersion}");
            return true;
        }
        catch (MySqlException ex)
        {
            print($"错误代码：{ex.Number}");
            print($"错误信息：{ex.Message}");
            return false;
        }
    }
/// <summary>
/// 关闭连接
/// </summary>
/// <returns></returns>
    public bool CloseConnection()
    {
        if (conn!=null)
        {
            try
            {
                //关闭连接 
                conn.Close();
                return true;
            }
            catch (MySqlException ex)
            {
                print(ex.Message);
                return false;
                
            }
        }
        //当前没有连接
        return false;
    }
/// <summary>
/// 查询语句的方法
/// </summary>
/// <param name="query">查询语句</param>
/// <returns></returns>
    public DataTable SelectQuery(string query)
    {
        var dt = new DataTable();
        var cmd = new MySqlCommand(query, conn);
        var reader = cmd.ExecuteReader();
        dt.Load(reader);
        return dt;//行和列的数据结构   dt[行][列]
    }
/// <summary>
/// 非查询语句  
/// </summary>
/// <param name="query"></param>
    public void NonQuery(string query)
    {
        var cmd = new MySqlCommand(query, conn);
        cmd.ExecuteNonQuery();
    }

    // 执行更新语句（关键）
    public int ExecuteNonQuery(string sql)
    {
        try
        {
            MySqlCommand cmd = new MySqlCommand(sql, conn);
            return cmd.ExecuteNonQuery(); // 返回受影响的行数
        }
        catch (Exception ex)
        {
            Debug.LogError("执行SQL失败：" + ex.Message);
            return -1;
        }
    }

}
