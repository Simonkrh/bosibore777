using UnityEngine;

public enum AbilityActivationResult
{
    NotActivated = 0,
    ActivatedConsume = 1,
    ActivatedKeep = 2
}

public abstract class AbilityBehavior : ScriptableObject
{
    public abstract AbilityActivationResult TryActivateServer(TankController owner, int shotSequence);

    public virtual void NotifyInputReleasedServer(TankController owner)
    {
    }

    public virtual void NotifyOwnerDiedServer(TankController owner)
    {
    }
}
