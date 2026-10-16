// Matches scripts/build-mana-font.py and the bundled Crucible Mana WOFF2.
// Keep private-use characters here; callers should use ManaSymbol/ManaCost.
export const manaGlyphs: Readonly<
  Record<string, { glyph: string; name: string; label: string }>
> = {
  W: { glyph: "\uE600", name: "white", label: "White mana" },
  U: { glyph: "\uE601", name: "blue", label: "Blue mana" },
  B: { glyph: "\uE602", name: "black", label: "Black mana" },
  R: { glyph: "\uE603", name: "red", label: "Red mana" },
  G: { glyph: "\uE604", name: "green", label: "Green mana" },
  C: { glyph: "\uE605", name: "colorless", label: "Colorless mana" },
  T: { glyph: "\uE606", name: "tap", label: "Tap" },
};

const otherLabels: Readonly<Record<string, string>> = {
  X: "X generic mana",
  Y: "Y generic mana",
  Z: "Z generic mana",
  P: "Phyrexian (2 life)",
  S: "Snow mana",
  Q: "Untap",
  E: "Energy",
};

export function symbolLabel(token: string): string {
  const key = token.toUpperCase();
  if (key.includes("/")) return key.split("/").map(symbolLabel).join(" or ");
  return (
    manaGlyphs[key]?.label ??
    otherLabels[key] ??
    (/^\d+$/.test(key) ? `${key} generic mana` : `Symbol {${token}}`)
  );
}

export function isGenericSymbol(token: string): boolean {
  return /^(?:\d+|[XYZ])$/i.test(token);
}

function knownPart(token: string): boolean {
  return (
    !!manaGlyphs[token.toUpperCase()] ||
    isGenericSymbol(token) ||
    Object.hasOwn(otherLabels, token.toUpperCase())
  );
}

export function hybridParts(token: string): string[] {
  const parts = token.toUpperCase().split("/");
  return parts.length > 1 && parts.every(knownPart) ? parts : [];
}

// Arena rules use {o2oB} and {o(G/U)o(G/U)}. Normalize complete groups only:
// unfamiliar/malformed notation must remain readable instead of losing data.
export function normalizeMana(text: string): string {
  return text.replace(/\{([^{}]+)\}/g, (whole, body: string) => {
    const arenaSymbol = /o(?:\(([^)]+)\)|([0-9]+|[WUBRGCTQXYZSEP]))/g;
    if (body.replace(arenaSymbol, "") !== "") return whole;
    return body.replace(
      arenaSymbol,
      (_: string, hybrid: string, single: string) => `{${hybrid ?? single}}`,
    );
  });
}

export type ManaSegment = { kind: "text" | "symbol"; value: string };

export function manaSegments(text: string | null | undefined): ManaSegment[] {
  return Array.from(
    normalizeMana(text ?? "").matchAll(/\{([^{}]+)\}|([^{}]+|[{}])/g),
    (match) =>
      match[1] !== undefined
        ? { kind: "symbol", value: match[1] }
        : { kind: "text", value: match[0] },
  );
}

export function costSegments(cost: string | null | undefined): ManaSegment[] {
  const text = (cost ?? "").trim();
  if (!text || /^no cost$/i.test(text)) return [];
  // Forge replay costs may be space-delimited or compact ("2 U U", "2UU").
  if (
    !/[{}]/.test(text) &&
    /^(?:\s*(?:\d+|[WUBRGCTQXYZSEP](?:\/[WUBRGCP])?|2\/[WUBRG]))+\s*$/i.test(
      text,
    )
  ) {
    return (
      text.match(/2\/[WUBRG]|[WUBRGCTQXYZSEP](?:\/[WUBRGCP])?|\d+/gi) ?? []
    ).map((value) => ({ kind: "symbol", value }));
  }
  return manaSegments(text);
}

export function costLabel(segments: ManaSegment[]): string {
  return segments.length
    ? `Mana cost: ${segments
        .map((s) =>
          s.kind === "symbol" ? symbolLabel(s.value) : s.value.trim(),
        )
        .filter(Boolean)
        .join(", ")}`
    : "No mana cost";
}

export function manaColors(costs: readonly string[]): string[] {
  const present = new Set(
    costs.flatMap((cost) =>
      costSegments(cost)
        .filter((s) => s.kind === "symbol")
        .flatMap((s) => s.value.toUpperCase().split("/")),
    ),
  );
  return [..."WUBRG"].filter((color) => present.has(color));
}
