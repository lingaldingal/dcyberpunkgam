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

    /// <summary>Current hit points (starts at 5 + Constitution).</summary>
    public int HitPoints { get; set; }

    /// <summary>Full hit points: 5 + Constitution.</summary>
    public int MaxHitPoints => 5 + Constitution;

    /// <summary>True once hit points have fallen to zero.</summary>
    public bool IsDead => HitPoints <= 0;

    /// <summary>Tiles this combatant may move on its turn: 5 + Agility/2 (floor).</summary>
    public int MovementRange => 5 + Agility / 2;

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
