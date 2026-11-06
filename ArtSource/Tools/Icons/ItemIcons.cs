// Item icons, one per Assets/Data/Items asset. Standard items sit on a neutral tile so the UI's rarity border speaks;
// Legendary and Unique items get their power's color, a glow and one idea from their power in the silhouette.
using System;
using System.Drawing;

public static class ItemIcons
{
    const string Root = "ItemIcons/";

    static void Reg(string slot, string name, Action<Ic> draw) { IconRegistry.Reg(Root + slot, name, draw); }

    public static void Register()
    {
        Reg("Amulet", "BronzePendant", BronzePendant);
        Reg("Amulet", "JadeAmulet", JadeAmulet);
        Reg("Amulet", "EyeOfTheStorm", EyeOfTheStorm);
        Reg("Amulet", "GeyserPendant", GeyserPendant);
        Reg("Amulet", "HeartOfCinders", HeartOfCinders);
        Reg("Amulet", "RimeheartLocket", RimeheartLocket);
        Reg("Amulet", "StormcallersTorc", StormcallersTorc);
        Reg("Amulet", "VipersFang", VipersFang);
        Reg("Ring", "CopperRing", CopperRing);
        Reg("Ring", "SilverRing", SilverRing);
        Reg("Ring", "GoldRing", GoldRing);
        Reg("Ring", "EmberheartRing", EmberheartRing);
        Reg("Ring", "SignetOfTheSage", SignetOfTheSage);
        Reg("Belt", "Sash", Sash);
        Reg("Belt", "PlatedBelt", PlatedBelt);
        Reg("Belt", "GirdleOfGiants", GirdleOfGiants);
        Reg("Belt", "RiptideCord", RiptideCord);
        Reg("Belt", "SurvivorsCinch", SurvivorsCinch);
        Reg("Boots", "LeatherBoots", LeatherBoots);
        Reg("Boots", "PlatedBoots", PlatedBoots);
        Reg("Boots", "Blightwalkers", Blightwalkers);
        Reg("Boots", "RestlessTreads", RestlessTreads);
        Reg("Boots", "Stormstriders", Stormstriders);
        Reg("Chest", "QuiltedArmor", QuiltedArmor);
        Reg("Chest", "FullPlateMail", FullPlateMail);
        Reg("Chest", "DeathwishCuirass", DeathwishCuirass);
        Reg("Chest", "DebtorsHauberk", DebtorsHauberk);
        Reg("Chest", "HeartOfTheWyrm", HeartOfTheWyrm);
        Reg("Chest", "HermitsShroud", HermitsShroud);
        Reg("Hands", "LeatherGloves", LeatherGloves);
        Reg("Hands", "Gauntlets", Gauntlets);
        Reg("Hands", "ButchersGrips", ButchersGrips);
        Reg("Hands", "FistsOfRuin", FistsOfRuin);
        Reg("Hands", "RimeshardGrips", RimeshardGrips);
        Reg("Head", "Cap", Cap);
        Reg("Head", "Circlet", Circlet);
        Reg("Head", "GreatHelm", GreatHelm);
        Reg("Head", "CrownOfAges", CrownOfAges);
        Reg("Head", "ThiefsCowl", ThiefsCowl);
        Reg("Head", "ThunderheadHelm", ThunderheadHelm);
        Reg("Legs", "ClothLeggings", ClothLeggings);
        Reg("Legs", "MailChausses", MailChausses);
        Reg("Legs", "FrostveinLeggings", FrostveinLeggings);
        Reg("Legs", "LegsOfTheFallen", LegsOfTheFallen);
        Reg("Legs", "RiposteGreaves", RiposteGreaves);
        Reg("Offhand", "Buckler", Buckler);
        Reg("Offhand", "TowerShield", TowerShield);
        Reg("Offhand", "Tome", Tome);
        Reg("Offhand", "ForkedLens", ForkedLens);
        Reg("Offhand", "GravityOrb", GravityOrb);
        Reg("Offhand", "Vanguard", Vanguard);
        Reg("Shoulders", "ClothMantle", ClothMantle);
        Reg("Shoulders", "Spaulders", Spaulders);
        Reg("Shoulders", "PlatePauldrons", PlatePauldrons);
        Reg("Shoulders", "MantleOfTheWatcher", MantleOfTheWatcher);
        Reg("Shoulders", "PyrewingMantle", PyrewingMantle);
        Reg("Weapon", "ShortSword", ShortSword);
        Reg("Weapon", "Dagger", Dagger);
        Reg("Weapon", "Bow", Bow);
        Reg("Weapon", "Crossbow", Crossbow);
        Reg("Weapon", "Staff", Staff);
        Reg("Weapon", "Wand", Wand);
        Reg("Weapon", "Ambusher", Ambusher);
        Reg("Weapon", "BrokenPromises", BrokenPromises);
        Reg("Weapon", "EndlessWell", EndlessWell);
        Reg("Weapon", "Farsight", Farsight);
        Reg("Weapon", "MasterSword", MasterSword);
    }

    // ---------- Shared details ----------

    /// <summary>A cut gem: flat top, point at the bottom.</summary>
    static void Gem(Ic c, float x, float y, float r, Pal p)
    {
        var shape = G.Poly(x - r, y - r * .3f, x - r * .55f, y - r * .85f, x + r * .55f, y - r * .85f, x + r, y - r * .3f, x, y + r);
        c.Add(shape, p).Then(() =>
        {
            c.Fill(G.Poly(x - r * .45f, y - r * .3f, x + r * .45f, y - r * .3f, x, y + r), Color.White, 60);
            c.Fill(G.Poly(x - r, y - r * .3f, x - r * .55f, y - r * .85f, x + r * .55f, y - r * .85f, x + r, y - r * .3f), Color.White, 90);
            c.Fill(G.Poly(x + r * .45f, y - r * .3f, x + r, y - r * .3f, x, y + r), p.D, 150);
        }).Thin();
    }

    /// <summary>A round polished stone.</summary>
    static void Stone(Ic c, float x, float y, float r, Pal p)
    {
        c.Add(G.Circle(x, y, r), p).Then(() => c.Gloss(x - r * .32f, y - r * .36f, r * .34f, r * .2f, -35, 220)).Thin();
    }

    /// <summary>Rows of small rings, for mail.</summary>
    static void Mail(Ic c, float x0, float y0, float x1, float y1, float step, Color color)
    {
        int row = 0;
        for (float y = y0; y < y1; y += step * .8f, row++)
            for (float x = x0 + (row % 2) * step / 2; x < x1; x += step)
                c.Stroke(color, 2.6f, x - step / 2, y, x, y + step * .55f, x + step / 2, y);
    }

    /// <summary>Overlapping scales, drawn from the bottom row up.</summary>
    static void Scales(Ic c, float x0, float y0, float x1, float y1, float step, Pal p)
    {
        int rows = (int)((y1 - y0) / (step * .7f));
        for (int row = rows; row >= 0; row--)
        {
            float y = y0 + row * step * .7f;
            for (float x = x0 + (row % 2) * step / 2; x < x1; x += step)
            {
                var scale = G.Soft(x - step * .52f, y - step * .3f, x + step * .52f, y - step * .3f, x + step * .52f, y + step * .3f, x, y + step * .8f, x - step * .52f, y + step * .3f);
                c.Fill(scale, row % 2 == 0 ? p.M : p.L);
                using (var pen = new Pen(Ic.Ink, 2.6f)) c.G2.DrawPath(pen, scale);
            }
        }
    }

    static void Rivets(Ic c, Color color, params float[] xy)
    {
        for (int i = 0; i < xy.Length; i += 2)
        {
            c.Fill(G.Circle(xy[i], xy[i + 1], 6), Ic.Ink);
            c.Fill(G.Circle(xy[i] - 1, xy[i + 1] - 1, 3.6f), color);
        }
    }

    static void Pair(Ic c, GraphicsPathHolder shape, Pal p)
    {
        c.Add(shape.Path, p);
        c.Add(G.Mirror(shape.Path, 128), p);
    }

    /// <summary>Lets a part be declared once and used for both shoulders.</summary>
    class GraphicsPathHolder
    {
        public System.Drawing.Drawing2D.GraphicsPath Path;
        public static implicit operator GraphicsPathHolder(System.Drawing.Drawing2D.GraphicsPath p) { return new GraphicsPathHolder { Path = p }; }
    }

