using Godot;

namespace Dcyberpunkgam;

/// <summary>
/// A party combatant. <see cref="Agility"/> (10 = normal, 20 = max) is the base
/// for turn order; a d5 initiative roll is added to it once at the start of
/// the encounter.
/// </summary>
public class Character
{
    public string Name { get; }
    public Color Color { get; }
    public int Agility { get; }

    public Character(string name, Color color, int agility)
    {
        Name = name;
        Color = color;
        Agility = agility;
    }
}
