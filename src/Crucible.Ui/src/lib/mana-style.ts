import { writable } from "svelte/store";

type ManaStyle = "symbols" | "letters";
const storageKey = "crucible:mana-style:v1";

function savedStyle(): ManaStyle {
  try {
    return localStorage.getItem(storageKey) === "letters"
      ? "letters"
      : "symbols";
  } catch {
    return "symbols";
  }
}

export const manaStyle = writable<ManaStyle>(savedStyle());

export function toggleManaStyle(): void {
  manaStyle.update((current) => {
    const next = current === "symbols" ? "letters" : "symbols";
    try {
      localStorage.setItem(storageKey, next);
    } catch {}
    return next;
  });
}