    // ---------- Amulets ----------

    static void Cord(Ic c, Pal p)
    {
        c.Add(G.Rope(11, 54, 12, 66, 78, 128, 118), p).Thin();
        c.Add(G.Rope(11, 202, 12, 190, 78, 128, 118), p).Thin();
        c.Outer = 5;
        c.Flush();
        c.Outer = 7;
    }

    static void BronzePendant(Ic c)
    {
        Tiles.Item(c);
        Cord(c, Pal.Leather);
        c.Add(G.Ring(128, 110, 15, 6), Pal.Bronze).Thin();
        c.Add(G.Circle(128, 170, 62), Pal.Bronze).Then(() =>
        {
            c.Stroke(Pal.Bronze.D, 6, 86, 170, 128, 128, 170, 170);
            c.Stroke(Pal.Bronze.D, 6, 86, 170, 128, 212, 170, 170);
        });
        c.Flush();
        Stone(c, 128, 170, 22, Pal.Candle);
        c.Flush();
    }

    static void JadeAmulet(Ic c)
    {
        Tiles.Item(c);
        Cord(c, Pal.Gold);
        c.Add(G.Ring(128, 106, 14, 5), Pal.Gold).Thin();
        c.Add(G.Ell(128, 172, 52, 66), Pal.Gold);
        c.Flush();
        c.Add(G.Ell(128, 172, 36, 49), Pal.Jade).Then(() =>
        {
            c.Gloss(114, 148, 11, 16, 18, 190);
            c.Stroke(Pal.Jade.D, 5, 128, 148, 142, 170, 128, 196);
            c.Stroke(Pal.Jade.D, 5, 128, 148, 114, 170, 128, 196);
        }).Thin();
        c.Flush();
    }

    // A storm with an eye at its center: lightning strikes while you channel
    static void EyeOfTheStorm(Ic c)
    {
        Tiles.Lightning(c);
        c.Glow(128, 164, 104, Pal.Hex("#FFE85A"), 120);
        Cord(c, Pal.Gold);
        c.Add(G.Ring(128, 106, 14, 5), Pal.Gold).Thin();
        c.Add(G.Star(128, 170, 80, 62, 9, 0), new Pal("#8C96D8", "#4C55A8", "#2C2F70"));
        c.Add(G.Ell(128, 170, 46, 30), Pal.Mirror).Then(() =>
        {
            c.Fill(G.Circle(128, 170, 27), Pal.Hex("#FFE85A"));
            c.Fill(G.Bolt(116, 148, 24, 44), Ic.Ink);
            c.Gloss(112, 156, 8, 5, -30, 230);
        });
        c.Flush();
        c.Spark(208, 104, 13, Pal.Hex("#FFE85A"));
    }

