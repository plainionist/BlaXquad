import { expect, test } from '@playwright/test'
import {
  deliverHostMessages,
  loadSnapshot,
  protocolMessage,
  roleTranscript,
  stateSnapshot,
} from './support/dashboardHarness'

test('retains the final text across rapid transcript replacements', async ({ page }) => {
  await loadSnapshot(page)

  await deliverHostMessages(page, Array.from({ length: 100 }, (_, index) =>
    protocolMessage('transcript.update', {
      payload: {
        role: 'coder',
        sequence: index + 2,
        operation: 'replace',
        entryIndex: 0,
        entry: { occurredAt: '2026-03-01T12:00:00Z', source: 'assistant', content: `chunk-${index + 1}` },
      },
    })))

  await expect(roleTranscript(page, 'coder')
    .locator('.transcript-content')
    .filter({ hasText: 'chunk-100' })).toBeVisible()
})

test('applies transcript deltas to one role without replacing prior entries', async ({ page }) => {
  await loadSnapshot(page)

  await deliverHostMessages(page, [
    protocolMessage('transcript.update', {
      payload: {
        role: 'reviewer',
        sequence: 2,
        operation: 'append',
        entryIndex: 1,
        entry: { occurredAt: '2026-03-01T12:02:00Z', source: 'assistant', content: 'Review ' },
      },
    }),
    protocolMessage('transcript.update', {
      payload: {
        role: 'reviewer',
        sequence: 3,
        operation: 'append-content',
        entryIndex: 1,
        content: 'complete',
      },
    }),
  ])

  await expect(page.getByText('Review complete')).toBeVisible()
  await expect(page.getByText('Reading the workspace.')).toBeVisible()
})

test('requests synchronization after an out-of-order transcript update', async ({ page }) => {
  await loadSnapshot(page)
  await page.evaluate(() => { window.__blaxquadHarness!.messages.length = 0 })

  await deliverHostMessages(page, [
    protocolMessage('transcript.update', {
      payload: {
        role: 'coder',
        sequence: 99,
        operation: 'append-content',
        entryIndex: 0,
        content: 'missed updates',
      },
    }),
  ])

  await expect.poll(() => page.evaluate(() => window.__blaxquadHarness?.messages.at(-1))).toContain('"type":"transcript.synchronize"')
  const request = await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))
  expect(request.payload.roles).toContainEqual({
    role: 'coder',
    visualSequence: 1,
    announcementSequence: 1,
  })
  await expect(page.getByText('Reading the workspace.')).toBeVisible()
})

test('ignores duplicate transcript updates without requesting synchronization', async ({ page }) => {
  await loadSnapshot(page)
  await page.evaluate(() => { window.__blaxquadHarness!.messages.length = 0 })

  await deliverHostMessages(page, [
    protocolMessage('transcript.update', {
      payload: {
        role: 'coder',
        sequence: 1,
        operation: 'replace',
        entryIndex: 0,
        entry: { occurredAt: '2026-03-01T12:00:00Z', source: 'assistant', content: 'duplicate' },
      },
    }),
  ])

  await page.waitForTimeout(50)
  expect(await page.evaluate(() => window.__blaxquadHarness?.messages.length)).toBe(0)
  await expect(page.getByText('Reading the workspace.')).toBeVisible()
})

test('loads an older transcript page across sparse retained indices', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 3,
          entries: [
            { entryIndex: 0, occurredAt: '2026-03-01T12:00:00Z', source: 'user', content: 'live oldest' },
            { entryIndex: 2, occurredAt: '2026-03-01T12:02:00Z', source: 'assistant', content: 'newest' },
          ],
          hasMore: true,
          historyTruncated: false,
        }],
      },
    }),
  ], { clearClientMessagesAfter: true })

  await page.getByRole('button', { name: 'Load older' }).click()
  await expect.poll(() => page.evaluate(() => window.__blaxquadHarness?.messages.at(-1))).toContain('"beforeIndex":2')
  await deliverHostMessages(page, [
    protocolMessage('transcript.page', {
      payload: {
        role: 'coder',
        entries: [
          { entryIndex: 0, occurredAt: '2026-03-01T12:00:00Z', source: 'user', content: 'stale oldest' },
          { entryIndex: 1, occurredAt: '2026-03-01T12:01:00Z', source: 'assistant', content: 'older' },
        ],
        hasMore: false,
        historyTruncated: true,
      },
    }),
  ])

  await expect(page.getByText('live oldest')).toBeVisible()
  await expect(page.getByText('stale oldest')).toHaveCount(0)
  await expect(page.getByText('newest')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Load older' })).toHaveCount(0)
  await expect(page.getByText('Earlier transcript history is unavailable.')).toBeVisible()
})

