using ExposureUnnoticed2.Scripts.InGame;

namespace RestraintHub
{
    /// <summary>
    /// Worn items that hold her arms count as the game's handcuffs: a postfix on <c>GameStateFacade.IsAttachSomeHandcuffs</c>
    /// answers true while one is worn, so the game refuses everything it refuses in handcuffs (its <c>PlayerFacade.CanUseHand</c>
    /// asks it: masturbating, poses, the coat, picking up dropped clothes ...) and mods that ask the game see handcuffs. Items that
    /// also hold her in place (<see cref="BodyParts.Spot"/>: the hogtie, the wall restraint) count as handcuffs on an object in front:
    /// a postfix on the getter <c>GameStateData.PlayerIsAttachMapHandcuffs</c>. The game's own handcuff logic is not touched: its
    /// controller, struggle, arm pose and save read the real fields [observed 2026-11-14: with the answers faked, no model, no arm pose,
    /// no struggle, and a save written meanwhile loaded with both false]. Off while blocking is switched off (<c>V</c>) or with
    /// <c>General.ActAsHandcuffs</c> false. The hub itself reads the real state (<see cref="Real"/>).
    /// </summary>
    internal static class GameCuffs
    {
        private static int _bypass;

        /// <summary>The game's real handcuffs (either kind), without the hub's answer.</summary>
        internal static bool Real()
        {
            _bypass++;
            try { return GameStateFacade.IsAttachSomeHandcuffs() || (GameState.GameStateData != null && GameState.GameStateData.PlayerIsAttachMapHandcuffs); }
            finally { _bypass--; }
        }

        /// <summary>A worn item holds her arms (and, for <paramref name="inPlace"/>, her place too) and the hub acts as handcuffs.</summary>
        internal static bool Holding(bool inPlace)
        {
            if (_bypass > 0 || !Plugin.ActAsHandcuffs.Value || !Plugin.BlockEachOther.Value) return false;
            foreach (var w in Hub.All)
            {
                if ((w.Holds & BodyParts.Arms) == 0 || (inPlace && (w.Holds & BodyParts.Spot) == 0)) continue;
                if (w.Worn) return true;
            }
            return false;
        }

        // Harmony postfixes (Patches.Apply). Called often (several times a frame): keep them cheap; never throw.
        internal static void SomeHandcuffsPostfix(ref bool __result)
        {
            try { if (!__result && Holding(false)) __result = true; }
            catch (System.Exception e) { HubController.Fail("handcuff answer", e); }
        }

        internal static void MapHandcuffsPostfix(ref bool __result)
        {
            try { if (!__result && Holding(true)) __result = true; }
            catch (System.Exception e) { HubController.Fail("map handcuff answer", e); }
        }
    }
}
