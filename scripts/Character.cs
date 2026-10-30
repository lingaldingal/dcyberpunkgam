using Godot;

namespace Dcyberpunkgam;

/// <summary>
/// A combatant (party or enemy). <see cref="Agility"/> (10 = normal, 20 = max)
/// is the base for turn order; a d5 initiative roll is added to it once at the
/// start of the encounter. <see cref="MovementRange"/> is how many tiles the
/// combatant may move on its turn: 5 + Agility/2 (rounded down).
/// <see cref="HitPoints"/> starts at 5 + Constitution and drops to zero on
/// defeat.
/// </summary>
public class Character
{
    public string Name { get; }
    public Color Color { get; }
    public int Agility { get; }
    public int Constitution { get; }
    public bool IsEnemy { get; }

    /// <summary>Equipped gear (head, body, weapon). The weapon drives the shoot mechanic.</summary>
    public Equipment Equipment { get; set; } = null!;

    /// <summary>Consumable items carried (counts per item).</summary>
    public Dictionary<Item, int> Items { get; } = new();

    /// <summary>Current hit points (starts at 5 + Constitution).</summary>
    public int HitPoints { get; set; }

    /// <summary>Full hit points: 5 + Constitution.</summary>
    public int MaxHitPoints => 5 + Constitution;

    /// <summary>True once hit points have fallen to zero.</summary>
    public bool IsDead => HitPoints <= 0;

    /// <summary>Tiles this combatant may move on its turn: 5 + Agility/2 (floor).</summary>
    public int MovementRange => 5 + Agility / 2;

    /// <summary>Number of copies of <paramref name="item"/> carried (0 = none).</summary>
    public int GetItemCount(Item item) => Items.TryGetValue(item, out var n) ? n : 0;

    /// <summary>
    /// Use one copy of <paramref name="item"/>: heals by its value (capped at
    /// max HP) and decrements the count. Returns false if none are carried or
    /// the combatant is already at full HP.
    /// </summary>
    public bool UseItem(Item item)
    {
        if (IsDead || HitPoints >= MaxHitPoints || GetItemCount(item) <= 0)
            return false;
        HitPoints = System.Math.Min(MaxHitPoints, HitPoints + item.HealValue);
        return ConsumeItem(item);
    }

    /// <summary>
    /// Consume one copy of <paramref name="item"/> (decrements the count)
    /// without applying any effect. Returns false if none are carried.
    /// </summary>
    public bool ConsumeItem(Item item)
    {
        if (IsDead || GetItemCount(item) <= 0)
            return false;
        Items[item] = GetItemCount(item) - 1;
        if (Items[item] <= 0)
            Items.Remove(item);
        return true;
    }

    public Character(string name, Color color, int agility, int constitution, bool isEnemy = false)
    {
        Name = name;
        Color = color;
        Agility = agility;
        Constitution = constitution;
        HitPoints = MaxHitPoints;
        IsEnemy = isEnemy;
    }
}
