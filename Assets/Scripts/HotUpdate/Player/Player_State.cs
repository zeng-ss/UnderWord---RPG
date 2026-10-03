using UnityEngine;

/// <summary>
/// 玩家状态基类 继承状态基类
/// </summary>
public class Player_State : State_Base
{
    protected PlayerCtrl _player;

    public override void _Init(IState_MachineOwner owner)
    {
        base._Init(owner);
        _player = (PlayerCtrl)owner;
    }

    // 检查当前动画是否播放完毕
    protected bool IsAnimationFinished(string animationName)
    {
        AnimatorStateInfo stateInfo = _player.playerModel.Animator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.IsName(animationName) && stateInfo.normalizedTime >= 1.0f;
    }

    // 检查动画是否播放了特定时间
    protected bool IsAnimationMoreThanTime(string animationName, float time)
    {
        AnimatorStateInfo stateInfo = _player.playerModel.Animator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.IsName(animationName) && stateInfo.normalizedTime >= time;
    }
}