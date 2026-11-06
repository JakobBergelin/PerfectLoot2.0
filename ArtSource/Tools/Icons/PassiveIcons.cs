// Passive icons. Drawn for Assets/Data/Passives assets whose Icon was empty.
using System;
using System.Drawing;

public static class PassiveIcons
{
    const string Dir = "SkillIcons";

    public static void Register()
    {
        IconRegistry.Reg(Dir, "Afterglow", Afterglow);
        IconRegistry.Reg(Dir, "Anchor", Anchor);
        IconRegistry.Reg(Dir, "BloodPact", BloodPact);
        IconRegistry.Reg(Dir, "Bloodrush", Bloodrush);
        IconRegistry.Reg(Dir, "Brawler", Brawler);
        IconRegistry.Reg(Dir, "Brittle", Brittle);
        IconRegistry.Reg(Dir, "Bulwark", Bulwark);
        IconRegistry.Reg(Dir, "LifeLink", LifeLink);
        IconRegistry.Reg(Dir, "Meditation", Meditation);
        IconRegistry.Reg(Dir, "PackInstinct", PackInstinct);
        IconRegistry.Reg(Dir, "Prism", Prism);
        IconRegistry.Reg(Dir, "Quarry", Quarry);
    }

    // A warm sun setting behind a hill, the last light still strong
    static void Afterglow(Ic c)
    {
        Tiles.Rose(c);
        c.Glow(128, 150, 124, Pal.Hex("#FFC870"), 210);
        for (int i = 0; i < 7; i++)
            c.Add(G.Rot(G.Poly(116, 62, 128, 16, 140, 62), -72 + i * 24, 128, 156), Pal.Candle);
        c.Add(G.Circle(128, 156, 76), new Pal("#FFF6C8", "#FFC870", "#F07A54")).Then(() => c.Gloss(100, 116, 18, 10, -30, 170));
        c.Flush();
        c.Add(G.Soft(8, 248, 8, 176, 70, 166, 128, 186, 190, 166, 248, 176, 248, 248), new Pal("#9C5A8C", "#6E3C6C", "#40244C"));
        c.Flush();
        // Only the tile keeps the hill: the corners stay rounded
        c.Spark(46, 62, 12, Pal.Hex("#FFEFC4"));
        c.Spark(212, 50, 15, Pal.Hex("#FFEFC4"));
    }

