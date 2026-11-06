// The 24 original skill and passive icons, redrawn in the tile style. Each keeps its original idea, file name and GUID.
using System;
using System.Collections.Generic;
using System.Drawing;

public static class RedrawnIcons
{
    const string Dir = "SkillIcons";

    static void Reg(string name, Action<Ic> draw) { IconRegistry.Reg(Dir, name, draw); }

    public static void Register()
    {
        Reg("BallLightning", BallLightning); Reg("Barrage", Barrage); Reg("CaptainsShield", CaptainsShield); Reg("Dash", Dash);
        Reg("Fireball", Fireball); Reg("FistsOfThunder", FistsOfThunder); Reg("Flamethrower", Flamethrower); Reg("FrozenOrb", FrozenOrb);
        Reg("HookshotSlash", HookshotSlash); Reg("Invulnerability", Invulnerability); Reg("Shadowstep", Shadowstep);
        Reg("SonicBall", SonicBall); Reg("Teleport", Teleport); Reg("Tornado", Tornado); Reg("Whirlwind", Whirlwind);
        Reg("Balance", Balance); Reg("Ghost", Ghost); Reg("Golem", Golem); Reg("Huskar", Huskar); Reg("Luminosity", Luminosity);
        Reg("Medusa", Medusa); Reg("PixieDust", PixieDust); Reg("Quickening", Quickening); Reg("Recharge", Recharge);
    }

    static void Small(Ic c)
    {
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
    }

    /// <summary>A sword standing on its pommel at (x, y = blade middle), for use under Ic.Xf.</summary>
    static void Sword(Ic c, float x, float y, Pal blade, Pal hilt)
    {
        c.Add(G.Poly(x, y - 104, x + 24, y - 56, x + 24, y + 40, x - 24, y + 40, x - 24, y - 56), blade).Then(() =>
            c.Fill(G.Poly(x, y - 104, x + 24, y - 56, x + 24, y + 40, x, y + 40), blade.D, 120));
        c.Add(G.RR(x - 9, y + 50, 18, 44, 6), Pal.DarkLeather).Thin();
        c.Add(G.RR(x - 44, y + 36, 88, 20, 9), hilt);
        c.Add(G.Circle(x, y + 100, 12), hilt);
    }

    /// <summary>A sword trail: an arc from start, sweep degrees clockwise, pointed at both ends and width thick at the middle.</summary>
    static System.Drawing.Drawing2D.GraphicsPath Slash(float x, float y, float r, float start, float sweep, float width)
    {
        const int steps = 24;
        var xy = new List<float>();
        for (int i = 0; i <= steps; i++)
        {
            double a = (start + sweep * i / steps) * Math.PI / 180;
            xy.Add(x + (float)Math.Cos(a) * r); xy.Add(y + (float)Math.Sin(a) * r);
        }
        for (int i = steps - 1; i > 0; i--)
        {
            double a = (start + sweep * i / steps) * Math.PI / 180;
            float inner = r - width * (float)Math.Sin(Math.PI * i / steps);
            xy.Add(x + (float)Math.Cos(a) * inner); xy.Add(y + (float)Math.Sin(a) * inner);
        }
        return G.Poly(xy.ToArray());
    }

    static void ClockHands(Ic c, float x, float y, float r)
    {
        c.Ink2(r * .16f, x, y, x, y - r * .68f);
        c.Ink2(r * .16f, x, y, x + r * .48f, y + r * .24f);
        c.Fill(G.Circle(x, y, r * .12f), Ic.Ink);
    }

    // ---------- Skills ----------

    static void BallLightning(Ic c)
    {
        Tiles.Lightning(c);
        c.Glow(128, 128, 118, Pal.Hex("#FFE85A"), 150);
        c.Add(G.Circle(128, 128, 84), new Pal("#D0F6FF", "#4CC4E8", "#2478B8")).Then(() => c.Gloss(94, 90, 20, 12, -35, 210));
        c.Flush();
        c.Add(G.Bolt(82, 58, 96, 140), Pal.Lightning);
        c.Flush();
        c.Add(G.Bolt(22, 20, 40, 52), Pal.Lightning).Thin();
        c.Add(G.Bolt(196, 186, 40, 52), Pal.Lightning).Thin();
        Small(c);
    }

