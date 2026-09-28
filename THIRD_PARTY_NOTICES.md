# Third-party notices

The root MIT license applies to the authored StoryMint/Rift Hunt code and content. It does not relicense dependencies or third-party resources.

- Unity Engine, URP, uGUI/TextMesh Pro and Unity Package Manager dependencies retain their applicable Unity/package licenses. Versions are pinned in `Packages/manifest.json` and `Packages/packages-lock.json`; engine/package implementations are not vendored.
- Supplied TextMesh Pro resources under `Assets/TextMesh Pro` retain their original notices. In particular, Liberation Sans retains the full **SIL Open Font License** in `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt`; the EmojiOne sample retains `Assets/TextMesh Pro/Sprites/EmojiOne Attribution.txt`.
- Imagine WebAR and OpenCV for Unity are separately licensed optional dependencies. Only the author's integration code is included. Their SDK source, binaries, templates and demo media are excluded.
- Purchased portal/effect packs are excluded. Original integration/fallback scripts may mention their resource names, but their implementation/assets are not bundled or claimed as authored work.
- The public Node checks use only Node's built-in modules and require no third-party npm packages.
