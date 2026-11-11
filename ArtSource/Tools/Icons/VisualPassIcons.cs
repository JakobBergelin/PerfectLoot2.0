// Icons for assets that had none after the skill batches of November 2026: active skills, passives, the Execute power,
// status effects, classes and resources. Effects that come from a power or passive reuse that icon instead (set in Unity).
// C# 5 (Windows PowerShell's Add-Type): no $"", ?. or => members.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

public static class VisualPassIcons
{
    const string Skills = "SkillIcons";
    const string Powers = "PowerIcons";
    const string Effects = "EffectIcons";
    const string Classes = "ClassIcons";
    const string Resources = "ResourceIcons";

    public static void Register()
    {
        IconRegistry.Reg(Skills, "BloodRite", BloodRite);
        IconRegistry.Reg(Skills, "CursedHammer", CursedHammer);
        IconRegistry.Reg(Skills, "EarthBending", EarthBending);
        IconRegistry.Reg(Skills, "FrostLance", FrostLance);
        IconRegistry.Reg(Skills, "GaleWard", GaleWard);
        IconRegistry.Reg(Skills, "Immolation", Immolation);
        IconRegistry.Reg(Skills, "Meteor", Meteor);
        IconRegistry.Reg(Skills, "Multishot", Multishot);
        IconRegistry.Reg(Skills, "PhantomEdge", PhantomEdge);
        IconRegistry.Reg(Skills, "PlagueArrow", PlagueArrow);
        IconRegistry.Reg(Skills, "Riposte", Riposte);
        IconRegistry.Reg(Skills, "ShieldWall", ShieldWall);
        IconRegistry.Reg(Skills, "Stances", Stances);
        IconRegistry.Reg(Skills, "Swipe", Swipe);
        IconRegistry.Reg(Skills, "Upheaval", Upheaval);

        IconRegistry.Reg(Skills, "Ambidextrous", Ambidextrous);
        IconRegistry.Reg(Skills, "FrostAura", FrostAura);
        IconRegistry.Reg(Skills, "MomentumOfMind", MomentumOfMind);
        IconRegistry.Reg(Skills, "RaiseDead", RaiseDead);
        IconRegistry.Reg(Skills, "ShadowPriest", ShadowPriest);

        IconRegistry.Reg(Powers, "Execute", Execute);

        IconRegistry.Reg(Effects, "Bleeding", Bleeding);
        IconRegistry.Reg(Effects, "Burning", Burning);
        IconRegistry.Reg(Effects, "Chilled", Chilled);
        IconRegistry.Reg(Effects, "Frozen", Frozen);
        IconRegistry.Reg(Effects, "Poisoned", Poisoned);
        IconRegistry.Reg(Effects, "DelayedDamage", DelayedDamage);

        IconRegistry.Reg(Classes, "Caster", Caster);
        IconRegistry.Reg(Classes, "Cleric", Cleric);
        IconRegistry.Reg(Classes, "Fencer", Fencer);
        IconRegistry.Reg(Classes, "Fighter", Fighter);
        IconRegistry.Reg(Classes, "Mage", Mage);
        IconRegistry.Reg(Classes, "Monk", Monk);
        IconRegistry.Reg(Classes, "Paladin", Paladin);
        IconRegistry.Reg(Classes, "Rogue", Rogue);
        IconRegistry.Reg(Classes, "Savant", Savant);
        IconRegistry.Reg(Classes, "Warrior", Warrior);

        IconRegistry.Reg(Resources, "Arcane", Arcane);
        IconRegistry.Reg(Resources, "Energy", Energy);
        IconRegistry.Reg(Resources, "Focus", Focus);
        IconRegistry.Reg(Resources, "Mana", Mana);
        IconRegistry.Reg(Resources, "Rage", Rage);
    }

    // ---------- shared shapes ----------

    /// <summary>A sword pointing up, blade tip at (x, y - 104).</summary>
    static void Sword(Ic c, float x, float y, Pal blade, Pal hilt)
    {
        c.Add(G.Poly(x, y - 104, x + 22, y - 60, x + 22, y + 40, x - 22, y + 40, x - 22, y - 60), blade).Then(() =>
            c.Fill(G.Poly(x, y - 104, x + 22, y - 60, x + 22, y + 40, x, y + 40), blade.D, 120));
        c.Add(G.RR(x - 9, y + 50, 18, 44, 6), Pal.DarkLeather).Thin();
        c.Add(G.RR(x - 44, y + 36, 88, 20, 9), hilt);
        c.Add(G.Circle(x, y + 100, 12), hilt);
    }

    /// <summary>A thin rapier pointing up.</summary>
    static void Rapier(Ic c, float x, float y, Pal blade, Pal hilt)
    {
        c.Add(G.Poly(x, y - 118, x + 8, y - 100, x + 8, y + 40, x - 8, y + 40, x - 8, y - 100), blade);
        c.Add(G.Arc(x, y + 52, 26, 9, 180, 180), hilt);
        c.Add(G.RR(x - 7, y + 46, 14, 40, 5), Pal.DarkLeather).Thin();
        c.Add(G.RR(x - 30, y + 38, 60, 14, 7), hilt);
        c.Add(G.Circle(x, y + 92, 10), hilt);
    }

