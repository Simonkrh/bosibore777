using UnityEngine;

public abstract class AbilityBehavior : ScriptableObject
{
    public abstract bool TryActivateServer(TankController owner, int shotSequence);
}
