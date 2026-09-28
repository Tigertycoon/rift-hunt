# Tracking integrations

The public project defaults to a manual provider for inspecting gameplay. `ManualARProvider` only emits explicit test events; it is not an image tracker.

## Imagine WebAR

The authored `ImagineARProvider` adapter and Imagine-specific editor tools are compiled only with **`STORYMINT_IMAGINE`**. To work on this path:

1. Install your own licensed Imagine WebAR Image Tracker package.
2. Add `STORYMINT_IMAGINE` to the scripting defines for the relevant target.
3. Create/configure an Imagine scene with its camera, tracker and target entries.
4. Set the chosen trail's `sceneName` to that scene and use target IDs matching the JSON.
5. Configure the vendor WebGL template and camera permissions for your package version.

The retained Imagine editor tools contain original project-specific scene paths. Review them before enabling the symbol; some run automatically when the editor loads. The public preview is independent of those tools. Vendor SDK source, generated target meshes and templates are not supplied.

## OpenCV for Unity

The authored camera/tracker/setup code and `OpenCVARProvider` are compiled only with **`STORYMINT_OPENCV`**. Install a separately licensed compatible OpenCV for Unity package before adding the symbol.

The original factory selects this path for `SampleScene` or an empty `sceneName`. It loads an authored target photograph from `Resources/StoryMint/Targets/<targetId>` and configures the tracker. This integration has not been compiled against a freshly installed SDK in the public extraction; API adaptation may be required for newer package versions.

## Using your own provider

Implement `IRiftHuntARProvider` on a scene component. The runtime factory prefers such a component and the experience controller uses its target poses and found/lost events. Remove the manual preview provider when configuring physical tracking so it does not take precedence.

## Browser builds

Unity WebGL build support is a separate editor module. HTTPS and browser permission handling are necessary for device camera/location/motion APIs. The `.jslib` files are included, but no new WebGL build or mobile-device acceptance is claimed for this publication.

Historical trails refer to original scene names and locations. They are example content, not a claim that the clean checkout recreates those deployed experiences automatically. `rift-hunt-preview.json` is the default SDK-free entry point.
