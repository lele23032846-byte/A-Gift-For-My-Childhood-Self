using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class MemoryCardEntry
{
    public ChapterId chapter;
    public string memoryLabel;
    [TextArea(1, 3)] public string chinese;
    [TextArea(1, 3)] public string english;
}

/// <summary>Configurable bilingual copy and timing for chapter memory cards.</summary>
[CreateAssetMenu(fileName = "MemoryCardCatalog", menuName = "The Late Gift/UI/Memory Card Catalog")]
public sealed class MemoryCardCatalog : ScriptableObject
{
    [SerializeField] private MemoryCardEntry[] entries = Array.Empty<MemoryCardEntry>();
    [SerializeField, Min(0.01f)] private float fadeInDuration = 0.5f;
    [SerializeField, Min(0f)] private float holdDuration = 4f;
    [SerializeField, Min(0.01f)] private float fadeOutDuration = 0.7f;

    public float FadeInDuration => fadeInDuration;
    public float HoldDuration => holdDuration;
    public float FadeOutDuration => fadeOutDuration;

    public bool TryGet(ChapterId chapter, out MemoryCardEntry entry)
    {
        foreach (MemoryCardEntry candidate in entries)
        {
            if (candidate != null && candidate.chapter == chapter)
            {
                entry = candidate;
                return true;
            }
        }

        entry = null;
        return false;
    }

    public void Configure(IEnumerable<MemoryCardEntry> configuredEntries, float fadeIn, float hold, float fadeOut)
    {
        entries = configuredEntries == null ? Array.Empty<MemoryCardEntry>() : new List<MemoryCardEntry>(configuredEntries).ToArray();
        fadeInDuration = Mathf.Max(0.01f, fadeIn);
        holdDuration = Mathf.Max(0f, hold);
        fadeOutDuration = Mathf.Max(0.01f, fadeOut);
    }
}
