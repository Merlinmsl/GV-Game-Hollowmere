# Third-party assets

Asset Store packs live here, one folder per pack. The folder contents are git-ignored:
every member imports the packs locally from *Package Manager → My Assets*.
Only the files the game actually uses are committed (see the root `README.md`).

After importing a pack, move its folder **inside the Unity Editor** (drag in the Project
window) to `Assets/ThirdParty/<Pack folder>/`, then convert its materials to URP.

| Pack | Folder | Publisher | Used for | Status |
|---|---|---|---|---|
| Decrepit Dungeon LITE | `Decrepit Dungeon LITE` | Prodigious Creations | Undercroft walls, floors, pillars | Imported, URP |
| Flooded Grounds | `Flooded_Grounds` | Sandro T | Outskirts buildings, props, rocks, terrain layers | Imported, URP (see notes) |
| Old Building | `Old building` | Rusik3Dmodels | Marrow House exterior shell | Imported, URP |
| Conifers [BOTD] | `Forst` | forst | Outskirts tree line | Imported, ships URP shaders |
| Free Bushes 2018 | `YughuesFreeBushes2018` | Yughues | Outskirts undergrowth | Imported, URP |
| The Wasteland LITE | | Aquarius Max | Outskirts props (car wreck, shed) | Downloaded, to import |
| Free Open Building | | Dr.Bean | Marrow House interior layout | Downloaded, to import |
| Altar Ruins Free | | Underhill Labz | Drowned Altar arena | To import |
| Dark Fantasy Kit [Lite] | | Runemark Studio | Crypt torches, bones, candles | To import |
| Make Your Fantasy Game LITE | | Xiaolianhuastudio | Shrine lanterns only | To import |

The pack list is still under review.

## Import notes

### Flooded Grounds
- **Delete `Flooded_Grounds/PostProcessing/` straight after importing.** It is the 2017
  Post-Processing Stack v1; its `MinDrawer.cs` no longer compiles (`MinAttribute` is ambiguous)
  and stops the whole project from opening. URP has its own post-processing.
- Its custom shaders are Built-in pipeline only. `PBR_TopBlend` and `Triplanar_BumpSpec`
  materials were switched to URP Lit (the moss top-blend layer is lost).
- `BGR_Water` (`PBR_Water` shader) is not URP compatible. Do not use it; the game uses its own water material.
- The `TreeCreator_*` tree prefabs render pink in URP (Tree Creator is unsupported). Do not place them directly.
- Unity generates `Assets/_TerrainAutoUpgrade/` (terrain layers for the pack's demo terrain) and
  recreates it if moved. It is git-ignored; leave it where it is.

### Old Building
- The prefab is imported at a very small scale (about 1.7 m tall). Scale it up when placing it.

### URP conversion
The Render Pipeline Converter's batch API fails in URP 17.3, so use *Window → Rendering →
Render Pipeline Converter* by hand, or *Edit → Rendering → Materials → Convert Selected
Built-in Materials to URP* on the pack's materials.
