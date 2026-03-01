using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Ability Definition", fileName = "AbilityDefinition")]
public class AbilityDefinition : ScriptableObject
{
    [SerializeField] private string id = "ability.id";
    [SerializeField] private string displayName = "Ability";
    [SerializeField] private float spawnWeight = 1f;
    [SerializeField] private GameObject pickupVisualPrefab;
    [SerializeField] private GameObject tankModelOverridePrefab;
    [SerializeField] private AbilityBehavior behavior;

    public string Id => id;
    public string DisplayName => displayName;
    public float SpawnWeight => spawnWeight;
    public GameObject PickupVisualPrefab => pickupVisualPrefab;
    public GameObject TankModelOverridePrefab => tankModelOverridePrefab;
    public AbilityBehavior Behavior => behavior;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            id = name.Trim().ToLowerInvariant().Replace(' ', '.');
        }

        spawnWeight = Mathf.Max(0f, spawnWeight);
    }
}
