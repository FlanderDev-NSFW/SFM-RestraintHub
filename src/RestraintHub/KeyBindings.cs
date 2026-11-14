using System;
using System.Collections.Generic;
using RestraintHub.Logic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace RestraintHub
{
    /// <summary>
    /// The hub's key settings (<see cref="KeySpec"/>) as keys on the player's keyboard. A character entry ("+", "U") counts every
    /// key labelled with it on the current layout (<c>KeyControl.displayName</c>) and every key that typed it
    /// (<c>Keyboard.OnTextInput</c>, learned while playing - so it works even where the label is not reported as expected); a key
    /// name ("NumpadPlus") is a fixed position. Resolved keys are cached per keyboard layout; a layout change is logged with what
    /// each setting now means.
    /// <para>Observed 2026-11-14 (US layout 00000409): <c>FindKeyOnCurrentKeyboardLayout("+")</c> returns only the first key with
    /// that label (the numpad's there) - so this class matches all of them.</para>
    /// </summary>
    internal static class KeyBindings
    {
        private static readonly Dictionary<string, List<KeyControl>> Cache = new Dictionary<string, List<KeyControl>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<Key>> Learned = new Dictionary<string, HashSet<Key>>(StringComparer.Ordinal);
        private static string _layout;
        private static IntPtr _keyboard;

        /// <summary>The keys of a setting (a spec), on the current keyboard and layout. Empty without a keyboard.</summary>
        internal static List<KeyControl> Resolve(string spec)
        {
            var kb = Keyboard.current;
            if (kb == null) return new List<KeyControl>();
            CheckLayout(kb);
            spec ??= "";
            if (Cache.TryGetValue(spec, out var keys)) return keys;
            keys = new List<KeyControl>();
            foreach (var e in KeySpec.Parse(spec))
            {
                if (!e.IsCharacter)
                {
                    if (Enum.TryParse(e.Text, true, out Key key) && key != Key.None) Add(keys, kb[key]);
                    continue;
                }
                var all = kb.allKeys;
                for (int i = 0; i < all.Count; i++)
                {
                    var k = all[i];
                    if (k != null && KeySpec.SameLabel(k.displayName, e.Text)) Add(keys, k);
                }
                foreach (var learnedKey in LearnedFor(e.Text)) Add(keys, kb[learnedKey]);
            }
            Cache[spec] = keys;
            return keys;
        }

        private static void Add(List<KeyControl> keys, KeyControl k)
        {
            if (k == null) return;
            foreach (var have in keys) if (have.keyCode == k.keyCode) return;
            keys.Add(k);
        }

        private static IEnumerable<Key> LearnedFor(string character)
        {
            foreach (var pair in Learned)
                if (KeySpec.SameLabel(pair.Key, character))
                    foreach (var k in pair.Value) yield return k;
        }

        /// <summary>Any key of the setting is held now.</summary>
        internal static bool Held(string spec)
        {
            foreach (var k in Resolve(spec)) if (k.isPressed) return true;
            return false;
        }

        /// <summary>"'+' = RightBracket, NumpadPlus; ..." - what a setting means now (log, diagnostics).</summary>
        internal static string Describe(string spec)
        {
            var kb = Keyboard.current;
            if (kb == null) return "no keyboard";
            var parts = new List<string>();
            foreach (var e in KeySpec.Parse(spec))
            {
                var keys = Resolve(e.Text);
                parts.Add(e + " = " + (keys.Count == 0 ? "(not found)" : Names(keys)));
            }
            return parts.Count == 0 ? "(no keys)" : string.Join("; ", parts);
        }

        internal static string Names(List<KeyControl> keys)
        {
            var names = new List<string>();
            foreach (var k in keys) names.Add(k.keyCode + (k.displayName == null || k.displayName.Length != 1 ? "" : " '" + k.displayName + "'"));
            return string.Join(", ", names);
        }

        /// <summary>The entries of a setting that find no key (character not on this layout yet, or no such key name).</summary>
        internal static List<KeyEntry> Missing(string spec)
        {
            var missing = new List<KeyEntry>();
            foreach (var e in KeySpec.Parse(spec))
                if (Resolve(e.Text).Count == 0) missing.Add(e);
            return missing;
        }

        /// <summary>The game's keyboard sent a typed character (Harmony postfix on <c>Keyboard.OnTextInput</c>): if a hub setting
        /// uses that character, remember which key typed it on this layout.</summary>
        internal static void OnTextInput(char c)
        {
            var kb = Keyboard.current;
            if (kb == null || char.IsControl(c)) return;
            string ch = c.ToString();
            if (!Watched(ch)) return;
            // The key that typed it is down now; modifiers (Shift, AltGr ...) are not it.
            KeyControl typed = null, fallback = null;
            int down = 0;
            var all = kb.allKeys;
            for (int i = 0; i < all.Count; i++)
            {
                var k = all[i];
                if (k == null || !k.isPressed || IsModifier(k.keyCode)) continue;
                down++;
                fallback = k;
                if (k.wasPressedThisFrame) typed = k;
            }
            typed ??= down == 1 ? fallback : null;
            if (typed == null) return;
            CheckLayout(kb);
            if (!Learned.TryGetValue(ch, out var set)) Learned[ch] = set = new HashSet<Key>();
            if (!set.Add(typed.keyCode)) return;
            Cache.Clear();
            Plugin.Logger.LogInfo("[SFM-ACCEPT] keys: '" + ch + "' is typed with " + typed.keyCode + " on layout " + kb.keyboardLayout + " (learned)");
        }

        private static bool Watched(string ch)
        {
            foreach (var spec in Plugin.KeySettings())
                foreach (var e in KeySpec.Parse(spec.Value))
                    if (e.IsCharacter && KeySpec.SameLabel(e.Text, ch)) return true;
            return false;
        }

        private static bool IsModifier(Key k) =>
            k == Key.LeftShift || k == Key.RightShift || k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt
            || k == Key.LeftMeta || k == Key.RightMeta || k == Key.CapsLock;

        /// <summary>A new keyboard or layout: forget the cached and learned keys and log what the settings mean now.</summary>
        private static void CheckLayout(Keyboard kb)
        {
            string layout = kb.keyboardLayout ?? "";
            if (layout == _layout && kb.Pointer == _keyboard) return;
            bool first = _layout == null;
            _layout = layout;
            _keyboard = kb.Pointer;
            Cache.Clear();
            Learned.Clear();
            if (!first) LogSettings("keyboard layout changed to " + layout);
        }

        /// <summary>Log every key setting as it resolves now; warn for entries that find no key.</summary>
        internal static void LogSettings(string why)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            Plugin.Logger.LogInfo("[SFM-ACCEPT] keys: " + why + " (layout " + kb.keyboardLayout + ")");
            foreach (var entry in Plugin.KeySettings())
            {
                string name = entry.Definition.Section + "." + entry.Definition.Key;
                Plugin.Logger.LogInfo("[SFM-ACCEPT] keys: " + name + " = " + Describe(entry.Value));
                foreach (var e in Missing(entry.Value))
                    Plugin.Logger.LogWarning(name + ": " + (e.IsCharacter
                        ? "no key is labelled '" + e.Text + "' on this keyboard layout; it is found once typed, or set a key name (e.g. RightBracket, NumpadPlus)."
                        : "'" + e.Text + "' is not an Input System key name."));
            }
        }
    }
}
