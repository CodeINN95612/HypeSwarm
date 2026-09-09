# Hype Swarm

A game built in Unity 6 (`6000.6.0f1`) on the Universal Render Pipeline.

## Requirements

- Unity `6000.6.0f1` — install it with [Unity Hub](https://unity.com/download), or from the Unity CLI:
  ```
  unity install 6000.6.0f1
  ```

## Getting started

```
git clone https://github.com/CodeINN95612/HypeSwarm.git
```

Open the cloned folder in Unity Hub, or from the CLI:

```
unity open HypeSwarm
```

Unity regenerates `Library/`, the solution files and `UserSettings/` on first import, so they are not tracked here.

## Tests

EditMode tests live in `Assets/Tests/EditMode` and run in a few seconds. Open **Window → General →
Test Runner** in the Editor, or from the CLI with the Editor closed:

```
unity test --mode EditMode
```

What belongs in a test — and what deliberately does not — is set out in [CLAUDE.md](CLAUDE.md).

## Scene merging

Unity ships a YAML-aware merge tool that resolves conflicts in scenes and prefabs far better than a plain text merge. `.gitattributes` already routes those file types to it; register the driver once per clone:

```
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver '"C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
```

## License

[PolyForm Noncommercial License 1.0.0](LICENSE.md).

You may use, modify, build and redistribute this code freely for any **noncommercial** purpose — personal projects, learning, research, hobby work, and use by nonprofits, schools and government bodies. You may **not** use it in a commercial product or any commercial purpose.

This is a source-available license, not an OSI-approved open source license.
