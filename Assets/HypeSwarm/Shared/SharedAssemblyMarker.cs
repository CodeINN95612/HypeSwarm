namespace HypeSwarm.Shared
{
    /// <summary>
    /// Anchor type for locating the <c>HypeSwarm.Shared</c> assembly by reflection.
    /// Used by the architecture tests that police this assembly's boundary.
    /// </summary>
    /// <remarks>
    /// <b>Shared is the gameplay assembly.</b> It runs identically on the host, on clients, and in a
    /// headless build, which is why it may never reach for presentation: no <c>UnityEngine.UI</c>,
    /// no particle systems, no <c>AudioSource</c>, no input, no editor types (spec §12, §13.1).
    ///
    /// Effect steps emit gameplay events; a presentation layer in <c>HypeSwarm.ClientOnly</c>
    /// subscribes and spawns the visuals. Fusing the two is what makes a headless build impossible
    /// later, and the compile-time boundary is the only enforcement that actually holds.
    /// </remarks>
    public static class SharedAssemblyMarker
    {
        public const string AssemblyName = "HypeSwarm.Shared";
    }
}
