# Iaido_Katana

Beta, work in progress. Two katanas built around the perfect parry, which draws and strikes back on its own. Iai stance, scabbard, ink impact frame, own models and the Flametal's Thunderclap dash cut.

A Valheim mod by Onononoki. Get it on Thunderstore: https://thunderstore.io/c/valheim/p/Onononoki/Iaido_Katana/

The full description, settings and changelog are in [package/README.md](package/README.md) and [package/CHANGELOG.md](package/CHANGELOG.md).

## Bug reports and feedback

Open an issue here: https://github.com/Ononononoki/Iaido_Katana/issues. If something breaks, please add your mod list and the BepInEx log (`BepInEx/LogOutput.log`).

## Building

Needs the .NET SDK, Valheim and a mod manager profile with BepInEx and Jotunn.

```
dotnet build -c Release -p:GamePath="<Valheim folder>" -p:ProfilePath="<mod manager profile folder>"
```

The dll is built into `bin/Release/net472` and copied to `package/`.

The katana models and textures in `art/out` are made with Blender by `art/make_katana.py` (steel) and `art/make_flametal_concept.py` (flametal).

## License

MIT, see [LICENSE](LICENSE).
