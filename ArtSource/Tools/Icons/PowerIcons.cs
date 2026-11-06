// Legendary power icons, one per Assets/Data/LegendaryPowers asset (same file name). LegendaryPower has no Icon field
// yet, so nothing shows these. They are symbols of what the power does, not pictures of the item that carries it.
using System;
using System.Drawing;

public static class PowerIcons
{
    const string Dir = "PowerIcons";

    static void Reg(string name, Action<Ic> draw) { IconRegistry.Reg(Dir, name, draw); }

    public static void Register()
    {
        Reg("Ambush", Ambush); Reg("Ascetic", Ascetic); Reg("BorrowedTime", BorrowedTime); Reg("Bottomless", Bottomless);
        Reg("BrokenPromises", BrokenPromises); Reg("ColdBlooded", ColdBlooded); Reg("CounterStrike", CounterStrike);
        Reg("Crossfire", Crossfire); Reg("DeathsDoor", DeathsDoor); Reg("Earthshaker", Earthshaker);
        Reg("EchoingBlade", EchoingBlade); Reg("EventHorizon", EventHorizon); Reg("Frostbound", Frostbound);
        Reg("Insight", Insight); Reg("LastStand", LastStand); Reg("LongShot", LongShot); Reg("Momentum", Momentum);
        Reg("Overkill", Overkill); Reg("Patience", Patience); Reg("Pyromancy", Pyromancy); Reg("Refractionist", Refractionist);
        Reg("SecondWind", SecondWind); Reg("Shatter", Shatter); Reg("Slingshot", Slingshot); Reg("SoulThief", SoulThief);
        Reg("StaticField", StaticField); Reg("Steam", Steam); Reg("Stormborn", Stormborn); Reg("Stormcall", Stormcall);
        Reg("Sunder", Sunder); Reg("ToxicTrail", ToxicTrail); Reg("Undertow", Undertow); Reg("Vanguard", Vanguard);
        Reg("Venomous", Venomous); Reg("Vigil", Vigil); Reg("Wildfire", Wildfire); Reg("Wyrmfire", Wyrmfire);
    }

    static void Small(Ic c)
    {
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
    }

    /// <summary>A plain dagger along the 45 degree diagonal, tip at the top right.</summary>
    static void Knife(Ic c, float x, float y, float scale, Pal blade, Pal hilt)
    {
        c.Xf(45, x, y, scale);
        c.Add(G.Poly(x, y - 96, x + 24, y - 50, x + 24, y + 30, x - 24, y + 30, x - 24, y - 50), blade).Then(() =>
            c.Fill(G.Poly(x, y - 96, x + 24, y - 50, x + 24, y + 30, x, y + 30), blade.D, 120));
        c.Add(G.RR(x - 9, y + 40, 18, 44, 6), Pal.DarkLeather).Thin();
        c.Add(G.RR(x - 42, y + 26, 84, 20, 9), hilt);
        c.Add(G.Circle(x, y + 90, 12), hilt);
        c.NoXf();
    }

    static void Hourglass(Ic c, float x, float y, float s, Pal frame, Color sand)
    {
        c.Add(G.Soft(x - 44 * s, y - 66 * s, x + 44 * s, y - 66 * s, x + 44 * s, y - 40 * s, x + 10 * s, y, x + 44 * s, y + 40 * s,
                     x + 44 * s, y + 66 * s, x - 44 * s, y + 66 * s, x - 44 * s, y + 40 * s, x - 10 * s, y, x - 44 * s, y - 40 * s),
              new Pal("#E6F8FF", "#A8D8EE", "#6AA0C8")).Then(() =>
        {
            c.Fill(G.Soft(x - 36 * s, y - 38 * s, x + 36 * s, y - 38 * s, x + 2 * s, y - 4 * s, x - 2 * s, y - 4 * s), sand);
            c.Fill(G.Soft(x - 36 * s, y + 64 * s, x, y + 22 * s, x + 36 * s, y + 64 * s), sand);
            c.Stroke(sand, 5, x, y - 4 * s, x, y + 40 * s);
        });
        c.Add(G.RR(x - 58 * s, y - 82 * s, 116 * s, 22 * s, 10), frame);
        c.Add(G.RR(x - 58 * s, y + 60 * s, 116 * s, 22 * s, 10), frame);
    }

