# Third-party assets

Asset Store packs, one folder per pack. This folder is git-ignored; only the files a zone scene
actually uses are committed, with their textures re-encoded at 2048 px (see the root `README.md`).

**Teammates: do not import these packs.** What the game needs is already in the repository.

| Pack | Folder | Publisher | Used for | In repo |
|---|---|---|---|---|
| Flooded Grounds | `Flooded_Grounds` | Sandro T | Zone 1 cabins, guard house, boardwalk, gate, props, rocks, grass, terrain layers. Zone 2 Marrow House (`Villa2` pieces) and estate walls | Zone 1 + 2 subset |
| Conifers [BOTD] | `Forst` | forst | Zone 1 dead trees and tree line | Zone 1 subset |
| Free Bushes 2018 | `YughuesFreeBushes2018` | Yughues | Zone 1 shoreline bushes | Zone 1 subset |
| Decrepit Dungeon LITE | `Decrepit Dungeon LITE` | Prodigious Creations | Undercroft (Zone 3) | Zone 3 subset |
| Old Building | `Old building` | Rusik3Dmodels | Dropped: a small exterior shell with no interior. Marrow House uses Flooded Grounds' `Villa2` | Not used |
| The Wasteland LITE | | Aquarius Max | Under review | Not imported |
| Free Open Building | | Dr.Bean | Dropped: the villa already has an interior | Not imported |
| Altar Ruins Free | | Underhill Labz | Drowned Altar (Zone 4) | Not imported |
| Dark Fantasy Kit [Lite] | | Runemark Studio | Crypt torches, bones, candles | Not imported |
| Make Your Fantasy Game LITE | | Xiaolianhuastudio | Shrine lanterns only | Not imported |

The pack list is still under review.

## Adding assets from a pack (World Builder only)

1. Import the pack from *Package Manager → My Assets* and move its folder here from inside the Editor.
2. Convert its materials to URP (*Edit → Rendering → Materials → Convert Selected Built-in Materials to URP*).
3. Build the zone. When it is done, for every third-party file the scene depends on:
   - cap textures at 2048 px, enable streaming mipmaps and compression in the import settings;
   - re-encode the source texture at 2048 px (JPG if opaque colour, PNG if it is a normal map or
     has an alpha channel) and rename its `.meta` to match, so the asset ID is kept;
   - compare a screenshot before and after;
   - commit the files and their `.meta` files with `git add -f`.

Check the alpha channel with Unity (`TextureImporter.DoesSourceTextureHaveAlpha`), not with an image
tool: many of these TIFs keep smoothness or cut-out transparency in an extra channel that image
libraries silently drop. Export those through Unity.

## Import notes

### Flooded Grounds
- **Delete `Flooded_Grounds/PostProcessing/` straight after importing.** It is the 2017
  Post-Processing Stack v1; its `MinDrawer.cs` no longer compiles (`MinAttribute` is ambiguous)
  and stops the whole project from opening. URP has its own post-processing.
- The pack is built at a slightly large scale (doors are about 2.8 m tall). It is used at scale 1
  so doorways stay wide enough for the NavMesh agents.
- Its custom shaders are Built-in pipeline only. `PBR_TopBlend` and `Triplanar_BumpSpec`
  materials were switched to URP Lit (the moss top-blend layer is lost).
- `BGR_Water` (`PBR_Water` shader) is not URP compatible. The game uses `Hollowmere/Water` instead.
- The `TreeCreator_*` tree prefabs render pink in URP (Tree Creator is unsupported). Do not place them.
- Unity generates `Assets/_TerrainAutoUpgrade/` (terrain layers for the pack's demo terrain) and
  recreates it if moved. It is git-ignored; leave it where it is.

### Old Building
- The prefab is imported at a very small scale (about 1.7 m tall). Scale it up when placing it.

### URP conversion
The Render Pipeline Converter's batch API fails in URP 17.3, so convert by hand from the menu above.
