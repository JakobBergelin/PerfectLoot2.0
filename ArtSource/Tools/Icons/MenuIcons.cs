// The menu bar's icons (Assets/UI/MenuBar).
using System;
using System.Drawing;

public static class MenuIcons
{
    const string Dir = "MenuIcons";

    public static void Register()
    {
        IconRegistry.Reg(Dir, "Character", Character);
        IconRegistry.Reg(Dir, "Skills", Skills);
        IconRegistry.Reg(Dir, "ItemDatabase", ItemDatabase);
        IconRegistry.Reg(Dir, "Statistics", Statistics);
        IconRegistry.Reg(Dir, "GameMenu", GameMenu);
    }

    // A great helm in front of a sword
    static void Character(Ic c)
    {
        Tiles.Ruby(c);
        c.Add(G.Rot(G.Poly(116, 40, 128, 14, 140, 40, 140, 190, 116, 190), 45, 128, 128), Pal.Steel);
        c.Add(G.Rot(G.RR(84, 188, 88, 18, 8), 45, 128, 128), Pal.Gold);
        c.Add(G.Rot(G.RR(118, 204, 20, 42, 8), 45, 128, 128), Pal.Leather);
        c.Flush();
        c.Add(G.Soft(62, 110, 78, 66, 128, 48, 178, 66, 194, 110, 188, 192, 128, 222, 68, 192), Pal.Iron).Then(() => c.Gloss(96, 84, 16, 9, -35, 150));
        c.Add(G.Poly(117, 50, 139, 50, 139, 216, 117, 216), Pal.Gold);
        c.Flush();
        c.Fill(G.RR(76, 116, 40, 15, 4), Ic.Ink);
        c.Fill(G.RR(140, 116, 40, 15, 4), Ic.Ink);
        for (int i = 0; i < 3; i++)
        {
            c.Fill(G.Circle(94, 154 + i * 17, 5), Ic.Ink);
            c.Fill(G.Circle(162, 154 + i * 17, 5), Ic.Ink);
        }
    }

    // A wheel of six skills in the traits' colours around a star
    static void Skills(Ic c)
    {
        Tiles.Arcane(c);
        c.Glow(128, 128, 110, Pal.Hex("#C9A2FF"), 120);
        c.Add(G.Ring(128, 128, 84, 72), Pal.Silver);
        c.Flush();
        var colours = new[] { Pal.Fire, Pal.Lightning, Pal.Poison, Pal.Frost, Pal.Arcane, Pal.Ruby };
        for (int i = 0; i < 6; i++)
        {
            double a = (i * 60 - 90) * Math.PI / 180;
            float x = 128 + (float)Math.Cos(a) * 78, y = 128 + (float)Math.Sin(a) * 78;
            c.Add(G.Circle(x, y, 27), colours[i]).Then(() => c.Gloss(x - 9, y - 9, 8, 5, -35, 190));
        }
        c.Add(G.Star(128, 128, 44, 20, 5, -90), Pal.Gold);
        c.Flush();
    }

    // An open book with an item's gem on it
    static void ItemDatabase(Ic c)
    {
        Tiles.Teal(c);
        c.Add(G.Poly(26, 74, 128, 92, 230, 74, 230, 204, 128, 222, 26, 204), Pal.Leather);
        c.Flush();
        c.Add(G.Soft(38, 58, 84, 54, 128, 78, 128, 206, 84, 184, 38, 188), Pal.Bone);
        c.Add(G.Soft(218, 58, 172, 54, 128, 78, 128, 206, 172, 184, 218, 188), Pal.Bone);
        c.Flush();
        c.Add(G.Poly(84, 88, 106, 114, 84, 150, 62, 114), Pal.Ruby).Then(() => c.Gloss(77, 106, 7, 4, -35, 190));
        c.Flush();
        for (int i = 0; i < 4; i++) c.Stroke(Pal.Hex("#8E7C64"), 8, 150, 104 + i * 22, 198, 96 + i * 22);
    }

    // Four rising bars
    static void Statistics(Ic c)
    {
        Tiles.Sapphire(c);
        c.Add(G.RR(40, 148, 38, 66, 6), Pal.Frost);
        c.Add(G.RR(86, 116, 38, 98, 6), Pal.Poison);
        c.Add(G.RR(132, 82, 38, 132, 6), Pal.Lightning);
        c.Add(G.RR(178, 46, 38, 168, 6), Pal.Fire);
        c.Add(G.RR(26, 208, 204, 18, 8), Pal.Silver);
        c.Flush();
    }

    // A gear
    static void GameMenu(Ic c)
    {
        Tiles.Physical(c);
        for (int i = 0; i < 8; i++)
            c.Add(G.Rot(G.Poly(108, 70, 114, 26, 142, 26, 148, 70), i * 45, 128, 128), Pal.Steel);
        c.Flush();
        c.Add(G.Circle(128, 128, 76), Pal.Steel).Then(() => c.Gloss(98, 90, 18, 9, -35, 150));
        c.Flush();
        c.Add(G.Circle(128, 128, 32), Pal.Shadow);
        c.Flush();
    }
}
