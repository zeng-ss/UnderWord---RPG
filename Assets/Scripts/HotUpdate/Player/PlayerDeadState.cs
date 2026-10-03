public class PlayerDeadState : Player_State
{
    public override void Enter()
    {
        if (!_player.IsLocalPlayer || !_player.IsOwner) return;
        _player.PlayerAnimation("Dead");
    }
}
