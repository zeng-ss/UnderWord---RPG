using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;


[CreateAssetMenu(menuName = "Config/DepotConfig")]
public class DepotConfig : ScriptableObject
{
    public List<DepotData> depots = new();
}

public class DepotDataRuntime
{
    public string depotName;
    public int depotId;
    public int level;
    public string depotIconName;
    public List<int> materialsId;
    public float curLevelMaxFill;
    [HideInInspector] public float curLevelFillValue;
    [SerializeReference] // 使用 SerializeReference 支持多态
    public ValueData depotValue = new();

    public DepotDataRuntime() { }
    public DepotDataRuntime(DepotData depotData)
    {
        depotName = depotData.depotName;
        depotId = depotData.depotId;
        level = depotData.level;
        depotIconName = depotData.depotIconName;
        materialsId = depotData.materialsId;
        curLevelMaxFill = depotData.curLevelMaxFill;
        curLevelFillValue = depotData.curLevelFillValue;
        // 深拷贝 ValueData
        // 核心修复：空值保护 + 初始化默认值
        if (depotData.depotValue == null)
        {
            depotValue = new ValueData();
            Debug.LogWarning($"DepotId {depotId} 的depotValue为空，已初始化默认值");
        }
        else
        {
            depotValue = new ValueData
            {
                depotType = depotData.depotValue.depotType,
                baseValue = depotData.depotValue.baseValue,
                attackPercent = depotData.depotValue.attackPercent,
                defensePercent = depotData.depotValue.defensePercent,
                healthPercent = depotData.depotValue.healthPercent,
                baoJiPercent = depotData.depotValue.baoJiPercent
            };
        }
    }
    public DepotDataRuntime(DepotDataRuntime depotData)
    {
        depotName = depotData.depotName;
        depotId = depotData.depotId;
        level = depotData.level;
        depotIconName = depotData.depotIconName;
        materialsId = depotData.materialsId;
        curLevelMaxFill = depotData.curLevelMaxFill;
        curLevelFillValue = depotData.curLevelFillValue;
        if (depotData.depotValue == null)
        {
            Debug.LogWarning($"DepotDataRuntime[{depotId}]的depotValue为null！已创建默认值");
            depotValue = new ValueData();
        }
        else
        {
            depotValue = new ValueData
            {
                depotType = depotData.depotValue.depotType,
                baseValue = depotData.depotValue.baseValue,
                attackPercent = depotData.depotValue.attackPercent,
                defensePercent = depotData.depotValue.defensePercent,
                healthPercent = depotData.depotValue.healthPercent,
                baoJiPercent = depotData.depotValue.baoJiPercent
            };
        }
    }

    // 添加经验值
    public void AddExp(float exp)
    {
        curLevelFillValue += exp;
        // 检查是否可以升级
        while (curLevelFillValue >= curLevelMaxFill)
        {
            level++;
            if (level == 2) TaskManager.Instance.UpdateTaskProgress(TaskType.给每一个驱动盘都升一级);
            curLevelFillValue -= curLevelMaxFill;
            curLevelMaxFill = (int)Random.Range(curLevelMaxFill + 200, curLevelMaxFill + 500);
            // 升级时提升属性
            UpgradeValue();
        }
    }

    /// <summary>
    /// 检测材料数量是否足够可以增加经验值
    /// </summary>
    public bool CheckCanAddExp()
    {
        bool canAddExp = true;
        string tip = "";
        foreach (var materialId in materialsId)
        {
            // 遍历保存的已拥有的材料数量
            if (GameManager.Instance.materialNumDict.ContainsKey(materialId))
            {
                int needNum = materialId == 1 ? 10 : 1;
                if (GameManager.Instance.materialNumDict[materialId] < needNum)
                {
                    tip += $"{GameManager.Instance.materialDataRuntime[materialId].name}\n";
                    canAddExp = false;
                }
            }
        }

        if (!canAddExp)
        {
            UIManager.Instance.OpenPanel<TipPanel>(panel =>
            {
                panel.ShowTip($"{tip}不足");
            });
        }

        return canAddExp;
    }

    // 升级属性提升
    private void UpgradeValue()
    {
        // 根据类型提升不同的属性
        switch (depotValue.depotType)
        {
            case DepotType.Attack:
                depotValue.baseValue += Random.Range(20, 100);
                depotValue.attackPercent += 10f;
                depotValue.defensePercent += 5f;
                depotValue.healthPercent += 3f;
                depotValue.baoJiPercent += 3f;
                break;
            case DepotType.Defense:
                depotValue.baseValue += Random.Range(20, 100);
                depotValue.defensePercent += 10f;
                depotValue.healthPercent += 5f;
                depotValue.baoJiPercent += 2f;
                depotValue.attackPercent += 3f;
                break;
            case DepotType.Health:
                depotValue.baseValue += Random.Range(20, 100);
                depotValue.healthPercent += 10f;
                depotValue.defensePercent += 5f;
                depotValue.baoJiPercent += 1f;
                depotValue.attackPercent += 2f;
                break;
            case DepotType.BaoJi:
                depotValue.baseValue += Random.Range(5, 10);
                depotValue.baoJiPercent += 5f;
                depotValue.attackPercent += 3f;
                depotValue.defensePercent += 1f;
                depotValue.healthPercent += 1f;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    // 获取当前等级进度（0-1）
    public float GetLevelProgress() { return curLevelFillValue / curLevelMaxFill; }
}


[Serializable]
public class DepotData
{
    public string depotName;
    public int depotId;
    public int level;
    [Header("图标集")]
    public string depotIconName;
    public List<int> materialsId;
    [Header("当前升级的最大数值")]
    public float curLevelMaxFill;
    [HideInInspector] public float curLevelFillValue;
    [SerializeReference]
    public ValueData depotValue = new();
}

[Serializable]
public class ValueData
{
    public DepotType depotType;
    public int baseValue;
    public float attackPercent;
    public float defensePercent;
    public float healthPercent;
    public float baoJiPercent;
}

public enum DepotType
{
    Attack,
    Defense,
    Health,
    BaoJi
}


