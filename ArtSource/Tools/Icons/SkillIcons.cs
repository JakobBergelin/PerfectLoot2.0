// Active skill icons. Drawn for Assets/Data/ActiveSkills assets whose Icon was empty.
using System;
using System.Drawing;

/// <summary>Tile backgrounds: dark and quiet, tinted by the owner's trait so the silhouette carries the icon.</summary>
public static class Tiles
{
    public static void Fire(Ic c) { c.Tile("#3A1A1C", "#8A4426"); }
    public static void Frost(Ic c) { c.Tile("#14303E", "#2F7088"); }
    public static void Poison(Ic c) { c.Tile("#1A3022", "#3F7236"); }
    public static void Lightning(Ic c) { c.Tile("#232646", "#4E5088"); }
    public static void Arcane(Ic c) { c.Tile("#261C40", "#5A3F8C"); }
    public static void Physical(Ic c) { c.Tile("#26262E", "#585C68"); }
    public static void Summon(Ic c) { c.Tile("#32261C", "#7A5C34"); }
    public static void Rose(Ic c) { c.Tile("#381C2C", "#84405C"); }
    public static void Teal(Ic c) { c.Tile("#12302E", "#2C6E68"); }
    public static void Ruby(Ic c) { c.Tile("#34141E", "#7E2C38"); }
    public static void Sapphire(Ic c) { c.Tile("#182244", "#35508E"); }
    public static void Candle(Ic c) { c.Tile("#352518", "#88603A"); }
    public static void Shadow(Ic c) { c.Tile("#1C1826", "#443A5A"); }
    /// <summary>Standard items: a warm neutral, so the rarity border in the UI is the color that speaks.</summary>
    public static void Item(Ic c) { c.Tile("#2A2628", "#5C5450"); }
}

public static class SkillIcons
{
    const string Dir = "SkillIcons";

    public static void Register()
    {
        IconRegistry.Reg(Dir, "ColdSnap", ColdSnap);
        IconRegistry.Reg(Dir, "FireHydra", FireHydra);
        IconRegistry.Reg(Dir, "MirrorImage", MirrorImage);
        IconRegistry.Reg(Dir, "PoisonDart", PoisonDart);
        IconRegistry.Reg(Dir, "RaiseThrall", RaiseThrall);
        IconRegistry.Reg(Dir, "StoneGuardian", StoneGuardian);
        IconRegistry.Reg(Dir, "TeslaCoil", TeslaCoil);
        IconRegistry.Reg(Dir, "Timezone", Timezone);
        IconRegistry.Reg(Dir, "Wisp", Wisp);
        IconRegistry.Reg(Dir, "Wolf", Wolf);
    }

