namespace Dcyberpunkgam;

/// <summary>
/// A consumable item. <see cref="HealValue"/> is the hit points restored when
/// used (capped at the combatant's maximum). <see cref="ThrowRange"/> is the
/// max throw distance in squares for a throwable item (0 = not throwable);
/// <see cref="BlastDamage"/> is the damage dealt by its blast.
/// </summary>
public class Item
{
    public string Name { get; }
    public int HealValue { get; }
    public int ThrowRange { get; }
    public int BlastDamage { get; }

    public static readonly Item BioStim = new("BioStim", 6);
    public static readonly Item Grenade = new("Grenade", 0, 4, 6);

    public Item(string name, int healValue, int throwRange = 0, int blastDamage = 0)
    {
        Name = name;
        HealValue = healValue;
        ThrowRange = throwRange;
        BlastDamage = blastDamage;
    }
}
