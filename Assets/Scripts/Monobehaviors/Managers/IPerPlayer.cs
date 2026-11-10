/// <summary>
/// A manager or HUD each local player has their own of (0 is player 1). The rig's are player 1's; CoopManager makes
/// player 2's copies and sets PlayerIndex before their Awake.
/// </summary>
public interface IPerPlayer
{
    int PlayerIndex { get; set; }
}