    /// <summary>The four conversion powers: the element's symbol in one shared ring, so they read as a family.</summary>
    static void ElementRing(Ic c, Pal p, string glow)
    {
        c.Glow(128, 128, 116, Pal.Hex(glow), 170);
        c.Add(G.Ring(128, 128, 110, 88), p).Then(() =>
        {
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4 + Math.PI / 8;
                c.Fill(G.Circle(128 + (float)Math.Cos(a) * 99, 128 + (float)Math.Sin(a) * 99, 5), Ic.Ink);
            }
        });
        c.Flush();
    }

    // A full heart with a knife in it: full health means a critical hit
    static void Ambush(Ic c)
    {
        Tiles.Shadow(c);
        c.Glow(118, 138, 110, Pal.Hex("#B57BFF"), 110);
        c.Add(G.Heart(118, 140, 82), Pal.Ruby).Then(() => c.Gloss(86, 100, 18, 10, -35, 190));
        c.Flush();
        Knife(c, 142, 112, .95f, Pal.Mirror, Pal.Shadow);
    }

    // An empty bowl: the plain skills feed you
    static void Ascetic(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 112, 112, Pal.Hex("#FFC870"), 150);
        c.Add(G.Flame(128, 128, 62, 92), Pal.Candle).Then(() => c.Fill(G.Flame(128, 128, 28, 50), Color.White));
        c.Flush();
        c.Add(G.RR(92, 212, 72, 22, 8), Pal.Wood);
        c.Add(G.Soft(24, 124, 232, 124, 214, 196, 170, 222, 86, 222, 42, 196), Pal.Wood).Then(() =>
        {
            c.Stroke(Pal.Wood.D, 5, 40, 156, 128, 170, 216, 156);
        });
        c.Add(G.Ell(128, 126, 106, 16), Pal.DarkLeather).Thin();
        c.Flush();
    }

    // Time on credit: an hourglass with a coin on the scale
    static void BorrowedTime(Ic c)
    {
        Tiles.Candle(c);
        c.Glow(128, 128, 112, Pal.Hex("#FFC870"), 120);
        Hourglass(c, 112, 124, 1.25f, Pal.Bronze, Pal.Hex("#D92F45"));
        c.Flush();
        c.Add(G.Circle(194, 188, 38), Pal.Gold).Then(() =>
        {
            c.Fill(G.Ring(194, 188, 28, 23), Pal.Gold.D);
            c.Fill(G.Star(194, 188, 15, 6, 4, 0), Pal.Gold.D);
        });
        c.Flush();
    }

    // A spiral that goes down further than it should
    static void Bottomless(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 128, 116, Pal.Hex("#3FC4B4"), 130);
        c.Add(G.Circle(128, 128, 104), Pal.Stone);
        c.Flush();
        c.Add(G.Circle(128, 128, 82), new Pal("#3FC4B4", "#1C7F7C", "#0E3C44")).Then(() =>
        {
            c.Stroke(Pal.Hex("#A8F0E4"), 11, 208, 128, 180, 62, 110, 56, 66, 110, 86, 178, 148, 186, 176, 134, 146, 98, 112, 112, 118, 142, 134, 140);
            c.Fill(G.Circle(132, 134, 10), Ic.Ink);
        }).Thin();
        c.Flush();
    }

    // A heart snapped down the middle
    static void BrokenPromises(Ic c)
    {
        Tiles.Rose(c);
        c.Glow(128, 128, 116, Pal.Hex("#F2789C"), 120);
        c.Add(G.Heart(128, 132, 92), Pal.Rose).Then(() =>
        {
            c.Gloss(82, 86, 20, 11, -35, 200);
            c.Fill(G.Poly(116, 20, 150, 20, 124, 76, 160, 118, 118, 158, 146, 196, 128, 250, 120, 196, 90, 158, 130, 118, 96, 76), Pal.Hex("#2A1420"));
        });
        c.Flush();
    }

    // Blood turned to ice: a frozen drop
    static void ColdBlooded(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(128, 132, 112, Pal.Hex("#7FE6FF"), 130);
        c.Add(G.Drop(128, 146, 72), Pal.Blood).Then(() =>
        {
            c.Fill(G.Soft(40, 240, 40, 130, 92, 150, 128, 120, 170, 152, 220, 124, 220, 240), Pal.Hex("#BFF3FF"));
            c.Stroke(Pal.Hex("#5FBEDF"), 5, 92, 190, 128, 158, 164, 190);
            c.Stroke(Pal.Hex("#5FBEDF"), 5, 128, 158, 128, 214);
            c.Gloss(100, 104, 11, 20, 15, 190);
        });
        c.Flush();
        c.Spark(206, 66, 16, Color.White);
        c.Spark(50, 74, 12, Color.White);
    }

    // Spin and answer back
    static void CounterStrike(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 128, 112, Pal.Hex("#FFC870"), 100);
        c.Add(G.Arc(128, 128, 90, 26, 200, 130), Pal.Candle);
        c.Add(G.Poly(218, 148, 240, 92, 182, 104), Pal.Candle);
        c.Add(G.Arc(128, 128, 90, 26, 20, 130), Pal.Candle);
        c.Add(G.Poly(38, 108, 16, 164, 74, 152), Pal.Candle);
        c.Flush();
        Knife(c, 128, 120, .85f, Pal.Steel, Pal.Bronze);
    }

    // One nozzle, three fires
    static void Crossfire(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(128, 96, 120, Pal.Hex("#FF7A1A"), 170);
        c.Add(G.Rot(G.Flame(128, 180, 76, 130), -52, 128, 200), Pal.Fire).Then(() => c.Fill(G.Rot(G.Flame(128, 180, 34, 76), -52, 128, 200), Pal.Hex("#FFF4C0")));
        c.Add(G.Rot(G.Flame(128, 180, 76, 130), 52, 128, 200), Pal.Fire).Then(() => c.Fill(G.Rot(G.Flame(128, 180, 34, 76), 52, 128, 200), Pal.Hex("#FFF4C0")));
        c.Add(G.Flame(128, 176, 90, 158), Pal.Fire).Then(() => c.Fill(G.Flame(128, 176, 42, 96), Pal.Hex("#FFF4C0")));
        c.Flush();
        c.Add(G.Round(10, 92, 176, 164, 176, 176, 238, 80, 238), Pal.Iron).Then(() => c.Ink2(5, 88, 200, 168, 200));
        c.Flush();
    }

    // A door left ajar, and something in the dark behind it
    static void DeathsDoor(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(128, 128, 112, Pal.Hex("#D92F45"), 120);
        c.Add(G.Round(60, 40, 240, 40, 72, 216, 72, 216, 240), Pal.Stone);
        c.Flush();
        c.Add(G.Round(44, 62, 234, 62, 86, 194, 86, 194, 234), new Pal("#2A2030", "#1A1218", "#100C12")).Then(() =>
        {
            c.Fill(G.Circle(144, 150, 30), Pal.Hex("#E6D8B8"));
            c.Fill(G.Ell(133, 150, 8, 9), Ic.Ink);
            c.Fill(G.Ell(155, 150, 8, 9), Ic.Ink);
            c.Fill(G.RR(130, 170, 28, 20, 6), Pal.Hex("#E6D8B8"));
        }).Thin();
        c.Add(G.Poly(62, 86, 112, 66, 112, 248, 62, 234), Pal.Wood).Then(() =>
        {
            c.Stroke(Pal.Wood.D, 5, 80, 80, 80, 240);
            c.Stroke(Pal.Wood.D, 5, 96, 76, 96, 244);
            c.Fill(G.RR(58, 112, 54, 10, 3), Pal.Iron.D);
            c.Fill(G.RR(58, 196, 54, 10, 3), Pal.Iron.D);
        });
        c.Flush();
    }

    // The ground splits and rings out
    static void Earthshaker(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 150, 116, Pal.Hex("#FFC870"), 110);
        c.Add(G.EllRing(128, 150, 118, 62, 13), Pal.Candle).Thin();
        c.Add(G.EllRing(128, 150, 78, 40, 12), Pal.Candle).Thin();
        Small(c);
        c.Add(G.Poly(60, 240, 104, 150, 92, 122, 128, 34, 164, 122, 152, 150, 196, 240), Pal.Stone).Flat();
        c.Add(G.Poly(112, 240, 122, 170, 112, 150, 132, 82, 142, 150, 132, 172, 148, 240), new Pal("#FFE85A", "#FF8A2A", "#D2451A")).Thin();
        c.Flush();
    }

    // A blade with two ghosts of itself
    static void EchoingBlade(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 116, Pal.Hex("#7FE6FF"), 120);
        var echo = new Pal("#5C88E0", "#3F5CB8", "#2A3C80");
        c.Xf(70, 128, 128, 1f);
        c.Add(G.Poly(128, 10, 150, 60, 150, 180, 106, 180, 106, 60), echo).Flat().Thin();
        Small(c);
        c.Xf(20, 128, 128, 1f);
        c.Add(G.Poly(128, 10, 150, 60, 150, 180, 106, 180, 106, 60), echo).Flat().Thin();
        Small(c);
        c.NoXf();
        Knife(c, 128, 118, 1.1f, Pal.Mirror, Pal.Gold);
    }

    // Nothing escapes: rings falling into a dark center
    static void EventHorizon(Ic c)
    {
        Tiles.Arcane(c);
        c.Glow(128, 128, 118, Pal.Hex("#B57BFF"), 170);
        c.Add(G.Arc(128, 128, 96, 18, 150, 150), Pal.Arcane).Thin();
        c.Add(G.Arc(128, 128, 96, 18, 330, 150), Pal.Arcane).Thin();
        c.Add(G.Arc(128, 128, 66, 14, 60, 130), Pal.Mirror).Thin();
        c.Add(G.Arc(128, 128, 66, 14, 240, 130), Pal.Mirror).Thin();
        Small(c);
        c.Add(G.Circle(128, 128, 40), new Pal("#2E2448", "#14101E", "#0C0A12")).Thin();
        c.Flush();
        c.Add(G.Circle(216, 52, 14), Pal.Fire).Thin();
        c.Add(G.Circle(46, 204, 12), Pal.Frost).Thin();
        Small(c);
    }

    static void Frostbound(Ic c)
    {
        Tiles.Frost(c);
        ElementRing(c, Pal.Frost, "#7FE6FF");
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f;
            c.Add(G.Rot(G.RR(120, 54, 16, 74, 6), a, 128, 128), Pal.Frost).Thin();
            c.Add(G.Rot(G.Rope(11, 128, 88, 106, 68), a, 128, 128), Pal.Frost).Thin();
            c.Add(G.Rot(G.Rope(11, 128, 88, 150, 68), a, 128, 128), Pal.Frost).Thin();
        }
        Small(c);
    }

    // The eye that sees the free cast coming
    static void Insight(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 116, Pal.Hex("#FFE08A"), 150);
        for (int i = 0; i < 8; i++)
            c.Add(G.Rot(G.Poly(116, 56, 128, 14, 140, 56), i * 45, 128, 128), Pal.Summon).Thin();
        Small(c);
        c.Add(G.Soft(22, 128, 80, 66, 176, 66, 234, 128, 176, 190, 80, 190), Pal.Bone).Then(() =>
        {
            c.Fill(G.Circle(128, 128, 46), Pal.Hex("#3F74E0"));
            c.Fill(G.Circle(128, 128, 22), Ic.Ink);
            c.Gloss(112, 112, 12, 8, -30, 240);
        });
        c.Flush();
    }

    // A sword planted in the ground: this far and no further
    static void LastStand(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(128, 112, 112, Pal.Hex("#FFC870"), 120);
        c.Add(G.Poly(104, 40, 152, 40, 150, 188, 128, 214, 106, 188), Pal.Steel).Then(() =>
            c.Fill(G.Poly(128, 36, 156, 36, 156, 220, 128, 220), Pal.Steel.D, 120));
        c.Add(G.RR(116, 8, 24, 30, 6), Pal.DarkLeather).Thin();
        c.Add(G.RR(70, 30, 116, 22, 10), Pal.Gold);
        c.Flush();
        c.Add(G.Heart(128, 114, 26), Pal.Blood).Then(() => c.Gloss(118, 102, 6, 4, -35, 220)).Thin();
        Small(c);
        c.Add(G.Soft(14, 240, 26, 196, 84, 182, 128, 192, 172, 182, 230, 196, 242, 240), Pal.DarkLeather);
        c.Flush();
    }

    // The further it flies, the harder it lands
    static void LongShot(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(190, 66, 96, Pal.Hex("#FF8C94"), 120);
        c.Add(G.Ring(190, 66, 54, 30), Pal.Ruby).Thin();
        c.Add(G.Circle(190, 66, 15), Pal.Mirror).Thin();
        c.Flush();
        c.Add(G.Rope(9, 28, 228, 48, 208), Pal.Mirror).Flat().Thin();
        c.Add(G.Rope(9, 64, 192, 84, 172), Pal.Mirror).Flat().Thin();
        Small(c);
        c.Xf(45, 150, 106, 1f);
        c.Add(G.RR(144, 86, 12, 100, 4), Pal.Wood).Thin();
        c.Add(G.Poly(150, 50, 172, 94, 128, 94), Pal.Silver);
        c.Add(G.Poly(144, 156, 122, 196, 144, 186), Pal.Rose).Thin();
        c.Add(G.Poly(156, 156, 178, 196, 156, 186), Pal.Rose).Thin();
        c.NoXf();
    }

    // Keep going and it keeps building
    static void Momentum(Ic c)
    {
        Tiles.Rose(c);
        c.Glow(150, 128, 112, Pal.Hex("#FF8A2A"), 110);
        c.Add(G.Poly(14, 62, 44, 62, 104, 128, 44, 194, 14, 194, 72, 128), new Pal("#FFE0C0", "#F2A07A", "#B8604C"));
        c.Add(G.Poly(76, 44, 116, 44, 188, 128, 116, 212, 76, 212, 144, 128), new Pal("#FFC6A0", "#F2789C", "#B83F6C"));
        c.Add(G.Poly(146, 26, 196, 26, 246, 128, 196, 230, 146, 230, 204, 128), Pal.Fire);
        c.Flush();
    }

    // More than was needed, and the rest goes next door
    static void Overkill(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(112, 128, 116, Pal.Hex("#FF8A2A"), 170);
        c.Add(G.Star(108, 128, 100, 56, 10, 8), Pal.Fire).Then(() => c.Fill(G.Star(108, 128, 64, 36, 10, 8), Pal.Hex("#FFF4C0")));
        c.Flush();
        c.Add(G.RR(90, 146, 36, 22, 6), Pal.Bone).Thin();
        c.Add(G.Circle(108, 122, 34), Pal.Bone).Then(() =>
        {
            c.Fill(G.Ell(95, 124, 10, 11), Ic.Ink);
            c.Fill(G.Ell(121, 124, 10, 11), Ic.Ink);
        }).Thin();
        c.Flush();
        c.Add(G.Poly(186, 104, 214, 104, 214, 82, 250, 128, 214, 174, 214, 152, 186, 152), Pal.Candle);
        c.Flush();
    }

    // Slow and steady: a snail
    static void Patience(Ic c)
    {
        Tiles.Candle(c);
        c.Glow(128, 128, 112, Pal.Hex("#FFC870"), 120);
        var skin = new Pal("#FFE8C0", "#E8C080", "#AC8450");
        c.Add(G.Rope(9, 40, 130, 28, 92), skin).Thin();
        c.Add(G.Rope(9, 58, 130, 66, 92), skin).Thin();
        c.Add(G.Circle(28, 88, 9), skin).Thin();
        c.Add(G.Circle(66, 88, 9), skin).Thin();
        Small(c);
        c.Add(G.Soft(20, 214, 26, 130, 72, 126, 96, 180, 180, 180, 244, 214), skin).Then(() =>
        {
            c.Fill(G.Circle(42, 158, 6), Ic.Ink);
            c.Ink2(4, 30, 182, 42, 188, 54, 184);
        });
        c.Add(G.Circle(152, 134, 76), Pal.Rose).Then(() =>
        {
            c.Stroke(Pal.Rose.D, 10, 226, 136, 190, 76, 118, 80, 96, 140, 130, 186, 178, 170, 186, 126, 152, 110, 134, 134, 150, 148);
            c.Gloss(112, 90, 15, 9, -35, 150);
        });
        c.Flush();
    }

    static void Pyromancy(Ic c)
    {
        Tiles.Fire(c);
        ElementRing(c, Pal.Fire, "#FF7A1A");
        c.Add(G.Flame(128, 204, 118, 154), Pal.Fire).Then(() => c.Fill(G.Flame(128, 204, 58, 88), Pal.Hex("#FFF4C0")));
        c.Flush();
    }

    // Glass plates that take the hit for you
    static void Refractionist(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 116, Pal.Hex("#E9F1FA"), 130);
        c.Add(G.Rope(9, 16, 196, 84, 138), Pal.Fire).Flat().Thin();
        c.Add(G.Rope(9, 172, 138, 240, 196), Pal.Fire).Flat().Thin();
        Small(c);
        c.Add(G.Poly(128, 14, 206, 72, 180, 214, 128, 242, 76, 214, 50, 72), Pal.Mirror).Then(() =>
        {
            c.Fill(G.Poly(128, 14, 206, 72, 128, 110), Pal.Hex("#FFFFFF"), 220);
            c.Fill(G.Poly(128, 110, 206, 72, 180, 214, 128, 242), Pal.Hex("#7FE6FF"), 120);
            c.Fill(G.Poly(128, 110, 50, 72, 76, 214, 128, 242), Pal.Hex("#C9A2FF"), 110);
            c.Ink2(4, 128, 110, 128, 242);
            c.Ink2(4, 128, 110, 50, 72);
            c.Ink2(4, 128, 110, 206, 72);
        });
        c.Flush();
        c.Spark(212, 38, 15, Color.White);
    }

    // The breeze picks you up when you're low
    static void SecondWind(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 128, 112, Pal.Hex("#7CF0DC"), 120);
        var wind = new Pal("#FFFFFF", "#B4F4E8", "#44B4A8");
        c.Add(G.Rope(19, 18, 94, 110, 94, 164, 84, 170, 50, 136, 44), wind);
        c.Add(G.Rope(19, 34, 136, 150, 136, 214, 122, 236, 92, 216, 66), wind);
        c.Add(G.Rope(19, 18, 178, 98, 178, 128, 196, 112, 224, 84, 214), wind);
        c.Flush();
        c.Add(G.Heart(182, 188, 42), Pal.Rose).Then(() => c.Gloss(166, 170, 9, 5, -35, 220));
        c.Flush();
    }

    // A frozen shell bursting into flying shards
    static void Shatter(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(100, 150, 112, Pal.Hex("#7FE6FF"), 140);
        c.Add(G.Poly(158, 96, 196, 40, 214, 88), Pal.Frost);
        c.Add(G.Poly(170, 130, 240, 112, 214, 158), Pal.Frost);
        c.Add(G.Poly(150, 62, 142, 16, 178, 34), Pal.Frost).Thin();
        c.Add(G.Poly(174, 168, 228, 196, 182, 208), Pal.Frost).Thin();
        c.Flush();
        c.Add(G.Poly(30, 120, 76, 66, 130, 70, 112, 100, 156, 110, 118, 136, 160, 164, 112, 176, 124, 226, 62, 226, 22, 182), Pal.Frost).Then(() =>
        {
            c.Ink2(5, 60, 110, 84, 146, 58, 176, 84, 200);
            c.Ink2(4, 84, 146, 116, 136);
            c.Gloss(58, 96, 14, 8, -45, 220);
        });
        c.Flush();
    }

    // A fork that turns one shot into two
    static void Slingshot(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 90, 112, Pal.Hex("#FFC870"), 110);
        c.Add(G.Rope(30, 128, 240, 128, 158), Pal.Wood);
        c.Add(G.Rope(26, 128, 168, 78, 128, 62, 84), Pal.Wood);
        c.Add(G.Rope(26, 128, 168, 178, 128, 194, 84), Pal.Wood);
        c.Add(G.RR(106, 194, 44, 18, 8), Pal.DarkLeather).Thin();
        c.Flush();
        c.Add(G.Rope(9, 62, 84, 128, 104, 194, 84), Pal.Rose).Thin();
        Small(c);
        c.Add(G.Rot(G.Drop(92, 36, 18), 26, 92, 36), Pal.Fire).Thin();
        c.Add(G.Rot(G.Drop(164, 36, 18), -26, 164, 36), Pal.Fire).Thin();
        c.Flush();
    }

    // A small sack with a soul peeking out
    static void SoulThief(Ic c)
    {
        Tiles.Shadow(c);
        c.Glow(128, 96, 112, Pal.Hex("#7CF0DC"), 150);
        var spirit = new Pal("#FFFFFF", "#B4F4E8", "#44B4A8");
        c.Add(G.Soft(128, 8, 142, 40, 186, 62, 186, 124, 70, 124, 70, 62, 114, 40), spirit).Then(() =>
        {
            c.Fill(G.Ell(106, 86, 10, 14), Ic.Ink);
            c.Fill(G.Ell(150, 86, 10, 14), Ic.Ink);
            c.Ink2(5, 118, 110, 128, 104, 138, 110);
        });
        c.Flush();
        c.Add(G.Soft(30, 240, 30, 170, 72, 118, 184, 118, 226, 170, 226, 240), Pal.Plum).Then(() =>
        {
            c.Stroke(Pal.Plum.D, 5, 76, 150, 66, 220);
            c.Stroke(Pal.Plum.D, 5, 176, 150, 188, 220);
        });
        c.Add(G.Rope(14, 66, 124, 128, 136, 190, 124), Pal.Gold);
        c.Flush();
    }

    // The air around you bites
    static void StaticField(Ic c)
    {
        Tiles.Lightning(c);
        c.Glow(128, 128, 116, Pal.Hex("#FFE85A"), 130);
        for (int i = 0; i < 8; i++)
            c.Add(G.Arc(128, 128, 96, 16, i * 45 + 8, 24), Pal.Lightning).Thin();
        Small(c);
        c.Add(G.Bolt(72, 44, 112, 168), Pal.Lightning);
        c.Flush();
    }

    // A cozy kettle: fire underneath, cold poured in
    static void Steam(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 150, 116, Pal.Hex("#FFC870"), 120);
        c.Add(G.Circle(212, 72, 22), Pal.Mirror).Thin();
        c.Add(G.Circle(188, 42, 15), Pal.Mirror).Thin();
        c.Add(G.Circle(226, 34, 11), Pal.Mirror).Thin();
        Small(c);
        c.Add(G.Rope(22, 60, 86, 22, 134, 58, 182), Pal.Iron);
        c.Add(G.Soft(170, 148, 190, 118, 222, 92, 228, 110, 208, 138, 190, 196), Pal.Iron);
        c.Add(G.Soft(44, 214, 42, 134, 84, 92, 172, 92, 214, 134, 212, 214), Pal.Iron).Then(() =>
        {
            c.Gloss(80, 130, 14, 26, 25, 130);
            c.Fill(G.Flame(128, 214, 74, 66), Pal.Hex("#FF8A2A"));
            c.Fill(G.Flame(128, 214, 34, 36), Pal.Hex("#FFE85A"));
        });
        c.Add(G.Ell(128, 92, 52, 12), Pal.Steel).Thin();
        c.Add(G.Circle(128, 72, 12), Pal.Steel).Thin();
        c.Flush();
    }

    static void Stormborn(Ic c)
    {
        Tiles.Lightning(c);
        ElementRing(c, Pal.Lightning, "#FFE85A");
        c.Add(G.Bolt(78, 50, 100, 156), Pal.Lightning);
        c.Flush();
    }

    // Keep channeling and the sky answers
    static void Stormcall(Ic c)
    {
        Tiles.Lightning(c);
        c.Glow(128, 150, 112, Pal.Hex("#FFE85A"), 130);
        c.Add(G.Bolt(82, 106, 96, 138), Pal.Lightning);
        c.Flush();
        var cloud = new Pal("#D4D8EC", "#8E94C0", "#50568C");
        c.Add(G.Circle(70, 94, 46), cloud);
        c.Add(G.Circle(130, 72, 58), cloud);
        c.Add(G.Circle(190, 96, 44), cloud);
        c.Add(G.RR(50, 90, 158, 50, 22), cloud);
        c.Flush();
    }

    // Three notches in a split plate
    static void Sunder(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 128, 112, Pal.Hex("#FF8A2A"), 90);
        c.Add(G.Round(30, 34, 22, 222, 22, 222, 150, 128, 244, 34, 150), Pal.Steel).Then(() =>
        {
            c.Ink2(11, 146, 18, 122, 72, 148, 112, 112, 156, 134, 196, 124, 244);
            c.Stroke(Pal.Hex("#FF8A2A"), 4.5f, 146, 18, 122, 72, 148, 112, 112, 156, 134, 196, 124, 244);
            c.Ink2(7, 122, 72, 76, 88);
            c.Ink2(7, 148, 112, 196, 126);
        });
        c.Flush();
    }

    // Green footprints fading behind you
    static void ToxicTrail(Ic c)
    {
        Tiles.Poison(c);
        c.Glow(100, 170, 116, Pal.Hex("#8CFF3A"), 130);
        Step(c, 62, 192, 18, .8f);
        Step(c, 122, 128, -6, 1f);
        Step(c, 192, 76, 22, 1.2f);
        c.Flush();
        c.Add(G.Drop(58, 104, 13), Pal.Poison).Thin();
        c.Add(G.Drop(196, 192, 15), Pal.Poison).Thin();
        Small(c);
    }

    static void Step(Ic c, float x, float y, float rot, float s)
    {
        c.Add(G.Rot(G.Ell(x, y + 6 * s, 22 * s, 30 * s), rot + 20, x, y), Pal.Poison);
        for (int i = 0; i < 4; i++)
            c.Add(G.Rot(G.Circle(x - 25 * s + i * 17 * s, y - 34 * s - (i == 1 || i == 2 ? 6 * s : 0), 7.5f * s), rot + 20, x, y), Pal.Poison).Thin();
    }

    // A curling wave that drags everything toward it
    static void Undertow(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 128, 116, Pal.Hex("#3FC4B4"), 130);
        c.Add(G.Soft(10, 238, 24, 170, 62, 76, 150, 26, 232, 62, 240, 118, 200, 96, 160, 104, 156, 146, 198, 176, 246, 182, 246, 238),
              new Pal("#A8F0E4", "#3FC4B4", "#1C7F7C")).Then(() =>
        {
            c.Stroke(Color.White, 9, 62, 170, 86, 104, 142, 62, 196, 68);
            c.Stroke(Pal.Hex("#1C7F7C"), 6, 60, 214, 120, 198, 190, 214);
        });
        c.Flush();
    }

    // Arrows break on the front line
    static void Vanguard(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 112, Pal.Hex("#FFE08A"), 100);
        c.Add(G.Soft(52, 30, 204, 30, 212, 160, 128, 240, 44, 160), Pal.Gold);
        c.Flush();
        c.Add(G.Soft(74, 50, 182, 50, 188, 150, 128, 212, 68, 150), Pal.Sapphire).Then(() =>
        {
            c.Fill(G.Poly(128, 86, 200, 44, 200, 80, 128, 124, 56, 80, 56, 44), Pal.Hex("#FFEBA0"));
            c.Fill(G.Poly(128, 138, 200, 96, 200, 130, 128, 174, 56, 130, 56, 96), Pal.Hex("#FFEBA0"));
        }).Thin();
        c.Flush();
        c.Add(G.Rot(G.RR(10, 66, 40, 10, 4), 24, 30, 70), Pal.Wood).Thin();
        c.Add(G.Rot(G.RR(206, 108, 40, 10, 4), -24, 226, 112), Pal.Wood).Thin();
        Small(c);
    }

    static void Venomous(Ic c)
    {
        Tiles.Poison(c);
        ElementRing(c, Pal.Poison, "#8CFF3A");
        c.Add(G.Drop(128, 146, 48), Pal.Poison).Then(() => c.Gloss(110, 140, 10, 16, 15, 220));
        c.Flush();
    }

    // A lantern left burning: someone's keeping watch
    static void Vigil(Ic c)
    {
        Tiles.Candle(c);
        c.Glow(128, 140, 120, Pal.Hex("#FFC870"), 210);
        c.Add(G.Arc(128, 48, 30, 12, 180, 180), Pal.Iron).Thin();
        c.Add(G.Round(10, 76, 66, 180, 66, 164, 40, 92, 40), Pal.Iron);
        c.Add(G.Round(12, 72, 214, 184, 214, 190, 240, 66, 240), Pal.Iron);
        c.Add(G.Round(16, 84, 66, 172, 66, 178, 216, 78, 216), new Pal("#FFF6D0", "#FFD88A", "#F0A84A")).Then(() =>
        {
            c.Fill(G.Flame(128, 200, 58, 100), Pal.Hex("#FF8A2A"));
            c.Fill(G.Flame(128, 200, 28, 58), Pal.Hex("#FFF4C0"));
        });
        c.Flush();
        c.Ink2(6, 104, 68, 100, 214);
        c.Ink2(6, 152, 68, 156, 214);
    }

    // One fire becomes three
    static void Wildfire(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(128, 150, 118, Pal.Hex("#FF7A1A"), 170);
        c.Add(G.Flame(62, 226, 86, 112), Pal.Fire).Then(() => c.Fill(G.Flame(62, 226, 38, 62), Pal.Hex("#FFF4C0")));
        c.Add(G.Flame(194, 226, 86, 112), Pal.Fire).Then(() => c.Fill(G.Flame(194, 226, 38, 62), Pal.Hex("#FFF4C0")));
        c.Add(G.Flame(128, 232, 120, 198), Pal.Fire).Then(() => c.Fill(G.Flame(128, 232, 56, 118), Pal.Hex("#FFF4C0")));
        c.Flush();
        c.Add(G.Soft(30, 240, 76, 222, 180, 222, 226, 240), Pal.DarkLeather).Thin();
        Small(c);
    }

    // Touch it and it bites back with fire
    static void Wyrmfire(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(90, 150, 116, Pal.Hex("#FF7A1A"), 160);
        c.Add(G.Rot(G.Flame(58, 222, 90, 150), -74, 92, 170), Pal.Fire).Then(() => c.Fill(G.Rot(G.Flame(58, 222, 42, 86), -74, 92, 170), Pal.Hex("#FFF4C0")));
        c.Flush();
        c.Add(G.Poly(178, 72, 234, 18, 216, 84), Pal.Bone);
        c.Add(G.Poly(150, 62, 178, 12, 186, 74), Pal.Bone);
        c.Add(G.Soft(250, 240, 190, 240, 170, 186, 104, 186, 78, 150, 92, 104, 150, 64, 226, 92, 250, 150), Pal.Ruby).Then(() =>
        {
            c.Fill(G.Ell(160, 112, 15, 13), Pal.Hex("#FFE85A"));
            c.Fill(G.Ell(162, 112, 5, 11), Ic.Ink);
            c.Ink2(6, 86, 152, 124, 156, 170, 150);
            c.Fill(G.Circle(100, 132, 5), Ic.Ink);
        });
        c.Flush();
    }
}
