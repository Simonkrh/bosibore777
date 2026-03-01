using System.Collections.Generic;
using UnityEngine;

public static class AbilityRuntimeDatabase
{
    private static readonly List<AbilityDefinition> allDefinitions = new List<AbilityDefinition>();
    private static readonly Dictionary<string, AbilityDefinition> byId = new Dictionary<string, AbilityDefinition>();
    private static bool loaded;

    public static IReadOnlyList<AbilityDefinition> GetAllDefinitions()
    {
        EnsureLoaded();
        return allDefinitions;
    }

    public static bool TryGetById(string id, out AbilityDefinition definition)
    {
        EnsureLoaded();
        return byId.TryGetValue(id ?? string.Empty, out definition);
    }

    public static AbilityDefinition PickRandomDefinition(IList<AbilityDefinition> source)
    {
        if (source == null || source.Count == 0)
        {
            return null;
        }

        float totalWeight = 0f;
        for (int i = 0; i < source.Count; i++)
        {
            AbilityDefinition definition = source[i];
            if (definition == null)
            {
                continue;
            }

            totalWeight += Mathf.Max(0f, definition.SpawnWeight);
        }

        if (totalWeight <= 0f)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                {
                    return source[i];
                }
            }

            return null;
        }

        float roll = Random.value * totalWeight;
        for (int i = 0; i < source.Count; i++)
        {
            AbilityDefinition definition = source[i];
            if (definition == null)
            {
                continue;
            }

            roll -= Mathf.Max(0f, definition.SpawnWeight);
            if (roll <= 0f)
            {
                return definition;
            }
        }

        return source[source.Count - 1];
    }

    private static void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        allDefinitions.Clear();
        byId.Clear();

        AbilityDefinition[] loadedDefinitions = Resources.LoadAll<AbilityDefinition>("Abilities");
        for (int i = 0; i < loadedDefinitions.Length; i++)
        {
            AbilityDefinition definition = loadedDefinitions[i];
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                continue;
            }

            string key = definition.Id.Trim();
            if (byId.ContainsKey(key))
            {
                Debug.LogWarning($"[AbilityRuntimeDatabase] Duplicate ability id '{key}' ignored for asset '{definition.name}'.");
                continue;
            }

            byId[key] = definition;
            allDefinitions.Add(definition);
        }
    }
}
