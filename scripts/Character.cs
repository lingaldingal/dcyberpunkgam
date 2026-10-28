using Godot;

namespace Dcyberpunkgam;

/// <summary>
/// A combatant (party or enemy). <see cref="Agility"/> (10 = normal, 20 = max)
/// is the base for turn order; a d5 initiative roll is added to it once at the
/// start of the encounter. <see cref="MovementRange"/> is how many tiles the
/// combatant may move on its turn: 5 + Agility/2 (rounded down).
/// </summary>
public class Character
{
    public string Name { get; }
    public Color Color { get; }
    public int Agility { get; }
    public bool IsEnemy { get; }

    /// <summary>Tiles this combatant may move on its turn: 5 + Agility/2 (floor).</summary>
    public int MovementRange => 5 + Agility / 2;

    public Character(string name, Color color, int agility, bool isEnemy = false)
    {
        Name = name;
        Color = color;
        Agility = agility;
        IsEnemy = isEnemy;
    }
}
