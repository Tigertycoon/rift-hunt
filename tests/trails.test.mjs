import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";

const root = new URL("../", import.meta.url);
const folder = new URL("Assets/StreamingAssets/StoryMint/", root);
const types = new Set(["message", "geofence", "scan", "ar_interaction", "riddle", "portal_deactivation", "rune_sort", "simon_says", "gyro_maze", "rune_catcher", "seal_trace", "whack_a_rune", "rune_memory", "external_game"]);
for (const name of readdirSync(folder).filter(name => name.endsWith(".json"))) {
  test(`${name}: all steps are reachable and target references resolve`, () => {
    const trail = JSON.parse(readFileSync(new URL(name, folder)));
    assert.ok(trail.trailId); assert.ok(trail.steps.length);
    const byId = new Map(trail.steps.map(step => [step.id, step]));
    assert.equal(byId.size, trail.steps.length, "Duplicate step ID");
    const visited = new Set();
    let current = trail.startStepId || trail.steps[0].id;
    while (current && !visited.has(current)) {
      assert.ok(byId.has(current), `Broken nextStepId: ${current}`);
      visited.add(current); current = byId.get(current).nextStepId;
    }
    assert.equal(visited.size, trail.steps.length, "Unreachable authored steps");
    for (const step of trail.steps) {
      assert.ok(types.has(step.type), `Unknown type: ${step.type}`);
      if (step.targetId && step.targetId !== "preview-target") {
        const base = new URL(`Assets/Resources/StoryMint/Targets/${step.targetId}`, root);
        assert.ok([".jpg", ".png"].some(ext => existsSync(fileURLToPath(base) + ext)), `Missing target: ${step.targetId}`);
      }
      for (const key of ["gameUrl", "actionUrl"]) {
        if (step[key]) assert.ok(existsSync(new URL(`Assets/${step[key]}`, root)), `External placeholder missing: ${step[key]}`);
      }
      if (step.type === "geofence") {
        assert.ok(Number.isFinite(step.location?.latitude));
        assert.ok(Number.isFinite(step.location?.longitude));
        assert.ok(step.geofenceRadiusMeters > 0);
      }
    }
  });
}