    /// <summary>A dagger pointing up.</summary>
    static void Dagger(Ic c, float x, float y, Pal blade, Pal hilt)
    {
        c.Add(G.Poly(x, y - 70, x + 18, y - 30, x + 14, y + 20, x - 14, y + 20, x - 18, y - 30), blade).Then(() =>
            c.Fill(G.Poly(x, y - 70, x + 18, y - 30, x + 14, y + 20, x, y + 20), blade.D, 120));
        c.Add(G.RR(x - 34, y + 18, 68, 16, 8), hilt);
        c.Add(G.RR(x - 8, y + 30, 16, 36, 6), Pal.DarkLeather).Thin();
        c.Add(G.Circle(x, y + 70, 10), hilt);
    }

    /// <summary>An arrow pointing up, tip at (x, y).</summary>
    static void Arrow(Ic c, float x, float y, float length, Pal head, Pal fletch)
    {
        c.Add(G.RR(x - 5, y + 20, 10, length - 30, 4), Pal.Wood).Thin();
        c.Add(G.Poly(x - 6, y + length - 34, x - 22, y + length - 12, x - 22, y + length + 8, x - 4, y + length - 8), fletch).Thin();
        c.Add(G.Poly(x + 6, y + length - 34, x + 22, y + length - 12, x + 22, y + length + 8, x + 4, y + length - 8), fletch).Thin();
        c.Add(G.Poly(x, y, x + 18, y + 34, x, y + 26, x - 18, y + 34), head);
    }

