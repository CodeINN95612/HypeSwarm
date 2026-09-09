namespace HypeSwarm.ClientOnly
{
    /// <summary>
    /// Anchor type for locating the <c>HypeSwarm.ClientOnly</c> assembly by reflection.
    /// </summary>
    /// <remarks>
    /// <b>ClientOnly is the presentation and input assembly.</b> VFX, audio, UI, camera, and the
    /// input bindings live here. It subscribes to the gameplay events <c>HypeSwarm.Shared</c> emits
    /// and never the other way round — Shared does not know this assembly exists.
    ///
    /// It may not reference <c>HypeSwarm.ServerOnly</c>.
    /// </remarks>
    public static class ClientOnlyAssemblyMarker
    {
        public const string AssemblyName = "HypeSwarm.ClientOnly";
    }
}
