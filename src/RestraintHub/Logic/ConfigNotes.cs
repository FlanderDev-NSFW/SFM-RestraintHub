using System;

namespace RestraintHub.Logic
{
    /// <summary>Texts for the phone app's config screens (pure).</summary>
    public static class ConfigNotes
    {
        private static readonly string[] Later = { "restart", "next time", "next start", "applies when", "takes effect when" };

        /// <summary>Whether a setting's description says it only takes effect later (the next time something happens, or after a
        /// restart): the screen marks it.</summary>
        public static bool AppliesLater(string description)
        {
            if (string.IsNullOrEmpty(description)) return false;
            foreach (string w in Later)
                if (description.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>The row title of a setting: "Section: Key", marked " *" when it applies later.</summary>
        public static string Title(string section, string key, string description) =>
            (string.IsNullOrEmpty(section) ? key : section + ": " + key) + (AppliesLater(description) ? " *" : "");

        public const string LaterNote = "Changes are saved at once. * = takes effect the next time (e.g. the next time it is put on) or after a restart.";
    }
}
