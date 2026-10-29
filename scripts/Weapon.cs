namespace Dcyberpunkgam;

/// <summary>
/// A ranged weapon. The equipped weapon defines the combatant's shoot
/// mechanic: targets within <see cref="Range"/> squares along one of the 8
/// straight lines (with a clear line of sight) can be hit, and a hit deals
/// <see cref="Damage"/>.
/// </summary>
public class Weapon
{
    public string Name { get; }
    public int Range { get; }
    public int Damage { get; }

    public static readonly Weapon Pistol = new("Pistol", 4, 2);
    public static readonly Weapon Rifle = new("Rifle", 6, 3);

    public Weapon(string name, int range, int damage)
    {
        Name = name;
        Range = range;
        Damage = damage;
    }
}
