import { expect, test } from '@playwright/test'
import { formatTimestamp } from '../src/transcript/transcriptProjection'
import {
  deliverHostMessages,
  loadRoleSnapshots,
  loadSnapshot,
  protocolMessage,
  roleSnapshot,
  roleTranscript,
} from './support/dashboardHarness'

test('renders a semantic time element with local HH:mm:ss before the marker and content', async ({ page }) => {
  await loadSnapshot(page)

  const row = roleTranscript(page, 'coder').locator('.transcript-line').first()
  const time = row.locator('time')
  await expect(time).toHaveAttribute('datetime', '2026-03-01T12:00:00Z')
  await expect(time).toHaveText(formatTimestamp('2026-03-01T12:00:00Z'))

  const childOrder = await row.evaluate(element =>
    [...element.children].map(child => child.tagName))
  expect(childOrder[0]).toBe('TIME')
})

test('keeps exactly one timestamp on an entry across append and append-content updates', async ({ page }) => {
  await loadSnapshot(page)

  await deliverHostMessages(page, [
    protocolMessage('transcript.update', {
      payload: {
        role: 'reviewer',
        sequence: 2,
        operation: 'append',
        entryIndex: 1,
        entry: { occurredAt: '2026-03-01T12:02:30Z', source: 'assistant', content: 'Review ' },
      },
    }),
  ])

  const row = roleTranscript(page, 'reviewer').locator('[data-entry-index="1"]')
  await expect(row.locator('time')).toHaveCount(1)
  await expect(row.locator('time')).toHaveAttribute('datetime', '2026-03-01T12:02:30Z')
  await expect(row.locator('time')).toHaveText(formatTimestamp('2026-03-01T12:02:30Z'))

  await deliverHostMessages(page, [
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

  await expect(row.locator('.transcript-content')).toHaveText('Review complete')
  await expect(row.locator('time')).toHaveCount(1)
  await expect(row.locator('time')).toHaveAttribute('datetime', '2026-03-01T12:02:30Z')
})

test('keeps exactly one timestamp on an entry after a replace update', async ({ page }) => {
  await loadSnapshot(page)

  await deliverHostMessages(page, [
    protocolMessage('transcript.update', {
      payload: {
        role: 'coder',
        sequence: 2,
        operation: 'replace',
        entryIndex: 0,
        entry: { occurredAt: '2026-03-01T12:05:00Z', source: 'assistant', content: 'replaced text' },
      },
    }),
  ])

  const row = roleTranscript(page, 'coder').locator('[data-entry-index="0"]')
  await expect(row.locator('.transcript-content')).toHaveText('replaced text')
  await expect(row.locator('time')).toHaveCount(1)
  await expect(row.locator('time')).toHaveAttribute('datetime', '2026-03-01T12:05:00Z')
  await expect(row.locator('time')).toHaveText(formatTimestamp('2026-03-01T12:05:00Z'))
})

test('renders one timestamp for a multi-line entry rather than one per visual line', async ({ page }) => {
  await loadSnapshot(page)

  await deliverHostMessages(page, [
    protocolMessage('transcript.update', {
      payload: {
        role: 'coder',
        sequence: 2,
        operation: 'append',
        entryIndex: 1,
        entry: {
          occurredAt: '2026-03-01T12:03:00Z',
          source: 'assistant',
          content: 'line one\nline two\nline three',
        },
      },
    }),
  ])

  const row = roleTranscript(page, 'coder').locator('[data-entry-index="1"]')
  await expect(row.locator('.transcript-content')).toHaveText('line one\nline two\nline three')
  await expect(row.locator('time')).toHaveCount(1)
})

test('leaves the transient Thinking row untimestamped', async ({ page }) => {
  await loadRoleSnapshots(page, [
    roleSnapshot('thinking', { status: 'idle', isWorking: true }),
  ])

  const thinkingRow = roleTranscript(page, 'thinking').locator('.is-thinking')
  await expect(thinkingRow).toHaveText('>Thinking ...')
  await expect(thinkingRow.locator('time')).toHaveCount(0)
})
