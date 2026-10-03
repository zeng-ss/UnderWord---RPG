using System;
using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using DamageNumbersPro;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.ResourceManagement.AsyncOperations;

public class PlayerCtrl : NetworkBehaviour, IState_MachineOwner, ISkillOwner, IHurt
{
    #region 参数

    // 组件
    private PlayerProPanel playerProPanel;
    [HideInInspector] public CharacterController characterController;
    [HideInInspector] public Transform cameraTransform;
    [HideInInspector] public State_Machine stateMachine;
    public PlayerModel playerModel;
    private AudioSource audioSource;
    [HideInInspector] public CinemachineImpulseSource impulseSource;
    private ChromaticAberration chromaticAberration; //色差组件
    [HideInInspector] public Vignette vignette; 
    // 行为参数
    private float gravity = -5f;
    private Vector3 velocity;
    [HideInInspector] public bool isLock; 
    public float rotationSpeed;                       // 转向速度
    [HideInInspector] public bool hasGravity;
    [HideInInspector] public bool isOnGround;         // 是否在陆地上
    [HideInInspector] public Vector3 currentMoveDir;
    // 状态
    public PlayerStateType currentState;
    public PlayerStateType lastState;
    public DamageNumber damageNumber;
    // 敌人
    [HideInInspector] public EnemyCtrl enemy;
    // 相机
    [HideInInspector]
    public CinemachineVirtualCamera virtualCameraEx;
    private CinemachineVirtualCamera virtualCameraPin;
    private CinemachineFreeLook cinemachineFreeLook;

    #endregion
    #region 网络相关参数

    // 玩家当前属性
    public NetworkVariable<float> health = new();
    public NetworkVariable<PlayerData> playerData = new();
    