test('stops paging when a sparse transcript gap was truncated', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 3,
          entries: [
            { entryIndex: 0, occurredAt: '2026-03-01T12:00:00Z', source: 'harness', content: 'pending' },
            { entryIndex: 10, occurredAt: '2026-03-01T12:10:00Z', source: 'assistant', content: 'recent' },
          ],
          hasMore: true,
          historyTruncated: true,
        }],
      },
    }),
  ], { clearClientMessagesAfter: true })

  await page.getByRole('button', { name: 'Load older' }).click()
  await expect.poll(() => page.evaluate(() => window.__blaxquadHarness?.messages.at(-1))).toContain('"beforeIndex":10')
  await deliverHostMessages(page, [
    protocolMessage('transcript.page', {
      payload: {
        role: 'coder',
        entries: [],
        hasMore: false,
        historyTruncated: true,
      },
    }),
  ])

  await expect(page.getByRole('button', { name: 'Load older' })).toHaveCount(0)
  await expect(page.getByText('Earlier transcript history is unavailable.')).toBeVisible()
})

test('loads archived content for a truncated entry without accepting stale data', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 4,
          entries: [{
            entryIndex: 7,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: 'recent tail',
            hasArchivedContent: true,
          }],
          hasMore: false,
          historyTruncated: false,
        }],
      },
    }),
  ], { clearClientMessagesAfter: true })

  await page.getByRole('button', { name: 'Load full entry' }).click()
  await expect.poll(() => page.evaluate(() => window.__blaxquadHarness?.messages.at(-1))).toContain('"type":"transcript.entry"')
  await expect.poll(() => page.evaluate(() => window.__blaxquadHarness?.messages.at(-1))).toContain('"entryIndex":7')

  await deliverHostMessages(page, [
    protocolMessage('transcript.entry', {
      payload: {
        role: 'coder',
        sequence: 3,
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: 'stale archived content',
          hasArchivedContent: false,
        },
        contentTruncated: false,
        totalContentCharacters: 22,
        archivedPrefixCharacters: 22,
      },
    }),
  ])
  await expect(page.getByText('recent tail')).toBeVisible()
  await expect(page.getByText('stale archived content')).toHaveCount(0)

  await page.evaluate(() => { window.__blaxquadHarness!.messages.length = 0 })
  await page.getByRole('button', { name: 'Load full entry' }).click()
  await expect.poll(() =>
    page.evaluate(() => window.__blaxquadHarness?.messages.length)).toBe(1)
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages[0]))).toEqual({
    version: 3,
    type: 'transcript.entry',
    role: 'coder',
    payload: { entryIndex: 7 },
  })
  await deliverHostMessages(page, [
    protocolMessage('transcript.entry', {
      payload: {
        role: 'coder',
        sequence: 4,
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: 'complete archived content',
          hasArchivedContent: false,
        },
        contentTruncated: false,
        totalContentCharacters: 25,
        archivedPrefixCharacters: 25,
      },
    }),
  ])
  await expect(roleTranscript(page, 'coder')
    .locator('[data-entry-index="7"] .transcript-content'))
    .toHaveJSProperty('textContent', 'complete archived content')
  await expect(page.getByRole('button', { name: 'Load full entry' })).toHaveCount(0)
})

test('preserves the live tail when archived entry content is capped', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 4,
          entries: [{
            entryIndex: 7,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: '[Earlier content is available in transcript history.]\nfghijklmno',
            hasArchivedContent: true,
            contentStart: 5,
          }],
          hasMore: false,
          historyTruncated: true,
        }],
      },
    }),
  ])

  await page.getByRole('button', { name: 'Load full entry' }).click()
  await deliverHostMessages(page, [
    protocolMessage('transcript.entry', {
      payload: {
        role: 'coder',
        sequence: 4,
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: 'abcdefghij\n[Transcript content truncated at the configured storage limit.]',
          hasArchivedContent: false,
        },
        contentTruncated: true,
        totalContentCharacters: 15,
        archivedPrefixCharacters: 10,
      },
    }),
  ])

  await expect(roleTranscript(page, 'coder')
    .locator('[data-entry-index="7"] .transcript-content'))
    .toHaveJSProperty('textContent', 'abcdefghijklmno')
  await expect(page.getByText(/fghijfghij/)).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Load full entry' })).toHaveCount(0)
})

