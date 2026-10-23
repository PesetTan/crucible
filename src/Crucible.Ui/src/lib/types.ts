export type ExactInteger = number | string;
export interface Card {
  arenaId: number;
  name: string;
  quantity: number;
  zone: string;
  set: string;
  type: string;
  cost: string;
  text: string;
  locallyLegal: boolean;
  linkedFaces: string;
  attributes?: Record<string, unknown>;
}
export interface Deck {
  id: string;
  name: string;
  sourceType: string;
  variant: string;
  sourceDeckId: string;
  snapshot: string;
  sourceUrl?: string;
  listHash: string;
  manifestHash: string;
  importedAt: string;
  cards: Card[];
  freshnessNote: string;
  friendlyName?: string;
  parentDeckId?: string;
  archetypeTier?: string;
  strategy?: string;
  subtype?: string;
  publishedWinRatePercent?: number;
  publishedMatchesDisplayed?: string;
  tierSourceUrl?: string;
  tierSnapshotDate?: string;
  mainCount: number;
  landCount: number;
  creatureCount: number;
  sideCount: number;
}
export interface Coverage {
  deckId: string;
  status: string;
  cards: { name: string; status: string; reason?: string; script?: string }[];
  limitations: string[];
}
export interface DeckRow {
  manifest: Deck;
  coverage: Coverage;
}
export interface Library {
  version: number;
  decks: DeckRow[];
  manaValues: Record<string, number>;
}
export interface EngineSupport {
  id: string;
  providerId: string;
  engineVersion: string;
  target: string;
  cards: { name: string; kind: "Card" | "Token"; arenaId?: number }[];
  decks: Deck[];
  matchups: {
    id: string;
    label: string;
    deckA: string;
    deckB: string;
    eligibilityAsOf: string;
  }[];
}
export interface Protocol {
  id: string;
  name: string;
  condition: string;
  response: string;
  priority: number;
  maximumDelayTurns: number;
  preferences: Record<string, number>;
}
export interface ProtocolSet {
  version: string;
  profile: string;
  hash: string;
  definitions: Protocol[];
  settings: Record<string, unknown>;
  sets?: Record<
    string,
    {
      profile: string;
      parent: string;
      activation: string;
      definitions: Protocol[];
    }
  >;
}
export interface Capability {
  available: boolean;
  engine: string;
  engineHash: string;
  adapterHash: string;
  javaPath: string;
  blockers: string[];
  limitations: string[];
  attributesHash?: string;
  protocols?: ProtocolSet;
}
export interface RunConfig {
  deckIds: string[];
  gamesPerPair: number;
  workers: number;
  timeoutSeconds: number;
  turnLimit: number;
  rootSeed: ExactInteger;
  diagnostic: boolean;
  engineHash: string;
  adapterHash: string;
  pilot: string;
  openingPolicy: string;
  seedScheme: string;
  staggerStarts: boolean;
  estimatedGameSeconds: number;
  startLeadSeconds: number;
  startupSpacingSeconds: number | null;
  performanceMode: boolean;
  pilotProfiles?: Record<string, string>;
  attributesHash?: string;
  cardAttributes?: Record<string, unknown>;
  protocolSet?: ProtocolSet;
  optimizerPolicy?: { id: string; profile?: string; tuningHash?: string };
}
export interface Baseline {
  runId: string;
  deckId: string;
  opponentId: string;
}
export interface Run {
  id: string;
  createdAt: string;
  status: string;
  config: RunConfig;
  total: number;
  error?: string;
  updatedAt?: string;
  extensions?: unknown[];
  workshopDeckId?: string;
  workshopBaseline?: Baseline;
}
export interface Timing {
  id: string;
  runId: string;
  startedAt: string;
  updatedAt: string;
  finishedAt?: string;
  activeSeconds: number;
  attemptCount: number;
  scheduledSeconds: number;
  status: string;
}
export interface Progress {
  run: Run;
  completed: number;
  failed: number;
  cancelled: number;
  running: number;
  pending: number;
  reused: number;
  processed: number;
  percent: number;
}
export interface Matchup {
  deckA: string;
  deckB: string;
  scheduled: number;
  valid: number;
  winsA: number;
  winsB: number;
  draws: number;
  failed: number;
  cancelled: number;
  pending: number;
  running: number;
  attemptFailures: number;
  playA: number;
  playB: number;
  winRateA: number | null;
  winRateB: number | null;
  drawRate: number | null;
  intervalA: number[] | null;
  intervalB: number[] | null;
  winsAOnPlay: number;
  winsAOnDraw: number;
  winsBOnPlay: number;
  winsBOnDraw: number;
}
export interface HistoryRun {
  progress: Progress;
  matchups: Matchup[];
  timings: Timing[];
}
export interface History {
  version: number;
  decks: Deck[];
  capabilities: Capability;
  sessions: HistoryRun[];
}
export interface Sample {
  actualTurn: number;
  playerTurn: number;
  player: string;
  life: number;
  hand: number;
  lands: number;
  creatures: number;
  power: number;
  graveyard: number;
  library: number;
  artifacts: number;
  enchantments: number;
  handLands?: number;
  handSpells?: number;
}
export interface OpeningHand {
  player: string;
  [key: string]: unknown;
}
export interface ManaFlag {
  player: string;
  kind: string;
  [key: string]: unknown;
}
export interface GameResult {
  winner: string | null;
  draw: boolean;
  actualTurns: number;
  samples: Sample[];
  openingHands?: OpeningHand[];
  manaFlags?: ManaFlag[];
  durationMs: number;
  [key: string]: unknown;
}
export interface Attempt {
  number: number;
  startedAt: string;
  finishedAt?: string;
  status: string;
  error?: string;
  replay?: string;
  replayHash?: string;
}
export interface Slot {
  id: string;
  runId: string;
  deckA: string;
  deckB: string;
  starter: string;
  seed: ExactInteger;
  index: number;
  status: string;
  attempts: Attempt[];
  result?: GameResult;
  cachedFromRunId?: string;
  cachedFromGameId?: string;
}
export interface Snapshot {
  version: number;
  manaValues?: Record<string, number>;
  decks: DeckRow[];
  capabilities: Capability;
  runs: Run[];
  activeSessions: Progress[];
  timings: Record<string, Timing[]>;
  slots: Slot[];
  resources: Resource;
  turnStats?: {
    suggestedLimit: number;
    longestGame: number;
    games: number;
    sessions: number;
  };
}
export interface Mulligan {
  deckId: string;
  recordedPlayers: number;
  mean?: number;
  median?: number;
  maximum?: number;
  zero: number;
  one: number;
  two: number;
  threePlus: number;
  meanKeptSize?: number;
  meanOnPlay?: number;
  meanOnDraw?: number;
}
export interface Summary {
  run: Run;
  valid: number;
  failed: number;
  cancelled: number;
  running: number;
  pending: number;
  fullMatrix: boolean;
  matchups: Matchup[];
  manaStarvedGames: number;
  manaFloodedGames: number;
  manaAnalyzedGames: number;
  mulligans: Mulligan[];
  cachedGames: number;
  cachedSourceRunIds: string[];
}
export interface UiDeck {
  id: string;
  colors: string;
  type: string;
  subtype: string;
  variant: string;
  label: string;
  profiles: Record<string, string>;
}
export interface Mode {
  value: string;
  label: string;
  description: string;
}
export interface Catalog {
  maximumDecks: number;
  defaultPilotMode: string;
  modes: Mode[];
  decks: UiDeck[];
  pilotNames: Record<string, string>;
  pilotLabels: Record<string, string>;
  currentProfiles: string[];
}
export interface Resource {
  logicalProcessors: number;
  availableMemoryGb?: number;
  [key: string]: unknown;
}
export interface RunRequest {
  deckIds: string[];
  gamesPerPair: number;
  workers: number;
  timeoutSeconds: number;
  turnLimit: number;
  rootSeed: ExactInteger;
  diagnostic: boolean;
  staggerStarts: boolean;
  estimatedGameSeconds: number;
  startLeadSeconds: number;
  startupSpacingSeconds: number | null;
  performanceMode: boolean;
  pilotMode: string;
  workshopBaseline?: Baseline;
  workshopDeckId?: string;
}
export interface Estimate {
  preflight: {
    blockers: string[];
    limitations: string[];
    cachedGames: number;
    existingSessionId?: string;
    existingGamesPerPair?: number;
  };
  runtime?: {
    lowerSeconds: number;
    upperSeconds: number;
    observations: number;
    extrapolated: boolean;
    sessionObservations: number;
  };
}
export interface Note {
  id: string;
  title: string;
  body: string;
  author: string;
  model: string;
  exactModelId?: string;
  writtenAt: string;
  context?: string;
  evidence: string;
}
export interface ReplayCard {
  id: number;
  name: string;
  type: string;
  tapped: boolean;
  power?: number;
  toughness?: number;
  cost: string;
  text: string;
  owner: string;
  controller: string;
  counters: string;
  attachedTo?: string;
}
export interface ReplayState {
  activePlayer?: string;
  phase?: string;
  engineTurn: number;
  players: {
    id: string;
    life: number;
    handCount: number;
    libraryCount: number;
    personalTurn: number;
    battlefield: ReplayCard[];
    graveyard: ReplayCard[];
    exile: ReplayCard[];
    hand: ReplayCard[];
  }[];
  stack: string[];
}
export interface ReplayRecord {
  kind: string;
  sequence: ExactInteger;
  actualTurn: number;
  event?: string;
  detail?: string;
  state?: ReplayState;
  stateHash?: string;
  triggerCategory?: string;
  triggerSource?: string;
  triggerText?: string;
  pilotProfile?: string;
  decision?: string;
  selected?: string;
  reason?: string;
  options?: string[];
  visibleEvidence?: string[];
  activeProtocols?: string[];
  protocolSetVersion?: string;
  protocolDelay?: boolean;
  [key: string]: unknown;
}
export interface WorkshopSnapshot {
  decks: Deck[];
  runs: Run[];
  activeSessions: Progress[];
  manaValues: Record<string, number>;
  resources: Resource;
}
export interface Validation {
  cards: Card[];
  sideboard: Card[];
  errors: string[];
  blockers: string[];
  canSave: boolean;
}
export interface WorkshopMatch {
  run: Run;
  deck: Deck;
  opponent: Deck;
  summary: Matchup;
  wins: number;
  losses: number;
  draws: number;
  winRate: number | null;
  interval: number[] | null;
  medianRounds: number | null;
  replays: { gameId: string; outcome: string; actualTurns: number }[];
  [key: string]: unknown;
}
