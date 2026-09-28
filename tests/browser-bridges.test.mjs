import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

const root = new URL("../", import.meta.url);
function storage() {
  const data = new Map();
  return { data, setItem: (key, value) => data.set(key, String(value)), getItem: key => data.get(key) ?? null, removeItem: key => data.delete(key) };
}
function loadPlugin(name, extra = {}) {
  const memory = new Map(); let address = 1;
  const context = {
    LibraryManager: { library: {} }, mergeInto: Object.assign, UTF8ToString: value => value,
    lengthBytesUTF8: value => Buffer.byteLength(value), _malloc: () => address++,
    stringToUTF8: (value, ptr) => memory.set(ptr, value), console: { warn() {} }, localStorage: storage(), ...extra
  };
  vm.runInNewContext(readFileSync(new URL(`Assets/StoryMint/RiftHunt/Plugins/WebGL/${name}.jslib`, root), "utf8"), context);
  return { api: context.LibraryManager.library, context, memory };
}

test("WebGL persistence round-trips Unicode and removes only the selected key", () => {
  const { api, memory } = loadPlugin("StoryMintPersistence");
  api.StoryMintSave_Set("trail-a", "Tor geöffnet ✦"); api.StoryMintSave_Set("trail-b", "other");
  assert.equal(api.StoryMintSave_Has("trail-a"), 1);
  assert.equal(memory.get(api.StoryMintSave_Get("trail-a")), "Tor geöffnet ✦");
  api.StoryMintSave_Remove("trail-a");
  assert.equal(api.StoryMintSave_Has("trail-a"), 0);
  assert.equal(memory.get(api.StoryMintSave_Get("trail-b")), "other");
});

test("WebGL persistence handles browser storage being unavailable", () => {
  const unavailable = new Proxy({}, { get: () => () => { throw Error("Storage denied"); } });
  const { api, memory } = loadPlugin("StoryMintPersistence", { localStorage: unavailable });
  assert.doesNotThrow(() => api.StoryMintSave_Set("test", "value"));
  assert.equal(api.StoryMintSave_Has("test"), 0);
  assert.equal(memory.get(api.StoryMintSave_Get("test")), "");
  assert.doesNotThrow(() => api.StoryMintSave_Remove("test"));
});

test("navigation falls back to replace when same-tab assign fails", () => {
  let replaced;
  const { api } = loadPlugin("StoryMintNavigation", { window: { location: { assign() { throw Error("test"); }, replace(url) { replaced = url; } } } });
  api.StoryMintNavigate_To("https://example.test/trail");
  assert.equal(replaced, "https://example.test/trail");
});

test("external game completion writes trail-scoped progress before returning", () => {
  const localStorage = storage(); let finish;
  const href = "https://example.test/trail";
  const window = { location: { search: "?trailId=trail-a&gameId=puzzle&returnUrl=" + encodeURIComponent(href), href: "https://example.test/game" } };
  const document = { getElementById: id => id === "debug" ? {} : { addEventListener: (_, fn) => { finish = fn; } } };
  const html = readFileSync(new URL("Assets/StreamingAssets/StoryMint/bridge-test-game.html", root), "utf8");
  vm.runInNewContext(html.match(/<script>([\s\S]*?)<\/script>/)[1], { window, document, localStorage, URLSearchParams, URL, Date, history: { length: 1 }, console });
  finish();
  assert.equal(localStorage.getItem("sm_trail_trail-a_game_puzzle_complete"), "1");
  assert.equal(localStorage.getItem("sm_trail_trail-b_game_puzzle_complete"), null);
  assert.equal(window.location.href, href);
});