test('marks archived content unavailable with the Vue replacement marker after rotation', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 4,
          entries: [{
            entryIndex: 7,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: '[Earlier content is available in transcript history.]\nretained tail',
            hasArchivedContent: true,
            contentStart: 20,
          }],
          hasMore: false,
          historyTruncated: true,
        }],
      },
    }),
  ])

  await page.getByRole('button', { name: 'Load full entry' }).click()
  await deliverHostMessages(page, [
    protocolMessage('transcript.entry', {
      payload: {
        role: 'coder',
        sequence: 4,
        entryIndex: 7,
        entry: null,
        contentTruncated: false,
        totalContentCharacters: 0,
        archivedPrefixCharacters: 0,
      },
    }),
  ])

  await expect(roleTranscript(page, 'coder')
    .locator('[data-entry-index="7"] .transcript-content'))
    .toHaveJSProperty(
      'textContent',
      '[Earlier content is no longer available.]\nretained tail')
  await expect(page.getByRole('button', { name: 'Load full entry' })).toHaveCount(0)
})

test('uses the full Vue unavailable marker when archived content has no complete marker', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 4,
          entries: [{
            entryIndex: 7,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: '[Earlier content is available ',
            hasArchivedContent: true,
            contentStart: 20,
          }],
          hasMore: false,
          historyTruncated: true,
        }],
      },
    }),
  ])

  await page.getByRole('button', { name: 'Load full entry' }).click()
  await deliverHostMessages(page, [
    protocolMessage('transcript.entry', {
      payload: {
        role: 'coder',
        sequence: 4,
        entryIndex: 7,
        entry: null,
        contentTruncated: false,
        totalContentCharacters: 0,
        archivedPrefixCharacters: 0,
      },
    }),
  ])

  await expect(roleTranscript(page, 'coder')
    .locator('[data-entry-index="7"] .transcript-content'))
    .toHaveJSProperty(
      'textContent',
      '[Earlier content is no longer available.]')
})

test('marks unavailable middle content between a capped prefix and retained tail', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 4,
          entries: [{
            entryIndex: 7,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: '[Earlier content is available in transcript history.]\nklmnopqrst',
            hasArchivedContent: true,
            contentStart: 10,
          }],
          hasMore: false,
          historyTruncated: true,
        }],
      },
    }),
  ])

  await page.getByRole('button', { name: 'Load full entry' }).click()
  await deliverHostMessages(page, [
    protocolMessage('transcript.entry', {
      payload: {
        role: 'coder',
        sequence: 4,
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: 'abcde\n[Transcript content truncated at the configured storage limit.]',
          hasArchivedContent: false,
        },
        contentTruncated: true,
        totalContentCharacters: 20,
        archivedPrefixCharacters: 5,
      },
    }),
  ])

  await expect(roleTranscript(page, 'coder')
    .locator('[data-entry-index="7"] .transcript-content'))
    .toHaveJSProperty(
      'textContent',
      'abcde\n[Middle content unavailable due to storage limit.]\nklmnopqrst')
  await expect(page.getByRole('button', { name: 'Load full entry' })).toHaveCount(0)
})

test('preserves the zero-tail gap newline for subsequent live content', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 4,
          entries: [{
            entryIndex: 7,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: '[Earlier content is available ',
            hasArchivedContent: true,
            contentStart: 20,
          }],
          hasMore: false,
          historyTruncated: true,
        }],
      },
    }),
  ])

  await page.getByRole('button', { name: 'Load full entry' }).click()
  await deliverHostMessages(page, [
    protocolMessage('transcript.entry', {
      payload: {
        role: 'coder',
        sequence: 4,
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: 'abcde\n[Transcript content truncated at the configured storage limit.]',
          hasArchivedContent: false,
        },
        contentTruncated: true,
        totalContentCharacters: 20,
        archivedPrefixCharacters: 5,
      },
    }),
  ])

  await expect(roleTranscript(page, 'coder')
    .locator('[data-entry-index="7"] .transcript-content'))
    .toHaveJSProperty(
      'textContent',
      'abcde\n[Middle content unavailable due to storage limit.]')
  await expect(page.getByRole('button', { name: 'Load full entry' })).toHaveCount(0)

  await deliverHostMessages(page, [
    protocolMessage('transcript.update', {
      payload: {
        role: 'coder',
        sequence: 5,
        operation: 'append-content',
        entryIndex: 7,
        content: 'continued',
      },
    }),
  ])

  await expect(roleTranscript(page, 'coder')
    .locator('[data-entry-index="7"] .transcript-content'))
    .toHaveJSProperty(
      'textContent',
      'abcde\n[Middle content unavailable due to storage limit.]\ncontinued')
})