    #endregion
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Spawn();
    }

    private void Spawn()
    {
        print($"玩家 {OwnerClientId} Spawn成功，是否本地玩家：{IsLocalPlayer}");
        // 所有客户端都需要的基础组件初始化
        impulseSource = GetComponent<CinemachineImpulseSource>();
        characterController = GetComponent<CharacterController>();
        audioSource = GetComponent<AudioSource>();
        // PlayerModel初始化（所有客户端都需要，因为要播放动画）
        playerModel.Init(this);
        // 状态机初始化（所有客户端都需要）
        stateMachine = new State_Machine();
        stateMachine.Init(this);
        // 默认选中第一套连招配置（所有客户端都需要）
        if (skillConfigList is { Count: > 0 }) { curSkillConfig = skillConfigList[0]; }
        // 核心修改：本地玩家直接初始化，无需监听事件
        if (IsLocalPlayer && IsOwner)
        {
            StartCoroutine(CreateManagers());
            // 立即执行初始化
            Init();
            // 确保 CharacterController启用
            characterController.enabled = true;
            // 获取 Volume组件
            GameObject.Find("Volume")?.GetComponent<Volume>()?.profile.TryGet(out chromaticAberration);
            GameObject.Find("Volume")?.GetComponent<Volume>()?.profile.TryGet(out vignette);
            // 进入 Idle状态
            ChangeState(PlayerStateType.Idle);
        }
        // 监听血量变化
        health.OnValueChanged += OnHealthChanged;
        // 服务端初始化玩家数据
        if (IsServer)
        {
            // 服务端初始化玩家数据
            playerData.Value = new PlayerData
            {
                id = OwnerClientId,
                maxHealthValue = GameManager.Instance.playerBaseData.maxHealthValue,
                attackValue = GameManager.Instance.playerBaseData.attackValue,
                defenseValue = GameManager.Instance.playerBaseData.defenseValue,
                baoJiValue = GameManager.Instance.playerBaseData.baoJiValue,
                exAttackValue = GameManager.Instance.playerBaseData.exAttackValue
            };
            // 服务器设置初始血量
            health.Value = playerData.Value.maxHealthValue;
            Debug.Log($"服务端初始化玩家 {OwnerClientId} 数据：血量={health.Value}");
        }
        isInit = true;
    }

    #region 初始化方法

    private void Init()
    {
        // "PlayerCtrl Init 被调用 - 收到游戏开始事件";
        cameraTransform = Camera.main?.transform;
        characterController.enabled = true;
        hasGravity = true;
        // 查找并设置 FreeLook Camera
        virtualCameraPin = GameObject.Find("VirtualCamera_Pin").GetComponent<CinemachineVirtualCamera>();
        virtualCameraEx = GameObject.Find("VirtualCamera_Ex").GetComponent<CinemachineVirtualCamera>();
        cinemachineFreeLook = FindObjectOfType<CinemachineFreeLook>();
        Transform lookAtTarget = playerModel.transform.Find("LookAt");
        if (lookAtTarget)
        {
            cinemachineFreeLook.Follow = lookAtTarget;
            cinemachineFreeLook.LookAt = lookAtTarget;
            virtualCameraPin.Follow = transform;
            virtualCameraPin.LookAt = transform;
            virtualCameraPin.gameObject.SetActive(false);
            virtualCameraEx.Follow = lookAtTarget;
            virtualCameraEx.LookAt = lookAtTarget;
            virtualCameraEx.gameObject.SetActive(false);
        }
        else { Debug.LogError("找不到 lookAt 子物体，请检查角色层级"); }
    }
    
    private IEnumerator CreateManagers()
    {
        string[] addresses = { "SoundManager" };
        List<AsyncOperationHandle<GameObject>> handles = new List<AsyncOperationHandle<GameObject>>();
        foreach (string address in addresses)
        {
            var handle = Addressables.InstantiateAsync(address);
            handles.Add(handle);
        }
        // 等待所有实例化完成
        foreach (var handle in handles)
        {
            yield return handle;
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                // 可以设置 DontDestroyOnLoad 或单例逻辑
                GameObject obj = handle.Result;
                if (obj.GetComponent<NetworkObject>() != null)
                {
                    obj.GetComponent<NetworkObject>().Spawn();
                }
            }
            else
            {
                Debug.LogError($"实例化失败 {handle.DebugName}");
            }
        }
        //Debug.Log("所有管理器实例化完成");
    }

    #endregion

    private bool isInit;
    private void Update()
    {
        if (!IsLocalPlayer || !IsOwner || !isInit) return;
        if (isLock) return;
        #region 拼刀

        if (Input.GetKeyDown(KeyCode.E) && !isPining)
        {
            enemy = FindObjectOfType<EnemyCtrl>();
            if (enemy && enemy.isStartPin.Value && !enemy.isPinFinished.Value)
            {
                TeleportToEnemy();
                enemy.isStartPinTip.gameObject.SetActive(false);
                isPining = true;
                DOVirtual.DelayedCall(1f, () =>
                {
                    isPining = false;
                    virtualCameraPin.gameObject.SetActive(false);
                });
                // 发送请求僵硬
                enemy.EnemyStiffServerRpc();
            }
        }

        #endregion
        if (Input.GetKeyDown(KeyCode.Alpha6))
        {
            if (!IsServer) { HealHealthServerRpc(50); }
            else { health.Value = Mathf.Clamp(health.Value + 50, 50, playerData.Value.maxHealthValue); }
        }
        chatPanel ??= FindObjectOfType<ChatPanel>();
        if (!characterController.enabled && currentState != PlayerStateType.Dead) characterController.enabled = true;
        #region 重力

        // 仅本地玩家执行重力逻辑
        if (!hasGravity || !characterController.enabled) return;
        characterController.Move(velocity * Time.deltaTime);
        isOnGround = characterController.isGrounded;
        if (isOnGround) { velocity.y = -2f; }
        else { velocity.y += gravity * Time.deltaTime; }

        #endregion
        HandleSkillSwitch();
        HandleEvadeSwitch();
    }
    
    #region 闪避相关

    private float lastShiftPressTime;
    private const float DoubleTapInterval = 0.3f;
    private bool isShiftDoubleTap;
    [HideInInspector] public ChatPanel chatPanel;
    
    private void HandleEvadeSwitch()
    {
        if (chatPanel && chatPanel.chatInput.isFocused) return;
        // 检测 Shift键
        if (!Input.GetKeyDown(KeyCode.LeftShift)) return;
        // 判断是否是双击
        if (Time.time - lastShiftPressTime < DoubleTapInterval)
        {
            // 双击 Shift - 进入闪避状态
            isShiftDoubleTap = true;
            ChangeState(PlayerStateType.Evade);
        }
        else
        {
            // 单击 Shift - 前进逻辑
            isShiftDoubleTap = false;
            ChangeState(PlayerStateType.Evade);
        }
        PlayerEvadeState state = (PlayerEvadeState)stateMachine.CurrentState;
        state.SetEvade(isShiftDoubleTap);
        lastShiftPressTime = Time.time;
    }

    #endregion
    
    #region 动画和声音相关

    /// <summary>
    /// 切换状态
    /// </summary>
    /// <param name="stateType"></param>
    /// <param name="isResfeshState"></param>
    public void ChangeState(PlayerStateType stateType, bool isResfeshState = false)
    {
        if (isLock) return;
        // 记录上一个状态（排除重复切换相同状态的情况）
        if (currentState != stateType) { lastState = currentState; }
        currentState = stateType;
        switch (stateType)
        {
            case PlayerStateType.Idle:
                //调用状态机身上的切换状态方法
                stateMachine.ChangeState<PlayerIdleState>(isResfeshState);
                break;
            case PlayerStateType.Move:
                stateMachine.ChangeState<PlayerMoveState>(isResfeshState);
                break;
            case PlayerStateType.Attack:
                stateMachine.ChangeState<PlayerAttackState>(isResfeshState);
                break;
            case PlayerStateType.Evade:
                stateMachine.ChangeState<PlayerEvadeState>(isResfeshState);
                break;
            case PlayerStateType.Dead:
                stateMachine.ChangeState<PlayerDeadState>(isResfeshState);
                break;
            case PlayerStateType.Hurt:
                stateMachine.ChangeState<PlayerHurtState>(isResfeshState);
                break;
            case PlayerStateType.EX:
                stateMachine.ChangeState<EXAttackState>(isResfeshState);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stateType), stateType, null);
        }
    }
    //播放动画
    public void PlayerAnimation(string animationName,float fixedTransitionTime = 0.1f)
    {
        // 使用 CrossFade 替代 Play
        // 参数1: 动画名称或Hash值
        // 参数2: 过渡持续时间（normalized）
        // 参数3: 层索引（通常是0）
        // 参数4: 目标动画的起始播放位置（normalizedTime，通常从0开始）
        // playerModel.Animator.CrossFade(animationName, fixedTransitionTime, 0, 0f);
        // 如果你希望使用固定秒数作为过渡时间（而不是百分比），可以用 CrossFadeInFixedTime
        playerModel.Animator.CrossFadeInFixedTime(animationName, fixedTransitionTime, 0, 0f);
    }
    
    private void PlayerAudio(AudioClip audioClip) { audioSource.PlayOneShot(audioClip); }

    #endregion
    
    #region 技能连招配置切换

    private void HandleSkillSwitch()
    {
        // 仅本地玩家处理输入（非本地玩家跳过）
        if (!IsLocalPlayer) return;
        // 切换到第二套连招
        if (Input.GetKeyDown(KeyCode.Alpha2) && curSkillConfig != skillConfigList[1] && skillConfigList.Count > 1)
        {
            // 1. 本地先更新（预测，提升手感）
            UpdateSkillConfig(1);
            // 2. 通知服务端同步给所有客户端
            if (IsServer)
            {
                SyncSkillConfigClientRpc(1);
            }
            else SwitchSkillServerRpc(1);
        }
        // 切换回第一套连招（Alpha1 或 重击状态下按左键）
        else if ((Input.GetKeyDown(KeyCode.Alpha1) || (Input.GetKeyDown(KeyCode.Mouse0) && curSkillConfig == skillConfigList[2])) 
                 && curSkillConfig != skillConfigList[0])
        {
            if (skillConfigList.Count > 0)
            {
                UpdateSkillConfig(0);
                if (IsServer)
                {
                    SyncSkillConfigClientRpc(0);
                }
                else SwitchSkillServerRpc(0);
            }
        }
        // 切换重击连招
        else if (Input.GetKeyDown(KeyCode.Mouse1) && curSkillConfig != skillConfigList[2] && skillConfigList.Count > 2)
        {
            UpdateSkillConfig(2);
            if (IsServer)
            {
                SyncSkillConfigClientRpc(2);
            }
            else SwitchSkillServerRpc(2);
        }
        else if (Input.GetKeyDown(KeyCode.R) && curSkillConfig != skillConfigList[3])
        {
            UpdateSkillConfig(3);
            if (IsServer)
            {
                SyncSkillConfigClientRpc(3);
            }
            else SwitchSkillServerRpc(3);
            ChangeState(PlayerStateType.EX);
        }
    }

    /// <summary>
    /// 客户端通知服务端：切换连招
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SwitchSkillServerRpc(int skillIndex, bool isPin = false)
    {
        SyncSkillConfigClientRpc(skillIndex, isPin);
    }

    /// <summary>
    /// 服务端同步连招状态到所有客户端
    /// </summary>
    [ClientRpc]
    public void SyncSkillConfigClientRpc(int skillIndex, bool isPin = false)
    {
        if (IsLocalPlayer) return;
        UpdateSkillConfig(skillIndex, isPin);
    }
    
    /// <summary>
    /// 统一更新连招配置
    /// </summary>
    /// <param name="skillIndex">0=第一套，1=第二套，2=重击</param>
    /// <param name="isPin">0=是否是拼刀切换</param>
    public void UpdateSkillConfig(int skillIndex, bool isPin = false)
    {
        // 索引越界防护
        if (skillIndex < 0 || skillIndex >= skillConfigList.Count)
        {
            Debug.LogWarning($"切换连招索引越界：{skillIndex}，默认切回第一套");
            skillIndex = 0;
        }
        // 重置攻击/特效索引
        CurAttackIndex = isPin ? 1 : 0;
        CurVFXIndex = isPin ? 1 : 0;
        // 更新当前连招配置
        curSkillConfig = skillConfigList[skillIndex];
    }

    #endregion

    #region 拼刀瞬移

    private bool isPining; // 是否在拼刀
    private void TeleportToEnemy()
    {
        if (!enemy) return;
        // 计算敌人指向玩家的方向
        Vector3 dirToPlayer = (transform.position - enemy.transform.position).normalized;
        dirToPlayer.y = 0;
        // 瞬移位置
        Vector3 teleportPos = enemy.transform.position + dirToPlayer * 1f;
        teleportPos.y = transform.position.y;
        // 临时禁用 CharacterController
        characterController.enabled = false;
        // 执行瞬移
        transform.position = teleportPos;
        // 重新启用
        characterController.enabled = true;
        virtualCameraPin.gameObject.SetActive(true);
        // 面向敌人
        transform.LookAt(enemy.transform);
        // 连招切换
        UpdateSkillConfig(1, true);
        if (IsServer) SyncSkillConfigClientRpc(1, true);
        else SwitchSkillServerRpc(1, true);
        ChangeState(PlayerStateType.Attack, true);
    }

    #endregion
    
    #region 技能相关

    private int curAttackIndex;
    private int curVFXIndex;  // 当前多段伤害的特效下标
    public int CurVFXIndex
    {
        get => curVFXIndex;
        // 适配curSkillConfig的越界保护
        private set 
        {
            if ( CurAttackIndex == -1 || curSkillConfig == null || curSkillConfig.skillConfigs.Count <= CurAttackIndex)
            {
                curVFXIndex = 0;
                return;
            }
            curVFXIndex = value >= curSkillConfig.skillConfigs[CurAttackIndex].VFXDataList.Count ? 0 : value;
        }
    }
    public int CurAttackIndex
    {
        //防止超出下标
        get => curAttackIndex;
        set => curAttackIndex = value >= curSkillConfig.skillConfigs.Count ? 0 : value;
    }
    [HideInInspector] public SkillConfig curSkillConfig;
    public List<SkillConfig> skillConfigList; // 存放多套连招配置
    public bool CanSwitchSkill { get; set; }

    /// <summary>
    /// 开始技能
    /// </summary>
    /// <param name="attackData">攻击数据</param>
    public void StartSkill(AttackData attackData)
    {
        CurVFXIndex = 0;
        CanSwitchSkill = false;
        PlayerAnimation(attackData.attackAnimationName);
        PlayerAudio(attackData.VFXDataList[CurVFXIndex].VFXClip);
    }
    
    public void StartSkillHit(int weaponIndex)
    {
        if (CurAttackIndex == -1) CurAttackIndex = 0;
        if (CurVFXIndex < 0 || CurVFXIndex >= curSkillConfig.skillConfigs[CurAttackIndex].VFXDataList.Count) CurVFXIndex = 0;
        StartCoroutine(DoSpawnVFX(curSkillConfig.skillConfigs[CurAttackIndex].VFXDataList[CurVFXIndex]));
    }
    private IEnumerator DoSpawnVFX(VFXData vfxData)
    {
        yield return new WaitForSeconds(vfxData.spawnTime); // 等待特效生成时机
        // 本地偏移转世界坐标（避免非本地客户端坐标错乱）
        Vector3 worldPos = playerModel.transform.TransformPoint(vfxData.spawnPos);
        Quaternion worldRot = playerModel.transform.rotation * Quaternion.Euler(vfxData.spawnRot);
        Vector3 spawnScale = vfxData.spawnScale;
    
        // 1. 本地客户端先生成特效（预测，提升手感）
        var vfxObj = Instantiate(vfxData.prefab);
        vfxObj.GetComponent<ParticleCtrl>()?.Init(this);
        vfxObj.transform.position = worldPos;
        vfxObj.transform.rotation = worldRot;
        vfxObj.transform.localScale += spawnScale;
        // 本地特效销毁逻辑
        Destroy(vfxObj, vfxObj.TryGetComponent<ParticleSystem>(out var ps) ? ps.main.duration : 2f);
    
        // 2. 特效同步：客户端→服务端→所有客户端（Netcode标准流程）
        if (IsServer)
        {
            // 若当前是Host（服务端+客户端），直接调 ClientRpc
            SpawnVFXClientRpc(CurAttackIndex, CurVFXIndex, worldPos, worldRot, spawnScale);
        }
        else
        {
            // 纯客户端，先调ServerRpc通知服务端
            SpawnVFXServerRpc(CurAttackIndex, CurVFXIndex, worldPos, worldRot, spawnScale);
        }
    }

    /// <summary>
    /// 客户端通知服务端：生成攻击特效
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void SpawnVFXServerRpc(int attackIndex, int vfxIndex, Vector3 worldPos, Quaternion worldRot, Vector3 spawnScale)
    {
        // 服务端验证索引（防止恶意客户端传越界值）
        if (curSkillConfig == null || curSkillConfig.skillConfigs == null || attackIndex < 0 || attackIndex >= curSkillConfig.skillConfigs.Count)
        {
            Debug.LogWarning($"SpawnVFXServerRpc: 攻击索引{attackIndex}越界，拒绝同步");
            return;
        }
        if (vfxIndex < 0 || vfxIndex >= curSkillConfig.skillConfigs[attackIndex].VFXDataList.Count)
        {
            Debug.LogWarning($"SpawnVFXServerRpc: VFX索引{vfxIndex}越界，拒绝同步");
            return;
        }
        // 服务端转发给所有客户端
        SpawnVFXClientRpc(attackIndex, vfxIndex, worldPos, worldRot, spawnScale);
    }

    /// <summary>
    /// 服务端同步攻击特效到所有客户端
    /// </summary>
    [ClientRpc]
    private void SpawnVFXClientRpc(int attackIndex, int vfxIndex, Vector3 worldPos, Quaternion worldRot, Vector3 spawnScale)
    {
        // 本地客户端已生成过，跳过（避免重复）
        if (IsLocalPlayer) return;
        // 索引+空值防护
        if (curSkillConfig == null || curSkillConfig.skillConfigs == null || attackIndex < 0 || attackIndex >= curSkillConfig.skillConfigs.Count)
        {
            Debug.LogWarning("SpawnVFXClientRpc: 攻击索引越界或配置表为空");
            return;
        }
        if (vfxIndex < 0 || vfxIndex >= curSkillConfig.skillConfigs[attackIndex].VFXDataList.Count)
        {
            Debug.LogWarning($"SpawnVFXClientRpc: VFX索引{vfxIndex}越界，使用默认索引0");
            vfxIndex = 0;
        }
    
        // 所有非本地客户端生成特效
        VFXData vfxData = curSkillConfig.skillConfigs[attackIndex].VFXDataList[vfxIndex];
        var vfxObj = Instantiate(vfxData.prefab);
        vfxObj.transform.position = worldPos;
        vfxObj.transform.rotation = worldRot;
        vfxObj.transform.localScale = vfxData.prefab.transform.localScale + spawnScale;
    
        // 智能销毁（兼容有无ParticleSystem的情况）
        Destroy(vfxObj, vfxObj.TryGetComponent<ParticleSystem>(out var ps) ? ps.main.duration : 2f); // 默认2秒
    }

    public void StopSkillHit(int weaponIndex) { CurVFXIndex++; }
    public void SkillCanSwitch() { CanSwitchSkill = true; }
    
    #endregion
    
    #region 网络攻击同步
    public void OnHit(IHurt hurt, Vector3 hurtPos)
    {
        if (!IsLocalPlayer) return;
        // 获取敌人的 NetworkObject
        NetworkObject targetNetObj = (hurt as MonoBehaviour)?.GetComponent<NetworkObject>();
        if (targetNetObj == null) { print("有问题"); return; }
        // 本地预测：立即播放攻击特效
        PlayLocalHitEffects(hurtPos);
        if (IsServer)
        {
            // 确保 Host/Server 端也可以攻击到敌人
            hurt.OnHurt(curSkillConfig.skillConfigs[CurAttackIndex].HitData, this, hurtPos);
            PlayHitEffectsClientRpc(targetNetObj, hurtPos, CurAttackIndex, CurVFXIndex);
        }
        else
        {
            // 发送攻击请求到服务器
            RequestDamageServerRpc(targetNetObj, hurtPos, CurAttackIndex, CurVFXIndex);
        }
    }
    private void PlayLocalHitEffects(Vector3 hurtPos)
    {
        if (CurAttackIndex == -1) CurAttackIndex = 0;
        // 本地立即播放特效（预测）
        HitData hitData = curSkillConfig.skillConfigs[CurAttackIndex].HitData;
        // 克隆受击特效
        GameObject obj = Instantiate(hitData.hitPrefabs[CurVFXIndex]);
        obj.transform.position = hurtPos;
        Destroy(obj, obj.GetComponent<ParticleSystem>().main.duration);
        // 震动效果（本地立即响应）
        impulseSource.GenerateImpulse(hitData.screenImpulseValue);
        // 色差效果
        if (chromaticAberration != null && vignette != null)
        {
            DOTween.To(
                    () => chromaticAberration.intensity.value, 
                    x => chromaticAberration.intensity.value = x,
                    hitData.chromaticAberrationValue,
                    0.2f)
                .OnComplete(() => { chromaticAberration.intensity.value = 0; });
        }
        else
        {
            GameObject.Find("Volume")?.GetComponent<Volume>()?.profile.TryGet(out chromaticAberration);
            GameObject.Find("Volume")?.GetComponent<Volume>()?.profile.TryGet(out vignette);
            Debug.LogWarning("Host端组件未获取到，已重新获取");
        }
        // 受击声音
        SoundManager.Instance.PlaySound(hitData.hitClip, hurtPos);
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void RequestDamageServerRpc(NetworkObjectReference targetRef, Vector3 hurtPos, int attackIndex, int vfxIndex)
    {
        // 服务器验证攻击有效性
        if (!targetRef.TryGet(out NetworkObject targetObj)) return;
        // 验证距离
        float distance = Vector3.Distance(transform.position, targetObj.transform.position);
        if (distance >= 8) { print("Dis"); return; }
        targetObj.GetComponent<IHurt>()?.OnHurt(curSkillConfig.skillConfigs[attackIndex].HitData, this, hurtPos);
        PlayHitEffectsClientRpc(targetObj, hurtPos, attackIndex, vfxIndex);
    }
    
    [ClientRpc]
    private void PlayHitEffectsClientRpc(NetworkObjectReference targetRef, Vector3 hurtPos, int attackIndex, int vfxIndex)
    {
        // 避免重复播放 已经本地预测过了
        if (IsLocalPlayer) return;
        if (!targetRef.TryGet(out NetworkObject targetObj)) return;
        HitData hitData = curSkillConfig.skillConfigs[attackIndex].HitData;
        // 所有客户端播放受击特效
        GameObject obj = Instantiate(hitData.hitPrefabs[vfxIndex]);
        obj.transform.position = hurtPos;
        Destroy(obj, obj.GetComponent<ParticleSystem>()?.main.duration ?? 1f);
        // 非本地玩家播放受击声音
        SoundManager.Instance.PlaySound(hitData.hitClip, transform.position);
    }
    
    #endregion

    #region 受伤相关
    public void OnHurt(HitData hitData, ISkillOwner hurtSource, Vector3 hurtPos)
    {
        if (!IsServer)
        {
            RequestHurtServerRpc(hitData.damageValue, hitData.vignetteValue, hitData.screenImpulseValue);
            return;
        }
        // 仅服务端执行伤害计算、状态同步、死亡逻辑
        health.Value -= Math.Max(0, hitData.damageValue - (float)Math.Round(playerData.Value.defenseValue / 10, 2));
        if (health.Value <= 0)
        {
            PlayerDeadClientRpc();
            return;
        }
        PlayerHurtClientRpc(hitData.vignetteValue, hitData.screenImpulseValue, hitData.damageValue);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestHurtServerRpc(float damageValue, float vignetteValue, float screenImpulseValue)
    {
        if (health.Value <= 0) return; 
        health.Value -= Math.Max(0, damageValue - (float)Math.Round(playerData.Value.defenseValue / 10, 2));
        if (health.Value <= 0)
        {
            PlayerDeadClientRpc();
            return;
        }
        PlayerHurtClientRpc(vignetteValue, screenImpulseValue, damageValue);
    }

    [ClientRpc]
    private void PlayerHurtClientRpc(float vignetteValue, float screenImpulseValue, float damageValue)
    {
        ChangeState(PlayerStateType.Hurt, true);
        damageNumber.Spawn(transform.position, damageValue);
        // 触发本地客户端的受击反馈
        PlayPlayerHurtLocalFeedback(vignetteValue, screenImpulseValue);
    }
    
    private Tweener vignetteTweener;
    /// <summary>
    /// 玩家本地客户端的受击反馈（红边+震动），仅本地执行
    /// </summary>
    private void PlayPlayerHurtLocalFeedback(float vignetteValue, float screenImpulseValue)
    {
        // 仅本地玩家执行（避免其他玩家的客户端触发反馈）
        if (!IsLocalPlayer) return;
        // 屏幕震动
        impulseSource.GenerateImpulse(screenImpulseValue);
        // 屏幕红边（Vignette）
        if (vignette != null)
        {
            // 中断旧动画（如果存在），并平滑过渡到新动画
            if (vignetteTweener != null && vignetteTweener.IsActive()) { vignetteTweener.Kill(); }
            // 播放红边增强动画（0.2秒到目标强度）
            vignetteTweener = DOTween.To(
                    () => vignette.intensity.value,
                    x => vignette.intensity.value = x,
                    vignetteValue,
                    0.2f)
                // 增强完成后，渐变归零
                .OnComplete(() => 
                {
                    // 渐变归零，而非直接设0，动画更流畅
                    DOTween.To(() => vignette.intensity.value, x => vignette.intensity.value = x, 0, 0.3f); 
                } );// 0.3秒渐变归零
        }
        else
        {
            // 重新尝试获取 Volume组件，修复 Host端/晚加入客户端的组件缺失问题
            Volume globalVolume = FindObjectOfType<Volume>();
            globalVolume.profile.TryGet(out vignette);
            globalVolume.profile.TryGet(out chromaticAberration);
            Debug.LogWarning("[玩家本地] 重新获取Volume组件成功，再次尝试红边");
            // 重新执行红边逻辑
            if (vignetteTweener != null && vignetteTweener.IsActive()) { vignetteTweener.Kill(); }
            // 首次获取，直接设目标强度
            vignetteTweener = DOTween.To(
                    () => vignette.intensity.value,
                    x => vignette.intensity.value = x,
                    vignetteValue,
                    0.2f)
                .OnComplete(() => 
                {
                    DOTween.To(() => vignette.intensity.value, x => vignette.intensity.value = x, 0, 0.5f);
                });
        }
    }

    [ClientRpc]
    private void PlayerDeadClientRpc() { ChangeState(PlayerStateType.Dead); tag = "Untagged"; }

    #endregion
    
    #region 同步血量血条和玩家数据

    // 更新血条 UI
    private void OnHealthChanged(float previousValue, float newValue)
    {
        if (!IsLocalPlayer) return;
        if (!playerProPanel)
        {
            UIManager.Instance.OpenPanel<PlayerProPanel>(panel =>
            {
                playerProPanel = panel;
                // 更新 UI，使用当前血量，不设置血量
                playerProPanel.UpdateHealthFill(health.Value, playerData.Value.maxHealthValue);
            });
        }
        else playerProPanel.UpdateHealthFill(health.Value, playerData.Value.maxHealthValue);
    }
    
    // 计算玩家属性信息
    public PlayerData CalculatePlayerData(List<DepotDataRuntime> depotsData)
    {
        // 从基础值开始
        float baoJi = GameManager.Instance.playerBaseData.baoJiValue;
        float attack = GameManager.Instance.playerBaseData.attackValue;
        float defense = GameManager.Instance.playerBaseData.defenseValue;
        float healthValue = GameManager.Instance.playerBaseData.maxHealthValue;
        float exAttackValue = GameManager.Instance.playerBaseData.exAttackValue;
        // 应用所有已装备物品的加成
        foreach (var depot in depotsData)
        {
            // 公共百分比计算
            baoJi = (float)Math.Round(baoJi * (1 + depot.depotValue.baoJiPercent / 100), 2);
            attack = (float)Math.Round(attack * (1 + depot.depotValue.attackPercent / 100), 2);
            healthValue = (float)Math.Round(healthValue * (1 + depot.depotValue.healthPercent / 100), 2);
            defense = (float)Math.Round(defense * (1 + depot.depotValue.defensePercent / 100), 2);
            exAttackValue = (float)Math.Round(exAttackValue * (1 + depot.depotValue.attackPercent / 100), 2);
            // 根据类型添加基础值
            switch (depot.depotValue.depotType)
            {
                case DepotType.Attack: attack += depot.depotValue.baseValue; break;
                case DepotType.BaoJi: baoJi += depot.depotValue.baseValue; break;
                case DepotType.Defense: defense += depot.depotValue.baseValue; break;
                case DepotType.Health: healthValue += depot.depotValue.baseValue; break;
                default: throw new ArgumentOutOfRangeException();
            }
        }
        // 计算新的 NetworkVariable的值，方便后面服务端设置数据让所有客户端自动同步
        return new PlayerData
        {
            id = NetworkManager.LocalClientId,
            maxHealthValue = healthValue,
            attackValue = attack,
            defenseValue = defense,
            baoJiValue = baoJi,
            exAttackValue = exAttackValue
        };
    }

    // 客户端要改变数据时要走的方法（客户端不能改 NetworkVariable变量）
    [ServerRpc(RequireOwnership = false)]
    public void CalculatePlayerDataServerRpc(PlayerData newPlayerData) { playerData.Value = newPlayerData; }
    
    // 客户端要改变血量数据时要走的方法（和 CalculatePlayerDataServerRpc类似）
    [ServerRpc(RequireOwnership = false)]
    private void HealHealthServerRpc(float healValue) { health.Value = Mathf.Clamp(health.Value + healValue, healValue, playerData.Value.maxHealthValue); }

    #endregion

    public override void OnNetworkDespawn()
    {
        EventCenter.Instance.RemoveEventListener(GameEvent.游戏开始, Init);
        health.OnValueChanged -= OnHealthChanged;
    }

    #region 攻击范围可视化

    /*[SerializeField] private float attackDetectRange = 8f; // 敌人检测范围
    [SerializeField] private float normalAttackRange = 1.5f; // 普通攻击有效距离
    [SerializeField] private float rushAttackRange = 5f; // 冲刺杀检测范围
    [SerializeField] private float lockAttackDistance = 1.2f; // 攻击锁定距离（角色-敌人）
    
    /// <summary>
    /// 场景视图绘制攻击范围线（仅编辑模式可见）
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!IsLocalPlayer) { return; }
        // 绘制敌人检测范围（大圈，青色）
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, attackDetectRange);

        // 绘制普通攻击有效范围（中圈，绿色）
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, normalAttackRange);

        // 绘制冲刺杀检测范围（大圈，黄色）
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, rushAttackRange);

        // 绘制攻击锁定距离（小圈，红色）
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, lockAttackDistance);
    }*/

    #endregion
    
}
