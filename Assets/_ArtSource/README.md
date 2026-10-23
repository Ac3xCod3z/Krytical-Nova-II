# _ArtSource — working files, not shipped directly

`_ArtSource/{Photoshop,Blender,Substance,ConceptArt,Reference}/`

Export game-ready assets (FBX, PNG, ...) into the owning system in `_Game/` and keep the sources here.
Unity still imports files in this folder; keep huge or unsupported sources (e.g. `.blend` without Blender installed)
in a folder whose name ends with `~` (e.g. `Blender~/`), which Unity ignores.

Big working files (`.blend`, review sheets, renders, AI outputs) no longer go here: they live on Google Drive in
`G:\My Drive\KryticalNova\Art\` (Blender work in `Art\Blender\`), so git LFS does not grow with every save. Only
`Blender~/Swatch/` stays here. See CLAUDE.md, "Git LFS size and Google Drive".

Rules: `Assets/_Game/_Docs/ProjectOrganization.md`
