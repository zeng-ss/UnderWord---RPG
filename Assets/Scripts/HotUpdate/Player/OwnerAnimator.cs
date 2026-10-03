using Unity.Netcode.Components;

public class OwnerAnimator : NetworkAnimator
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
