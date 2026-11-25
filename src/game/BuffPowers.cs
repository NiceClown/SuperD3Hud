using System;
using System.Collections.Generic;

namespace SuperD3Hud.Game
{
    /// <summary>
    /// Names of the buff powers the HUD knows about.
    ///
    /// The buff reader does not need this catalogue to work: the game stores
    /// one attribute per active icon, so any power can be listed by SNO. The
    /// names below only make the overlay readable. A power that is not listed
    /// is shown as "Сила #SNO" instead of being hidden, so an unknown power
    /// can never silently disappear from the list.
    ///
    /// Only SNOs verified against the game data are kept here. A wrong entry
    /// is worse than a missing one, because it would attach a wrong name to a
    /// real buff.
    /// </summary>
    public static class BuffPowers
    {
        private static readonly Dictionary<int, string> Known = CreateKnown();

        private static Dictionary<int, string> CreateKnown()
        {
            Dictionary<int, string> map = new Dictionary<int, string>();

            // Convention of Elements: icons 1..7 are the rotating elements.
            map.Add(430674, "Convention of Elements");
            // Squirt's Necklace: stacking damage buff while untouched.
            map.Add(483552, "Squirt's Necklace");
            // Stricken: stacking damage against the current target.
            map.Add(428348, "Stricken");

            return map;
        }

        /// <summary>
        /// Russian display name of a power, or "Сила #SNO" when the power is
        /// not in the catalogue.
        /// </summary>
        public static string NameOf(int powerSno)
        {
            string name;
            if (Known.TryGetValue(powerSno, out name)) return name;

            // The public plugin API lists every power by SNO with its English
            // name in a comment. That catalogue is free to use and turns an
            // opaque number into something readable.
            if (powerSno > 0)
            {
                string api = SuperD3Hud.TurboHost.HostNames.PowerName((uint)powerSno);
                if (!string.IsNullOrEmpty(api)) return api;
            }

            return "Сила #" + powerSno;
        }

        /// <summary>
        /// True when the power is an internal game flag rather than a buff a
        /// player would recognise. The game stores these in the same attribute
        /// space as real buffs, so they must be filtered from the overlay.
        /// </summary>
        public static bool IsInternal(int powerSno)
        {
            string name;
            if (Known.TryGetValue(powerSno, out name)) return false;
            if (powerSno <= 0) return true;

            // The decision must be made on the original identifier, not on the
            // prettified label: "Generic_BanterCooldown" becomes
            // "Banter Cooldown" and would otherwise look like a real buff.
            string code = SuperD3Hud.TurboHost.HostNames.PowerNameCode((uint)powerSno);
            if (string.IsNullOrEmpty(code)) return true;

            // Only clearly internal families are hidden. A power that the
            // player recognises is kept even if its identifier looks internal,
            // which is why the whitelist above is checked first.
            if (code.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (code.IndexOf("Cosmetic", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (code.IndexOf("Community", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (code.IndexOf("Passive", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (code.EndsWith("Cooldown", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        /// <summary>True when the power has a name in the catalogue.</summary>
        public static bool IsKnown(int powerSno)
        {
            return Known.ContainsKey(powerSno);
        }
    }
}
