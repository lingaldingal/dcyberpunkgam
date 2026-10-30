namespace Dcyberpunkgam;

/// <summary>
/// Gear equipped by a combatant: head, body and weapon. The weapon drives the
/// shoot mechanic (range + damage); head and body are basic gear for now.
/// </summary>
public class Equipment
{
    public string Head { get; }
    public string Body { get; }
    public Weapon Weapon { get; }

    public Equipment(string head, string body, Weapon weapon)
    {
        Head = head;
        Body = body;
        Weapon = weapon;
    }

    /// <summary>One-line readout, e.g. "Rifle · Ballistic Cap · Weave Vest".</summary>
    public string ToText() => $"{Weapon.Name} · {Head} · {Body}";
}
