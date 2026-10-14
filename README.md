# Iaido_Katana

Two katanas built around the perfect parry, which draws and strikes back on its own. Comes with an iai stance, a scabbard, an ink impact frame and the Flametal Katana's Thunderclap dash cut. Please report bugs in Issues.

A Valheim mod by Onononoki. Get it on Thunderstore: https://thunderstore.io/c/valheim/p/Onononoki/Iaido_Katana/

The full description, settings and changelog are in [package/README.md](package/README.md) and [package/CHANGELOG.md](package/CHANGELOG.md).

## Bug reports and feedback

Bug reports and ideas: open an issue at https://github.com/Ononononoki/Iaido_Katana/issues. If something breaks, please add your mod list and the BepInEx log (`BepInEx/LogOutput.log`).

## Building

Needs the .NET SDK, Valheim and a mod manager profile with BepInEx and Jotunn.

```
dotnet build -c Release -p:GamePath="<Valheim folder>" -p:ProfilePath="<mod manager profile folder>"
```

The dll is built into `bin/Release/net472` and copied to `package/`.

The katana models and textures in `art/out` are made with Blender by `art/make_katana.py` (steel) and `art/make_flametal_concept.py` (flametal).

## License

MIT, see [LICENSE](LICENSE).
