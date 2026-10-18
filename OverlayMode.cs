using System;

namespace DynamicOverlay.Poc
{
    /// <summary>
    /// Defines the core overlay interaction modes introduced in Phase 2.
    /// </summary>
    public enum OverlayMode
    {
        /// <summary>
        /// Lite Mode (Pinned Overlay - Default):
        /// Overlay stays visible above ordinary windows when clicking outside,
        /// while allowing normal pointer and keyboard interaction with the entire remaining screen.
        /// </summary>
        Lite = 0,

        /// <summary>
        /// Ultra Mode (Hide When Clicking Outside):
        /// Overlay automatically hides to the background when the user clicks outside it,
        /// preserving all workspace state and restoring via Ctrl + Alt + Space.
        /// </summary>
        Ultra = 1
    }

    public static class OverlayModeExtensions
    {
        public static string ToIdentifier(OverlayMode mode)
        {
            return mode == OverlayMode.Ultra ? "Ultra" : "Lite";
        }

        public static string ToHeaderBadgeText(OverlayMode mode)
        {
            return mode == OverlayMode.Ultra
                ? "Mode: ULTRA (Hide Outside)"
                : "Mode: LITE (Pinned)";
        }

        public static bool TryParse(string value, out OverlayMode mode)
        {
            mode = OverlayMode.Lite;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (string.Equals(trimmed, "Lite", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "0", StringComparison.OrdinalIgnoreCase))
            {
                mode = OverlayMode.Lite;
                return true;
            }

            if (string.Equals(trimmed, "Ultra", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "1", StringComparison.OrdinalIgnoreCase))
            {
                mode = OverlayMode.Ultra;
                return true;
            }

            return false;
        }
    }
}
