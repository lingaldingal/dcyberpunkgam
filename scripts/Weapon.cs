namespace Dcyberpunkgam;

/// <summary>
/// A ranged weapon. The equipped weapon defines the combatant's shoot
/// mechanic: targets must sit exactly <see cref="Range"/> squares away along
/// one of the 8 straight lines, and a hit deals <see cref="Damage"/>.
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
