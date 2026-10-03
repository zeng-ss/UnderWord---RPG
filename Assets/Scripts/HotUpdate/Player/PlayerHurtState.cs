using UnityEngine;

public class PlayerHurtState : Player_State
{
    public override void Enter()
    {
        if (!_player.IsLocalPlayer || !_player.IsOwner) return;
        _player.playerModel.SetRootMotionAction(OnRootMotion);
        _player.PlayerAnimation("Hurt");
    }

    private void OnRootMotion(Vector3 arg1, Quaternion arg2) { _player.characterController.Move(arg1); }
    public override void Update()
    {
        if (!_player.IsLocalPlayer || !_player.IsOwner) return;
        if (IsAnimationMoreThanTime("Hurt", 0.5f)) { _player.ChangeState(PlayerStateType.Idle); }
    }

    public override void Exit()
    {
        
    }
}
