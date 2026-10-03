using System;
using System.Net;
using Unity.Netcode;
using UnityEngine.Events;

// 强制 Netcode 生成所有基础类型序列化
[GenerateSerializationForType(typeof(bool))]
[GenerateSerializationForType(typeof(int))]
[GenerateSerializationForType(typeof(float))]
[GenerateSerializationForType(typeof(string))]
[GenerateSerializationForType(typeof(PlayerData))]
public static class NetcodeSerializationGenerator
{
    // 什么都不用写
    // 强制实例化 UnityEvent<IPEndPoint, DiscoveryResponseData>
    private static void InstantiateUnityEvent()
    {
        var evt = new UnityEvent<IPEndPoint, DiscoveryResponseData>();
        evt.AddListener((endPoint, data) => { /* dummy */ });
        // 也可以调用 Invoke 来确保生成
        evt.Invoke(null, default);
    }
}

[Serializable]
public struct PlayerData : INetworkSerializable
{
    public ulong id;
    public float maxHealthValue;
    public float attackValue;
    public float defenseValue;
    public float baoJiValue;
    public float exAttackValue;
    
    // 实现网络序列化
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref id);
        serializer.SerializeValue(ref maxHealthValue);
        serializer.SerializeValue(ref attackValue);
        serializer.SerializeValue(ref defenseValue);
        serializer.SerializeValue(ref baoJiValue);
        serializer.SerializeValue(ref exAttackValue);
    }
}