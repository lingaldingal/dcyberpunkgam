namespace Dcyberpunkgam;

/// <summary>
/// A consumable item. <see cref="HealValue"/> is the hit points restored when
/// used (capped at the combatant's maximum).
/// </summary>
public class Item
{
    public string Name { get; }
    public int HealValue { get; }

    public static readonly Item BioStim = new("BioStim", 6);

    public Item(string name, int healValue)
    {
        Name = name;
        HealValue = healValue;
    }
}