    /// <summary>A sword trail: an arc pointed at both ends, width thick at the middle. Angles clockwise from 3 o'clock.</summary>
    static GraphicsPath Slash(float x, float y, float r, float start, float sweep, float width)
    {
        const int steps = 24;
        var outer = new List<float>();
        var inner = new List<float>();
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            double a = (start + sweep * t) * Math.PI / 180;
            float w = width * (float)Math.Sin(t * Math.PI);
            outer.Add(x + (float)Math.Cos(a) * (r + w * .5f)); outer.Add(y + (float)Math.Sin(a) * (r + w * .5f));
            inner.Insert(0, y + (float)Math.Sin(a) * (r - w * .5f)); inner.Insert(0, x + (float)Math.Cos(a) * (r - w * .5f));
        }
        outer.AddRange(inner);
        return G.Poly(outer.ToArray());
    }

    /// <summary>A rough rock.</summary>
    static GraphicsPath Rock(float x, float y, float r, float rot)
    {
        return G.Rot(G.Round(r * .25f, x - r, y + r * .5f, x - r * .8f, y - r * .4f, x - r * .1f, y - r, x + r * .8f, y - r * .6f, x + r, y + r * .3f, x + r * .3f, y + r), rot, x, y);
    }

    static void Snowflake(Ic c, float x, float y, float r, Pal pal)
    {
        for (int i = 0; i < 3; i++)
            c.Add(G.Rot(G.RR(x - r * .12f, y - r, r * .24f, r * 2, r * .1f), i * 60, x, y), pal).Thin();
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60;
            c.Add(G.Rot(G.Poly(x - r * .32f, y - r * .78f, x, y - r * .52f, x + r * .32f, y - r * .78f, x + r * .32f, y - r * .6f, x, y - r * .34f, x - r * .32f, y - r * .6f), a, x, y), pal).Thin();
        }
        c.Add(G.Star(x, y, r * .3f, r * .3f, 6, 30), pal).Thin();
    }

    // ---------- active skills ----------

    // A ceremonial dagger bleeding into a bowl
    static void BloodRite(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(128, 176, 100, Pal.Hex("#D92F45"), 150);
        c.Add(G.Dome(128, 196, 74, 46), Pal.Gold).Then(() => c.Fill(G.Ell(128, 182, 60, 14), Pal.Blood.M));
        c.Add(G.RR(110, 210, 36, 22, 8), Pal.Gold);
        c.Flush();
        c.Xf(180, 128, 96, 1f);
        Dagger(c, 128, 96, Pal.Silver, Pal.Ruby);
        c.NoXf();
        c.Add(G.Drop(128, 168, 9), Pal.Blood);
        c.Add(G.Drop(96, 150, 7), Pal.Blood);
        c.Flush();
    }

    // A tumbling hammer crackling with lightning
    static void CursedHammer(Ic c)
    {
        Tiles.Lightning(c);
        c.Glow(140, 116, 110, Pal.Hex("#FFE85A"), 120);
        c.Add(Slash(128, 128, 94, 150, 120, 14), Pal.Lightning).Flat();
        c.Add(Slash(128, 128, 94, -30, 120, 14), Pal.Lightning).Flat();
        c.Flush();
        c.Xf(-35, 128, 128, 1f);
        c.Add(G.RR(116, 90, 24, 130, 8), Pal.Wood);
        c.Add(G.Round(12, 66, 44, 190, 44, 190, 108, 66, 108), Pal.Iron).Then(() =>
        {
            c.Fill(G.Poly(66, 76, 190, 76, 190, 108, 66, 108), Pal.Iron.D, 90);
            c.Ink2(4, 96, 50, 104, 72, 96, 92);
        });
        c.Add(G.RR(56, 54, 18, 44, 6), Pal.Shadow);
        c.Add(G.RR(182, 54, 18, 44, 6), Pal.Shadow);
        c.Flush();
        c.NoXf();
        c.Add(G.Bolt(160, 128, 80, 110), Pal.Lightning);
        c.Flush();
        c.Spark(56, 196, 14, Color.White);
    }

    // A stone wall breaking out of the ground
    static void EarthBending(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 140, 110, Pal.Hex("#C8B898"), 90);
        c.Add(G.Round(10, 22, 228, 234, 228, 226, 206, 30, 206), Pal.Moss);
        c.Add(G.Round(8, 30, 214, 36, 120, 80, 104, 88, 214), Pal.Stone).Then(() => c.Ink2(4, 54, 130, 62, 160, 52, 186));
        c.Add(G.Round(8, 86, 216, 90, 66, 130, 46, 172, 66, 172, 216), Pal.Stone).Then(() =>
        {
            c.Ink2(4, 120, 80, 132, 110, 122, 140, 136, 170);
            c.Fill(G.Poly(150, 60, 172, 66, 172, 216, 150, 216), Pal.Stone.D, 110);
        });
        c.Add(G.Round(8, 170, 216, 176, 110, 214, 96, 226, 216), Pal.Stone).Then(() => c.Ink2(4, 198, 124, 192, 156, 204, 182));
        c.Flush();
        c.Add(Rock(54, 70, 12, 20), Pal.Stone);
        c.Add(Rock(212, 62, 10, -30), Pal.Stone);
        c.Add(Rock(196, 34, 7, 10), Pal.Stone);
        c.Flush();
    }

    // A long lance of ice thrust diagonally
    static void FrostLance(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(150, 106, 110, Pal.Hex("#7FE6FF"), 160);
        c.Add(G.Poly(30, 226, 70, 196, 74, 200, 36, 232), Pal.Frost).Flat().NoLine();
        c.Add(G.Poly(232, 24, 196, 104, 172, 92), Pal.Frost).Then(() => c.Fill(G.Poly(232, 24, 196, 104, 186, 98), Pal.Frost.D, 120));
        c.Add(G.Poly(176, 94, 194, 106, 76, 210, 58, 196), Pal.Frost).Then(() => c.Fill(G.Poly(176, 94, 194, 106, 76, 210), Pal.Frost.D, 90));
        c.Add(G.Poly(62, 178, 82, 198, 50, 236, 24, 206), Pal.Silver);
        c.Flush();
        c.Spark(220, 120, 14, Color.White);
        c.Spark(130, 60, 10, Color.White);
    }

    // A ring of wind arcs around a calm middle, an arrow caught and bent
    static void GaleWard(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 128, 112, Pal.Hex("#7FE6FF"), 110);
        for (int i = 0; i < 3; i++)
            c.Add(Slash(128, 128, 88, i * 120 - 80, 100, 26), Pal.Frost);
        for (int i = 0; i < 3; i++)
            c.Add(Slash(128, 128, 58, i * 120 - 20, 80, 14), Pal.Teal);
        c.Flush();
        c.Xf(70, 128, 128, .62f);
        Arrow(c, 128, 34, 150, Pal.Steel, Pal.Rose);
        c.NoXf();
        c.Spark(196, 52, 13, Color.White);
    }

    // A heart wreathed in fire
    static void Immolation(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(128, 140, 116, Pal.Hex("#FF7A1A"), 190);
        c.Add(G.Flame(70, 232, 90, 170), Pal.Fire);
        c.Add(G.Flame(186, 232, 90, 170), Pal.Fire);
        c.Add(G.Flame(128, 210, 150, 200), Pal.Fire).Then(() => c.Fill(G.Flame(128, 214, 96, 130), Pal.Hex("#FFD24A")));
        c.Flush();
        c.Add(G.Heart(128, 150, 48), Pal.Ruby).Then(() => c.Gloss(110, 134, 12, 8, -35, 200));
        c.Flush();
    }

    // A flaming rock falling onto a glowing impact
    static void Meteor(Ic c)
    {
        Tiles.Fire(c);
        c.Fill(G.Ell(76, 206, 64, 22), Pal.Hex("#FF7A1A"), 110);
        c.Glow(100, 160, 100, Pal.Hex("#FF7A1A"), 160);
        c.Add(G.Rot(G.Flame(160, 196, 104, 210), 45, 160, 120), Pal.Fire).Then(() =>
            c.Fill(G.Rot(G.Flame(160, 186, 64, 150), 45, 160, 120), Pal.Hex("#FFD24A")));
        c.Flush();
        c.Add(Rock(98, 158, 46, 15), Pal.Stone).Then(() =>
        {
            c.Ink2(4, 80, 142, 98, 160, 92, 184);
            c.Stroke(Pal.Hex("#FF8A2A"), 5, 108, 132, 118, 152);
        });
        c.Flush();
    }

    // A fan of five arrows
    static void Multishot(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 90, 110, Pal.Hex("#FFC870"), 110);
        float[] angles = { -48, -24, 0, 24, 48 };
        foreach (float a in angles)
        {
            c.Xf(a, 128, 214, .82f);
            Arrow(c, 128, 26, 170, Pal.Steel, Pal.Candle);
            c.Flush();
        }
        c.NoXf();
    }

    // A dagger striking out of a trail of its own fading copies
    static void PhantomEdge(Ic c)
    {
        Tiles.Shadow(c);
        c.Glow(150, 110, 110, Pal.Hex("#A8F0E4"), 90);
        c.Xf(45, 140, 120, 1.25f);
        // Afterimages trail behind the blade (local +y is behind it)
        c.Fill(G.Poly(140, 110, 158, 150, 154, 200, 126, 200, 122, 150), Pal.Teal.M, 70);
        c.Fill(G.Poly(140, 80, 158, 120, 154, 170, 126, 170, 122, 120), Pal.Teal.M, 120);
        Dagger(c, 140, 120, Pal.Mirror, Pal.Shadow);
        c.NoXf();
        c.Spark(206, 52, 16, Pal.Hex("#A8F0E4"));
        c.Spark(66, 200, 10, Pal.Hex("#A8F0E4"));
    }

    // A slow arrow leaving a festering cloud
    static void PlagueArrow(Ic c)
    {
        Tiles.Poison(c);
        c.Glow(100, 160, 110, Pal.Hex("#8CFF3A"), 120);
        c.Add(G.Blob(30, 210, 50, 170, 84, 176, 100, 144, 130, 160, 120, 200, 90, 226, 50, 230), Pal.Poison).Then(() =>
        {
            c.Fill(G.Circle(70, 196, 10), Pal.Poison.L, 160);
            c.Fill(G.Circle(104, 180, 7), Pal.Poison.L, 160);
        });
        c.Flush();
        c.Xf(45, 150, 108, .9f);
        Arrow(c, 150, 10, 196, Pal.Poison, Pal.Moss);
        c.NoXf();
        c.Add(G.Drop(196, 120, 9), Pal.Poison);
        c.Flush();
    }

    // A thrust that turns a projectile around
    static void Riposte(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(150, 100, 110, Pal.Hex("#E9F1FA"), 110);
        c.Add(G.Arc(160, 104, 62, 14, 200, 230), Pal.Mirror).Flat();
        c.Add(G.Poly(98, 80, 128, 54, 132, 98), Pal.Mirror).Flat();
        c.Flush();
        c.Xf(45, 110, 150, .9f);
        Rapier(c, 110, 150, Pal.Steel, Pal.Gold);
        c.NoXf();
        c.Add(G.Circle(186, 58, 14), Pal.Fire);
        c.Flush();
    }

    // A tower shield with arrows stuck in it
    static void ShieldWall(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 120, 110, Pal.Hex("#E9F1FA"), 90);
        c.Add(G.Round(16, 52, 34, 204, 34, 204, 168, 128, 230, 52, 168), Pal.Steel).Then(() =>
        {
            c.Fill(G.Poly(128, 34, 204, 34, 204, 168, 128, 230), Pal.Steel.D, 80);
            c.Fill(G.RR(116, 40, 24, 180, 6), Pal.Sapphire.M);
            c.Fill(G.RR(60, 92, 136, 22, 6), Pal.Sapphire.M);
        });
        c.Flush();
        // Arrow shafts stuck in the face, tips buried
        StuckArrow(c, 92, 78, -35);
        StuckArrow(c, 168, 132, 25);
        StuckArrow(c, 104, 168, -10);
        c.Spark(214, 196, 13, Color.White);
    }

    // Three attribute gems turning around one another
    static void Stances(Ic c)
    {
        Tiles.Candle(c);
        c.Glow(128, 128, 110, Pal.Hex("#FFC870"), 120);
        c.Add(G.Ring(128, 132, 78, 64), Pal.Gold);
        c.Flush();
        c.Add(G.Star(128, 50, 32, 32, 4, 0), Pal.Ruby).Then(() => c.Gloss(120, 42, 8, 5, -35, 200));
        c.Add(G.Star(60, 172, 32, 32, 4, 0), Pal.Jade).Then(() => c.Gloss(52, 164, 8, 5, -35, 200));
        c.Add(G.Star(196, 172, 32, 32, 4, 0), Pal.Sapphire).Then(() => c.Gloss(188, 164, 8, 5, -35, 200));
        c.Flush();
    }

    // A heavy axe with a wide sweeping trail
    static void Swipe(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 140, 116, Pal.Hex("#E9F1FA"), 90);
        c.Add(Slash(128, 128, 104, 110, 170, 44), Pal.Mirror);
        c.Flush();
        c.Xf(-30, 128, 128, .95f);
        c.Add(G.RR(120, 40, 18, 190, 7), Pal.Wood);
        c.Add(G.Soft(132, 46, 210, 30, 226, 96, 210, 154, 132, 128), Pal.Steel).Then(() =>
            c.Fill(G.Poly(200, 30, 226, 96, 210, 154, 196, 150), Pal.Steel.L, 140));
        c.Add(G.RR(114, 210, 30, 26, 8), Pal.Bronze);
        c.Flush();
        c.NoXf();
    }

    // A gauntlet slamming the ground, rocks heaving up around it
    static void Upheaval(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 190, 110, Pal.Hex("#FFC870"), 110);
        c.Add(G.Round(10, 16, 232, 240, 232, 226, 198, 30, 198), Pal.Moss);
        c.Add(G.Poly(30, 210, 46, 150, 70, 204), Pal.Stone);
        c.Add(G.Poly(186, 206, 210, 140, 230, 210), Pal.Stone);
        c.Add(G.Poly(56, 214, 84, 170, 104, 214), Pal.Stone);
        c.Add(G.Poly(150, 214, 174, 172, 198, 214), Pal.Stone);
        c.Flush();
        c.Add(G.Round(14, 88, 40, 168, 40, 172, 110, 84, 110), Pal.Iron);
        c.Add(G.Round(16, 74, 104, 182, 104, 176, 196, 80, 196), Pal.Steel).Then(() =>
        {
            c.Ink2(4, 102, 108, 102, 150);
            c.Ink2(4, 128, 108, 128, 150);
            c.Ink2(4, 154, 108, 154, 150);
        });
        c.Flush();
        c.Add(Rock(40, 112, 12, 10), Pal.Stone);
        c.Add(Rock(216, 104, 10, -20), Pal.Stone);
        c.Flush();
    }

    static void StuckArrow(Ic c, float x, float y, float rot)
    {
        c.Xf(rot, x, y, 1f);
        c.Add(G.RR(x - 4, y - 50, 8, 52, 3), Pal.Wood).Thin();
        c.Add(G.Poly(x - 4, y - 50, x - 16, y - 66, x - 16, y - 80, x - 3, y - 64), Pal.Ruby).Thin();
        c.Add(G.Poly(x + 4, y - 50, x + 16, y - 66, x + 16, y - 80, x + 3, y - 64), Pal.Ruby).Thin();
        c.NoXf();
    }

    // ---------- passives ----------

    // Two blades crossed, one in each hand
    static void Ambidextrous(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 120, 110, Pal.Hex("#A8F0E4"), 100);
        c.Xf(-38, 128, 128, .88f);
        Sword(c, 128, 120, Pal.Steel, Pal.Bronze);
        c.Flush();
        c.Xf(38, 128, 128, .88f);
        Sword(c, 128, 120, Pal.Silver, Pal.Gold);
        c.NoXf();
    }

    // A snowflake inside a cold ring
    static void FrostAura(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(128, 128, 112, Pal.Hex("#7FE6FF"), 170);
        c.Add(G.Ring(128, 128, 104, 90), Pal.Frost).Then(() => { });
        c.Flush();
        Snowflake(c, 128, 128, 70, Pal.Frost);
        c.Flush();
    }

    // An eye ringed by cooldown arcs
    static void MomentumOfMind(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 112, Pal.Hex("#9CC4FF"), 140);
        c.Add(G.Arc(128, 128, 92, 16, -90, 100), Pal.Sapphire);
        c.Add(G.Arc(128, 128, 92, 16, 30, 100), Pal.Sapphire);
        c.Add(G.Arc(128, 128, 92, 16, 150, 100), Pal.Sapphire);
        c.Flush();
        c.Add(G.Poly(40, 128, 84, 88, 128, 78, 172, 88, 216, 128, 172, 168, 128, 178, 84, 168), Pal.Bone).Then(() =>
        {
            c.Fill(G.Circle(128, 128, 36), Pal.Sapphire.M);
            c.Fill(G.Circle(128, 128, 16), Ic.Ink);
            c.Fill(G.Circle(118, 116, 8), Color.White);
        });
        c.Flush();
    }

    // A bony hand clawing out of the ground
    static void RaiseDead(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 120, 112, Pal.Hex("#3FC4B4"), 150);
        c.Add(G.Round(10, 18, 232, 238, 232, 222, 196, 34, 196), Pal.DarkLeather);
        c.Add(G.Round(10, 96, 200, 92, 140, 164, 140, 160, 200), Pal.Bone);
        float[] fingers = { 96, 118, 140, 162 };
        for (int i = 0; i < 4; i++)
        {
            float x = fingers[i], top = i == 0 || i == 3 ? 62 : 40;
            c.Add(G.RR(x - 9, top, 18, 150 - top, 8), Pal.Bone);
        }
        c.Add(G.Rot(G.RR(66, 108, 18, 56, 8), -40, 76, 136), Pal.Bone);
        c.Flush();
        c.Spark(206, 64, 14, Pal.Hex("#A8F0E4"));
        c.Spark(46, 82, 10, Pal.Hex("#A8F0E4"));
    }

    // A hooded priest with dark orbs that turn to healing
    static void ShadowPriest(Ic c)
    {
        Tiles.Shadow(c);
        c.Glow(128, 120, 110, Pal.Hex("#F2789C"), 110);
        c.Add(G.Soft(52, 232, 70, 120, 96, 58, 128, 40, 160, 58, 186, 120, 204, 232), Pal.Shadow).Then(() =>
        {
            c.Fill(G.Ell(128, 112, 34, 40), Pal.Hex("#14101E"));
            c.Fill(G.Ell(116, 112, 6, 4), Pal.Rose.L);
            c.Fill(G.Ell(140, 112, 6, 4), Pal.Rose.L);
        });
        c.Flush();
        c.Add(G.Circle(44, 70, 16), Pal.Shadow);
        c.Add(G.Circle(212, 82, 14), Pal.Shadow);
        c.Add(G.Heart(206, 196, 22), Pal.Rose);
        c.Add(G.Heart(54, 186, 18), Pal.Rose);
        c.Flush();
    }

    // ---------- power ----------

    // A headsman's axe over a nearly empty health bar
    static void Execute(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(128, 110, 110, Pal.Hex("#D92F45"), 130);
        c.Add(G.RR(30, 196, 196, 30, 10), Pal.Iron).Then(() => c.Fill(G.RR(36, 202, 40, 18, 6), Pal.Blood.M));
        c.Flush();
        c.Xf(-20, 128, 110, 1f);
        c.Add(G.RR(122, 30, 18, 150, 7), Pal.Wood);
        c.Add(G.Soft(56, 44, 130, 34, 130, 112, 56, 132, 38, 88), Pal.Steel).Then(() =>
            c.Fill(G.Poly(38, 88, 56, 44, 72, 46, 56, 128), Pal.Steel.L, 140));
        c.Flush();
        c.NoXf();
        c.Add(Slash(128, 170, 86, 200, 70, 18), Pal.Ruby).Flat();
        c.Flush();
    }

    // ---------- status effects ----------

    static void Bleeding(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(128, 140, 110, Pal.Hex("#D92F45"), 150);
        c.Add(G.Drop(128, 150, 54), Pal.Blood).Then(() => c.Gloss(108, 140, 12, 18, -20, 200));
        c.Add(G.Drop(58, 190, 22), Pal.Blood);
        c.Add(G.Drop(200, 196, 18), Pal.Blood);
        c.Flush();
    }

    static void Burning(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(128, 150, 112, Pal.Hex("#FF7A1A"), 180);
        c.Add(G.Flame(128, 230, 176, 210), Pal.Fire).Then(() =>
        {
            c.Fill(G.Flame(128, 234, 110, 130), Pal.Hex("#FFD24A"));
            c.Fill(G.Flame(128, 236, 54, 64), Pal.Hex("#FFF4C0"));
        });
        c.Flush();
    }

    static void Chilled(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(128, 128, 112, Pal.Hex("#7FE6FF"), 140);
        Snowflake(c, 128, 112, 66, Pal.Frost);
        c.Flush();
        c.Add(G.Round(8, 70, 200, 186, 200, 186, 224, 70, 224), Pal.Frost).Then(() => c.Fill(G.RR(70, 200, 50, 24, 6), Pal.Sapphire.M));
        c.Flush();
    }

    static void Frozen(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(128, 140, 112, Pal.Hex("#7FE6FF"), 170);
        c.Add(G.Poly(40, 220, 56, 120, 86, 220), Pal.Frost);
        c.Add(G.Poly(170, 220, 200, 108, 222, 220), Pal.Frost);
        c.Add(G.Poly(76, 222, 128, 26, 180, 222), Pal.Frost).Then(() =>
        {
            c.Fill(G.Poly(128, 26, 180, 222, 128, 222), Pal.Frost.D, 110);
            c.Gloss(112, 100, 8, 26, 15, 180);
        });
        c.Flush();
        c.Spark(206, 70, 15, Color.White);
    }

    static void Poisoned(Ic c)
    {
        Tiles.Poison(c);
        c.Glow(128, 150, 112, Pal.Hex("#8CFF3A"), 150);
        c.Add(G.RR(104, 34, 48, 22, 6), Pal.Wood);
        c.Add(G.Soft(108, 56, 148, 56, 150, 96, 206, 140, 196, 220, 60, 220, 50, 140, 106, 96), Pal.Mirror).Then(() =>
        {
            c.Fill(G.Poly(52, 150, 204, 150, 196, 220, 60, 220), Pal.Poison.M);
            c.Fill(G.Circle(96, 182, 9), Pal.Poison.L);
            c.Fill(G.Circle(144, 196, 6), Pal.Poison.L);
            c.Gloss(84, 116, 8, 18, 30, 180);
        });
        c.Flush();
    }

    static void DelayedDamage(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(128, 128, 112, Pal.Hex("#D92F45"), 120);
        c.Add(G.RR(60, 26, 136, 20, 8), Pal.Wood);
        c.Add(G.RR(60, 210, 136, 20, 8), Pal.Wood);
        c.Add(G.Poly(76, 46, 180, 46, 136, 128, 180, 210, 76, 210, 120, 128), Pal.Mirror).Then(() =>
        {
            c.Fill(G.Poly(90, 58, 166, 58, 128, 110), Pal.Blood.M);
            c.Fill(G.Poly(128, 160, 166, 204, 90, 204), Pal.Blood.M);
            c.Fill(G.RR(124, 118, 8, 50, 3), Pal.Blood.M);
        });
        c.Flush();
    }

    // ---------- classes ----------

    // A wand tipped with a star
    static void Caster(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(160, 90, 110, Pal.Hex("#9CC4FF"), 150);
        c.Xf(40, 128, 128, 1f);
        c.Add(G.RR(120, 90, 16, 150, 7), Pal.Wood);
        c.Add(G.RR(114, 190, 28, 18, 6), Pal.Gold);
        c.Flush();
        c.NoXf();
        c.Add(G.Star(172, 72, 46, 20, 5, 0), Pal.Gold).Then(() => c.Gloss(162, 62, 8, 5, -35, 200));
        c.Flush();
        c.Spark(70, 60, 13, Color.White);
        c.Spark(214, 156, 10, Color.White);
    }

    // A chalice with a healing glow
    static void Cleric(Ic c)
    {
        Tiles.Rose(c);
        c.Glow(128, 80, 110, Pal.Hex("#FFC6D6"), 160);
        c.Add(G.Round(8, 82, 214, 174, 214, 160, 196, 96, 196), Pal.Gold);
        c.Add(G.RR(116, 140, 24, 62, 8), Pal.Gold);
        c.Add(G.Dome(128, 100, 62, 70), Pal.Gold);
        c.Add(G.Round(12, 66, 60, 190, 60, 160, 140, 96, 140), Pal.Gold).Then(() =>
        {
            c.Fill(G.Ell(128, 66, 58, 10), Pal.Rose.M);
            c.Fill(G.Star(128, 100, 18, 18, 4, 45), Pal.Ruby.M);
        });
        c.Flush();
        c.Add(G.Heart(128, 36, 16), Pal.Rose);
        c.Flush();
    }

    // A rapier with a crest
    static void Fencer(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 100, 110, Pal.Hex("#E9F1FA"), 110);
        c.Add(G.Arc(128, 128, 90, 12, 200, 140), Pal.Mirror).Flat();
        c.Flush();
        c.Xf(30, 128, 140, 1f);
        Rapier(c, 128, 140, Pal.Silver, Pal.Gold);
        c.NoXf();
    }

    // A sword over a round shield
    static void Fighter(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 128, 110, Pal.Hex("#FFC870"), 100);
        c.Add(G.Circle(100, 150, 70), Pal.Wood).Then(() =>
        {
            c.Fill(G.Ring(100, 150, 70, 58), Pal.Iron.M);
            c.Fill(G.Circle(100, 150, 18), Pal.Iron.M);
        });
        c.Flush();
        c.Xf(30, 160, 120, .9f);
        Sword(c, 160, 120, Pal.Steel, Pal.Bronze);
        c.NoXf();
    }

    // A pointed hat over a glowing orb
    static void Mage(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 176, 100, Pal.Hex("#9CC4FF"), 170);
        c.Add(G.Circle(128, 182, 40), Pal.Sapphire).Then(() => c.Gloss(112, 166, 12, 8, -35, 210));
        c.Flush();
        c.Add(G.Soft(30, 130, 226, 130, 200, 112, 56, 112), Pal.Linen);
        c.Add(G.Round(8, 74, 116, 128, 20, 160, 54, 150, 70, 182, 116), Pal.Linen).Then(() =>
            c.Fill(G.Star(118, 80, 12, 5, 5, 0), Pal.Gold.M));
        c.Flush();
    }

    // A fist wrapped in prayer beads
    static void Monk(Ic c)
    {
        Tiles.Candle(c);
        c.Glow(128, 120, 110, Pal.Hex("#FFC870"), 130);
        c.Add(G.Round(14, 68, 92, 188, 92, 196, 176, 172, 208, 84, 208, 60, 176), Pal.Flesh).Then(() =>
        {
            c.Ink2(4, 98, 96, 98, 140);
            c.Ink2(4, 128, 96, 128, 140);
            c.Ink2(4, 158, 96, 158, 140);
            c.Ink2(4, 68, 142, 188, 142);
        });
        c.Add(G.Round(10, 50, 120, 92, 120, 92, 168, 50, 160), Pal.Flesh);
        c.Flush();
        for (int i = 0; i < 9; i++)
            c.Add(G.Circle(70 + i * 15, 176 + (float)Math.Sin(i * .8) * 6, 9), Pal.Ruby).Thin();
        c.Flush();
    }

    // A kite shield with a sunburst
    static void Paladin(Ic c)
    {
        Tiles.Candle(c);
        c.Glow(128, 100, 110, Pal.Hex("#FFEFC4"), 150);
        c.Add(G.Round(16, 48, 36, 208, 36, 208, 140, 128, 232, 48, 140), Pal.Silver).Then(() =>
        {
            c.Fill(G.Poly(128, 36, 208, 36, 208, 140, 128, 232), Pal.Silver.D, 70);
            c.Fill(G.Star(128, 116, 52, 22, 8, 0), Pal.Gold.M);
            c.Fill(G.Circle(128, 116, 20), Pal.Gold.L);
        });
        c.Flush();
    }

    // A hooded mask between two daggers
    static void Rogue(Ic c)
    {
        Tiles.Shadow(c);
        c.Glow(128, 120, 110, Pal.Hex("#A8F0E4"), 90);
        c.Xf(-30, 60, 140, .8f);
        Dagger(c, 60, 140, Pal.Steel, Pal.Shadow);
        c.Flush();
        c.Xf(30, 196, 140, .8f);
        Dagger(c, 196, 140, Pal.Steel, Pal.Shadow);
        c.NoXf();
        c.Add(G.Soft(64, 214, 72, 100, 100, 48, 128, 36, 156, 48, 184, 100, 192, 214), Pal.Shadow).Then(() =>
        {
            c.Fill(G.Ell(128, 128, 40, 46), Pal.Hex("#14101E"));
            c.Fill(G.Poly(98, 116, 120, 122, 102, 130), Pal.Teal.L);
            c.Fill(G.Poly(158, 116, 136, 122, 154, 130), Pal.Teal.L);
        });
        c.Flush();
    }

    // An open book with an eye
    static void Savant(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 100, 110, Pal.Hex("#A8F0E4"), 140);
        c.Add(G.Poly(20, 200, 28, 92, 128, 108, 128, 216), Pal.Cloth).Then(() =>
        {
            c.Ink2(3, 46, 120, 108, 132);
            c.Ink2(3, 44, 146, 108, 158);
            c.Ink2(3, 42, 172, 108, 184);
        });
        c.Add(G.Poly(236, 200, 228, 92, 128, 108, 128, 216), Pal.Cloth).Then(() =>
        {
            c.Fill(G.Poly(236, 200, 228, 92, 128, 108, 128, 216), Pal.Cloth.D, 60);
            c.Ink2(3, 210, 120, 148, 132);
            c.Ink2(3, 212, 146, 148, 158);
            c.Ink2(3, 214, 172, 148, 184);
        });
        c.Flush();
        c.Add(G.Poly(76, 52, 102, 32, 128, 26, 154, 32, 180, 52, 154, 72, 128, 78, 102, 72), Pal.Bone).Then(() =>
        {
            c.Fill(G.Circle(128, 52, 18), Pal.Teal.M);
            c.Fill(G.Circle(128, 52, 8), Ic.Ink);
        });
        c.Flush();
    }

    // A great helm with horns
    static void Warrior(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(128, 120, 110, Pal.Hex("#FF8C94"), 110);
        c.Add(G.Soft(36, 40, 70, 60, 84, 110, 60, 100), Pal.Bone);
        c.Add(G.Soft(220, 40, 186, 60, 172, 110, 196, 100), Pal.Bone);
        c.Add(G.Round(20, 64, 70, 192, 70, 196, 210, 60, 210), Pal.Iron).Then(() =>
        {
            c.Fill(G.RR(76, 120, 104, 14, 6), Ic.Ink);
            c.Fill(G.RR(120, 134, 16, 60, 6), Ic.Ink);
            c.Fill(G.Poly(128, 70, 192, 70, 196, 210, 128, 210), Pal.Iron.D, 70);
            c.Gloss(90, 90, 12, 7, -30, 170);
        });
        c.Flush();
    }

    // ---------- resources (colored to match ResourceColors) ----------

    static void Arcane(Ic c)
    {
        var pal = new Pal("#F2B8EE", "#C93BC0", "#7A1E74");
        c.Tile("#2A1430", "#6A2A66");
        c.Glow(128, 128, 112, Pal.Hex("#C93BC0"), 160);
        c.Add(G.Star(128, 128, 100, 38, 4, 0), pal).Then(() => c.Gloss(108, 96, 12, 8, -35, 200));
        c.Add(G.Star(128, 128, 30, 12, 4, 45), Pal.Bone).Flat();
        c.Flush();
    }

    static void Energy(Ic c)
    {
        var pal = new Pal("#E4FF9A", "#A6F01E", "#5E9010");
        c.Tile("#1C2A12", "#46661E");
        c.Glow(128, 128, 112, Pal.Hex("#A6F01E"), 160);
        c.Add(G.Bolt(56, 20, 144, 216), pal);
        c.Flush();
    }

    static void Focus(Ic c)
    {
        var pal = new Pal("#FFEAA0", "#FFC21F", "#B07A10");
        c.Tile("#2E2410", "#6E5418");
        c.Glow(128, 128, 112, Pal.Hex("#FFC21F"), 150);
        c.Add(G.Ring(128, 128, 92, 72), pal);
        c.Add(G.Ring(128, 128, 50, 32), pal);
        c.Add(G.Circle(128, 128, 14), pal);
        c.Flush();
    }

    static void Mana(Ic c)
    {
        var pal = new Pal("#B4E8FF", "#1FB4FF", "#0E64A8");
        c.Tile("#10223A", "#1E4E7E");
        c.Glow(128, 140, 112, Pal.Hex("#1FB4FF"), 170);
        c.Add(G.Drop(128, 150, 70), pal).Then(() => c.Gloss(104, 136, 14, 22, -20, 210));
        c.Flush();
    }

    static void Rage(Ic c)
    {
        var pal = new Pal("#FF9A86", "#D8362E", "#7E1414");
        c.Tile("#2E1012", "#6A1E1E");
        c.Glow(128, 150, 112, Pal.Hex("#D8362E"), 170);
        c.Add(G.Flame(128, 230, 180, 210), pal).Then(() => c.Fill(G.Flame(128, 232, 100, 120), Pal.Hex("#FF8A6E")));
        c.Flush();
    }
}
