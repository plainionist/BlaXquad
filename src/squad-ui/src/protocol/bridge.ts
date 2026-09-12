import {
  type ArchivedTranscriptEntry,
  type Envelope,
  type IssueListPayload,
  type Snapshot,
  type TranscriptPage,
  type TranscriptSynchronization,
  type TranscriptUpdate,
  type WorkspaceToolsSnapshot,
} from './messages'

interface HostExternal { sendMessage(message: string): void; receiveMessage(callback: (message: string) => void): void }

declare global {
  interface Window { __blaxquadHarness?: { messages: string[]; receive: (message: Envelope) => void; receiveRaw: (raw: string) => void } }
}

export function createBridge() {
  const host = window.external as unknown as HostExternal | undefined
  let snapshotListener: (snapshot: Snapshot) => void = () => undefined
  let transcriptSynchronizationListener: (synchronization: TranscriptSynchronization) => void = () => undefined
  let transcriptUpdateListener: (update: TranscriptUpdate) => void = () => undefined
  let transcriptPageListener: (page: TranscriptPage) => void = () => undefined
  let archivedTranscriptEntryListener: (entry: ArchivedTranscriptEntry) => void = () => undefined
  let issuesListener: (issues: IssueListPayload, requestId?: string) => void = () => undefined
  let workspaceToolsListener: (snapshot: WorkspaceToolsSnapshot) => void = () => undefined
  let errorListener: (message: string, requestId?: string) => void = () => undefined
  const receive = (raw: string) => {
    try {
      const message = JSON.parse(raw) as Envelope
      if (message.type === 'state.snapshot') return snapshotListener(message.payload as Snapshot)
      if (message.type === 'transcript.synchronize') return transcriptSynchronizationListener(message.payload as TranscriptSynchronization)
      if (message.type === 'transcript.update') return transcriptUpdateListener(message.payload as TranscriptUpdate)
      if (message.type === 'transcript.page') return transcriptPageListener(message.payload as TranscriptPage)
      if (message.type === 'transcript.entry') return archivedTranscriptEntryListener(message.payload as ArchivedTranscriptEntry)
      if (message.type === 'issues.list') return issuesListener(message.payload as IssueListPayload, message.requestId)
      if (message.type === 'workspace-tools.snapshot') return workspaceToolsListener(message.payload as WorkspaceToolsSnapshot)
      if (message.type === 'protocol.error') return errorListener((message.payload as { message?: string })?.message ?? 'The host rejected a message.', message.requestId)
      errorListener(`Unknown host message '${message.type}'.`)
    } catch {
      errorListener('The host sent malformed protocol data.')
    }
  }

  if (host?.receiveMessage) host.receiveMessage(receive)
  else window.__blaxquadHarness = { messages: [], receive: (message) => receive(JSON.stringify(message)), receiveRaw: receive }

  return {
    onSnapshot(listener: (snapshot: Snapshot) => void) { snapshotListener = listener },
    onTranscriptSynchronization(listener: (synchronization: TranscriptSynchronization) => void) { transcriptSynchronizationListener = listener },
    onTranscriptUpdate(listener: (update: TranscriptUpdate) => void) { transcriptUpdateListener = listener },
    onTranscriptPage(listener: (page: TranscriptPage) => void) { transcriptPageListener = listener },
    onArchivedTranscriptEntry(listener: (entry: ArchivedTranscriptEntry) => void) { archivedTranscriptEntryListener = listener },
    onIssues(listener: (issues: IssueListPayload, requestId?: string) => void) { issuesListener = listener },
    onWorkspaceTools(listener: (snapshot: WorkspaceToolsSnapshot) => void) { workspaceToolsListener = listener },
    onError(listener: (message: string, requestId?: string) => void) { errorListener = listener },
    send(type: string, options: Omit<Envelope, 'type'> = {}) {
      const message: Envelope = { type, ...options }
      const serialized = JSON.stringify(message)
      if (host?.sendMessage) host.sendMessage(serialized)
      else window.__blaxquadHarness?.messages.push(serialized)
    },
    dispose() { if (!host?.sendMessage) delete window.__blaxquadHarness },
  }
}