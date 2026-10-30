namespace Dcyberpunkgam;

/// <summary>
/// A weapon. Ranged weapons (pistol, rifle) define the shoot mechanic:
/// targets within <see cref="Range"/> squares (dx^2 + dy^2 <= range^2, any
/// direction, clear line of sight) can be hit for <see cref="Damage"/>.
/// Melee weapons (knife, katana) hit a target exactly one square away on one
/// of the four straight directions (left, right, up, down) for
/// <see cref="Damage"/>.
/// </summary>
public class Weapon
{
    public string Name { get; }
    public int Range { get; }
    public int Damage { get; }

    /// <summary>True for knives/katanas: hits the four directly adjacent squares.</summary>
    public bool Melee { get; }

    public static readonly Weapon Pistol = new("Pistol", 4, 2, false);
    public static readonly Weapon Rifle = new("Rifle", 6, 3, false);
    public static readonly Weapon Knife = new("Knife", 1, 3, true);
    public static readonly Weapon Katana = new("Katana", 1, 5, true);

    public Weapon(string name, int range, int damage, bool melee)
    {
        Name = name;
        Range = range;
        Damage = damage;
        Melee = melee;
    }
}