    // A burst of ice shards flying out from the caster
    static void ColdSnap(Ic c)
    {
        Tiles.Frost(c);
        c.Glow(128, 128, 112, Pal.Hex("#7FE6FF"), 170);
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f;
            c.Add(G.Rot(G.Poly(128, 20, 158, 72, 128, 112, 98, 72), a, 128, 128), Pal.Frost).Then(() => { });
            c.Add(G.Rot(G.Poly(128, 62, 143, 92, 128, 122, 113, 92), a + 30, 128, 128), Pal.Frost);
        }
        c.Add(G.Star(128, 128, 38, 38, 6, 30), Pal.Frost).Then(() => c.Gloss(116, 116, 12, 7, -35, 200));
        c.Flush();
        c.Spark(206, 58, 16, Color.White);
        c.Spark(52, 202, 12, Color.White);
    }

    // Three serpent necks spitting fire from one coiled base
    static void FireHydra(Ic c)
    {
        Tiles.Fire(c);
        c.Glow(128, 96, 110, Pal.Hex("#FF7A1A"), 150);
        var skin = new Pal("#FFB45A", "#EE6A2A", "#A02C1E");

        c.Add(G.Rope(26, 90, 214, 62, 170, 62, 124), skin);
        c.Add(G.Rope(26, 166, 214, 194, 170, 194, 124), skin);
        c.Add(G.Rope(32, 128, 214, 128, 150, 128, 96), skin);
        c.Add(G.Soft(40, 232, 216, 232, 200, 196, 56, 196), Pal.Bronze);
        Head(c, 58, 104, .78f, -1, skin);
        Head(c, 198, 104, .78f, 1, skin);
        Head(c, 128, 72, 1f, 0, skin);
        c.Flush();
    }

    static void Head(Ic c, float x, float y, float s, int side, Pal skin)
    {
        var head = G.Soft(x - 34 * s, y + 18 * s, x - 36 * s, y - 16 * s, x - 14 * s, y - 34 * s, x + 14 * s, y - 34 * s,
                          x + 36 * s, y - 16 * s, x + 34 * s, y + 18 * s, x + 16 * s, y + 34 * s, x - 16 * s, y + 34 * s);
        c.Add(G.Poly(x - 26 * s, y - 24 * s, x - 34 * s, y - 56 * s, x - 6 * s, y - 32 * s), Pal.Fire);
        c.Add(G.Poly(x + 26 * s, y - 24 * s, x + 34 * s, y - 56 * s, x + 6 * s, y - 32 * s), Pal.Fire);
        c.Add(head, skin).Then(() =>
        {
            c.Fill(G.Ell(x, y + 18 * s, 21 * s, 12 * s), Ic.Ink);
            c.Fill(G.Ell(x, y + 20 * s, 13 * s, 7 * s), Pal.Hex("#FFE85A"));
            c.Fill(G.Ell(x - 15 * s, y - 8 * s, 8 * s, 7 * s), Pal.Hex("#FFF4C0"));
            c.Fill(G.Ell(x + 15 * s, y - 8 * s, 8 * s, 7 * s), Pal.Hex("#FFF4C0"));
            c.Fill(G.Ell(x - 14 * s + side * 2, y - 8 * s, 3 * s, 6 * s), Ic.Ink);
            c.Fill(G.Ell(x + 14 * s + side * 2, y - 8 * s, 3 * s, 6 * s), Ic.Ink);
        });
    }

    // A hand mirror holding a dark copy with bright eyes
    static void MirrorImage(Ic c)
    {
        Tiles.Arcane(c);
        c.Glow(128, 112, 112, Pal.Hex("#B57BFF"), 120);
        c.Add(G.RR(114, 176, 28, 66, 12), Pal.Gold);
        c.Add(G.Ell(128, 108, 88, 96), Pal.Gold);
        c.Add(G.Ell(128, 108, 68, 76), new Pal("#6E5AA0", "#40325E", "#241C3A")).Then(() =>
        {
            c.Fill(G.Soft(70, 200, 74, 148, 104, 136, 152, 136, 182, 148, 186, 200), Pal.Hex("#14101E"));
            c.Fill(G.Circle(128, 102, 33), Pal.Hex("#14101E"));
            c.Fill(G.Ell(115, 104, 7, 9), Pal.Hex("#E6D0FF"));
            c.Fill(G.Ell(141, 104, 7, 9), Pal.Hex("#E6D0FF"));
            c.Fill(G.Poly(166, 30, 190, 30, 110, 190, 86, 190), Color.White, 34);
            c.Fill(G.Poly(198, 40, 208, 40, 128, 200, 118, 200), Color.White, 26);
        });
        c.Flush();
        c.Spark(208, 46, 15, Color.White);
    }

    // A flying dart with a dripping green tip
    static void PoisonDart(Ic c)
    {
        Tiles.Poison(c);
        c.Glow(90, 166, 96, Pal.Hex("#8CFF3A"), 120);
        c.Xf(45, 128, 128, 1.08f);
        c.Add(G.Poly(128, 2, 156, 76, 100, 76), Pal.Poison).Then(() => c.Fill(G.Poly(128, 8, 128, 80, 150, 80), Pal.Hex("#46A82E"), 110));
        c.Add(G.RR(106, 70, 44, 24, 9), Pal.Silver);
        c.Add(G.RR(114, 90, 28, 92, 9), Pal.Wood);
        c.Add(G.Poly(116, 146, 72, 212, 92, 238, 124, 204), Pal.Rose);
        c.Add(G.Poly(140, 146, 184, 212, 164, 238, 132, 204), Pal.Rose);
        c.Add(G.Poly(118, 156, 138, 156, 142, 240, 128, 226, 114, 240), Pal.Rose);
        c.NoXf();
        c.Add(G.Drop(66, 196, 15), Pal.Poison).Then(() => c.Gloss(60, 196, 4, 6, 20, 210));
        c.Add(G.Drop(110, 212, 10), Pal.Poison);
        c.Flush();
    }

    // A skull climbing out of a grave, called by the summoner's gold
    static void RaiseThrall(Ic c)
    {
        Tiles.Summon(c);
        c.Glow(128, 112, 112, Pal.Hex("#FFD24A"), 130);
        // Two bony hands either side
        c.Add(G.Rope(16, 56, 206, 44, 150), Pal.Bone);
        c.Add(G.Rope(10, 44, 150, 30, 116), Pal.Bone);
        c.Add(G.Rope(10, 44, 150, 48, 110), Pal.Bone);
        c.Add(G.Rope(10, 44, 150, 64, 118), Pal.Bone);
        c.Add(G.Rope(16, 200, 206, 212, 150), Pal.Bone);
        c.Add(G.Rope(10, 212, 150, 226, 116), Pal.Bone);
        c.Add(G.Rope(10, 212, 150, 208, 110), Pal.Bone);
        c.Add(G.Rope(10, 212, 150, 192, 118), Pal.Bone);
        c.Add(G.Soft(90, 188, 90, 128, 166, 128, 166, 188), Pal.Bone);
        c.Add(G.Circle(128, 96, 62), Pal.Bone).Then(() =>
        {
            c.Fill(G.Ell(103, 104, 19, 21), Ic.Ink);
            c.Fill(G.Ell(153, 104, 19, 21), Ic.Ink);
            c.Fill(G.Ell(103, 106, 8, 9), Pal.Hex("#FFD24A"));
            c.Fill(G.Ell(153, 106, 8, 9), Pal.Hex("#FFD24A"));
            c.Fill(G.Poly(128, 118, 138, 138, 118, 138), Ic.Ink);
        });
        c.Add(G.RR(102, 148, 52, 34, 10), Pal.Bone).Then(() =>
        {
            c.Ink2(4, 115, 150, 115, 182);
            c.Ink2(4, 128, 150, 128, 182);
            c.Ink2(4, 141, 150, 141, 182);
        });
        c.Add(G.Soft(16, 238, 24, 196, 76, 184, 128, 192, 180, 184, 232, 196, 240, 238), Pal.DarkLeather).Then(() =>
        {
            c.Fill(G.Ell(84, 216, 11, 5), Pal.Hex("#8C6A58"));
            c.Fill(G.Ell(170, 224, 14, 5), Pal.Hex("#8C6A58"));
        });
        c.Flush();
    }

    // A chunky stone warrior with a glowing rune on its chest
    static void StoneGuardian(Ic c)
    {
        Tiles.Summon(c);
        c.Glow(128, 128, 112, Pal.Hex("#FFD24A"), 110);
        c.Add(G.Soft(70, 176, 118, 176, 118, 240, 66, 240), Pal.Stone);
        c.Add(G.Soft(138, 176, 186, 176, 190, 240, 138, 240), Pal.Stone);
        c.Add(G.Soft(20, 86, 62, 72, 72, 164, 28, 178), Pal.Stone);
        c.Add(G.Soft(236, 86, 194, 72, 184, 164, 228, 178), Pal.Stone);
        c.Add(G.Soft(20, 150, 72, 142, 76, 196, 26, 204), Pal.Stone);
        c.Add(G.Soft(236, 150, 184, 142, 180, 196, 230, 204), Pal.Stone);
        c.Add(G.Soft(52, 74, 204, 74, 188, 194, 68, 194), Pal.Stone).Then(() =>
        {
            c.Ink2(4, 82, 120, 96, 136, 88, 166);
            c.Ink2(4, 182, 92, 170, 114);
            c.Fill(G.Star(128, 136, 34, 15, 4, 0), Pal.Hex("#FFD24A"));
            c.Fill(G.Star(128, 136, 18, 7, 4, 0), Color.White);
        });
        c.Add(G.Soft(92, 20, 164, 20, 174, 84, 82, 84), Pal.Stone).Then(() =>
        {
            c.Fill(G.RR(100, 46, 22, 10, 4), Pal.Hex("#FFD24A"));
            c.Fill(G.RR(134, 46, 22, 10, 4), Pal.Hex("#FFD24A"));
            c.Ink2(4, 112, 68, 144, 68);
        });
        c.Flush();
    }

    // A copper coil crowned with a charged ball
    static void TeslaCoil(Ic c)
    {
        Tiles.Lightning(c);
        c.Glow(128, 76, 110, Pal.Hex("#FFE85A"), 170);
        c.Add(G.Soft(58, 240, 66, 206, 190, 206, 198, 240), Pal.Iron);
        c.Add(G.RR(102, 88, 52, 130, 14), Pal.Iron);
        for (int i = 0; i < 5; i++)
        {
            float y = 196 - i * 24, w = 104 - i * 10;
            c.Add(G.RR(128 - w / 2, y - 9, w, 20, 10), Pal.Copper);
        }
        c.Add(G.Circle(128, 68, 40), Pal.Lightning).Then(() => c.Gloss(114, 54, 13, 8, -35, 230));
        c.Flush();
        c.Add(G.Bolt(16, 20, 74, 82), Pal.Lightning).Thin();
        c.Add(G.Mirror(G.Bolt(16, 34, 74, 82), 128), Pal.Lightning).Thin();
        c.Outer = 5f;
        c.Flush();
    }

    // An hourglass in a ring of slowed time
    static void Timezone(Ic c)
    {
        Tiles.Sapphire(c);
        c.Glow(128, 128, 112, Pal.Hex("#7FE6FF"), 110);
        c.Add(G.Ring(128, 128, 110, 84), Pal.Frost).Then(() =>
        {
            for (int i = 0; i < 12; i++)
            {
                double a = i * Math.PI / 6;
                c.Ink2(i % 3 == 0 ? 8 : 5, 128 + (float)Math.Cos(a) * 89, 128 + (float)Math.Sin(a) * 89,
                                            128 + (float)Math.Cos(a) * 103, 128 + (float)Math.Sin(a) * 103);
            }
        });
        c.Flush();
        c.Add(G.Soft(84, 62, 172, 62, 172, 88, 138, 128, 172, 168, 172, 194, 84, 194, 84, 168, 118, 128, 84, 88),
              new Pal("#E6F8FF", "#A8D8EE", "#6AA0C8")).Then(() =>
        {
            c.Fill(G.Soft(92, 90, 164, 90, 130, 124, 126, 124), Pal.Hex("#FFD24A"));
            c.Fill(G.Soft(92, 192, 128, 150, 164, 192), Pal.Hex("#FFD24A"));
            c.Stroke(Pal.Hex("#FFD24A"), 5, 128, 124, 128, 170);
            c.Gloss(101, 76, 5, 10, 0, 180);
        });
        c.Add(G.RR(70, 46, 116, 22, 10), Pal.Gold);
        c.Add(G.RR(70, 188, 116, 22, 10), Pal.Gold);
        c.Flush();
    }

    // A small shy spirit that keeps its friends alive
    static void Wisp(Ic c)
    {
        Tiles.Teal(c);
        c.Glow(128, 124, 118, Pal.Hex("#7CF0DC"), 190);
        var spirit = new Pal("#FFFFFF", "#B4F4E8", "#44B4A8");
        var body = G.Soft(128, 12, 150, 60, 200, 92, 206, 160, 176, 218, 128, 232, 80, 218, 50, 160, 56, 92, 106, 60);
        c.Add(body, spirit).Then(() =>
        {
            c.Fill(G.Ell(102, 142, 13, 18), Ic.Ink);
            c.Fill(G.Ell(154, 142, 13, 18), Ic.Ink);
            c.Fill(G.Circle(106, 134, 5), Color.White);
            c.Fill(G.Circle(158, 134, 5), Color.White);
            c.Fill(G.Ell(80, 170, 13, 8), Pal.Hex("#FFB4C4"));
            c.Fill(G.Ell(176, 170, 13, 8), Pal.Hex("#FFB4C4"));
            c.Ink2(5, 118, 172, 128, 180, 138, 172);
        });
        c.Flush();
        c.Spark(214, 60, 15, Color.White);
        c.Spark(38, 92, 11, Color.White);
        c.Spark(218, 208, 10, Color.White);
    }

    // A friendly wolf head, seen from the front
    static void Wolf(Ic c)
    {
        Tiles.Summon(c);
        c.Glow(128, 120, 112, Pal.Hex("#FFD24A"), 90);
        var fur = new Pal("#C0C8D8", "#8A94AC", "#4E5670");
        var pale = new Pal("#FFFFFF", "#E0E4EC", "#9CA4B8");
        c.Add(G.Soft(44, 124, 28, 16, 110, 78), fur).Then(() => c.Fill(G.Soft(56, 96, 46, 44, 92, 80), Pal.Hex("#F2A0AE")));
        c.Add(G.Soft(212, 124, 228, 16, 146, 78), fur).Then(() => c.Fill(G.Soft(200, 96, 210, 44, 164, 80), Pal.Hex("#F2A0AE")));
        c.Add(G.Soft(128, 60, 196, 76, 234, 142, 202, 158, 196, 196, 128, 240, 60, 196, 54, 158, 22, 142, 60, 76), fur).Then(() =>
        {
            c.Fill(G.Soft(128, 118, 170, 168, 160, 214, 128, 236, 96, 214, 86, 168), Pal.Hex("#E0E4EC"));
            c.Fill(G.Ell(88, 132, 15, 14), Pal.Hex("#FFD24A"));
            c.Fill(G.Ell(168, 132, 15, 14), Pal.Hex("#FFD24A"));
            c.Fill(G.Ell(90, 132, 6, 11), Ic.Ink);
            c.Fill(G.Ell(166, 132, 6, 11), Ic.Ink);
            c.Ink2(7, 68, 116, 106, 126);
            c.Ink2(7, 188, 116, 150, 126);
        });
        c.Add(G.Soft(106, 172, 150, 172, 142, 196, 128, 204, 114, 196), new Pal("#584C60", "#2A2230", "#1A1218")).Thin();
        c.Flush();
        c.Ink2(5, 108, 218, 128, 208, 148, 218);
    }
}

public static class PowerIcons { public static void Register() { } }