test('reconstructs an archived entry evicted from backend live retention but retained by the browser', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 5,
          entries: [{
            entryIndex: 8,
            occurredAt: '2026-03-01T12:01:00Z',
            source: 'assistant',
            content: 'new live entry',
          }],
          hasMore: true,
          historyTruncated: false,
        }],
      },
    }),
  ], { clearClientMessagesAfter: true })

  await page.getByRole('button', { name: 'Load older' }).click()
  await deliverHostMessages(page, [
    protocolMessage('transcript.page', {
      payload: {
        role: 'coder',
        entries: [{
          entryIndex: 7,
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: '[Earlier content is available in transcript history.]\ntail',
          hasArchivedContent: true,
          contentStart: 16,
        }],
        hasMore: false,
        historyTruncated: false,
      },
    }),
  ])

  await page.getByRole('button', { name: 'Load full entry' }).click()
  await deliverHostMessages(page, [
    protocolMessage('transcript.entry', {
      payload: {
        role: 'coder',
        sequence: 5,
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:01:00Z',
          source: 'assistant',
          content: 'backend archived content',
          hasArchivedContent: false,
        },
        contentTruncated: false,
        totalContentCharacters: 24,
        archivedPrefixCharacters: 24,
      },
    }),
  ])

  const transcript = roleTranscript(page, 'coder')
  await expect(transcript
    .locator('[data-entry-index="7"] .transcript-content'))
    .toHaveJSProperty('textContent', 'backend archived content')
  await expect(transcript
    .locator('[data-entry-index="8"] .transcript-content'))
    .toHaveJSProperty('textContent', 'new live entry')
  await expect(page.getByRole('button', { name: 'Load full entry' })).toHaveCount(0)

  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        recovery: true,
        roles: [{
          role: 'coder',
          sequence: 5,
          entries: [{
            entryIndex: 8,
            occurredAt: '2026-03-01T12:01:00Z',
            source: 'assistant',
            content: 'new live entry',
          }],
          hasMore: true,
          historyTruncated: false,
        }],
      },
    }),
  ])
  await expect(transcript
    .locator('[data-entry-index="7"] .transcript-content'))
    .toHaveJSProperty('textContent', 'backend archived content')
})

test('clears a pending archive request when the entry was evicted before the response', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 4,
          entries: [{
            entryIndex: 7,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: 'retained tail',
            hasArchivedContent: true,
            contentStart: 20,
          }],
          hasMore: false,
          historyTruncated: false,
        }],
      },
    }),
  ], { clearClientMessagesAfter: true })

  await page.getByRole('button', { name: 'Load full entry' }).click()
  await expect.poll(() =>
    page.evaluate(() => window.__blaxquadHarness?.messages.length)).toBe(1)

  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 5,
          entries: [{
            entryIndex: 8,
            occurredAt: '2026-03-01T12:01:00Z',
            source: 'assistant',
            content: 'new live entry',
          }],
          hasMore: false,
          historyTruncated: false,
        }],
      },
    }),
    protocolMessage('transcript.entry', {
      payload: {
        role: 'coder',
        sequence: 5,
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: 'post-eviction archive response',
          hasArchivedContent: false,
        },
        contentTruncated: false,
        totalContentCharacters: 30,
        archivedPrefixCharacters: 30,
      },
    }),
  ])

  await expect(roleTranscript(page, 'coder')
    .locator('[data-entry-index="7"]')).toHaveCount(0)
  await expect(page.getByText('post-eviction archive response')).toHaveCount(0)

  await deliverHostMessages(page, [
    protocolMessage('transcript.synchronize', {
      payload: {
        roles: [{
          role: 'coder',
          sequence: 6,
          entries: [{
            entryIndex: 7,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: 'available again',
            hasArchivedContent: true,
            contentStart: 20,
          }],
          hasMore: false,
          historyTruncated: false,
        }],
      },
    }),
  ], { clearClientMessagesAfter: true })

  await page.getByRole('button', { name: 'Load full entry' }).click()
  await expect.poll(() =>
    page.evaluate(() => window.__blaxquadHarness?.messages.length)).toBe(1)
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages[0]))).toEqual({
    version: 3,
    type: 'transcript.entry',
    role: 'coder',
    payload: { entryIndex: 7 },
  })
})

test('rejects version 2 host messages', async ({ page }) => {
  await loadSnapshot(page)

  await deliverHostMessages(page, [{
    ...protocolMessage('state.snapshot', { payload: stateSnapshot }),
    version: 2,
  }])
  await expect(page.locator('.protocol-error')).toContainText('unsupported protocol version')
})
