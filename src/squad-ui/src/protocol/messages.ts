export const PROTOCOL_VERSION = 3

export interface TranscriptEntry { occurredAt: string; source: string; content: string; hasArchivedContent?: boolean; contentStart?: number }
export interface IndexedTranscriptEntry extends TranscriptEntry { entryIndex: number }
export interface TranscriptAnnouncement {
  entryIndex: number
  operation: 'append' | 'append-content' | 'replace'
  content: string
  truncated?: boolean
}
export interface SequencedTranscriptAnnouncement extends TranscriptAnnouncement {
  sequence: number
}
export interface TranscriptMutation {
  generation: number
  kind: 'reset' | 'append' | 'replace' | 'merge'
  entryIndex?: number
  sourceIndex?: number
  previousSourceLength?: number
  previouslyRenderable?: boolean
}

export interface RoleSnapshot {
  role: string
  status: string
  lastEventAt?: string
  error?: string
  activeTool?: string
  isWorking: boolean
  model?: string
  effort?: string
  aicUsed?: number | null
  contextUsedTokens?: number | null
  contextLimitTokens?: number | null
  eventCount: number
}

export interface RoleState extends RoleSnapshot {
  transcriptEntries: TranscriptEntry[]
  transcriptEntryIndices: number[]
  transcriptMutation?: TranscriptMutation
}

export interface RoleTranscriptSynchronization {
  role: string
  sequence: number
  entries: IndexedTranscriptEntry[]
  hasMore: boolean
  historyTruncated: boolean
  announcementAfter?: number
  announcementThrough?: number
  announcement?: { fragments: SequencedTranscriptAnnouncement[]; truncated: boolean } | null
}

export interface TranscriptSynchronization {
  recovery?: boolean
  roles: RoleTranscriptSynchronization[]
}

export interface TranscriptPage {
  role: string
  entries: IndexedTranscriptEntry[]
  hasMore: boolean
  historyTruncated: boolean
}

export interface ArchivedTranscriptEntry {
  role: string
  sequence: number
  entryIndex: number
  entry?: TranscriptEntry | null
  contentTruncated: boolean
  totalContentCharacters: number
  archivedPrefixCharacters: number
}

export interface TranscriptUpdate {
  role: string
  sequence: number
  operation: 'append' | 'append-content' | 'replace'
  entryIndex: number
  entry?: TranscriptEntry | null
  content?: string | null
  announcement?: TranscriptAnnouncement | null
}

export interface Permission { requestId: string; role: string; description: string }
export interface InputRequest { requestId: string; role: string; prompt: string; choices?: string[]; allowFreeform: boolean }
export interface ElicitationChoice { const: string | number; title?: string }
export interface ElicitationProperty {
  type?: 'string' | 'number' | 'integer' | 'boolean' | 'array'
  title?: string
  default?: unknown
  enum?: Array<string | number>
  oneOf?: ElicitationChoice[]
  items?: { type?: 'string'; enum?: string[]; oneOf?: Array<{ const: string; title?: string }> }
  minLength?: number
  maxLength?: number
  pattern?: string
  minimum?: number
  maximum?: number
  minItems?: number
  maxItems?: number
}
export interface ElicitationSchema {
  type?: 'object'
  properties?: Record<string, ElicitationProperty>
  required?: string[]
}
export interface Elicitation { requestId: string; role: string; prompt: string; mode: string; requestedSchema?: ElicitationSchema; url?: string }
export interface Snapshot { roles: RoleSnapshot[]; permissions: Permission[]; inputs: InputRequest[]; elicitations: Elicitation[] }

export interface Envelope { version: number; type: string; requestId?: string; role?: string; payload?: unknown }
