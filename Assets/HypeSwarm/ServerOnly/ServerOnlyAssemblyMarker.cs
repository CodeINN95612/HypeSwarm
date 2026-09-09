namespace HypeSwarm.ServerOnly
{
    /// <summary>
    /// Anchor type for locating the <c>HypeSwarm.ServerOnly</c> assembly by reflection.
    /// </summary>
    /// <remarks>
    /// <b>ServerOnly is the host-authority assembly.</b> Damage resolution, deaths, drops, wave
    /// direction, and progression writes live here (spec §10) — everything a dedicated server needs
    /// and a pure client must not be able to run.
    ///
    /// It may not reference <c>HypeSwarm.ClientOnly</c>. Keeping this assembly free of presentation
    /// is what makes the headless build in Phase 5 a build target rather than a cleanup project.
    /// </remarks>
    public static class ServerOnlyAssemblyMarker
    {
        public const string AssemblyName = "HypeSwarm.ServerOnly";
    }
}