    // A heavy iron anchor: stand still and hold
    static void Anchor(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 110, Pal.Hex("#5C88E0"), 110);
        c.Add(G.Ring(128, 44, 28, 12), Pal.Gold);
        c.Add(G.RR(114, 64, 28, 156, 10), Pal.Iron);
        c.Add(G.RR(74, 82, 108, 26, 12), Pal.Iron);
        c.Add(G.Arc(128, 132, 86, 28, 22, 136), Pal.Iron);
        c.Add(G.Poly(214, 150, 236, 118, 186, 118), Pal.Iron);
        c.Add(G.Poly(42, 150, 20, 118, 70, 118), Pal.Iron);
        c.Add(G.Circle(128, 216, 16), Pal.Iron);
        c.Flush();
    }

    // A sealed scroll: a promise signed in blood, with a paw print beside the thumb
    static void BloodPact(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(128, 128, 112, Pal.Hex("#D92F45"), 120);
        c.Add(G.Soft(58, 40, 198, 40, 198, 216, 58, 216), Pal.Cloth).Then(() =>
        {
            c.Stroke(Pal.Hex("#8E7C64"), 6, 78, 76, 178, 76);
            c.Stroke(Pal.Hex("#8E7C64"), 6, 78, 100, 178, 100);
        });
        c.Add(G.RR(36, 26, 184, 32, 16), Pal.Cloth);
        c.Add(G.RR(36, 198, 184, 32, 16), Pal.Cloth);
        c.Flush();
        c.Add(G.Drop(128, 158, 36), Pal.Blood).Then(() => c.Gloss(114, 152, 8, 13, 15, 200));
        c.Flush();
    }

    // A racing heart
    static void Bloodrush(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(146, 128, 110, Pal.Hex("#FF5A6A"), 140);
        c.Add(G.RR(16, 84, 62, 16, 8), Pal.Rose).Thin();
        c.Add(G.RR(26, 122, 46, 16, 8), Pal.Rose).Thin();
        c.Add(G.RR(16, 160, 62, 16, 8), Pal.Rose).Thin();
        c.Add(G.Heart(150, 128, 82), Pal.Blood).Then(() =>
        {
            c.Gloss(116, 88, 20, 11, -35, 190);
        });
        c.Flush();
    }

    // A wrapped fist, up close
    static void Brawler(Ic c)
    {
        Tiles.Physical(c);
        for (int i = 0; i < 8; i++)
            c.Add(G.Rot(G.Poly(118, 40, 128, 8, 138, 40), i * 45 + 22, 128, 128), Pal.Candle).Flat().Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
        c.Add(G.Soft(76, 150, 180, 150, 176, 234, 80, 234), Pal.Cloth).Then(() =>
        {
            c.Stroke(Pal.Hex("#8E7C64"), 5, 78, 182, 178, 168);
            c.Stroke(Pal.Hex("#8E7C64"), 5, 78, 208, 178, 194);
        });
        c.Add(G.Soft(54, 70, 202, 70, 208, 164, 50, 164), Pal.Flesh);
        for (int i = 0; i < 4; i++)
            c.Add(G.RR(50 + i * 40, 46 + (i == 0 || i == 3 ? 10 : 0), 38, 62, 16), Pal.Flesh);
        c.Add(G.Soft(46, 128, 150, 118, 158, 148, 52, 164), Pal.Flesh);
        c.Flush();
    }

    // An ice block with a spreading crack and a chip flying off
    static void Brittle(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(128, 128, 110, Pal.Hex("#7FE6FF"), 120);
        var side = new Pal("#8ADCF0", "#62BEDC", "#3C8CB8");
        c.Add(G.Poly(50, 92, 128, 134, 128, 226, 50, 184), side);
        c.Add(G.Poly(206, 92, 128, 134, 128, 226, 206, 184), new Pal("#5CB0D4", "#4498C0", "#2C709C"));
        c.Add(G.Poly(128, 48, 206, 92, 128, 134, 50, 92), Pal.Frost).Then(() => c.Gloss(112, 78, 22, 7, 28, 220));
        c.Flush();
        c.Ink2(6, 128, 134, 112, 158, 124, 176, 100, 202);
        c.Ink2(5, 124, 176, 152, 164, 164, 184);
        c.Ink2(5, 152, 164, 174, 134);
        c.Add(G.Poly(214, 36, 240, 54, 218, 74), Pal.Frost).Thin();
        c.Add(G.Poly(36, 42, 58, 34, 52, 60), Pal.Frost).Thin();
        c.Outer = 5;
        c.Flush();
    }

    // A castle wall with a shield on its face: strong from the front
    static void Bulwark(Ic c)
    {
        Tiles.Physical(c);
        c.Add(G.Soft(26, 90, 230, 90, 230, 232, 26, 232), Pal.Stone).Then(() =>
        {
            c.Ink2(4, 30, 136, 226, 136);
            c.Ink2(4, 30, 184, 226, 184);
            c.Ink2(4, 72, 92, 72, 136);
            c.Ink2(4, 184, 92, 184, 136);
            c.Ink2(4, 48, 136, 48, 184);
            c.Ink2(4, 208, 136, 208, 184);
            c.Ink2(4, 72, 184, 72, 230);
            c.Ink2(4, 184, 184, 184, 230);
        });
        for (int i = 0; i < 3; i++)
            c.Add(G.RR(26 + i * 76, 34, 52, 72, 10), Pal.Stone);
        c.Flush();
        c.Add(G.Soft(84, 94, 172, 94, 172, 168, 128, 222, 84, 168), Pal.Steel).Then(() =>
        {
            c.Fill(G.RR(120, 94, 16, 130, 2), Pal.Hex("#EEBB4A"));
            c.Fill(G.RR(84, 134, 90, 16, 2), Pal.Hex("#EEBB4A"));
        });
        c.Flush();
    }

    // Two hearts tied together
    static void LifeLink(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 128, 112, Pal.Hex("#FF9CB8"), 120);
        c.Add(G.Heart(70, 92, 50), Pal.Rose).Then(() => c.Gloss(50, 66, 12, 7, -35, 200));
        c.Add(G.Heart(186, 170, 50), Pal.Rose).Then(() => c.Gloss(166, 144, 12, 7, -35, 200));
        c.Flush();
        // Two gold links join them
        c.Add(G.Rot(G.Ell(112, 116, 30, 19), 34, 112, 116), Pal.Gold).Then(() => c.Fill(G.Rot(G.Ell(112, 116, 16, 7), 34, 112, 116), Pal.Hex("#6E5040")));
        c.Add(G.Rot(G.Ell(146, 140, 30, 19), 34, 146, 140), Pal.Gold).Then(() => c.Fill(G.Rot(G.Ell(146, 140, 16, 7), 34, 146, 140), Pal.Hex("#6E5040")));
        c.Outer = 5;
        c.Flush();
    }

    // A seated figure with a halo of calm
    static void Meditation(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 112, 120, Pal.Hex("#FFC870"), 150);
        c.Add(G.Ring(128, 118, 106, 90), Pal.Candle).Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
        var robe = new Pal("#7AE0D0", "#3FC4B4", "#1C7F7C");
        c.Add(G.Soft(30, 222, 40, 178, 104, 156, 152, 156, 216, 178, 226, 222, 170, 236, 86, 236), robe);
        c.Add(G.Soft(86, 186, 92, 104, 164, 104, 170, 186), robe);
        c.Add(G.Rope(22, 86, 122, 62, 172, 104, 196), robe);
        c.Add(G.Rope(22, 170, 122, 194, 172, 152, 196), robe);
        c.Add(G.Circle(128, 68, 36), Pal.Flesh).Then(() =>
        {
            c.Ink2(5, 106, 72, 114, 78, 122, 72);
            c.Ink2(5, 134, 72, 142, 78, 150, 72);
            c.Ink2(4, 120, 90, 128, 94, 136, 90);
        });
        c.Flush();
    }

    // A paw print with a sharp twinkle of critical luck
    static void PackInstinct(Ic c)
    {
        Tiles.Summon(c);
        c.Glow(128, 132, 112, Pal.Hex("#FFD24A"), 130);
        var paw = Pal.Summon;
        c.Add(G.Soft(128, 118, 180, 150, 196, 196, 160, 218, 128, 204, 96, 218, 60, 196, 76, 150), paw);
        c.Add(G.Ell(58, 108, 24, 32), paw);
        c.Add(G.Ell(106, 68, 25, 35), paw);
        c.Add(G.Ell(158, 68, 25, 35), paw);
        c.Add(G.Ell(204, 108, 24, 32), paw);
        c.Flush();
        c.Spark(214, 42, 22, Color.White);
    }

    // White light in, every element out
    static void Prism(Ic c)
    {
        Tiles.Shadow(c);
        c.Add(G.Rope(14, 10, 158, 100, 124), Pal.Mirror).Thin();
        c.Add(G.Poly(150, 120, 246, 74, 246, 102, 156, 134), Pal.Fire).Thin();
        c.Add(G.Poly(156, 130, 246, 106, 246, 134, 156, 144), Pal.Lightning).Thin();
        c.Add(G.Poly(156, 140, 246, 138, 246, 166, 156, 156), Pal.Poison).Thin();
        c.Add(G.Poly(156, 150, 246, 170, 246, 198, 150, 168), Pal.Frost).Thin();
        c.Outer = 4;
        c.Flush();
        c.Outer = 7;
        c.Add(G.Poly(128, 38, 204, 192, 52, 192), new Pal("#FFFFFF", "#C4E4F4", "#7EA4CC")).Then(() =>
        {
            c.Fill(G.Poly(128, 38, 204, 192, 128, 192), Pal.Hex("#7EA4CC"), 90);
            c.Gloss(108, 112, 8, 28, 26, 190);
        });
        c.Flush();
    }

    // A skull in the sights
    static void Quarry(Ic c)
    {
        Tiles.Ruby(c);
        c.Glow(128, 128, 110, Pal.Hex("#FF5A6A"), 100);
        c.Add(G.Ring(128, 128, 98, 74), Pal.Ruby);
        c.Add(G.RR(116, 8, 24, 62, 8), Pal.Ruby);
        c.Add(G.RR(116, 186, 24, 62, 8), Pal.Ruby);
        c.Add(G.RR(8, 116, 62, 24, 8), Pal.Ruby);
        c.Add(G.RR(186, 116, 62, 24, 8), Pal.Ruby);
        c.Flush();
        c.Add(G.RR(106, 150, 44, 28, 8), Pal.Bone);
        c.Add(G.Circle(128, 120, 44), Pal.Bone).Then(() =>
        {
            c.Fill(G.Ell(110, 124, 13, 14), Ic.Ink);
            c.Fill(G.Ell(146, 124, 13, 14), Ic.Ink);
            c.Fill(G.Poly(128, 136, 136, 150, 120, 150), Ic.Ink);
        });
        c.Flush();
    }
}
