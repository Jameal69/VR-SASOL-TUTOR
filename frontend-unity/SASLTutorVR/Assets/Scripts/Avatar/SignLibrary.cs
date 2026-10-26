using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which animation clip demonstrates which sign. Filled in by
/// SASL > Build Sign Animations: a hand-made clip (Assets/Animations/Signs/Handmade)
/// wins over a generated one (Assets/Animations/Signs/Generated).
/// </summary>
[CreateAssetMenu(menuName = "SASL/Sign Library", fileName = "SignLibrary")]
public class SignLibrary : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string sign;
        public AnimationClip clip;
        [Tooltip("True for clips made by hand (e.g. Blender); false for clips generated from recordings.")]
        public bool handmade;
    }

    public List<Entry> entries = new List<Entry>();

    public AnimationClip Find(string sign)
    {
        string key = Normalise(sign);
        foreach (var entry in entries)
            if (entry.clip != null && Normalise(entry.sign) == key)
                return entry.clip;
        return null;
    }

    /// <summary>"Thank You", "thank_you" and "thankyou" all count as the same sign.</summary>
    public static string Normalise(string sign)
    {
        if (string.IsNullOrEmpty(sign)) return "";
        return sign.ToLowerInvariant().Replace(" ", "").Replace("_", "");
    }
}
