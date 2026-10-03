using UnityEngine;

public class PlayerDefenceState : Player_State
{
    private enum DefenceChildState
    {
        Enter,             //进入格挡
        Hold,              //一直按着 F不松手，一直处于格挡状态
        CounterAttack,     //反击，动画结束回到 Idle状态
        Exit               //退出格挡
    }
    private DefenceChildState defenceState;
    private DefenceChildState DefenceState
    {
        get => defenceState;
        set
        {
            defenceState = value;
            switch (defenceState)
            {
                case DefenceChildState.Enter:
                    _player.PlayerAnimation("EnterDefence");
                    break;
                case DefenceChildState.Hold:
                    _player.PlayerAnimation("HoldDefence");
                    break;
                case DefenceChildState.CounterAttack:
                    _player.playerModel.transform.LookAt(((Component)_player.enemy).transform); //面向敌人
                    if (!_player.IsLocalPlayer) return;
                    // 本地先更新（预测，提升手感）
                    _player.UpdateSkillConfig(1);
                    // 通知服务端同步给所有客户端
                    if (_player.IsServer)
                    {
                        _player.SyncSkillConfigClientRpc(1);
                    }
                    else _player.SwitchSkillServerRpc(1);
                    _player.CurAttackIndex = 1;
                    _player.StartSkill(_player.curSkillConfig.skillConfigs[_player.CurAttackIndex]);
                    break;
                case DefenceChildState.Exit:
                    _player.PlayerAnimation("ExitDefence");
                    break;
            }
        }
    }
    
    public override void Enter()
    {
        _player.playerModel.SetRootMotionAction(OnRootMotion);
        DefenceState = DefenceChildState.Enter;
    }

    private void OnRootMotion(Vector3 arg1, Quaternion arg2) { _player.characterController.Move(arg1); }

    public override void Update()
    {
        
    }

    public override void Exit()
    {
        _player.playerModel.ClearRootMotionAction();
    }
    
}
