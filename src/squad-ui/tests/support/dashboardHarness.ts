import { expect, type Locator, type Page } from '@playwright/test'
import type { RoleSnapshot } from '../../src/protocol/messages'

export const PROTOCOL_VERSION = 3

interface ProtocolMessage {
  version: number
  type: string
  requestId?: string
  role?: string
  payload?: unknown
}

interface HostDeliveryOptions {
  clearClientMessagesAfter?: boolean
}

export function protocolMessage(
  type: string,
  options: Omit<ProtocolMessage, 'version' | 'type'> = {},
): ProtocolMessage {
  return { version: PROTOCOL_VERSION, type, ...options }
}

export async function deliverHostMessages(
  page: Page,
  messages: ProtocolMessage[],
  options: HostDeliveryOptions = {},
) {
  await page.evaluate(({ hostMessages, clearClientMessagesAfter }) => {
    for (const message of hostMessages)
      window.__blaxquadHarness?.receive(message)
    if (clearClientMessagesAfter)
      window.__blaxquadHarness!.messages.length = 0
  }, {
    hostMessages: messages,
    clearClientMessagesAfter: options.clearClientMessagesAfter ?? false,
  })
}

export function roleTranscript(page: Page, role: string) {
  return page.locator('.role-panel')
    .filter({ has: page.getByRole('heading', { name: role, exact: true }) })
    .locator('.transcript')
}

export function roleSnapshot(
  role: string,
  overrides: Partial<Omit<RoleSnapshot, 'role'>> = {},
): RoleSnapshot {
  return {
    role,
    status: 'idle',
    isWorking: false,
    eventCount: 0,
    ...overrides,
  }
}

export const snapshot = {
  roles: [
    { role: 'coder', status: 'running', lastEventAt: '2026-03-01T12:00:00Z', transcriptEntries: [{ occurredAt: '2026-03-01T12:00:00Z', source: 'assistant', content: 'Reading the workspace.' }], activeTool: 'read_file', isWorking: true, eventCount: 42 },
    { role: 'reviewer', status: 'idle', lastEventAt: '2026-03-01T12:01:00Z', transcriptEntries: [{ occurredAt: '2026-03-01T12:01:00Z', source: 'assistant', content: 'Ready for review.' }], isWorking: false, eventCount: 7 },
    { role: 'writer', status: 'error', transcriptEntries: [{ occurredAt: '2026-03-01T12:02:00Z', source: 'error', content: 'Runtime disconnected.' }], error: 'Runtime disconnected.', isWorking: false, eventCount: 4 },
  ],
  permissions: [{ requestId: 'permission-1', role: 'coder', description: 'Run the selected command?' }],
  inputs: [{ requestId: 'input-1', role: 'reviewer', prompt: 'Choose a review depth.', choices: ['Quick', 'Thorough'], allowFreeform: true }],
  elicitations: [{
    requestId: 'elicitation-1',
    role: 'writer',
    prompt: 'Provide release details.',
    mode: 'form',
    requestedSchema: {
      type: 'object',
      required: ['name', 'count', 'mode'],
      properties: {
        name: { type: 'string', title: 'Release name', default: 'August', minLength: 2, maxLength: 20 },
        count: { type: 'integer', title: 'Build count', default: 2, minimum: 1, maximum: 10 },
        enabled: { type: 'boolean', title: 'Publish now', default: true },
        mode: { type: 'string', title: 'Release mode', default: 'safe', oneOf: [{ const: 'safe', title: 'Safe rollout' }, { const: 'fast', title: 'Fast rollout' }] },
        tags: { type: 'array', title: 'Tags', default: ['docs'], items: { type: 'string', enum: ['docs', 'runtime'] } },
      },
    },
  }, {
    requestId: 'elicitation-url',
    role: 'writer',
    prompt: 'Complete sign-in.',
    mode: 'url',
    url: 'https://example.test/authorize?client=blaxquad',
  }],
}

export const stateSnapshot = {
  ...snapshot,
  roles: snapshot.roles.map(({ transcriptEntries: _, ...role }) => role),
}

export const transcriptSynchronization = {
  roles: snapshot.roles.map(role => ({
    role: role.role,
    sequence: 1,
    entries: role.transcriptEntries.map((entry, entryIndex) => ({ ...entry, entryIndex })),
    hasMore: false,
    historyTruncated: false,
  })),
}

export async function loadSnapshot(page: Page) {
  await page.goto('/')
  await expect.poll(() => page.evaluate(() => window.__blaxquadHarness?.messages.length ?? 0)).toBe(1)
  await deliverHostMessages(page, [
    protocolMessage('state.snapshot', { payload: stateSnapshot }),
    protocolMessage('transcript.synchronize', { payload: transcriptSynchronization }),
  ])
}

export async function loadRoleSnapshots(page: Page, roles: RoleSnapshot[]) {
  await page.goto('/')
  await expect.poll(() => page.evaluate(() => window.__blaxquadHarness?.messages.length ?? 0)).toBe(1)
  await deliverHostMessages(page, [
    protocolMessage('state.snapshot', {
      payload: {
        ...stateSnapshot,
        roles,
        permissions: [],
        inputs: [],
        elicitations: [],
      },
    }),
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: roles.map(role => ({
          role: role.role,
          sequence: 1,
          entries: [],
          hasMore: false,
          historyTruncated: false,
        })),
      },
    }),
  ])
}

export function latestAnnouncement(announcer: Locator) {
  return announcer.locator('[data-transcript-announcement]').last()
}

export async function loadPagedTranscriptAtReadingPosition(page: Page) {
  await loadSnapshot(page)
  const liveEntries = Array.from({ length: 100 }, (_, offset) => ({
    entryIndex: offset + 100,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `live-${offset + 100}`,
  }))
  const pagedEntries = Array.from({ length: 100 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T11:00:00Z',
    source: 'assistant',
    content: `paged-${entryIndex}`,
  }))
  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 10,
          entries: liveEntries,
          hasMore: true,
          historyTruncated: false,
        }],
      },
    }),
    protocolMessage('transcript.page', {
      payload: {
        role: 'coder',
        entries: pagedEntries,
        hasMore: false,
        historyTruncated: false,
      },
    }),
  ])
  const transcript = roleTranscript(page, 'coder')
  await transcript.evaluate(element => {
    element.scrollTop = 40 * 22
    element.dispatchEvent(new Event('scroll'))
    element.dataset.sawInvalidResetWindow = 'false'
    new MutationObserver(() => {
      const ids = [...element.querySelectorAll<HTMLElement>(
        '.transcript-line[data-entry-index]')]
        .map(row => row.dataset.entryIndex)
      if (ids.length === 0 || new Set(ids).size !== ids.length)
        element.dataset.sawInvalidResetWindow = 'true'
    }).observe(element, { childList: true, subtree: true })
  })
  const anchor = transcript.locator('[data-entry-index="40"]')
  await expect(anchor).toBeVisible()
  return {
    transcript,
    anchorOffset: await anchor.evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)),
  }
}

export async function firstVisibleTranscriptRow(transcript: Locator) {
  return transcript.evaluate((element) => {
    const viewportTop = element.getBoundingClientRect().top
    const row = [...element.querySelectorAll<HTMLElement>('.transcript-line')]
      .find(candidate => candidate.getBoundingClientRect().bottom >= viewportTop)
    if (!row) throw new Error('No visible transcript row.')
    return {
      entryIndex: Number(row.dataset.entryIndex),
      offset: row.getBoundingClientRect().top - viewportTop,
    }
  })
}
