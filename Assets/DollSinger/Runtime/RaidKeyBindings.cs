using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS.DollSinger
{
    public enum RaidKey
    {
        Forward, Backward, Left, Right, Sprint, Jump, Crouch, HoldCrouch, Prone, TiptoeOrCancel,
        LeanLeft, LeanRight, Reload, Interact, Inspect, Grenade,
        Defense, Transfer, DeployHalo, HaloMode, Scan, Light,
        ViewOrDarkSight, ClashView, Inventory, Status, Map, Journal, Buy, Scoreboard
    }

    public static class RaidKeyBindings
    {
        private const string PreferencePrefix = "Moonkov.Keys.";
        private static readonly (RaidKey Action, Key Default, string Label)[] s_Bindings =
        {
            (RaidKey.Forward, Key.W, "Move forward"),
            (RaidKey.Backward, Key.S, "Move backward"),
            (RaidKey.Left, Key.A, "Move left"),
            (RaidKey.Right, Key.D, "Move right"),
            (RaidKey.Sprint, Key.LeftShift, "Sprint"),
            (RaidKey.Jump, Key.Space, "Jump"),
            (RaidKey.Crouch, Key.C, "Toggle crouch"),
            (RaidKey.HoldCrouch, Key.LeftCtrl, "Hold to crouch"),
            (RaidKey.Prone, Key.Z, "Toggle prone"),
            (RaidKey.TiptoeOrCancel, Key.X, "Tiptoe / Cancel action"),
            (RaidKey.LeanLeft, Key.Q, "Lean left"),
            (RaidKey.LeanRight, Key.E, "Lean right"),
            (RaidKey.Reload, Key.R, "Reload"),
            (RaidKey.Interact, Key.F, "Interact / Loot"),
            (RaidKey.Inspect, Key.O, "Inspect target"),
            (RaidKey.Grenade, Key.G, "Equip / Stow grenade"),
            (RaidKey.Defense, Key.T, "Defend"),
            (RaidKey.Transfer, Key.Y, "Transfer energy"),
            (RaidKey.DeployHalo, Key.U, "Deploy / Recall halo"),
            (RaidKey.HaloMode, Key.K, "Switch halo mode"),
            (RaidKey.Scan, Key.N, "Scan"),
            (RaidKey.Light, Key.L, "Halo light"),
            (RaidKey.ViewOrDarkSight, Key.V, "Change view / Dark Sight"),
            (RaidKey.ClashView, Key.F8, "Change view (Clash)"),
            (RaidKey.Inventory, Key.I, "RAID INVENTORY"),
            (RaidKey.Status, Key.H, "STATUS CHECK"),
            (RaidKey.Map, Key.M, "Raid map"),
            (RaidKey.Journal, Key.J, "Journal"),
            (RaidKey.Buy, Key.B, "Buy equipment"),
            (RaidKey.Scoreboard, Key.Tab, "SCOREBOARD")
        };
        private static Key[] s_Keys;

        public static IReadOnlyList<(RaidKey Action, Key Default, string Label)> Bindings => s_Bindings;
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_Keys = null;
            Changed = null;
        }

        public static Key DefaultKey(RaidKey action) => s_Bindings[(int)action].Default;
        public static string ActionLabel(RaidKey action) => s_Bindings[(int)action].Label;
        public static Key GetKey(RaidKey action)
        {
            Load();
            return s_Keys[(int)action];
        }

        public static bool Held(RaidKey action) => Keyboard.current?[GetKey(action)].isPressed == true;
        public static bool Pressed(RaidKey action) => Keyboard.current?[GetKey(action)].wasPressedThisFrame == true;
        public static string DisplayName(RaidKey action) => DisplayName(GetKey(action));
        public static string DisplayName(Key key) => Keyboard.current?[key].displayName ?? key.ToString();

        // These keys also serve UI navigation, chat, stash actions or fixed shortcuts.
        public static bool CanAssign(RaidKey action, Key key) =>
            key > Key.None && key <= Key.MediaForward && (key <= Key.OEM5 || key >= Key.F13) &&
            key is not (Key.Escape or Key.Enter or Key.NumpadEnter or Key.LeftAlt or Key.RightAlt
                or Key.LeftMeta or Key.RightMeta or Key.F1 or Key.Delete) &&
            !(key >= Key.Digit1 && key <= Key.Digit9) && (key != Key.R || action == RaidKey.Reload);

        public static bool TryAssign(RaidKey action, Key key, out RaidKey conflict)
        {
            Load();
            conflict = action;
            if (!CanAssign(action, key) || FindConflict(s_Keys, action, key, out conflict)) return false;
            if (s_Keys[(int)action] == key) return true;
            s_Keys[(int)action] = key;
            string preference = PreferencePrefix + action;
            if (key == DefaultKey(action)) PlayerPrefs.DeleteKey(preference);
            else PlayerPrefs.SetInt(preference, (int)key);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        public static void RestoreDefaults()
        {
            s_Keys = Defaults();
            ClearPreferences();
            Changed?.Invoke();
        }

        private static void ClearPreferences()
        {
            foreach (var binding in s_Bindings) PlayerPrefs.DeleteKey(PreferencePrefix + binding.Action);
            PlayerPrefs.Save();
        }

        private static bool FindConflict(Key[] keys, RaidKey action, Key key, out RaidKey conflict)
        {
            // Both Shift keys sprint until the player changes the default.
            if (action != RaidKey.Sprint && key == Key.RightShift && keys[(int)RaidKey.Sprint] == Key.LeftShift)
            {
                conflict = RaidKey.Sprint;
                return true;
            }
            for (int i = 0; i < keys.Length; i++)
            {
                if (i == (int)action) continue;
                if (keys[i] == key || action == RaidKey.Sprint && key == Key.LeftShift && keys[i] == Key.RightShift)
                {
                    conflict = (RaidKey)i;
                    return true;
                }
            }
            conflict = action;
            return false;
        }

        private static Key[] Defaults()
        {
            var keys = new Key[s_Bindings.Length];
            foreach (var binding in s_Bindings) keys[(int)binding.Action] = binding.Default;
            return keys;
        }

        private static void Load()
        {
            if (s_Keys != null) return;
            var keys = Defaults();
            foreach (var binding in s_Bindings)
                keys[(int)binding.Action] = (Key)PlayerPrefs.GetInt(PreferencePrefix + binding.Action, (int)binding.Default);
            // Read the entire set before checking it so moving a key between actions survives a restart.
            foreach (var binding in s_Bindings)
            {
                var key = keys[(int)binding.Action];
                if (CanAssign(binding.Action, key) && !FindConflict(keys, binding.Action, key, out _)) continue;
                s_Keys = Defaults();
                ClearPreferences();
                return;
            }
            s_Keys = keys;
        }
    }
}
