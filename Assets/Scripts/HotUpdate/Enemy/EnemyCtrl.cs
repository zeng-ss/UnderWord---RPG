using System;
using System.Linq;
using DG.Tweening;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;
using DamageNumbersPro;

public class EnemyCtrl : NetworkBehaviour , IHurt, ISkillOwner, IState_MachineOwner
{
    #region 数据
    
    // 数据
    public HitData hitData;
    [HideInInspector] public float disToPlayer;
    // 组件
    public EnemyModel enemyModel;
    private AudioSource audioSource;
    private State_Machine stateMachine;
    private CapsuleCollider capsuleCollider;
    [HideInInspector] public PlayerCtrl player;
    [HideInInspector] public CharacterController characterController;

    // 行为参数
    private float gravity = -5f;
    private Vector3 velocity;
    [HideInInspector] public bool hasGravity;
    [HideInInspector] public bool isOnGround;         // 是否在陆地上
    // 状态
    [HideInInspector] public EnemyStateType currentState;
    [HideInInspector] public EnemyStateType lastState;
    
    // 网络同步数据
    // NetworkVariable 默认会同步给后加入的客户端
    public NetworkVariable<float> networkHealth = new(
        100f, // 默认值
        NetworkVariableReadPermission.Everyone,  // 所有人都可读
        NetworkVariableWritePermission.Server    // 只有服务器可写
    );
    [HideInInspector] public NetworkVariable<float> maxHealth = new(200);
    // 是否可以播放 Hurt 动画
    [HideInInspector] public NetworkVariable<bool> isCanPlayHurtAni = new(true);
    // 是否开启锁定
    [HideInInspector] public NetworkVariable<bool> isStartLock = new();
    // 要求生成的玩家的 id
    [HideInInspector] public NetworkVariable<ulong> requestSpawnId = new();
    [HideInInspector] public NetworkVariable<bool> isStartPin; // 是否开始拼刀
    // 标记是否已完成一次拼刀
    public NetworkVariable<bool> isPinFinished = new();
    // UI
    public GameObject healthBar;
    public Image fillImage;
    public TMP_Text healthText;
    public GameObject isStartPinTip;

