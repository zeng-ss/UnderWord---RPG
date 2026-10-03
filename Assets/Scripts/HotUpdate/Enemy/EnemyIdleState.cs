using UnityEngine;

public class EnemyIdleState : Enemy_State
{
    private float randomValue;
    private float timer;
    public override void Enter()
    {
        if (!enemy.IsServer) return;
        enemy.isCanPlayHurtAni.Value = true;
        enemy.PlayerAnimation("Idle");
        randomValue = Random.Range(3f, 8f);
    }

    public override void Update()
    {
        if (!enemy.IsServer) return;
        // **如果没有玩家，直接返回，不尝试攻击**
        if (enemy.player == null) { timer = 0; return; }
        timer += Time.deltaTime;
        if (timer >= randomValue)
        {
            enemy.ChangeState(EnemyStateType.Attack);
            randomValue = Random.Range(3f, 8f);
            timer = 0f;
        }
    }

    public override void Exit() { timer = 0f; }
    
}
