using CBuilding.Data;
using UnityEngine;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — single source of truth for the HUD color language.
    /// Flat, matte, high-contrast colors designed to pop against the
    /// desaturated Inside-style environment. No gradients, no glows.
    /// </summary>
    public static class UIPalette
    {
        // ---- Core vitals ----
        public static readonly Color Health = FromHex("#FF003C"); // Vibrant Red
        public static readonly Color Shield = FromHex("#00F3FF"); // Neon Cyan/Turquoise

        // ---- Class coding (GDD roles) ----
        public static readonly Color ClassTank       = FromHex("#FFB300"); // Neon Orange/Yellow
        public static readonly Color ClassDPS        = FromHex("#FF003C"); // Vibrant Red
        public static readonly Color ClassController = FromHex("#00FF9C"); // Cyber Green/Cyan
        public static readonly Color ClassSupport    = FromHex("#A78BFA"); // Light Purple/Blue

        // ---- Functional ----
        public static readonly Color CooldownMask  = new Color(0f, 0f, 0f, 0.60f); // #000000 @ 60%
        public static readonly Color IconWhite     = Color.white;
        public static readonly Color LedReady      = FromHex("#39FF14"); // neon green
        public static readonly Color LedOff        = new Color(0.10f, 0.10f, 0.10f, 1f);
        public static readonly Color PermadeathRed = FromHex("#FF003C");
        public static readonly Color BlackoutFill  = new Color(0f, 0f, 0f, 0.92f);
        public static readonly Color DepletedSlot  = new Color(0.08f, 0.08f, 0.08f, 0.85f);

        public static Color GetClassColor(HeroRole role) => role switch
        {
            HeroRole.Tank       => ClassTank,
            HeroRole.DPS        => ClassDPS,
            HeroRole.Controller => ClassController,
            HeroRole.Support    => ClassSupport,
            _ => IconWhite
        };

        private static Color FromHex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }
    }
}
