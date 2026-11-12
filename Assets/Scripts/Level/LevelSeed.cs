/// <summary>
/// The seed the next generated level with a random seed uses (LevelLayout.RandomSeed). The host picks it before a scene
/// change and sends it to its clients (NetSceneSync), so every machine builds the same level. Unset: a random one.
/// </summary>
public static class LevelSeed
{
    public static uint? Next;

    /// <summary>The seed of the level built last (a joining player builds the host's current level with it).</summary>
    public static uint Current { get; private set; }

    /// <summary>The seed for a level built now: Next if set (then cleared), else a random one.</summary>
    public static uint Take()
    {
        uint seed = Next ?? (uint)System.DateTime.Now.Ticks;
        Next = null;
        Current = seed;
        return seed;
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Next = null;
        Current = 0;
    }
}
