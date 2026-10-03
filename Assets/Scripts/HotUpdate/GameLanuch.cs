using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// 游戏启动器
/// 在热更新完成后被实例化，负责：
/// 1. 加载游戏主场景
/// 2. 清理自己
/// 注意：这个脚本本身应该是热更代码（放在HotUpdate程序集中）
/// </summary>
public class GameLanuch : MonoBehaviour
{
    private void Awake()
    {
        // 打印大量日志，便于调试和确认执行顺序
        Debug.Log("游戏开始,主流程开始ssssssssssssssss");
        Addressables.LoadSceneAsync("EnterScene");
        // 2. 释放自己（ GameLanuch对象）
        // 启动器完成任务后就不需要了
        Addressables.ReleaseInstance(gameObject);
    }
}