    static void Barrage(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 128, 112, Pal.Hex("#FF8C94"), 90);
        c.Xf(45, 128, 128, 1f);
        float[] xs = { 66, 128, 190 }, ys = { 34, 6, 34 };
        for (int i = 0; i < 3; i++)
        {
            float x = xs[i], y = ys[i];
            c.Add(G.Poly(x - 8, y + 146, x - 30, y + 196, x - 8, y + 184), Pal.Ruby).Thin();
            c.Add(G.Poly(x + 8, y + 146, x + 30, y + 196, x + 8, y + 184), Pal.Ruby).Thin();
            c.Add(G.RR(x - 10, y + 42, 20, 146, 7), Pal.Wood);
            c.Add(G.Poly(x, y, x + 24, y + 54, x - 24, y + 54), Pal.Steel);
        }
        c.NoXf();
    }

    static void CaptainsShield(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(146, 128, 110, Pal.Hex("#9CC4FF"), 110);
        c.Add(G.RR(12, 76, 44, 14, 7), Pal.Mirror).Thin();
        c.Add(G.RR(4 + 8, 120, 30, 14, 7), Pal.Mirror).Thin();
        c.Add(G.RR(12, 164, 44, 14, 7), Pal.Mirror).Thin();
        Small(c);
        c.Add(G.Circle(150, 128, 92), Pal.Silver);
        c.Flush();
        c.Add(G.Circle(150, 128, 70), Pal.Sapphire).Thin();
        c.Add(G.Circle(150, 128, 47), Pal.Silver).Thin();
        c.Add(G.Circle(150, 128, 25), Pal.Sapphire).Then(() => c.Gloss(141, 118, 7, 4, -35, 220)).Thin();
        c.Flush();
    }

    static void Dash(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(150, 128, 110, Pal.Hex("#FFC870"), 120);
        c.Add(G.Poly(26, 36, 80, 36, 152, 128, 80, 220, 26, 220, 98, 128), Pal.Candle);
        c.Add(G.Poly(104, 36, 158, 36, 230, 128, 158, 220, 104, 220, 176, 128), Pal.Gold);
        c.Flush();
    }

    static void Fireball(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(150, 150, 112, Pal.Hex("#FF7A1A"), 180);
        c.Add(G.Rot(G.Flame(150, 234, 168, 240), -45, 150, 150), Pal.Fire).Then(() =>
        {
            c.Fill(G.Rot(G.Flame(150, 234, 104, 150), -45, 150, 150), Pal.Hex("#FFD24A"));
            c.Fill(G.Circle(166, 166, 34), Pal.Hex("#FFF4C0"));
        });
        c.Flush();
    }

    static void FistsOfThunder(Ic c)
    {
        Tiles.Lightning(c);
        c.Glow(150, 110, 112, Pal.Hex("#FFE85A"), 130);
        c.Add(G.Round(10, 62, 172, 164, 172, 172, 236, 54, 236), Pal.Leather);
        for (int i = 0; i < 4; i++)
            c.Add(G.RR(26 + i * 38, 44, 40, 80, 15), Pal.Steel);
        c.Add(G.Round(20, 26, 88, 176, 88, 170, 186, 34, 186), Pal.Steel);
        c.Add(G.Round(14, 16, 124, 126, 116, 132, 148, 26, 164), Pal.Steel);
        c.Flush();
        c.Add(G.Bolt(138, 10, 112, 176), Pal.Lightning);
        c.Flush();
    }

    static void Flamethrower(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(160, 128, 110, Pal.Hex("#FF7A1A"), 170);
        c.Add(G.Rot(G.Flame(128, 216, 138, 200), 90, 128, 128), Pal.Fire).Then(() =>
            c.Fill(G.Rot(G.Flame(128, 216, 70, 116), 90, 128, 128), Pal.Hex("#FFF4C0")));
        c.Flush();
        c.Add(G.Round(8, 34, 150, 62, 150, 56, 214, 26, 214), Pal.Wood);
        c.Add(G.RR(10, 92, 66, 72, 12), Pal.Iron);
        c.Add(G.Round(8, 70, 104, 102, 94, 102, 162, 70, 152), Pal.Steel);
        c.Flush();
    }

    static void FrozenOrb(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(128, 128, 116, Pal.Hex("#7FE6FF"), 160);
        for (int i = 0; i < 4; i++)
            c.Add(G.Rot(G.Poly(112, 62, 128, 8, 144, 62), 45 + i * 90, 128, 128), Pal.Frost).Thin();
        c.Add(G.Circle(128, 128, 80), new Pal("#E6FBFF", "#6CD4EE", "#2C8CC0")).Then(() =>
        {
            c.Stroke(Color.White, 11, 200, 136, 170, 76, 104, 72, 70, 124, 96, 176, 150, 178, 168, 132, 138, 110, 114, 128, 128, 146);
        });
        c.Flush();
    }

    static void HookshotSlash(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(150, 110, 112, Pal.Hex("#7FE6FF"), 90);
        c.Add(Slash(122, 118, 108, -16, 150, 34), Pal.Mirror);
        c.Flush();
        for (int i = 0; i < 4; i++)
        {
            float x = 38 + i * 29, y = 218 - i * 29;
            c.Add(G.Rot(G.Ell(x, y, 22, i % 2 == 0 ? 12 : 8), -45, x, y), Pal.Steel).Thin();
        }
        Small(c);
        c.Xf(45, 172, 84, 1f);
        c.Add(G.RR(163, 76, 18, 62, 6), Pal.Steel);
        c.Add(G.Rope(13, 172, 92, 136, 76, 136, 34), Pal.Steel);
        c.Add(G.Rope(13, 172, 92, 208, 76, 208, 34), Pal.Steel);
        c.Add(G.Poly(172, 8, 192, 56, 172, 100, 152, 56), Pal.Silver);
        c.NoXf();
    }

    static void Invulnerability(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 118, Pal.Hex("#7FE6FF"), 130);
        c.Add(G.Circle(128, 128, 104), new Pal("#F2FDFF", "#A4E8F6", "#4CA8D0")).Then(() =>
        {
            c.Gloss(82, 78, 24, 12, -40, 220);
            c.Gloss(190, 186, 9, 5, -40, 170);
        });
        c.Flush();
        c.Add(G.Star(128, 134, 76, 34, 5, 0), Pal.Gold);
        c.Outer = 5;
        c.Flush();
        c.Outer = 7;
    }

    static void Shadowstep(Ic c)
    {
        Tiles.Shadow(c);
        c.Glow(128, 128, 112, Pal.Hex("#8050CC"), 130);
        // The shadow it steps out of, beside the blade
        c.Xf(45, 150, 170, 1f);
        c.Add(G.Poly(150, 66, 174, 114, 174, 210, 126, 210, 126, 114), Pal.Shadow).Flat().Thin();
        c.Add(G.RR(106, 206, 88, 20, 9), Pal.Shadow).Flat().Thin();
        Small(c);
        c.Xf(45, 116, 112, 1f);
        Sword(c, 116, 112, Pal.Arcane, Pal.Plum);
        c.NoXf();
    }

    static void SonicBall(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(150, 128, 110, Pal.Hex("#9CC4FF"), 110);
        c.Add(G.RR(10, 74, 50, 14, 7), Pal.Mirror).Thin();
        c.Add(G.RR(10, 121, 34, 14, 7), Pal.Mirror).Thin();
        c.Add(G.RR(10, 168, 50, 14, 7), Pal.Mirror).Thin();
        Small(c);
        c.Add(G.Circle(150, 128, 92), Pal.Sapphire).Then(() =>
        {
            c.Ink2(8, 116, 42, 92, 128, 116, 214);
            c.Ink2(8, 182, 42, 206, 128, 182, 214);
            c.Gloss(112, 80, 18, 10, -40, 190);
        });
        c.Flush();
    }

    static void Teleport(Ic c)
    {
        Tiles.Arcane(c);
        c.Glow(128, 128, 118, Pal.Hex("#B57BFF"), 170);
        c.Add(G.Circle(128, 128, 98), new Pal("#6E5AA0", "#3E2E66", "#20183A")).Then(() =>
        {
            c.Stroke(Pal.Hex("#C9A2FF"), 13, 218, 128, 190, 56, 104, 46, 50, 112, 78, 190, 152, 200, 186, 138, 150, 92, 104, 110, 108, 150, 136, 152);
        });
        c.Flush();
        c.Spark(218, 44, 16, Color.White);
        c.Spark(36, 212, 12, Color.White);
    }

    static void Tornado(Ic c)
    {
        Tiles.Poison(c);
        c.Glow(128, 120, 116, Pal.Hex("#8CFF3A"), 130);
        var wind = new Pal("#EEFFD8", "#B8EC90", "#62AE56");
        float[] y = { 42, 76, 110, 142, 172, 200, 224 };
        float[] rx = { 106, 90, 72, 56, 40, 27, 15 };
        float[] x = { 128, 138, 122, 134, 122, 130, 124 };
        for (int i = 6; i >= 0; i--)
            c.Add(G.Ell(x[i], y[i], rx[i], 21 - i * 1.4f), wind).Thin();
        c.Flush();
    }

    static void Whirlwind(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 128, 116, Pal.Hex("#7FE6FF"), 100);
        c.Add(Slash(128, 128, 108, 170, 150, 30), Pal.Mirror);
        c.Add(Slash(128, 128, 108, -10, 150, 30), Pal.Mirror);
        c.Flush();
        c.Xf(45, 128, 128, .8f);
        Sword(c, 128, 118, Pal.Steel, Pal.Bronze);
        c.NoXf();
    }

    // ---------- Passives ----------

    static void Balance(Ic c)
    {
        Tiles.Candle(c);
        c.Glow(128, 112, 112, Pal.Hex("#FFC870"), 130);
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? 60 : 196;
            c.Add(G.Rope(5, x, 66, x - 38, 150), Pal.Gold).Flat().NoLine();
            c.Add(G.Rope(5, x, 66, x + 38, 150), Pal.Gold).Flat().NoLine();
        }
        Small(c);
        c.Add(G.Soft(22, 150, 98, 150, 86, 186, 34, 186), Pal.Gold);
        c.Add(G.Soft(158, 150, 234, 150, 222, 186, 170, 186), Pal.Gold);
        c.Add(G.RR(116, 52, 24, 170, 8), Pal.Gold);
        c.Add(G.Round(12, 84, 214, 172, 214, 188, 240, 68, 240), Pal.Gold);
        c.Add(G.RR(34, 54, 188, 20, 10), Pal.Gold);
        c.Add(G.Circle(128, 40, 20), Pal.Gold);
        c.Flush();
    }

    static void Ghost(Ic c)
    {
        Tiles.Shadow(c);
        c.Glow(128, 120, 118, Pal.Hex("#E9F1FA"), 120);
        var xy = new List<float>();
        for (int i = 0; i <= 12; i++)
        {
            double a = Math.PI + i * Math.PI / 12;
            xy.Add(128 + (float)Math.Cos(a) * 84); xy.Add(110 + (float)Math.Sin(a) * 90);
        }
        float[] hem = { 212, 236, 184, 204, 156, 236, 128, 204, 100, 236, 72, 204, 44, 236 };
        xy.AddRange(hem);
        c.Add(G.Round(9, xy.ToArray()), Pal.Mirror).Then(() =>
        {
            c.Fill(G.Ell(102, 104, 13, 18), Ic.Ink);
            c.Fill(G.Ell(154, 104, 13, 18), Ic.Ink);
            c.Fill(G.Ell(128, 144, 11, 14), Ic.Ink);
            c.Fill(G.Ell(78, 130, 12, 7), Pal.Hex("#FFB4C4"));
            c.Fill(G.Ell(178, 130, 12, 7), Pal.Hex("#FFB4C4"));
        });
        c.Flush();
    }

    static void Golem(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 128, 112, Pal.Hex("#7FE6FF"), 80);
        c.Add(G.Round(22, 84, 18, 176, 22, 232, 84, 226, 176, 170, 236, 84, 234, 26, 176, 28, 80), Pal.Stone).Then(() =>
        {
            c.Ink2(5, 84, 26, 104, 54, 96, 72);
            c.Ink2(5, 214, 94, 192, 76);
            c.Ink2(5, 40, 180, 62, 172, 74, 192);
            c.Fill(G.Soft(66, 122, 112, 134, 108, 146, 70, 140), Pal.Hex("#7FE6FF"));
            c.Fill(G.Soft(190, 122, 144, 134, 148, 146, 186, 140), Pal.Hex("#7FE6FF"));
            c.Ink2(10, 58, 106, 114, 128);
            c.Ink2(10, 198, 106, 142, 128);
            c.Ink2(8, 98, 188, 128, 178, 158, 188);
        });
        c.Flush();
    }

    static void Huskar(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(118, 136, 112, Pal.Hex("#D92F45"), 130);
        c.Add(G.Drop(116, 146, 72), Pal.Blood).Then(() => c.Gloss(86, 138, 12, 22, 15, 200));
        c.Flush();
        c.Add(G.Flame(186, 238, 76, 104), Pal.Fire).Then(() => c.Fill(G.Flame(186, 238, 34, 58), Pal.Hex("#FFF4C0")));
        c.Outer = 6;
        c.Flush();
        c.Outer = 7;
    }

    static void Luminosity(Ic c)
    {
        Tiles.Candle(c);
        c.Glow(128, 128, 120, Pal.Hex("#FFD24A"), 170);
        c.Add(G.Star(128, 128, 114, 82, 12, 0), Pal.Summon);
        c.Flush();
        c.Add(G.Circle(128, 128, 68), Pal.Gold);
        c.Flush();
        c.Add(G.Circle(128, 128, 50), new Pal("#3E5A44", "#26382C", "#16221A")).Thin();
        c.Add(G.Round(5, 114, 88, 142, 88, 142, 114, 168, 114, 168, 142, 142, 142, 142, 168, 114, 168, 114, 142, 88, 142, 88, 114, 114, 114), Pal.Jade).Thin();
        c.Flush();
    }

    static void Medusa(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 128, 116, Pal.Hex("#3FC4B4"), 110);
        c.Add(G.Rope(30, 30, 226, 120, 236, 200, 206, 176, 150, 92, 136, 74, 90, 130, 62), Pal.Jade).Then(() =>
        {
            c.Stroke(Pal.Hex("#B0F0C4"), 5, 60, 226, 120, 228, 186, 204);
            c.Stroke(Pal.Hex("#B0F0C4"), 5, 164, 152, 100, 132);
        });
        c.Add(G.Soft(112, 72, 124, 36, 180, 32, 218, 56, 184, 84, 132, 92), Pal.Jade).Then(() =>
        {
            c.Fill(G.Ell(166, 52, 9, 8), Pal.Hex("#FFE85A"));
            c.Fill(G.Ell(167, 52, 3, 7), Ic.Ink);
        });
        c.Flush();
        c.Add(G.Bar(6, 214, 60, 238, 56, 248, 44), Pal.Ruby).Flat().NoLine();
        c.Add(G.Bar(6, 238, 56, 248, 68), Pal.Ruby).Flat().NoLine();
        c.Outer = 3;
        c.Flush();
        c.Outer = 7;
    }

    static void PixieDust(Ic c)
    {
        Tiles.Rose(c);
        c.Glow(118, 138, 112, Pal.Hex("#FFE08A"), 170);
        c.Add(G.Star(116, 140, 96, 24, 4, 0), Pal.Summon).Then(() => c.Gloss(108, 128, 8, 5, -35, 230));
        c.Flush();
        c.Add(G.Star(200, 62, 40, 11, 4, 0), Pal.Arcane).Thin();
        c.Add(G.Star(50, 54, 26, 8, 4, 0), Pal.Rose).Thin();
        c.Add(G.Star(206, 198, 28, 8, 4, 0), Pal.Teal).Thin();
        c.Add(G.Circle(150, 40, 9), Pal.Rose).Thin();
        c.Add(G.Circle(38, 214, 9), Pal.Arcane).Thin();
        c.Add(G.Circle(228, 126, 8), Pal.Summon).Thin();
        Small(c);
    }

    static void Quickening(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(118, 140, 110, Pal.Hex("#FFE85A"), 110);
        c.Add(G.Circle(114, 142, 92), Pal.Gold);
        c.Flush();
        c.Add(G.Circle(114, 142, 68), Pal.Mirror).Then(() => ClockHands(c, 114, 142, 58)).Thin();
        c.Flush();
        c.Add(G.Bolt(150, 10, 94, 146), Pal.Lightning);
        c.Flush();
    }

    static void Recharge(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 116, Pal.Hex("#FFE08A"), 100);
        c.Add(G.Arc(124, 132, 96, 26, 30, 300), Pal.Gold);
        c.Add(G.Poly(232, 124, 246, 58, 176, 96), Pal.Gold);
        c.Flush();
        c.Add(G.Circle(124, 132, 62), Pal.Sapphire).Then(() =>
        {
            c.Gloss(98, 106, 14, 8, -35, 130);
            c.Stroke(Color.White, 9, 124, 132, 124, 92);
            c.Stroke(Color.White, 9, 124, 132, 152, 148);
        });
        c.Flush();
    }
}