    #endregion
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // 1. 基础组件初始化（所有客户端都执行）
        audioSource = GetComponent<AudioSource>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        characterController = GetComponent<CharacterController>();
        enemyModel.Init(this);
        //初始化 生成状态机
        stateMachine = new State_Machine();
        stateMachine.Init(this);
        ChangeState(EnemyStateType.Idle);
        // 监听血量变更，所有客户端实时更新血条
        networkHealth.OnValueChanged += OnHealthChanged;
        // 2. 逻辑层初始化（仅服务端执行：状态机、AI、血量）
        if (IsServer)
        {
            isStartPin.Value = false;
            maxHealth.Value = 10000;
            networkHealth.Value = maxHealth.Value;
            isStartLock.Value = false;
            isCanPlayHurtAni.Value = true;
            capsuleCollider.enabled = false;
            player = FindNearestPlayer();
            hasGravity = true;
            isOnGround = characterController.isGrounded;
        }
        else
        {
            characterController.enabled = false;
        }
        fillImage.fillAmount = networkHealth.Value / maxHealth.Value;
        healthText.text = $"{networkHealth.Value}/{maxHealth.Value}";
    }
    private void OnHealthChanged(float previousValue, float newValue)
    {
        float fillAmount = Mathf.Clamp01(newValue / maxHealth.Value);
        fillImage.DOFillAmount(fillAmount, 0.5f);
        healthText.text = $"{newValue}/{maxHealth.Value}";
    }
    private void Update()
    {
        // 控制血条和拼刀提示看向玩家
        KeepLookPlayer(healthBar);
        KeepLookPlayer(isStartPinTip);
        if (!IsServer) return;
        player = FindNearestPlayer();
        if (player) { disToPlayer = Vector3.Distance(transform.position, player.transform.position); }
        #region 重力

        // 仅本地玩家执行重力逻辑
        if (!hasGravity || characterController.enabled == false) return;
        characterController.Move(velocity * Time.deltaTime);
        isOnGround = characterController.isGrounded;
        if (isOnGround) { velocity.y = -2f; }
        else { velocity.y += gravity * Time.deltaTime; }

        #endregion
        #region 锁定和玩家的距离

        if (!player) return;
        // 当在攻击状态且距离大于锁定距离且可以被伤害时，开启锁定玩家
        if (currentState == EnemyStateType.Attack && disToPlayer >= 2f && isStartLock.Value) { KeepLockDistance(); }

        #endregion
    }
    private void KeepLookPlayer(GameObject obj)
    {
        obj.transform.LookAt(Camera.main.transform.position);
        obj.transform.Rotate(0, 180, 0);
    }

    #region 动画和声音相关
    
    /// <summary>
    /// 切换状态
    /// </summary>
    /// <param name="stateType"></param>
    public void ChangeState(EnemyStateType stateType, bool IsResfeshState = false)
    {
        // 记录上一个状态（排除重复切换相同状态的情况）
        if (currentState != stateType) { lastState = currentState; }
        currentState = stateType;
        switch (stateType)
        {
            case EnemyStateType.Idle: stateMachine.ChangeState<EnemyIdleState>(IsResfeshState);
                break;
            case EnemyStateType.Attack: stateMachine.ChangeState<EnemyAttackState>(IsResfeshState);
                break;
            case EnemyStateType.Dead: stateMachine.ChangeState<EnemyDeadState>(IsResfeshState);
                break;
            case EnemyStateType.Hurt: stateMachine.ChangeState<EnemyHurtState>(IsResfeshState);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(stateType), stateType, null);
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
    
        // 如果希望使用固定秒数作为过渡时间（而不是百分比），可以用 CrossFadeInFixedTime
        enemyModel.Animator.CrossFadeInFixedTime(animationName, fixedTransitionTime, 0, 0f);
    }
    
    private void PlayerAudio(AudioClip audioClip) { audioSource.PlayOneShot(audioClip); }
    
    #endregion

    #region 玩家相关方法

    // 强制面向玩家
    public void FaceToPlayer()
    {
        if (!player || currentState == EnemyStateType.Idle) return;
        Vector3 toPlayer = player.transform.position - transform.position;
        if (toPlayer == Vector3.zero) return;
        toPlayer.y = 0;
        enemyModel.transform.rotation = Quaternion.Slerp(enemyModel.transform.rotation
            , Quaternion.LookRotation(toPlayer), Time.deltaTime * 5f);
    }
    
    /// <summary>
    /// 检测最近的玩家
    /// </summary>
    private PlayerCtrl FindNearestPlayer()
    {
        // 查找所有带 PlayerCtrl 组件的对象
        PlayerCtrl[] allPlayers = FindObjectsOfType<PlayerCtrl>();
        if (allPlayers.Length == 0) return null;

        PlayerCtrl nearestPlayer = null;
        float minDistance = Mathf.Infinity;
        foreach (PlayerCtrl playerObj in allPlayers)
        {
            // 玩家未死亡
            if (playerObj.currentState == PlayerStateType.Dead) continue;

            float dis = Vector3.Distance(transform.position, playerObj.transform.position);
            if (dis < minDistance)
            {
                minDistance = dis;
                nearestPlayer = playerObj;
            }
        }
        return nearestPlayer;
    }
    
    // 保持与玩家的锁定攻击距离
    [Header("锁定距离配置")]
    [SerializeField] private float lockDistance = 2f; // 目标锁定距离
    [SerializeField] private float minSafeDistance = 2f; // 最小安全距离（死区下限）
    [SerializeField] private float maxSafeDistance = 2f; // 最大安全距离（死区上限）
    [SerializeField] private float moveSpeed = 5f; // 移动速度

    /// <summary>
    /// 稳定保持与玩家的锁定攻击距离
    /// </summary>
    private void KeepLockDistance()
    {
        if (!IsServer || !player) return;
        // 计算方向和距离
        Vector3 enemyPos = new Vector3(transform.position.x, 0, transform.position.z);
        Vector3 playerPos = new Vector3(player.transform.position.x, 0, player.transform.position.z);
        Vector3 toPlayer = playerPos - enemyPos;
        float currentDistance = toPlayer.magnitude;

        // 死区判断：距离在 xx~xx米之间，不移动
        if (currentDistance >= minSafeDistance && currentDistance <= maxSafeDistance)
        {
            // 仅转向玩家，不移动
            FaceToPlayer();
            return;
        }

        // 距离过近：向后撤退
        if (currentDistance < minSafeDistance)
        {
            FaceToPlayer();
            Vector3 backDir = -toPlayer.normalized; // 向远离玩家的方向后退（而非-transform.forward）
            backDir.y = 0;
            characterController.Move(backDir * (moveSpeed * Time.deltaTime)); // 带碰撞的移动
            return;
        }

        // 距离过远：向目标位置移动
        if (currentDistance > maxSafeDistance)
        {
            FaceToPlayer();
            // 计算目标位置：玩家位置 - 朝向敌人的单位向量 * 目标锁定距离
            Vector3 targetPos = playerPos - toPlayer.normalized * lockDistance;
            targetPos.y = transform.position.y; // 还原Y轴高度（适配地形）
            // 计算移动方向（仅水平方向）
            Vector3 moveDir = targetPos - transform.position;
            moveDir.y = 0;
            moveDir = moveDir.normalized;
            // 带碰撞的平滑移动
            characterController.Move(moveDir * (moveSpeed * Time.deltaTime));
        }
    }

    #endregion
    
    #region 技能相关
    public void StartSkillHit(int weaponIndex) { }
    public void StopSkillHit(int weaponIndex) { }
    public void SkillCanSwitch() { }
    
    #endregion

    #region 攻击相关
    public void OnHit(IHurt hurt, Vector3 hurtPos)
    {
        if (!IsServer) return;
        // 克隆受击特效
        GameObject obj = Instantiate(hitData.hitPrefabs[0]);
        obj.transform.position = hurtPos;
        Destroy(obj, obj.GetComponent<ParticleSystem>().main.duration);
        // 受击声音
        SoundManager.Instance.PlaySound(hitData.hitClip, hurtPos);
        // 仅调用玩家 OnHurt，由玩家自身处理扣血和本地反馈（震动，红边）
        hurt.OnHurt(hitData, this, hurtPos);
        // 下发客户端播放攻击方的特效
        PlayHitEffectsClientRpc(hurtPos);
    }

    [ClientRpc]
    private void PlayHitEffectsClientRpc(Vector3 hurtPos)
    {
        if (IsServer) return;
        // 克隆受击特效
        GameObject obj = Instantiate(hitData.hitPrefabs[0]);
        obj.transform.position = hurtPos;
        Destroy(obj, obj.GetComponent<ParticleSystem>().main.duration);
        // 受击音效
        SoundManager.Instance.PlaySound(hitData.hitClip, hurtPos);
    }

    #endregion
    
    #region 受伤相关

    public DamageNumber damageNumber;
    /// <summary>
    /// 受击逻辑（服务端权威，客户端仅触发请求）
    /// </summary>
    /// <param name="hitData">伤害数据</param>
    /// <param name="hurtSource">攻击者</param>
    /// <param name="hurtPos">受击位置</param>
    public void OnHurt(HitData hitData, ISkillOwner hurtSource, Vector3 hurtPos)
    {
        PlayerCtrl playerSource = ((Component)hurtSource).GetComponent<PlayerCtrl>();
        // 拿到伤害数值
        float damage = playerSource.currentState == PlayerStateType.EX
            ? playerSource.playerData.Value.exAttackValue
            : playerSource.playerData.Value.attackValue;
        float baoJiPercent = playerSource.playerData.Value.baoJiValue;
        // 客户端仅发送受击请求到服务端
        if (!IsServer)
        {
            // 客户端调用 ServerRpc，通知服务端处理受击
            RequestHurtServerRpc(damage, baoJiPercent, hurtPos, playerSource.OwnerClientId); return;
        }
        // 服务端直接处理
        DoServer(damage, baoJiPercent, hurtPos, playerSource.OwnerClientId);
    }
    
    /// <summary>
    /// 客户端请求服务端处理受击（仅传必要数据，避免序列化大对象）
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestHurtServerRpc(float damageValue, float baoJiPercent, Vector3 hurtPos, ulong attackerClientId)
    {
        DoServer(damageValue, baoJiPercent, hurtPos, attackerClientId);
    }

    /// <summary>
    /// 服务端核心处理逻辑
    /// </summary>
    private void DoServer(float damageValue, float baoJiPercent, Vector3 hurtPos, ulong attackerClientId)
    {
        // 如果已经死亡，直接播放死亡动画（可能因为之前的伤害已经死亡）
        if (networkHealth.Value <= 0)
        {
            PlayDeathAnimationClientRpc(attackerClientId);
            return;
        }
        // 在服务端根据攻击者ID找到玩家
        PlayerCtrl attacker = FindObjectsOfType<PlayerCtrl>().FirstOrDefault(source => source.OwnerClientId == attackerClientId);
        if (attacker == null) return;
        // 在服务端重新计算伤害（权威）
        damageValue = attacker.currentState == PlayerStateType.EX
            ? attacker.playerData.Value.exAttackValue
            : attacker.playerData.Value.attackValue;
        // 暴击计算
        bool isBaoJi = false;
        if (Random.Range(1, 100) <= baoJiPercent)
        {
            damageValue += Random.Range(200, 1500);
            isBaoJi = true;
        }
        networkHealth.Value = Mathf.Clamp(networkHealth.Value - damageValue, 0, maxHealth.Value);
        if (networkHealth.Value <= 0)
        {
            PlayDeathAnimationClientRpc(attackerClientId);
            return;
        }
        if (isCanPlayHurtAni.Value) PlayHurtAnimationClientRpc(isBaoJi, damageValue, hurtPos);
    }

    [ClientRpc]
    private void PlayHurtAnimationClientRpc(bool isBaoJi, float damage, Vector3 hurtPos)
    {
        ChangeState(EnemyStateType.Hurt, true);
        damageNumber.SetColor(isBaoJi ? Color.red : Color.white);
        damageNumber.Spawn(hurtPos, damage);
        // 检查当前状态是否为 Hurt 状态
        if (currentState is EnemyStateType.Hurt)
        {
            EnemyHurtState hurt = (EnemyHurtState)stateMachine.CurrentState;
            hurt.SetHurtPos(hurtPos);
        }
    }

    /// <summary>
    /// 同步死亡动画到所有客户端
    /// </summary>
    [ClientRpc]
    private void PlayDeathAnimationClientRpc(ulong attackerClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == attackerClientId)
        {
            TaskManager.Instance.UpdateTaskProgress(TaskType.击败第一个敌人);
        }
        capsuleCollider.enabled = false;
        tag = "Untagged";
        ChangeState(EnemyStateType.Dead);
        DOVirtual.DelayedCall(5, () => { GetComponent<NetworkObject>().Despawn(); });
    }

    #endregion

    #region 僵硬请求

    // 在玩家脚本调用
    // 客户端请求服务端让敌人僵硬的 Rpc
    [ServerRpc(RequireOwnership = false)]
    public void EnemyStiffServerRpc()
    {
        // 仅服务端执行：敌人僵硬逻辑
        if (isPinFinished.Value) return; // 已拼刀过，跳过
        isStartPin.Value = false;
        isPinFinished.Value = true;
        isStartLock.Value = false;
        EnemyStiffClientRpc();
    }
    [ClientRpc]
    private void EnemyStiffClientRpc()
    {
        // 执行僵硬（服务端控制，同步给所有客户端）
        enemyModel.Animator.speed = 0;
        DOVirtual.DelayedCall(0.5f, () =>
        {
            enemyModel.Animator.speed = 1;
            ChangeState(EnemyStateType.Idle);
        });
    }

    #endregion
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        networkHealth.OnValueChanged -= OnHealthChanged;
    }
    
}
