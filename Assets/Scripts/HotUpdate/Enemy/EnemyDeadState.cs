
public class EnemyDeadState : Enemy_State
{
    public override void Enter()
    {
        if (!enemy.IsServer) return;
        enemy.PlayerAnimation("Dead");
        enemy.characterController.enabled = false;
        enemy.tag = "Untagged";
    }
}
