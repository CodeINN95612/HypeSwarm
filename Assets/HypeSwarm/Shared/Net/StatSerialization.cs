using HypeSwarm.Shared.Content;
using HypeSwarm.Shared.Stats;
using Mirror;

namespace HypeSwarm.Shared.Net
{
    /// <summary>
    /// Tells Mirror how to put a stat modifier on the wire.
    /// </summary>
    /// <remarks>
    /// <b>These have to be written by hand.</b> The weaver generates a serialiser for a struct out of
    /// its public fields, and every type here stores its state in properties over private fields — so
    /// the generated version would write nothing at all, and every modifier would arrive as a default
    /// value with no error anywhere. Defining the extension methods takes precedence over generation
    /// and makes the format explicit, which matters for something that will end up in save files too.
    ///
    /// <para>A <see cref="ContentId"/> travels as its text (§13.2). Interning it to an index would be
    /// smaller, but it would also mean a client and a host with different content builds silently
    /// disagreeing about which augment a number means, and modifier lists are small.</para>
    /// </remarks>
    public static class StatSerialization
    {
        public static void WriteContentId(this NetworkWriter writer, ContentId id)
        {
            writer.WriteString(id.Value);
        }

        /// <summary>
        /// Reads an id, or the invalid default if the text does not parse. Malformed content cannot
        /// be made valid here, and throwing would drop the connection over one bad modifier.
        /// </summary>
        public static ContentId ReadContentId(this NetworkReader reader)
        {
            return ContentId.TryParse(reader.ReadString(), out var id) ? id : default;
        }

        public static void WriteModifierSource(this NetworkWriter writer, ModifierSource source)
        {
            writer.WriteContentId(source.Content);
            writer.WriteInt(source.Instance);
        }

        public static ModifierSource ReadModifierSource(this NetworkReader reader)
        {
            var content = reader.ReadContentId();

            return new ModifierSource(content, reader.ReadInt());
        }

        public static void WriteStatModifier(this NetworkWriter writer, StatModifier modifier)
        {
            // Bytes rather than ints: eleven stats and three operations, and this is the type that
            // travels most often in the game.
            writer.WriteByte((byte)modifier.Stat);
            writer.WriteByte((byte)modifier.Operation);
            writer.WriteFloat(modifier.Value);
            writer.WriteModifierSource(modifier.Source);
        }

        public static StatModifier ReadStatModifier(this NetworkReader reader)
        {
            var stat = (StatId)reader.ReadByte();
            var operation = (ModifierOperation)reader.ReadByte();
            var value = reader.ReadFloat();

            return new StatModifier(stat, operation, value, reader.ReadModifierSource());
        }
    }
}
