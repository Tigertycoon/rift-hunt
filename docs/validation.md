# Validation

Checked on **2026-09-28**, using **Unity 6000.0.79f1** and **Node.js 22.17.1**.

| Check | Result | Scope |
| --- | --- | --- |
| Fresh Unity import and compilation | Passed | SDK-free configuration; licensed adapter branches disabled |
| Trail definitions | Passed | 16 files, 93 steps; Unity deserialization, known types and next-step references |
| Geofences | Passed | Accurate/poor/missing position cases and a known geodesic distance |
| Progress | Passed | Save/load/clear and isolation of completion between trails |
| Play Mode startup | Passed | Preview loads, intro advances, simulated image-found event enters the first minigame |
| Minigame initialization | Passed | All seven original controllers and their UI initialize |
| Node contract/bridge tests | 20 passed | 16 reachable trail graphs plus four persistence/navigation/handoff checks |

[Editor result](evidence/unity-validation.txt) · [Play Mode result](evidence/gameplay-smoke.txt)

The browser-bridge tests execute the actual JavaScript in a Node VM with browser/Unity interop test doubles. They do not certify Emscripten linking or behavior in mobile Safari/Chrome. Play Mode initialization is not a full playthrough, visual assessment or input-device test.

## Repeat

Run `npm test` from the repository root with Node.js 22 or newer. GitHub Actions repeats these checks without a Unity license.

For local Unity verification, invoke the Unity executable with:

```text
-batchmode -nographics -projectPath <absolute-checkout-path> -executeMethod StoryMint.RiftHunt.Editor.PortfolioValidation.Run -logFile <absolute-log-path>
```

For the Play Mode smoke check, replace the method with:

```text
StoryMint.RiftHunt.Editor.GameplaySmokeValidation.Run
```

The latter clears only the dedicated `portfolio-preview` save before/after its run. Results are written under `TestResults/`, which is ignored by Git.

Not established: complete win/lose/replay coverage for every game, device tracking, GPS accuracy outdoors, mobile permissions, gyro/touch behavior, shader appearance, WebGL build/deployment or compatibility with newly installed proprietary SDK releases.