    // Water and steam: fire and cold in one
    static void GeyserPendant(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 164, 104, Pal.Hex("#7FE6FF"), 110);
        Cord(c, Pal.Silver);
        c.Add(G.Drop(128, 176, 46), Pal.Silver);
        c.Flush();
        c.Add(G.Drop(128, 178, 32), Pal.Teal).Then(() => c.Gloss(114, 172, 8, 13, 15, 220)).Thin();
        c.Flush();
        c.Add(G.Circle(60, 128, 14), Pal.Mirror).Thin();
        c.Add(G.Circle(44, 104, 19), Pal.Mirror).Thin();
        c.Add(G.Circle(196, 128, 14), Pal.Mirror).Thin();
        c.Add(G.Circle(212, 104, 19), Pal.Mirror).Thin();
        c.Outer = 5;
        c.Flush();
        c.Outer = 7;
    }

    // A coal heart with fire showing through the cracks
    static void HeartOfCinders(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(128, 168, 106, Pal.Hex("#FF7A1A"), 170);
        Cord(c, Pal.Iron);
        c.Add(G.Ring(128, 110, 14, 5), Pal.Iron).Thin();
        c.Add(G.Heart(128, 172, 70), new Pal("#6A5A5C", "#3E3438", "#221C22")).Then(() =>
        {
            c.Stroke(Pal.Hex("#FF8A2A"), 11, 92, 134, 118, 160, 104, 186, 128, 218);
            c.Stroke(Pal.Hex("#FF8A2A"), 11, 118, 160, 152, 150, 166, 136);
            c.Stroke(Pal.Hex("#FF8A2A"), 9, 138, 154, 148, 186, 136, 200);
            c.Stroke(Pal.Hex("#FFE85A"), 5, 92, 134, 118, 160, 104, 186, 128, 218);
            c.Stroke(Pal.Hex("#FFE85A"), 5, 118, 160, 152, 150, 166, 136);
        });
        c.Flush();
    }

    static void RimeheartLocket(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(128, 168, 106, Pal.Hex("#7FE6FF"), 150);
        Cord(c, Pal.Silver);
        c.Add(G.Ring(128, 106, 14, 5), Pal.Silver).Thin();
        c.Add(G.Poly(58, 172, 40, 128, 82, 146), Pal.Frost).Thin();
        c.Add(G.Poly(198, 172, 216, 128, 174, 146), Pal.Frost).Thin();
        c.Add(G.Heart(128, 170, 70), Pal.Silver);
        c.Flush();
        c.Add(G.Heart(128, 170, 50), Pal.Frost).Then(() =>
        {
            c.Gloss(106, 144, 12, 7, -35, 240);
            c.Stroke(Pal.Frost.D, 4, 128, 136, 128, 212);
            c.Stroke(Pal.Frost.D, 4, 98, 152, 158, 196);
            c.Stroke(Pal.Frost.D, 4, 158, 152, 98, 196);
        }).Thin();
        c.Flush();
    }

    // An open neck ring with charged ends
    static void StormcallersTorc(Ic c)
    {
        Tiles.Lightning(c);
        c.Glow(128, 200, 88, Pal.Hex("#FFE85A"), 170);
        c.Add(G.Arc(128, 118, 82, 34, 118, 304), Pal.Gold).Then(() =>
        {
            for (int i = 0; i < 16; i++)
            {
                double a = (130 + i * 18.5) * Math.PI / 180;
                c.Stroke(Pal.Gold.D, 5, 128 + (float)Math.Cos(a) * 68, 118 + (float)Math.Sin(a) * 68,
                                        128 + (float)Math.Cos(a + .22) * 96, 118 + (float)Math.Sin(a + .22) * 96);
            }
        });
        c.Add(G.Circle(88, 192, 25), Pal.Lightning);
        c.Add(G.Circle(168, 192, 25), Pal.Lightning);
        c.Flush();
        c.Add(G.Bolt(96, 150, 66, 96), Pal.Lightning).Thin();
        c.Outer = 5;
        c.Flush();
        c.Outer = 7;
    }

    // A snake's fang on a cord, still dripping
    static void VipersFang(Ic c)
    {
        Tiles.Poison(c);
        c.Glow(128, 176, 96, Pal.Hex("#8CFF3A"), 130);
        Cord(c, Pal.DarkLeather);
        c.Add(G.RR(100, 104, 56, 28, 10), Pal.Gold);
        c.Add(G.Soft(100, 126, 158, 126, 164, 164, 150, 200, 118, 242, 116, 200, 102, 164), Pal.Bone).Then(() =>
        {
            c.Fill(G.Soft(100, 196, 170, 186, 130, 250, 100, 250), Pal.Hex("#9CFF4A"));
            c.Gloss(118, 150, 6, 18, 8, 200);
        });
        c.Flush();
        c.Add(G.Drop(164, 226, 11), Pal.Poison).Thin();
        c.Outer = 5;
        c.Flush();
        c.Outer = 7;
    }

    // ---------- Rings ----------

    static void Band(Ic c, Pal p)
    {
        c.Add(G.Arc(128, 150, 66, 32, 0, 360), p);
    }

    static void CopperRing(Ic c)
    {
        Tiles.Item(c);
        Band(c, Pal.Copper);
        c.Add(G.RR(104, 58, 48, 32, 10), Pal.Copper);
        c.Flush();
        Stone(c, 128, 62, 28, Pal.Teal);
        c.Flush();
    }

    static void SilverRing(Ic c)
    {
        Tiles.Item(c);
        Band(c, Pal.Silver);
        c.Add(G.Poly(92, 86, 104, 56, 152, 56, 164, 86), Pal.Silver);
        c.Flush();
        Gem(c, 128, 58, 40, Pal.Sapphire);
        c.Flush();
    }

    static void GoldRing(Ic c)
    {
        Tiles.Item(c);
        Band(c, Pal.Gold);
        c.Add(G.Poly(88, 84, 104, 50, 152, 50, 168, 84), Pal.Gold);
        c.Flush();
        Gem(c, 128, 56, 44, Pal.Ruby);
        c.Flush();
    }

    // Burning stone in a blackened band: fire spreads from it
    static void EmberheartRing(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(128, 74, 98, Pal.Hex("#FF7A1A"), 190);
        Band(c, new Pal("#6A5A5C", "#3E3438", "#221C22"));
        c.Add(G.Poly(92, 84, 104, 66, 152, 66, 164, 84), Pal.Iron);
        c.Flush();
        c.Add(G.Flame(128, 88, 84, 84), Pal.Fire).Then(() => c.Fill(G.Flame(128, 88, 40, 46), Pal.Hex("#FFF4C0")));
        c.Flush();
    }

    // A flat signet with the eye of insight
    static void SignetOfTheSage(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 84, 104, Pal.Hex("#FFE08A"), 130);
        Band(c, Pal.Gold);
        c.Flush();
        c.Add(G.RR(60, 16, 136, 124, 30), Pal.Gold);
        c.Flush();
        c.Add(G.RR(78, 34, 100, 88, 20), Pal.Sapphire).Then(() =>
        {
            c.Fill(G.Ell(128, 78, 38, 22), Pal.Hex("#FFF2C0"));
            c.Fill(G.Circle(128, 78, 16), Ic.Ink);
            c.Gloss(121, 72, 5, 4, 0, 255);
        }).Thin();
        c.Flush();
    }

    // ---------- Belts ----------

    static void Strap(Ic c, Pal p, float width)
    {
        c.Add(G.Rope(width, 22, 104, 74, 128, 128, 136, 182, 128, 234, 104), p);
    }

    static void Sash(Ic c)
    {
        Tiles.Item(c);
        c.Add(G.Soft(104, 150, 136, 150, 118, 240, 76, 232), Pal.Linen);
        c.Add(G.Soft(122, 150, 156, 150, 190, 228, 150, 240), Pal.Linen);
        Strap(c, Pal.Linen, 46);
        c.Add(G.Soft(102, 110, 154, 110, 158, 162, 98, 162), Pal.Linen);
        c.Flush();
    }

    static void PlatedBelt(Ic c)
    {
        Tiles.Item(c);
        Strap(c, Pal.DarkLeather, 54);
        c.Add(G.Soft(30, 80, 76, 98, 70, 150, 22, 128), Pal.Iron);
        c.Add(G.Soft(226, 80, 180, 98, 186, 150, 234, 128), Pal.Iron);
        c.Add(G.RR(90, 92, 76, 86, 14), Pal.Steel).Then(() =>
        {
            c.Fill(G.RR(106, 112, 44, 46, 8), Pal.Hex("#3A2822"));
            c.Fill(G.RR(124, 118, 44, 10, 4), Pal.Hex("#A8AEBA"));
            Rivets(c, Color.White, 100, 102, 156, 102, 100, 168, 156, 168);
        });
        c.Flush();
    }

    // A belt for something much bigger than you: the buckle shakes the ground
    static void GirdleOfGiants(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 136, 112, Pal.Hex("#FFC870"), 90);
        Strap(c, Pal.Bronze, 70);
        c.Add(G.Circle(128, 136, 72), Pal.Stone).Then(() =>
        {
            c.Ink2(7, 128, 76, 112, 110, 134, 134, 116, 164, 128, 196);
            c.Ink2(6, 134, 134, 170, 120);
            c.Ink2(6, 116, 164, 84, 150);
        });
        c.Add(G.Ring(128, 136, 78, 62), Pal.Bronze).Thin();
        c.Flush();
        Rivets(c, Pal.Hex("#FFEBA0"), 40, 104, 40, 148, 216, 104, 216, 148);
    }

    // A knotted rope that pulls like the tide
    static void RiptideCord(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 136, 108, Pal.Hex("#3FC4B4"), 130);
        var rope = new Pal("#A8F0E4", "#3FC4B4", "#1C7F7C");
        c.Add(G.Rope(16, 112, 170, 96, 214, 66, 236), rope);
        c.Add(G.Rope(16, 144, 170, 160, 214, 190, 236), rope);
        c.Add(G.Rope(30, 18, 112, 74, 122, 128, 130, 182, 122, 238, 112), rope).Then(() =>
        {
            for (int i = 0; i < 13; i++)
                c.Stroke(rope.D, 5, 18 + i * 18, 94, 28 + i * 18, 140);
        });
        c.Add(G.Circle(128, 132, 44), rope).Then(() =>
        {
            c.Stroke(rope.D, 7, 96, 150, 104, 110, 150, 106, 164, 140, 134, 158, 112, 136, 132, 120);
        });
        c.Flush();
    }

    // Patched and stitched, but it holds
    static void SurvivorsCinch(Ic c)
    {
        Tiles.Rose(c);
        c.Glow(128, 132, 108, Pal.Hex("#F2789C"), 110);
        c.Add(G.Soft(150, 150, 188, 150, 198, 234, 168, 240), Pal.Leather);
        Strap(c, Pal.Leather, 56);
        c.Add(G.Soft(40, 90, 76, 96, 72, 150, 34, 140), Pal.Cloth).Then(() =>
        {
            c.Ink2(3.5f, 44, 100, 52, 92);
            c.Ink2(3.5f, 64, 104, 70, 96);
            c.Ink2(3.5f, 38, 138, 46, 146);
            c.Ink2(3.5f, 58, 142, 64, 150);
        });
        c.Add(G.RR(92, 96, 72, 80, 14), Pal.Iron);
        c.Flush();
        c.Add(G.Heart(128, 136, 28), Pal.Rose).Then(() => c.Gloss(116, 124, 6, 4, -35, 220)).Thin();
        c.Outer = 3;
        c.Flush();
        c.Outer = 7;
    }

    // ---------- Boots ----------

    static System.Drawing.Drawing2D.GraphicsPath BootBody()
    {
        return G.Round(26, 98, 24, 184, 24, 188, 150, 204, 214, 20, 214, 20, 172, 62, 150, 96, 124);
    }

    static void Sole(Ic c, Pal p)
    {
        c.Add(G.Round(9, 12, 198, 112, 198, 118, 212, 150, 212, 152, 198, 212, 198, 212, 238, 146, 238, 140, 226, 112, 226, 106, 238, 12, 238), p);
    }

    static void LeatherBoots(Ic c)
    {
        Tiles.Item(c);
        Sole(c, Pal.DarkLeather);
        c.Add(BootBody(), Pal.Leather).Then(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                float y = 70 + i * 26;
                c.Ink2(6, 92 - i * 4, y, 124 - i * 2, y + 12);
                c.Ink2(6, 124 - i * 2, y, 92 - i * 4, y + 12);
            }
        });
        c.Add(G.RR(76, 18, 114, 34, 12), Pal.DarkLeather);
        c.Flush();
    }

    static void PlatedBoots(Ic c)
    {
        Tiles.Item(c);
        Sole(c, Pal.Iron);
        c.Add(BootBody(), Pal.Steel).Then(() =>
        {
            c.Fill(G.Poly(150, 20, 200, 20, 210, 220, 150, 220), Pal.Steel.D, 110);
            c.Ink2(5, 96, 92, 186, 92);
            c.Ink2(5, 96, 130, 188, 130);
            c.Ink2(5, 56, 158, 72, 212);
            c.Ink2(5, 92, 140, 112, 212);
            c.Ink2(5, 132, 134, 150, 212);
            Rivets(c, Color.White, 118, 62, 164, 62, 118, 110, 164, 110);
        });
        c.Add(G.RR(84, 14, 112, 30, 10), Pal.Gold);
        c.Flush();
    }

    // Each step leaves something behind
    static void Blightwalkers(Ic c)
    {
        Tiles.Poison(c);
        c.Glow(118, 226, 112, Pal.Hex("#8CFF3A"), 170);
        var rot = new Pal("#7A8C5C", "#4A5A3E", "#2A3426");
        c.Add(G.Soft(4, 228, 40, 212, 214, 212, 252, 228, 214, 246, 40, 246), Pal.Poison).Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
        Sole(c, Pal.DarkLeather);
        c.Add(BootBody(), rot).Then(() =>
        {
            c.Fill(G.Soft(36, 160, 94, 148, 110, 214, 26, 214), Pal.Hex("#9CFF4A"), 60);
            c.Fill(G.Circle(128, 96, 22), Pal.Hex("#9CFF4A"));
            c.Fill(G.Circle(158, 142, 13), Pal.Hex("#9CFF4A"));
            c.Fill(G.Circle(94, 158, 11), Pal.Hex("#9CFF4A"));
            c.Gloss(122, 90, 7, 5, -30, 230);
        });
        c.Add(G.RR(76, 18, 114, 34, 12), rot);
        c.Flush();
        c.Add(G.Drop(40, 152, 12), Pal.Poison).Thin();
        c.Add(G.Drop(226, 150, 10), Pal.Poison).Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
    }

    // They never stop: little wings and a rush of air
    static void RestlessTreads(Ic c)
    {
        Tiles.Rose(c);
        c.Glow(128, 128, 112, Pal.Hex("#F2789C"), 110);
        c.Add(G.RR(204, 74, 44, 14, 7), Pal.Mirror).Thin();
        c.Add(G.RR(214, 118, 34, 14, 7), Pal.Mirror).Thin();
        c.Add(G.RR(208, 160, 40, 14, 7), Pal.Mirror).Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
        c.Add(G.Soft(170, 66, 236, 26, 246, 62, 226, 74, 234, 96, 210, 110, 214, 124, 172, 130), Pal.Mirror);
        Sole(c, Pal.Bronze);
        c.Add(BootBody(), Pal.Rose).Then(() =>
        {
            c.Stroke(Pal.Hex("#FFC6D6"), 8, 40, 186, 120, 186);
            c.Stroke(Pal.Hex("#FFC6D6"), 8, 104, 100, 168, 100);
        });
        c.Add(G.RR(76, 18, 114, 34, 12), Pal.Gold);
        c.Flush();
    }

    // Blue boots with the storm in the stride
    static void Stormstriders(Ic c)
    {
        Tiles.Lightning(c);
        c.Glow(128, 120, 112, Pal.Hex("#FFE85A"), 110);
        Sole(c, Pal.Iron);
        c.Add(BootBody(), Pal.Sapphire);
        c.Add(G.RR(76, 18, 114, 34, 12), Pal.Gold);
        c.Flush();
        c.Add(G.Bolt(82, 58, 94, 134), Pal.Lightning);
        c.Outer = 5;
        c.Flush();
        c.Outer = 7;
        c.Spark(214, 76, 15, Pal.Hex("#FFE85A"));
        c.Spark(44, 110, 12, Pal.Hex("#FFE85A"));
    }

    // ---------- Chest armor ----------

    static System.Drawing.Drawing2D.GraphicsPath Torso()
    {
        return G.Soft(70, 26, 104, 26, 128, 52, 152, 26, 186, 26, 240, 80, 212, 128, 188, 108, 190, 236, 66, 236, 68, 108, 44, 128, 16, 80);
    }

    static void QuiltedArmor(Ic c)
    {
        Tiles.Item(c);
        c.Add(Torso(), Pal.Cloth).Then(() =>
        {
            for (int i = -6; i < 12; i++)
            {
                c.Stroke(Pal.Cloth.D, 3.5f, i * 34, 20, i * 34 + 220, 240);
                c.Stroke(Pal.Cloth.D, 3.5f, i * 34 + 220, 20, i * 34, 240);
            }
            c.Fill(G.RR(120, 52, 16, 186, 4), Pal.Hex("#8E7C64"));
        });
        c.Flush();
    }

    static void FullPlateMail(Ic c)
    {
        Tiles.Item(c);
        c.Add(Torso(), Pal.Steel).Then(() =>
        {
            c.Fill(G.Poly(128, 40, 128, 240, 200, 240, 200, 40), Pal.Steel.D, 90);
            c.Ink2(5, 128, 54, 128, 236);
            c.Ink2(5, 68, 170, 128, 196, 188, 170);
            c.Ink2(5, 68, 200, 128, 226, 188, 200);
            c.Gloss(96, 96, 15, 26, 18, 120);
            Rivets(c, Pal.Hex("#FFEBA0"), 88, 60, 168, 60);
        });
        c.Add(G.Rope(13, 104, 30, 128, 58, 152, 30), Pal.Gold).Thin();
        c.Flush();
    }

    // Worn by people who won't be stopped by being nearly dead
    static void DeathwishCuirass(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(128, 128, 112, Pal.Hex("#D92F45"), 130);
        c.Add(Torso(), new Pal("#6A5868", "#3E3244", "#221A2C")).Then(() =>
        {
            c.Ink2(5, 68, 200, 128, 226, 188, 200);
            Rivets(c, Pal.Hex("#D92F45"), 88, 60, 168, 60);
        });
        c.Flush();
        c.Add(G.RR(106, 146, 44, 32, 8), Pal.Bone).Thin();
        c.Add(G.Circle(128, 118, 44), Pal.Bone).Then(() =>
        {
            c.Fill(G.Ell(110, 122, 12, 14), Ic.Ink);
            c.Fill(G.Ell(146, 122, 12, 14), Ic.Ink);
            c.Fill(G.Ell(110, 124, 5, 6), Pal.Hex("#FF5A6A"));
            c.Fill(G.Ell(146, 124, 5, 6), Pal.Hex("#FF5A6A"));
            c.Fill(G.Poly(128, 134, 136, 148, 120, 148), Ic.Ink);
        }).Thin();
        c.Outer = 5;
        c.Flush();
        c.Outer = 7;
    }

    // Mail with a promise: every hit is paid for later, in gold links
    static void DebtorsHauberk(Ic c)
    {
        Tiles.Candle(c);
        c.Glow(128, 128, 112, Pal.Hex("#FFC870"), 90);
        c.Add(Torso(), Pal.Iron).Then(() => Mail(c, 10, 20, 248, 244, 19, Pal.Hex("#3C404C")));
        c.Flush();
        c.Add(G.Circle(128, 138, 42), Pal.Gold).Then(() =>
        {
            c.Fill(G.Ring(128, 138, 32, 27), Pal.Gold.D);
            c.Fill(G.Poly(104, 112, 152, 112, 128, 138), Pal.Gold.D);
            c.Fill(G.Poly(104, 164, 152, 164, 128, 138), Pal.Gold.D);
        });
        c.Flush();
        c.Add(G.Circle(84, 214, 21), Pal.Gold).Thin();
        c.Add(G.Circle(172, 214, 21), Pal.Gold).Thin();
        c.Outer = 5;
        c.Flush();
        c.Outer = 7;
    }

    // Dragon scale around a live coal
    static void HeartOfTheWyrm(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(128, 132, 116, Pal.Hex("#FF7A1A"), 150);
        c.Add(Torso(), Pal.Ruby).Then(() => Scales(c, 6, 28, 252, 240, 34, Pal.Ruby));
        c.Flush();
        c.Add(G.Heart(128, 130, 46), Pal.Fire).Then(() => c.Fill(G.Flame(128, 168, 46, 62), Pal.Hex("#FFF4C0")));
        c.Flush();
    }

    // A hood, a rope, nothing else
    static void HermitsShroud(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 96, 104, Pal.Hex("#FFC870"), 90);
        c.Add(G.Soft(42, 106, 92, 66, 164, 66, 214, 106, 226, 240, 30, 240), Pal.Moss).Then(() =>
        {
            c.Stroke(Pal.Moss.D, 5, 96, 160, 88, 236);
            c.Stroke(Pal.Moss.D, 5, 160, 160, 170, 236);
        });
        c.Add(G.Soft(128, 12, 182, 40, 192, 104, 128, 132, 64, 104, 74, 40), Pal.Moss).Then(() =>
        {
            c.Fill(G.Soft(128, 42, 162, 62, 164, 100, 128, 118, 92, 100, 94, 62), Pal.Hex("#1A1218"));
        });
        c.Add(G.Rope(13, 44, 164, 128, 178, 212, 164), Pal.Cloth).Thin();
        c.Add(G.Rope(10, 104, 176, 96, 226), Pal.Cloth).Thin();
        c.Flush();
    }

    // ---------- Gloves ----------

    static void Hand(Ic c, Pal p, Pal cuff, bool thick)
    {
        float w = thick ? 34 : 30;
        for (int i = 0; i < 4; i++)
        {
            float x = 76 + i * 37;
            float top = i == 1 || i == 2 ? 20 : 38;
            c.Add(G.RR(x - w / 2, top, w, 118, w / 2), p);
        }
        c.Add(G.Rope(thick ? 38 : 34, 66, 158, 38, 120, 28, 96), p);
        c.Add(G.Round(24, 56, 100, 200, 100, 194, 188, 64, 188), p);
        c.Add(G.Round(12, 60, 178, 196, 178, 208, 240, 48, 240), cuff);
    }

    static void LeatherGloves(Ic c)
    {
        Tiles.Item(c);
        Hand(c, Pal.Leather, Pal.DarkLeather, false);
        c.Flush();
        c.Ink2(5, 96, 150, 128, 142, 160, 150);
    }

    static void Gauntlets(Ic c)
    {
        Tiles.Item(c);
        Hand(c, Pal.Steel, Pal.Iron, true);
        c.Add(G.Soft(54, 104, 204, 104, 200, 150, 58, 150), Pal.Steel).Then(() =>
            Rivets(c, Color.White, 80, 126, 116, 126, 152, 126, 180, 126));
        c.Flush();
    }

    // Stained and heavy
    static void ButchersGrips(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(128, 128, 112, Pal.Hex("#D92F45"), 110);
        Hand(c, Pal.DarkLeather, Pal.Cloth, true);
        c.Flush();
        c.Add(G.Drop(100, 148, 24), Pal.Blood).Then(() => c.Gloss(92, 146, 6, 9, 15, 200)).Thin();
        c.Add(G.Drop(164, 118, 13), Pal.Blood).Thin();
        c.Add(G.Drop(146, 170, 10), Pal.Blood).Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
    }

    // A closed spiked fist that cracks what it hits
    static void FistsOfRuin(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 112, 112, Pal.Hex("#FF8A2A"), 90);
        for (int i = 0; i < 4; i++)
            c.Add(G.Poly(50 + i * 42, 64, 68 + i * 42, 10, 86 + i * 42, 64), Pal.Silver);
        for (int i = 0; i < 4; i++)
            c.Add(G.RR(44 + i * 42, 48, 46, 76, 14), Pal.Steel);
        c.Add(G.Round(20, 42, 96, 214, 96, 208, 178, 48, 178), Pal.Iron).Then(() =>
        {
            c.Ink2(9, 110, 178, 134, 150, 160, 158, 176, 136, 214, 130);
            c.Stroke(Pal.Hex("#FF8A2A"), 4.5f, 110, 178, 134, 150, 160, 158, 176, 136, 214, 130);
        });
        c.Add(G.Round(16, 26, 122, 128, 112, 138, 150, 34, 170), Pal.Steel);
        c.Add(G.Round(12, 60, 170, 196, 170, 208, 240, 48, 240), Pal.Iron).Then(() =>
            Rivets(c, Pal.Hex("#FFC870"), 78, 214, 128, 214, 178, 214));
        c.Flush();
    }

    // Knuckles of ice that shatter off
    static void RimeshardGrips(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(128, 96, 112, Pal.Hex("#7FE6FF"), 130);
        Hand(c, Pal.Linen, Pal.Silver, true);
        c.Flush();
        for (int i = 0; i < 4; i++)
            c.Add(G.Poly(52 + i * 38, 134, 74 + i * 38, 66, 92 + i * 38, 134), Pal.Frost);
        c.Flush();
        c.Spark(214, 42, 13, Color.White);
    }

    // ---------- Helmets ----------

    static void Cap(Ic c)
    {
        Tiles.Item(c);
        c.Add(G.Soft(30, 178, 30, 76, 90, 28, 166, 28, 226, 76, 226, 178), Pal.Leather).Then(() =>
        {
            c.Stroke(Pal.Leather.D, 5, 128, 32, 128, 170);
            c.Stroke(Pal.Leather.D, 5, 76, 42, 66, 170);
            c.Stroke(Pal.Leather.D, 5, 180, 42, 190, 170);
        });
        c.Add(G.Soft(14, 158, 242, 158, 242, 208, 14, 208), Pal.DarkLeather).Then(() =>
        {
            for (int i = 0; i < 11; i++)
                c.Ink2(3.5f, 26 + i * 20, 188, 34 + i * 20, 194);
        });
        c.Flush();
    }

    static void Circlet(Ic c)
    {
        Tiles.Item(c);
        c.Add(G.EllRing(128, 150, 106, 58, 20), Pal.Silver);
        c.Add(G.Poly(84, 204, 128, 74, 172, 204), Pal.Silver);
        c.Flush();
        Gem(c, 128, 164, 34, Pal.Sapphire);
        c.Flush();
    }

    static void GreatHelm(Ic c)
    {
        Tiles.Item(c);
        c.Add(G.Soft(46, 42, 88, 18, 168, 18, 210, 42, 216, 226, 40, 226), Pal.Steel).Then(() =>
        {
            c.Fill(G.RR(44, 94, 170, 26, 6), Ic.Ink);
            c.Fill(G.RR(116, 20, 24, 210, 4), Pal.Hex("#EEBB4A"));
            c.Fill(G.RR(44, 88, 170, 8, 2), Pal.Hex("#EEBB4A"));
            c.Fill(G.RR(112, 94, 32, 26, 2), Ic.Ink);
            for (int i = 0; i < 3; i++)
            {
                c.Fill(G.Circle(78, 152 + i * 24, 6), Ic.Ink);
                c.Fill(G.Circle(178, 152 + i * 24, 6), Ic.Ink);
            }
        });
        c.Flush();
    }

    // A very old crown, worn smooth: it can wait
    static void CrownOfAges(Ic c)
    {
        Tiles.Candle(c);
        c.Glow(128, 128, 112, Pal.Hex("#FFC870"), 130);
        c.Add(G.Poly(30, 196, 18, 64, 76, 118, 128, 44, 180, 118, 238, 64, 226, 196), Pal.Gold).Then(() =>
        {
            c.Fill(G.Soft(150, 120, 240, 90, 240, 200, 190, 200), Pal.Moss.M, 120);
        });
        c.Add(G.RR(24, 172, 208, 52, 18), Pal.Gold).Then(() =>
        {
            Rivets(c, Pal.Hex("#FFEFC4"), 52, 198, 204, 198);
        });
        Stone(c, 18, 60, 15, Pal.Sapphire);
        Stone(c, 128, 40, 18, Pal.Ruby);
        Stone(c, 238, 60, 15, Pal.Sapphire);
        c.Flush();
        c.Add(G.Circle(128, 180, 42), Pal.Bone).Then(() =>
        {
            for (int i = 0; i < 12; i++)
            {
                double a = i * Math.PI / 6;
                c.Ink2(4, 128 + (float)Math.Cos(a) * 30, 180 + (float)Math.Sin(a) * 30, 128 + (float)Math.Cos(a) * 36, 180 + (float)Math.Sin(a) * 36);
            }
            c.Ink2(7, 128, 180, 128, 156);
            c.Ink2(7, 128, 180, 146, 188);
        }).Thin();
        c.Flush();
    }

    // Just two eyes in a dark hood, and a stolen soul
    static void ThiefsCowl(Ic c)
    {
        Tiles.Shadow(c);
        c.Glow(128, 128, 112, Pal.Hex("#B57BFF"), 90);
        var cloth = new Pal("#7E6AA0", "#4C3C6C", "#2A2040");
        c.Add(G.Soft(20, 236, 40, 150, 62, 56, 128, 10, 194, 56, 216, 150, 236, 236), cloth).Then(() =>
        {
            c.Fill(G.Soft(128, 58, 172, 92, 178, 170, 128, 200, 78, 170, 84, 92), Pal.Hex("#14101E"));
            c.Fill(G.Soft(90, 128, 118, 134, 114, 148, 96, 146), Pal.Hex("#E6D0FF"));
            c.Fill(G.Soft(166, 128, 138, 134, 142, 148, 160, 146), Pal.Hex("#E6D0FF"));
            c.Stroke(cloth.D, 6, 40, 200, 60, 140);
            c.Stroke(cloth.D, 6, 216, 200, 196, 140);
        });
        c.Flush();
        c.Add(G.Drop(206, 66, 19), new Pal("#FFFFFF", "#B4F4E8", "#44B4A8")).Then(() => c.Gloss(200, 64, 5, 8, 15, 240)).Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
    }

    // A storm cloud sits on the crest
    static void ThunderheadHelm(Ic c)
    {
        Tiles.Lightning(c);
        c.Glow(128, 100, 112, Pal.Hex("#FFE85A"), 110);
        c.Add(G.Soft(56, 104, 200, 104, 208, 232, 146, 232, 128, 176, 110, 232, 48, 232), Pal.Steel).Then(() =>
        {
            c.Fill(G.RR(46, 142, 166, 18, 5), Ic.Ink);
            c.Fill(G.RR(116, 100, 24, 90, 5), Pal.Hex("#EEBB4A"));
        });
        c.Flush();
        var cloud = new Pal("#D4D8EC", "#8E94C0", "#50568C");
        c.Add(G.Circle(70, 86, 42), cloud);
        c.Add(G.Circle(124, 64, 52), cloud);
        c.Add(G.Circle(184, 84, 44), cloud);
        c.Add(G.Soft(44, 88, 212, 88, 212, 122, 44, 122), cloud);
        c.Flush();
        c.Add(G.Bolt(26, 118, 50, 80), Pal.Lightning).Thin();
        c.Add(G.Bolt(186, 116, 50, 80), Pal.Lightning).Thin();
        c.Outer = 5;
        c.Flush();
        c.Outer = 7;
    }

    // ---------- Legs ----------

    static System.Drawing.Drawing2D.GraphicsPath Trousers()
    {
        return G.Soft(66, 22, 190, 22, 216, 234, 146, 234, 128, 112, 110, 234, 40, 234);
    }

    static void Waist(Ic c, Pal p)
    {
        c.Add(G.RR(56, 16, 144, 34, 10), p);
    }

    static void ClothLeggings(Ic c)
    {
        Tiles.Item(c);
        c.Add(Trousers(), Pal.Linen).Then(() =>
        {
            c.Stroke(Pal.Linen.D, 5, 82, 60, 68, 228);
            c.Stroke(Pal.Linen.D, 5, 174, 60, 188, 228);
            c.Fill(G.Soft(50, 134, 104, 134, 102, 176, 46, 176), Pal.Hex("#8FA8C8"));
            c.Ink2(3.5f, 54, 140, 98, 140);
            c.Ink2(3.5f, 52, 170, 98, 170);
        });
        Waist(c, Pal.Cloth);
        c.Flush();
    }

    static void MailChausses(Ic c)
    {
        Tiles.Item(c);
        c.Add(Trousers(), Pal.Iron).Then(() => Mail(c, 30, 40, 226, 236, 19, Pal.Hex("#3C404C")));
        Waist(c, Pal.DarkLeather);
        c.Flush();
    }

    // Ice runs in the veins of these
    static void FrostveinLeggings(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(128, 128, 112, Pal.Hex("#7FE6FF"), 110);
        c.Add(Trousers(), new Pal("#6A7CA8", "#3F5078", "#28325A")).Then(() =>
        {
            var vein = Pal.Hex("#BFF3FF");
            c.Stroke(vein, 8, 84, 40, 94, 90, 72, 136, 84, 176, 72, 236);
            c.Stroke(vein, 6, 94, 90, 112, 112);
            c.Stroke(vein, 6, 72, 136, 54, 160);
            c.Stroke(vein, 8, 172, 40, 164, 90, 184, 136, 172, 176, 186, 236);
            c.Stroke(vein, 6, 164, 90, 146, 112);
            c.Stroke(vein, 6, 184, 136, 202, 160);
        });
        Waist(c, Pal.Silver);
        c.Flush();
    }

    // Battered plate, one more time
    static void LegsOfTheFallen(Ic c)
    {
        Tiles.Physical(c);
        c.Glow(128, 128, 112, Pal.Hex("#D92F45"), 80);
        c.Add(Trousers(), Pal.Iron).Then(() =>
        {
            c.Ink2(5, 56, 196, 74, 208, 66, 234);
            c.Ink2(5, 190, 74, 176, 94, 192, 110);
        });
        c.Add(G.Poly(38, 118, 112, 112, 106, 176, 44, 182), Pal.Steel).Then(() => c.Ink2(5, 58, 116, 74, 142, 64, 180));
        c.Add(G.Poly(144, 112, 218, 118, 212, 182, 150, 176), Pal.Steel);
        Waist(c, Pal.DarkLeather);
        c.Flush();
        c.Add(G.Circle(180, 146, 22), Pal.Bone).Then(() =>
        {
            c.Fill(G.Ell(172, 146, 6, 7), Ic.Ink);
            c.Fill(G.Ell(188, 146, 6, 7), Ic.Ink);
        }).Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
    }

    // Plate that strikes back: a spinning edge on each knee
    static void RiposteGreaves(Ic c)
    {
        Tiles.Physical(c);
        c.Add(Trousers(), Pal.Steel).Then(() =>
        {
            c.Ink2(5, 50, 200, 104, 196);
            c.Ink2(5, 152, 196, 208, 200);
        });
        Waist(c, Pal.Gold);
        c.Flush();
        c.Add(G.Star(78, 146, 37, 13, 4, 45), Pal.Gold).Thin();
        c.Add(G.Star(178, 146, 37, 13, 4, 45), Pal.Gold).Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
    }

    // ---------- Offhand ----------

    static void Buckler(Ic c)
    {
        Tiles.Item(c);
        c.Add(G.Circle(128, 128, 104), Pal.Wood).Then(() =>
        {
            c.Stroke(Pal.Wood.D, 4, 72, 26, 72, 230);
            c.Stroke(Pal.Wood.D, 4, 128, 26, 128, 230);
            c.Stroke(Pal.Wood.D, 4, 184, 26, 184, 230);
        });
        c.Add(G.Ring(128, 128, 104, 86), Pal.Iron);
        c.Add(G.Circle(128, 128, 38), Pal.Steel);
        c.Flush();
    }

    static void TowerShield(Ic c)
    {
        Tiles.Item(c);
        c.Add(G.Soft(38, 16, 218, 16, 226, 182, 128, 248, 30, 182), Pal.Steel);
        c.Flush();
        c.Add(G.Soft(58, 36, 198, 36, 204, 172, 128, 222, 52, 172), Pal.Linen).Then(() =>
        {
            c.Fill(G.RR(116, 20, 24, 220, 2), Pal.Hex("#C8D0DC"));
            c.Fill(G.RR(40, 92, 180, 24, 2), Pal.Hex("#C8D0DC"));
        }).Thin();
        c.Flush();
    }

    static void Tome(Ic c)
    {
        Tiles.Item(c);
        c.Add(G.Soft(44, 214, 212, 214, 212, 240, 44, 240), Pal.Cloth).Thin();
        c.Add(G.Soft(34, 22, 216, 22, 224, 224, 42, 224), Pal.Plum).Then(() =>
        {
            c.Fill(G.RR(34, 18, 26, 214, 4), Pal.Hex("#483260"));
            c.Stroke(Pal.Hex("#EEBB4A"), 6, 80, 50, 192, 50);
            c.Stroke(Pal.Hex("#EEBB4A"), 6, 80, 196, 192, 196);
            c.Fill(G.Star(136, 122, 50, 21, 4, 0), Pal.Hex("#EEBB4A"));
            c.Fill(G.Star(136, 122, 26, 10, 4, 0), Color.White);
        });
        c.Add(G.RR(196, 104, 42, 36, 10), Pal.Gold);
        c.Flush();
    }

    // One lens, two ways out: shots fork through it
    static void ForkedLens(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 96, 112, Pal.Hex("#BFF3FF"), 110);
        c.Add(G.Rope(26, 128, 244, 128, 170), Pal.Wood);
        c.Add(G.Rope(24, 128, 176, 70, 150, 54, 92), Pal.Gold);
        c.Add(G.Rope(24, 128, 176, 186, 150, 202, 92), Pal.Gold);
        c.Flush();
        c.Add(G.Circle(128, 92, 72), Pal.Gold);
        c.Flush();
        c.Add(G.Circle(128, 92, 54), new Pal("#FFFFFF", "#BFF3FF", "#6AA8D0")).Then(() =>
        {
            c.Fill(G.Poly(92, 150, 164, 36, 186, 56, 116, 160), Color.White, 130);
            c.Stroke(Pal.Hex("#3F74E0"), 7, 128, 132, 128, 96);
            c.Stroke(Pal.Hex("#3F74E0"), 7, 128, 96, 102, 62);
            c.Stroke(Pal.Hex("#3F74E0"), 7, 128, 96, 154, 62);
        }).Thin();
        c.Flush();
    }

    // A small black sun with shots caught in orbit
    static void GravityOrb(Ic c)
    {
        Tiles.Arcane(c);
        c.Glow(128, 128, 116, Pal.Hex("#B57BFF"), 170);
        var ring = new Pal("#FFFFFF", "#D9C2FF", "#9060D0");
        // The back half of the ring passes behind the orb
        c.Add(G.Rot(G.Arc(128, 128, 100, 13, 180, 180), -24, 128, 128), ring).Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
        c.Add(G.Circle(128, 128, 68), new Pal("#6E5AA0", "#2E2448", "#14101E")).Then(() =>
        {
            c.Gloss(102, 100, 18, 11, -35, 120);
            c.Stroke(Pal.Hex("#B57BFF"), 4, 86, 150, 110, 178, 150, 186);
        });
        c.Flush();
        c.Add(G.Rot(G.Arc(128, 128, 100, 13, 0, 180), -24, 128, 128), ring).Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 6;
        Stone(c, 212, 74, 17, Pal.Fire);
        Stone(c, 34, 170, 14, Pal.Frost);
        c.Flush();
        c.Outer = 7;
    }

    // A proud gold-trimmed tower shield, always at the front
    static void Vanguard(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 116, Pal.Hex("#FFE08A"), 100);
        c.Add(G.Soft(38, 16, 218, 16, 226, 182, 128, 248, 30, 182), Pal.Gold);
        c.Flush();
        c.Add(G.Soft(58, 36, 198, 36, 204, 172, 128, 222, 52, 172), Pal.Sapphire).Then(() =>
        {
            c.Fill(G.Poly(128, 96, 210, 40, 210, 80, 128, 136, 46, 80, 46, 40), Pal.Hex("#FFEBA0"));
            c.Fill(G.Poly(128, 156, 210, 100, 210, 134, 128, 190, 46, 134, 46, 100), Pal.Hex("#FFEBA0"));
        }).Thin();
        c.Flush();
    }

    // ---------- Shoulders ----------

    /// <summary>A pair of layered shoulder guards joined by a strap across the neck.</summary>
    static void Pauldron(Ic c, Pal top, Pal lower, Pal rim)
    {
        c.Add(G.RR(96, 92, 64, 26, 8), Pal.DarkLeather).Thin();
        for (int side = 0; side < 2; side++)
        {
            var low = G.Rot(G.Dome(68, 184, 60, 56), -12, 68, 120);
            var mid = G.Rot(G.Dome(68, 156, 62, 62), -12, 68, 120);
            var cap = G.Rot(G.Dome(68, 128, 62, 88), -12, 68, 120);
            c.Add(side == 0 ? low : G.Mirror(low, 128), rim);
            c.Add(side == 0 ? mid : G.Mirror(mid, 128), lower);
            c.Add(side == 0 ? cap : G.Mirror(cap, 128), top);
        }
    }

    static void ClothMantle(Ic c)
    {
        Tiles.Item(c);
        c.Add(G.Soft(32, 228, 18, 124, 66, 54, 190, 54, 238, 124, 224, 228, 170, 200, 128, 228, 86, 200), Pal.Rose).Then(() =>
        {
            c.Stroke(Pal.Rose.D, 5, 70, 86, 52, 200);
            c.Stroke(Pal.Rose.D, 5, 186, 86, 204, 200);
        });
        c.Add(G.Soft(70, 76, 96, 38, 160, 38, 186, 76, 128, 118), Pal.Cloth);
        c.Flush();
        Stone(c, 128, 104, 20, Pal.Gold);
        c.Flush();
    }

    static void Spaulders(Ic c)
    {
        Tiles.Item(c);
        Pauldron(c, Pal.Leather, Pal.DarkLeather, Pal.Leather);
        c.Flush();
        Rivets(c, Pal.Hex("#FFEBA0"), 60, 112, 196, 112, 68, 158, 188, 158);
    }

    static void PlatePauldrons(Ic c)
    {
        Tiles.Item(c);
        var spike = G.Poly(26, 92, 8, 40, 62, 60);
        Pair(c, spike, Pal.Silver);
        Pauldron(c, Pal.Steel, Pal.Iron, Pal.Gold);
        c.Flush();
    }

    // An eye that never closes
    static void MantleOfTheWatcher(Ic c)
    {
        Tiles.Arcane(c);
        c.Glow(128, 120, 112, Pal.Hex("#B57BFF"), 110);
        c.Add(G.Soft(32, 228, 18, 124, 66, 54, 190, 54, 238, 124, 224, 228, 170, 200, 128, 228, 86, 200), Pal.Plum).Then(() =>
        {
            c.Stroke(Pal.Plum.D, 5, 58, 90, 44, 200);
            c.Stroke(Pal.Plum.D, 5, 198, 90, 212, 200);
        });
        c.Add(G.Soft(70, 76, 96, 38, 160, 38, 186, 76, 128, 104), Pal.Gold);
        c.Flush();
        c.Add(G.Ell(128, 148, 66, 40), Pal.Bone).Then(() =>
        {
            c.Fill(G.Circle(128, 148, 30), Pal.Hex("#8050CC"));
            c.Fill(G.Circle(128, 148, 15), Ic.Ink);
            c.Gloss(118, 138, 8, 5, -30, 240);
        });
        c.Flush();
    }

    // Shoulders of feathered flame: two wings spray with you
    static void PyrewingMantle(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(128, 90, 118, Pal.Hex("#FF7A1A"), 170);
        c.Add(G.Rot(G.Flame(58, 128, 86, 118), -24, 58, 128), Pal.Fire).Then(() => c.Fill(G.Rot(G.Flame(58, 128, 40, 64), -24, 58, 128), Pal.Hex("#FFF4C0")));
        c.Add(G.Rot(G.Flame(198, 128, 86, 118), 24, 198, 128), Pal.Fire).Then(() => c.Fill(G.Rot(G.Flame(198, 128, 40, 64), 24, 198, 128), Pal.Hex("#FFF4C0")));
        c.Flush();
        c.Add(G.Soft(32, 234, 18, 136, 66, 76, 190, 76, 238, 136, 224, 234, 170, 206, 128, 234, 86, 206), Pal.Ruby).Then(() =>
        {
            c.Stroke(Pal.Ruby.D, 5, 70, 106, 52, 206);
            c.Stroke(Pal.Ruby.D, 5, 186, 106, 204, 206);
        });
        c.Add(G.Soft(70, 96, 96, 58, 160, 58, 186, 96, 128, 136), Pal.Gold);
        c.Flush();
        Stone(c, 128, 122, 21, Pal.Candle);
        c.Flush();
    }

    // ---------- Weapons ----------

    /// <summary>A sword standing on its pommel, to be turned with Ic.Xf. tip and bladeEnd are the blade's y range, w its half width.</summary>
    static void Blade(Ic c, float tip, float bladeEnd, float w, Pal blade, Pal guard, Pal grip, Pal pommel, float guardWidth)
    {
        c.Add(G.Poly(128, tip, 128 + w, tip + w * 2.4f, 128 + w, bladeEnd, 128 - w, bladeEnd, 128 - w, tip + w * 2.4f), blade).Then(() =>
            c.Fill(G.Poly(128, tip, 128 + w, tip + w * 2.4f, 128 + w, bladeEnd, 128, bladeEnd), blade.D, 120));
        c.Add(G.RR(119, bladeEnd + 14, 18, 42, 6), grip).Thin();
        c.Add(G.RR(128 - guardWidth, bladeEnd - 4, guardWidth * 2, 22, 10), guard);
        c.Add(G.Circle(128, bladeEnd + 62, 14), pommel);
    }

    static void ShortSword(Ic c)
    {
        Tiles.Item(c);
        c.Xf(45, 128, 128, 1.08f);
        Blade(c, 2, 170, 25, Pal.Steel, Pal.Bronze, Pal.Leather, Pal.Bronze, 50);
        c.NoXf();
    }

    static void Dagger(Ic c)
    {
        Tiles.Item(c);
        c.Xf(45, 128, 128, 1.05f);
        Blade(c, 26, 150, 30, Pal.Silver, Pal.Iron, Pal.DarkLeather, Pal.Iron, 44);
        c.NoXf();
    }

    static void Bow(Ic c)
    {
        Tiles.Item(c);
        c.Xf(-45, 128, 128, 1.08f);
        c.Add(G.Rope(7, 112, 22, 112, 234), Pal.Cloth).Flat().NoLine();
        c.Add(G.Rope(26, 112, 22, 172, 56, 214, 128, 172, 200, 112, 234), Pal.Wood);
        c.Add(G.RR(196, 96, 38, 64, 10), Pal.DarkLeather).Thin();
        c.Add(G.Poly(84, 128, 62, 108, 108, 108, 124, 128, 108, 148, 62, 148), Pal.Rose).Thin();
        c.Add(G.RR(84, 122, 150, 12, 5), Pal.Wood).Thin();
        c.Add(G.Poly(262, 128, 226, 106, 226, 150), Pal.Silver).Thin();
        c.NoXf();
    }

    static void Crossbow(Ic c)
    {
        Tiles.Item(c);
        c.Xf(45, 128, 128, 1.05f);
        c.Add(G.Rope(7, 20, 104, 128, 150, 236, 104), Pal.Cloth).Flat().NoLine();
        c.Add(G.RR(110, 16, 36, 226, 10), Pal.Wood);
        c.Add(G.Rope(24, 14, 108, 62, 64, 128, 54, 194, 64, 242, 108), Pal.Iron);
        c.Add(G.Poly(128, 0, 144, 34, 112, 34), Pal.Steel).Thin();
        c.Add(G.RR(120, 30, 16, 118, 4), Pal.Steel).Thin();
        c.NoXf();
    }

    static void Staff(Ic c)
    {
        Tiles.Item(c);
        c.Xf(40, 128, 128, 1f);
        c.Add(G.RR(116, 56, 24, 202, 10), Pal.Wood);
        c.Add(G.Rope(16, 116, 74, 86, 56, 86, 18, 128, 4), Pal.Wood);
        c.Add(G.Rope(16, 140, 74, 170, 56, 170, 18, 128, 4), Pal.Wood);
        c.Add(G.RR(106, 96, 44, 18, 8), Pal.Bronze).Thin();
        c.Flush();
        Stone(c, 128, 40, 26, Pal.Sapphire);
        c.NoXf();
    }

    static void Wand(Ic c)
    {
        Tiles.Item(c);
        c.Xf(45, 128, 128, 1f);
        c.Add(G.Poly(120, 84, 136, 84, 142, 236, 114, 236), Pal.Wood);
        c.Add(G.RR(108, 200, 40, 16, 6), Pal.Gold).Thin();
        c.Add(G.Star(128, 58, 54, 25, 5, -45), Pal.Summon);
        c.NoXf();
    }

    // A hooked dagger, drawn from the dark for the first strike
    static void Ambusher(Ic c)
    {
        Tiles.Shadow(c);
        c.Glow(128, 128, 110, Pal.Hex("#8050CC"), 120);
        c.Xf(45, 128, 128, 1.05f);
        c.Add(G.Soft(112, 164, 92, 110, 126, 66, 138, 26, 176, 2, 168, 62, 150, 112, 146, 164), new Pal("#E0E0F0", "#9894BC", "#544E78"));
        c.Add(G.RR(119, 174, 18, 42, 6), Pal.DarkLeather).Thin();
        c.Add(G.Soft(80, 156, 176, 156, 164, 182, 92, 182), Pal.Shadow);
        c.Add(G.Circle(128, 228, 14), Pal.Shadow);
        c.NoXf();
        c.Spark(50, 56, 16, Pal.Hex("#E6D0FF"));
    }

    // Snapped: it swears the next one will crit
    static void BrokenPromises(Ic c)
    {
        Tiles.Rose(c);
        c.Glow(128, 128, 110, Pal.Hex("#F2789C"), 110);
        c.Xf(45, 128, 128, 1.05f);
        c.Add(G.Poly(98, 150, 98, 92, 112, 100, 122, 84, 136, 104, 150, 86, 158, 98, 158, 150), Pal.Silver).Then(() =>
            c.Fill(G.Poly(128, 60, 170, 60, 170, 150, 128, 150), Pal.Silver.D, 120));
        c.Add(G.RR(119, 164, 18, 42, 6), Pal.DarkLeather).Thin();
        c.Add(G.RR(84, 146, 88, 22, 10), Pal.Rose);
        c.Add(G.Circle(128, 212, 14), Pal.Rose);
        c.Add(G.Poly(128, 0, 156, 60, 142, 70, 128, 54, 112, 66, 100, 58), Pal.Silver);
        c.NoXf();
    }

    // A staff with a little well of water that never runs out
    static void EndlessWell(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(172, 80, 96, Pal.Hex("#3FC4B4"), 190);
        c.Xf(40, 128, 128, 1f);
        c.Add(G.RR(116, 80, 24, 178, 10), Pal.Stone);
        c.Add(G.Rope(18, 116, 96, 72, 74, 62, 26), Pal.Stone);
        c.Add(G.Rope(18, 140, 96, 184, 74, 194, 26), Pal.Stone);
        c.Add(G.RR(104, 104, 48, 18, 8), Pal.Gold).Thin();
        c.Flush();
        c.Xf(0, 128, 128, 1f);
        c.Add(G.Drop(176, 76, 37), Pal.Teal).Then(() =>
        {
            c.Gloss(160, 72, 9, 14, 15, 220);
            c.Stroke(Color.White, 6, 166, 100, 184, 100, 194, 84);
        });
        c.Flush();
        c.Add(G.Drop(226, 132, 12), Pal.Teal).Thin();
        c.Add(G.Drop(130, 56, 10), Pal.Teal).Thin();
        c.Outer = 5;
        c.Flush();
        c.Outer = 7;
    }

    // A long bow with an eye in its grip
    static void Farsight(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 112, Pal.Hex("#FFE08A"), 100);
        c.Xf(-45, 128, 128, 1.08f);
        c.Add(G.Rope(7, 112, 22, 112, 234), Pal.Mirror).Flat().NoLine();
        c.Add(G.Rope(24, 112, 22, 172, 56, 214, 128, 172, 200, 112, 234), Pal.Sapphire);
        c.Add(G.Poly(112, 6, 92, 40, 128, 36), Pal.Gold).Thin();
        c.Add(G.Poly(112, 250, 92, 216, 128, 220), Pal.Gold).Thin();
        c.Flush();
        c.Add(G.Circle(214, 128, 36), Pal.Gold);
        c.Flush();
        c.Add(G.Circle(214, 128, 22), Pal.Bone).Then(() =>
        {
            c.Fill(G.Circle(214, 128, 14), Pal.Hex("#3F74E0"));
            c.Fill(G.Circle(214, 128, 7), Ic.Ink);
        }).Thin();
        c.NoXf();
    }

    // A broad blue-hilted blade with an echo of itself
    static void MasterSword(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 118, Pal.Hex("#FFE08A"), 130);
        c.Xf(45, 128, 128, 1.08f);
        c.Add(G.Poly(166, 18, 186, 58, 186, 150, 150, 150, 150, 58), Pal.Sapphire).Flat().Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
        Blade(c, -4, 170, 28, Pal.Mirror, Pal.Sapphire, Pal.Sapphire, Pal.Gold, 62);
        c.Add(G.Poly(66, 164, 54, 186, 92, 186), Pal.Gold).Thin();
        c.Add(G.Poly(190, 164, 202, 186, 164, 186), Pal.Gold).Thin();
        c.Flush();
        c.Add(G.Star(128, 176, 13, 6, 4, 0), Pal.Gold).Flat().Thin();
        c.Outer = 3;
        c.NoXf();
        c.Outer = 7;
    }
}
