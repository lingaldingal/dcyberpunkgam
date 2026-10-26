using System.Linq;

namespace Dcyberpunkgam;

/// <summary>
/// Round-based initiative turn system. Each round every combatant rolls a d5
/// for initiative; the roll is added to their Agility and combatants act in
/// descending order of that total. Ties break by higher Agility, then party
/// order. Everyone acts once per round, then a new round re-rolls initiative.
/// </summary>
public class TurnSystem
{
    public const int MaxAgility = 20;

    public List<Character> Combatants { get; }
    public int Round { get; private set; }
    public int[] Roll { get; private set; }
    public int[] Total { get; private set; }

    private readonly System.Random _rng;
    private int[] _order;
    private int _pointer;

    public TurnSystem(List<Character> combatants, int seed = 0)
    {
        Combatants = combatants;
        _rng = new System.Random(seed);
        Roll = new int[combatants.Count];
        Total = new int[combatants.Count];
        _order = new int[0];
    }

    public Character Current => Combatants[_order[_pointer]];
    public int CurrentIndex => _order[_pointer];

    public void StartRound()
    {
        Round++;
        for (int i = 0; i < Combatants.Count; i++)
        {
            Roll[i] = _rng.Next(1, 6); // d5
            Total[i] = Combatants[i].Agility + Roll[i];
        }
        _order = BuildOrder();
        _pointer = 0;
    }

    /// <summary>Advance to the next combatant; completing the round re-rolls it.</summary>
    public void EndTurn()
    {
        _pointer++;
        if (_pointer >= _order.Length)
            StartRound();
    }

    /// <summary>
    /// Test/debug hook: force this round's d5 results (by party index) and
    /// recompute the order without touching the RNG.
    /// </summary>
    public void SetRolls(int[] rolls)
    {
        for (int i = 0; i < Combatants.Count; i++)
        {
            Roll[i] = rolls[i];
            Total[i] = Combatants[i].Agility + rolls[i];
        }
        _order = BuildOrder();
        _pointer = 0;
    }

    public int[] GetOrder() => (int[])_order.Clone();

    private int[] BuildOrder()
    {
        var indices = Enumerable.Range(0, Combatants.Count).ToList();
        indices.Sort((a, b) =>
        {
            if (Total[b] != Total[a])
                return Total[b].CompareTo(Total[a]); // higher total first
            if (Combatants[b].Agility != Combatants[a].Agility)
                return Combatants[b].Agility.CompareTo(Combatants[a].Agility); // higher agility first
            return a.CompareTo(b); // stable party order
        });
        return indices.ToArray();
    }
}
