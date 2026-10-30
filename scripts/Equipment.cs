namespace Dcyberpunkgam;

/// <summary>
/// Gear equipped by a combatant: head, body, a ranged weapon (shoot) and a
/// melee weapon (melee). The weapons drive their combat actions; head and
/// body are basic gear for now.
/// </summary>
public class Equipment
{
    public string Head { get; }
    public string Body { get; }
    public Weapon Weapon { get; }
    public Weapon MeleeWeapon { get; }

    public Equipment(string head, string body, Weapon weapon, Weapon meleeWeapon)
    {
        Head = head;
        Body = body;
        Weapon = weapon;
        MeleeWeapon = meleeWeapon;
    }

    /// <summary>One-line readout, e.g. "Rifle · Katana · Ballistic Cap · Weave Vest".</summary>
    public string ToText() => $"{Weapon.Name} · {MeleeWeapon.Name} · {Head} · {Body}";
}
