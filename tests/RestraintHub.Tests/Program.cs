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
            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void PaddingTests()
        {
            // The game has 31 common ring entries [observed 2026-10-11]: one empty slot, the mods start the 5th page at index 32.
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
    }
}
