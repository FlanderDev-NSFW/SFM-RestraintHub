using System;
using System.Collections.Generic;
using RestraintHub.Logic;

namespace RestraintHub.Tests
{
    internal static class Program
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
            else { _failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private static int Main()
        {
            PaddingTests();
            ConflictTests();
            ListTests();
            TapTests();
            LockTests();
            ShortcutTests();
            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void PaddingTests()
        {
            // The game has 31 common ring entries [observed 2026-10-11]: one empty slot, the mods start the 5th page at index 32.
            var yoke = Blocks.Running | Blocks.HandActions | Blocks.PoseSkills | Blocks.Pee;
            Check(Rules.Effective(yoke, true) == yoke, "blocking on: everything the restraint forbids");
            Check(Rules.Effective(yoke, false) == Blocks.Running, "blocking off: animations allowed, running still blocked");
            Check(Rules.Effective(Blocks.Moving | Blocks.Crouching, false) == (Blocks.Moving | Blocks.Crouching), "blocking off: moving, crouching stay");
            Check(Rules.Padding(31) == 1, "padding: 31 game entries -> 1 empty slot");
            Check(Rules.Padding(32) == 0, "padding: full pages -> none");
            Check(Rules.Padding(25) == 7 && (25 + Rules.Padding(25)) % Rules.PageSize == 0, "padding: always ends a page");
        }

        private static void ConflictTests()
        {
            var worn = new List<(string, BodyParts)> { ("Spreader Bar", BodyParts.Legs), ("handcuffs", BodyParts.Arms) };
            Check(Rules.Conflict(BodyParts.Neck | BodyParts.Arms, worn) is { } c1 && c1.Label == "handcuffs" && c1.Shared == BodyParts.Arms,
                  "conflict: yoke vs handcuffs -> arms");
            var wall = BodyParts.Neck | BodyParts.Arms | BodyParts.Legs | BodyParts.Spot;
            Check(Rules.Conflict(wall, worn) is { } c2 && c2.Label == "Spreader Bar" && c2.Shared == BodyParts.Legs, "conflict: wall vs spreader bar -> legs (first worn)");
            Check(Rules.Conflict(BodyParts.Neck, new List<(string, BodyParts)> { ("Spreader Bar", BodyParts.Legs) }) == null, "conflict: none for other parts");
            Check(Rules.Describe(BodyParts.Arms) == "her arms" && Rules.Describe(BodyParts.Neck | BodyParts.Arms) == "her neck and arms"
                  && Rules.Describe(BodyParts.Neck | BodyParts.Arms | BodyParts.Legs) == "her neck, arms, legs", "describe: parts in words");
            Check(Rules.Describe(BodyParts.Legs | BodyParts.Spot) == "her legs and her in place", "describe: held in place");
        }

        private static void ListTests()
        {
            var l = new WornList();
            Check(!l.Update(new string[0]) && l.Selected == null, "list: empty");
            Check(l.Update(new[] { "Bar" }) && l.Selected == "Bar", "list: first worn is selected");
            l.Update(new[] { "Bar", "Yoke" });
            Check(l.Selected == "Yoke" && l.Items.Count == 2 && l.Items[1] == "Yoke", "list: newly worn is selected, appended");
            Check(!l.Update(new[] { "Yoke", "Bar" }), "list: same set in another order -> no change");
            Check(l.Next() == "Bar" && l.Next() == "Yoke", "list: next wraps");
            l.Update(new[] { "Bar", "Yoke", "(handcuffs)" });
            Check(l.Selected == "(handcuffs)", "list: handcuffs put on -> selected");
            l.Select("Bar");
            l.Update(new[] { "Yoke", "(handcuffs)" });
            Check(l.Selected == "Yoke" && l.Items[0] == "Yoke", "list: selected one comes off -> the next one");
            l.Select("(handcuffs)");
            l.Update(new[] { "Yoke" });
            Check(l.Selected == "Yoke", "list: last one comes off -> the one before");
            l.Update(new string[0]);
            Check(l.Selected == null && l.Items.Count == 0, "list: all off -> nothing selected");
            Check(!l.Select("Yoke"), "list: cannot select what is not worn");
            l.Update(new[] { "A", "B" });
            Check(l.Selected == "B", "list: two at once -> the later one in the given order is selected");
        }

        private static void TapTests()
        {
            var k = new TapOrHold();
            bool tap = false;
            for (int f = 0; f < 6; f++) tap |= k.Update(true, 1f / 60f);
            tap |= k.Update(false, 1f / 60f);
            Check(tap, "F tapped 0.1 s: a tap");
            bool held = false;
            tap = false;
            for (int f = 0; f < 40; f++) { tap |= k.Update(true, 1f / 60f); held |= k.Holding; }
            Check(held && !tap, "F held 0.67 s: holding (the struggle), no tap yet");
            Check(!k.Update(false, 1f / 60f) && !k.Holding, "released after a hold: no tap");
            k.Update(true, 1f / 60f);
            k.Cancel();
            Check(!k.Update(false, 1f / 60f), "a press cancelled (menu opened) is no tap");
        }

        private static void LockTests()
        {
            var k = new TapOrHold();
            for (int f = 0; f < 180; f++) k.Update(true, 1f / 60f);
            Check(Math.Abs(k.HeldFor - 179f / 60f) < 1e-3f && k.Holding, "V held 3 s: HeldFor counts (escape)");
            k.Update(false, 1f / 60f);
            Check(k.HeldFor == 0f, "released: HeldFor 0");

            var locks = new MissionLocks();
            var worn = new HashSet<string>();
            var running = new HashSet<string> { "Hunt", "Other" };
            var endedThreads = new HashSet<object>();
            List<(MissionLock Lock, LockAction Action, string Why)> Tick(float dt, bool clock = true) =>
                locks.Tick(dt, clock, id => worn.Contains(id), l => endedThreads.Contains(l.Thread), p => running.Contains(p));

            object t1 = new object();
            var l1 = locks.Take("Yoke", "Hunt", t1, false, 10f, null, false);
            Check(l1.Owner == "Hunt" && !l1.Struggle && l1.Remaining == 10f && l1.OnEnd == LockEnd.Thread && !l1.Seen, "take: options, defaults");
            Check(locks.Refuses("Yoke", "Hunt") == null && locks.Refuses("Yoke", "Other") != null && locks.Refuses("Bar", "Other") == null,
                  "owner: only the owning mission may change it; unowned free");
            Check(Tick(1f).Count == 0 && l1.Remaining == 10f, "not yet worn: waits, timer does not run");
            worn.Add("Yoke");
            Check(Tick(1f).Count == 0 && l1.Seen && l1.Remaining == 9f, "worn: seen, timer runs");
            Check(Tick(5f, false).Count == 0 && l1.Remaining == 9f, "paused / menu: timer stops");
            locks.Take("Yoke", "Hunt", null, null, null, LockEnd.Mission, true);
            Check(!l1.Struggle && l1.Remaining == 9f && l1.OnEnd == LockEnd.Mission && l1.Thread == t1, "update: unset options keep their values");
            var r = Tick(9.5f);
            Check(r.Count == 1 && r[0].Action == LockAction.TakeOff && r[0].Why == "timer" && locks.Get("Yoke") == null, "timer ends: take off");

            locks.Take("Bar", "Hunt", t1, null, null, null, true);
            worn.Add("Bar");
            endedThreads.Add(t1);
            r = Tick(0.1f);
            Check(r.Count == 1 && r[0].Action == LockAction.TakeOff && r[0].Why == "thread ended", "thread ends (default onEnd): take off");

            object t2 = new object();
            locks.Take("Bar", "Hunt", t2, null, null, LockEnd.Mission, true);
            endedThreads.Add(t2);
            Check(Tick(0.1f).Count == 0, "onEnd mission: the thread ending does not matter");
            running.Remove("Hunt");
            r = Tick(0.1f);
            Check(r.Count == 1 && r[0].Action == LockAction.TakeOff && r[0].Why == "mission ended", "onEnd mission: mission stops -> take off");

            locks.Take("Bar", "Other", t2, false, 0f, LockEnd.Keep, true);
            Check(Tick(0.1f).Count == 0, "onEnd keep: thread end ignored");
            running.Remove("Other");
            r = Tick(0.1f);
            Check(r.Count == 1 && r[0].Action == LockAction.Forget && worn.Contains("Bar"), "onEnd keep: mission stops -> let go, stays on");

            running.Add("Hunt");
            locks.Take("Bar", "Hunt", null, null, null, LockEnd.Mission, true);
            worn.Remove("Bar");
            r = Tick(0.1f);
            Check(r.Count == 1 && r[0].Action == LockAction.Forget && r[0].Why == "came off", "came off (struggle / escape): forgotten");
            locks.Take("Bar", "Hunt", null, null, null, null, false);
            Tick(3f);
            r = Tick(3f);
            Check(r.Count == 1 && r[0].Why == "never went on", "never went on within the timeout: forgotten");

            worn.Add("Yoke");
            locks.Take("Yoke", "Hunt\tx", t1, false, 61.5f, LockEnd.Keep, true);
            locks.Take("Bar", "Other", null, true, 0f, null, false);
            string saved = locks.Serialize();
            var back = new MissionLocks();
            back.Restore(saved + "\nbroken line\nX\tY\t1\tnan\tthread\nZ\tY\t1\t3\tforever");
            var y = back.Get("Yoke");
            Check(back.Count == 2 && y != null && y.Owner == "Hunt x" && !y.Struggle && Math.Abs(y.Remaining - 61.5f) < 1e-3f && y.OnEnd == LockEnd.Keep
                  && y.Thread == null && !y.Seen && back.Get("Bar").Struggle, "save: round trip (tab in a name cleaned), bad lines skipped");
            back.Restore("");
            Check(back.Count == 0, "save: empty -> no locks");
            Check(MissionLock.TryParseEnd(" Mission ", out var e1) && e1 == LockEnd.Mission && !MissionLock.TryParseEnd("later", out _), "onEnd names");
        }

        private static void ShortcutTests()
        {
            var g = new ShortcutGuard();
            var D = ShortcutGuard.Decision.Restore;
            Check(g.Decide(3, true, false, 0, 0, 0f) == ShortcutGuard.Decision.Keep, "shortcut: key still holds the restraint -> keep");
            Check(g.Decide(3, false, true, 7, 0, 0f) == D, "shortcut: after a load -> restore (the save's index is the hub's)");
            Check(g.Decide(3, false, false, 0, 0, 1f) == D, "shortcut: ring rebuilt, saved index unchanged -> restore");
            Check(g.Decide(3, false, false, 12, 0, 1f) == ShortcutGuard.Decision.PlayerChanged, "shortcut: saved index changed -> the player's key now");
            Check(g.Decide(3, false, false, 0, 0, 2f) == D, "shortcut: third restore within the window -> restore");
            Check(g.Decide(3, false, false, 0, 0, 3f) == ShortcutGuard.Decision.Yield, "shortcut: taken away a fourth time within 10 s -> yield");
            Check(g.Decide(4, false, false, 0, 0, 3f) == D, "shortcut: other keys are counted on their own");
            Check(g.Decide(3, false, false, 0, 0, 20f) == D, "shortcut: after the window -> restore again");
            g.Reset();
            for (int i = 0; i < 3; i++) g.Decide(5, false, false, 0, 0, 0f);
            Check(g.Decide(5, false, true, 0, 0, 0f) == D, "shortcut: a load always restores");
            int n = 0;
            for (int i = 0; i < 5; i++) n = g.Rebuilt(i * 0.1f);
            Check(n == 5 && g.Rebuilt(30f) == 1, "ring rebuilds counted within the window");
        }
    }
}
