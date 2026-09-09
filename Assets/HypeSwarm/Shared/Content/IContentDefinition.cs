namespace HypeSwarm.Shared.Content
{
    /// <summary>
    /// Anything the <see cref="ContentRegistry"/> can hold: augments, items, abilities, effect
    /// steps, champions, elites, runes.
    /// </summary>
    /// <remarks>
    /// Deliberately an interface rather than the <see cref="ContentDefinition"/> base class, so the
    /// registry and everything built on it can be exercised in EditMode tests with plain C# objects
    /// — no <c>ScriptableObject</c> assets, no scene, no import.
    /// </remarks>
    public interface IContentDefinition
    {
        ContentId Id { get; }
    }
}
