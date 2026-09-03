import { expect, test } from '@playwright/test'
import { latestAnnouncement, loadSnapshot } from './support/dashboardHarness'

test('announces live transcript updates without announcing virtualized history', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `history-${entryIndex}`,
  }))
  await page.evaluate((history) => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'coder',
          sequence: 1,
          entries: history,
          hasMore: false,
          historyTruncated: false,
        }],
      },
    })
  }, entries)

  const panel = page.locator('.role-panel').filter({ hasText: 'coder' })
  const transcript = panel.locator('.transcript')
  const announcer = panel.locator('[data-transcript-announcer]')
  await expect(transcript).toHaveAttribute('aria-live', 'off')
  await expect(announcer).toHaveAttribute('aria-live', 'polite')
  await expect(announcer).toBeEmpty()

  await transcript.evaluate(element => {
    element.scrollTop = element.scrollHeight / 2
    element.dispatchEvent(new Event('scroll'))
  })
  await page.waitForTimeout(50)
  await expect(announcer).toBeEmpty()

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 2,
        operation: 'append',
        entryIndex: 500,
        entry: {
          occurredAt: '2026-03-01T12:01:00Z',
          source: 'assistant',
          content: 'new live output',
        },
        announcement: {
          entryIndex: 500,
          operation: 'append',
          content: 'new live output',
        },
      },
    })
  })
  await expect(latestAnnouncement(announcer)).toHaveText('new live output')

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 3,
        operation: 'append-content',
        entryIndex: 500,
        content: ' continued',
        announcement: {
          entryIndex: 500,
          operation: 'append-content',
          content: ' continued',
        },
      },
    })
  })
  await expect(latestAnnouncement(announcer)).toHaveText(' continued')

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 4,
        operation: 'replace',
        entryIndex: 500,
        entry: {
          occurredAt: '2026-03-01T12:01:00Z',
          source: 'assistant',
          content: 'revised final output',
        },
        announcement: {
          entryIndex: 500,
          operation: 'replace',
          content: 'revised final output',
        },
      },
    })
  })
  await expect(latestAnnouncement(announcer)).toHaveText('revised final output')

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'coder',
          sequence: 5,
          entries: [{
            entryIndex: 7,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: '[Earlier content is available in transcript history.]\nabcdefghij',
            hasArchivedContent: true,
            contentStart: 100,
          }],
          hasMore: false,
          historyTruncated: false,
        }],
      },
    })
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 6,
        operation: 'replace',
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:01Z',
          source: 'assistant',
          content: '[Earlier content is available in transcript history.]\nfghijklmno',
          hasArchivedContent: true,
          contentStart: 105,
        },
        announcement: {
          entryIndex: 7,
          operation: 'append-content',
          content: 'klmno',
        },
      },
    })
  })
  await expect(latestAnnouncement(announcer)).toHaveText('klmno')

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 7,
        operation: 'replace',
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:02Z',
          source: 'assistant',
          content: '[Earlier content is available in transcript history.]\nXXXXXpqrst',
          hasArchivedContent: true,
          contentStart: 110,
        },
        announcement: {
          entryIndex: 7,
          operation: 'replace',
          content: 'XXXXXpqrst',
        },
      },
    })
  })
  await expect(latestAnnouncement(announcer)).toHaveText('XXXXXpqrst')

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 8,
        operation: 'replace',
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:03Z',
          source: 'assistant',
          content: '[Earlier content is available in transcript history.]\naaaaaaaaaa',
          hasArchivedContent: true,
          contentStart: 120,
        },
        announcement: {
          entryIndex: 7,
          operation: 'replace',
          content: 'aaaaaaaaaa',
        },
      },
    })
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 9,
        operation: 'replace',
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:04Z',
          source: 'assistant',
          content: '[Earlier content is available in transcript history.]\naaaaaaaaaa',
          hasArchivedContent: true,
          contentStart: 125,
        },
        announcement: {
          entryIndex: 7,
          operation: 'append-content',
          content: 'aaaaa',
        },
      },
    })
  })
  await expect(latestAnnouncement(announcer)).toHaveText('aaaaaaaaaaaaaaa')
})

test('announces recovered live output after transcript resynchronization', async ({ page }) => {
  await loadSnapshot(page)
  const panel = page.locator('.role-panel').filter({ hasText: 'coder' })
  const announcer = panel.locator('[data-transcript-announcer]')
  await expect(announcer).toBeEmpty()

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 3,
        announcementAfter: 1,
        announcementThrough: 3,
        operation: 'append-content',
        entryIndex: 0,
        content: ' missed',
      },
    })
  })
  await expect.poll(() => page.evaluate(() => window.__blaxquadHarness?.messages.at(-1)))
    .toContain('"type":"transcript.synchronize"')
  await expect(announcer).toBeEmpty()

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        recovery: true,
        roles: [{
          role: 'coder',
          sequence: 3,
          entries: [{
            entryIndex: 0,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: 'Reading the workspace. recovered',
          }],
          hasMore: false,
          historyTruncated: false,
          announcement: {
            fragments: [{
              sequence: 3,
              entryIndex: 0,
              operation: 'append-content',
              content: ' recovered',
            }],
            truncated: false,
          },
        }],
      },
    })
  })
  await expect(latestAnnouncement(announcer)).toHaveText(' recovered')

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        recovery: true,
        roles: [{
          role: 'coder',
          sequence: 4,
          announcementAfter: 3,
          announcementThrough: 4,
          entries: [{
            entryIndex: 0,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: 'Reading the workspace. recovered by host',
          }],
          hasMore: false,
          historyTruncated: false,
          announcement: {
            fragments: [{
              sequence: 4,
              entryIndex: 0,
              operation: 'append-content',
              content: ' by host',
            }],
            truncated: false,
          },
        }],
      },
    })
  })
  await expect(latestAnnouncement(announcer)).toHaveText(' by host')
})

test('announces live output after archived content changes the display entry', async ({ page }) => {
  await loadSnapshot(page)
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'coder',
          sequence: 4,
          entries: [{
            entryIndex: 7,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: '[Earlier content is available in transcript history.]\nabcdefghij',
            hasArchivedContent: true,
            contentStart: 100,
          }],
          hasMore: false,
          historyTruncated: false,
        }],
      },
    })
  })

  const panel = page.locator('.role-panel').filter({ hasText: 'coder' })
  const announcer = panel.locator('[data-transcript-announcer]')
  await panel.getByRole('button', { name: 'Load full entry' }).click()
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.entry',
      payload: {
        role: 'coder',
        sequence: 4,
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: `archived prefix\n${'[Transcript content truncated at the configured storage limit.]'}`,
          hasArchivedContent: false,
        },
        contentTruncated: true,
        totalContentCharacters: 110,
        archivedPrefixCharacters: 15,
      },
    })
  })
  await expect(announcer).toBeEmpty()

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 5,
        operation: 'replace',
        entryIndex: 7,
        entry: {
          occurredAt: '2026-03-01T12:00:01Z',
          source: 'assistant',
          content: '[Earlier content is available in transcript history.]\nfghijklmno',
          hasArchivedContent: true,
          contentStart: 105,
        },
        announcement: {
          entryIndex: 7,
          operation: 'append-content',
          content: 'klmno',
        },
      },
    })
  })
  await expect(latestAnnouncement(announcer)).toHaveText('klmno')
})

test('bounds live announcement bursts and reports omitted output', async ({ page }) => {
  await loadSnapshot(page)
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 2,
        operation: 'append',
        entryIndex: 1,
        entry: {
          occurredAt: '2026-03-01T12:01:00Z',
          source: 'assistant',
          content: 'bounded output',
        },
        announcement: {
          entryIndex: 1,
          operation: 'append',
          content: 'x'.repeat(20_000),
        },
      },
    })
  })

  const announcer = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('[data-transcript-announcer]')
  await expect(announcer).toContainText('[Earlier live output was omitted.]')
  const announcement = await announcer.textContent()
  expect(announcement?.endsWith('x'.repeat(100))).toBe(true)
  expect(announcement?.length).toBeLessThan(16_500)
})

test('preserves announcement whitespace and represents empty replacements', async ({ page }) => {
  await loadSnapshot(page)
  const panel = page.locator('.role-panel').filter({ hasText: 'coder' })
  const announcer = panel.locator('[data-transcript-announcer]')
  await page.evaluate(() => {
    const receive = (sequence: number, content: string, operation: 'append' | 'append-content' | 'replace') =>
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence,
          operation,
          entryIndex: 1,
          entry: operation === 'append'
            ? { occurredAt: '2026-03-01T12:00:00Z', source: 'assistant', content }
            : operation === 'replace'
              ? { occurredAt: '2026-03-01T12:00:00Z', source: 'assistant', content }
              : undefined,
          content: operation === 'append-content' ? content : undefined,
          announcement: { entryIndex: 1, operation, content },
        },
      })
    receive(2, 'Hello', 'append')
    receive(3, ' ', 'append-content')
    receive(4, 'world', 'append-content')
  })
  await expect(latestAnnouncement(announcer)).toHaveText('Hello world')

  await page.evaluate(() => {
    for (const [sequence, content] of [[5, '\n'], [6, 'next']] as const) {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence,
          operation: 'append-content',
          entryIndex: 1,
          content,
          announcement: {
            entryIndex: 1,
            operation: 'append-content',
            content,
          },
        },
      })
    }
  })
  await expect.poll(() => latestAnnouncement(announcer).textContent())
    .toBe('\nnext')

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 7,
        operation: 'replace',
        entryIndex: 1,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: '',
        },
        announcement: {
          entryIndex: 1,
          operation: 'replace',
          content: '',
        },
      },
    })
  })
  await expect(latestAnnouncement(announcer))
    .toHaveText('[Previous live output was removed.]')

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        recovery: true,
        roles: [{
          role: 'coder',
          sequence: 10,
          announcementAfter: 7,
          announcementThrough: 10,
          entries: [{
            entryIndex: 1,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: 'Hello world',
          }],
          hasMore: false,
          historyTruncated: false,
          announcement: {
            fragments: [
              { sequence: 8, entryIndex: 1, operation: 'append', content: 'Hello' },
              { sequence: 9, entryIndex: 1, operation: 'append-content', content: ' ' },
              { sequence: 10, entryIndex: 1, operation: 'append-content', content: 'world' },
            ],
            truncated: false,
          },
        }],
      },
    })
  })
  await expect(latestAnnouncement(announcer)).toHaveText('Hello world')

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        recovery: true,
        roles: [{
          role: 'coder',
          sequence: 11,
          announcementAfter: 10,
          announcementThrough: 11,
          entries: [{
            entryIndex: 1,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: '',
          }],
          hasMore: false,
          historyTruncated: false,
          announcement: {
            fragments: [{
              sequence: 11,
              entryIndex: 1,
              operation: 'replace',
              content: '',
            }],
            truncated: false,
          },
        }],
      },
    })
  })
  await expect(latestAnnouncement(announcer))
    .toHaveText('[Previous live output was removed.]')
})

test('retains sustained announcement batches in publication order', async ({ page }) => {
  await loadSnapshot(page)
  for (const [sequence, content] of [[2, 'one'], [3, ' two'], [4, ' three']] as const) {
    await page.evaluate(({ sequence, content }) => {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence,
          operation: 'append-content',
          entryIndex: 0,
          content,
          announcement: {
            entryIndex: 0,
            operation: 'append-content',
            content,
          },
        },
      })
    }, { sequence, content })
    await page.waitForTimeout(100)
  }

  const items = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('[data-transcript-announcement]')
  await expect(items).toHaveText(['one', ' two', ' three'])
  expect(await items.allTextContents())
    .not.toContain('[Earlier live output was omitted.]')
})

test('announces a late update already present in synchronized visual state once', async ({ page }) => {
  await loadSnapshot(page)
  const announcer = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('[data-transcript-announcer]')
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'coder',
          sequence: 2,
          announcementAfter: 1,
          announcementThrough: 1,
          entries: [{
            entryIndex: 0,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: 'Reading the workspace. late',
          }],
          hasMore: false,
          historyTruncated: false,
        }],
      },
    })
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 2,
        operation: 'append-content',
        entryIndex: 0,
        content: ' late',
        announcement: {
          entryIndex: 0,
          operation: 'append-content',
          content: ' late',
        },
      },
    })
  })

  await expect(announcer.locator('[data-transcript-announcement]')).toHaveCount(1)
  await expect(latestAnnouncement(announcer)).toHaveText(' late')
  await expect(page.getByText('Reading the workspace. late')).toBeVisible()
})

test('treats the initial announcement baseline as historical', async ({ page }) => {
  await loadSnapshot(page)
  const announcer = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('[data-transcript-announcer]')
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        recovery: false,
        roles: [{
          role: 'coder',
          sequence: 6,
          announcementAfter: 5,
          announcementThrough: 6,
          entries: [{
            entryIndex: 0,
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: 'historical then live',
          }],
          hasMore: false,
          historyTruncated: false,
          announcement: {
            fragments: [{
              sequence: 6,
              entryIndex: 0,
              operation: 'append-content',
              content: ' then live',
            }],
            truncated: false,
          },
        }],
      },
    })
  })

  await expect(latestAnnouncement(announcer)).toHaveText(' then live')
  await expect(announcer).not.toContainText('[Earlier live output was omitted.]')
})

test('keeps replacement boundaries correct after pending announcement truncation', async ({ page }) => {
  await loadSnapshot(page)
  await page.evaluate(() => {
    const receive = (
      sequence: number,
      operation: 'append' | 'replace',
      entryIndex: number,
      content: string,
    ) => window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence,
        operation,
        entryIndex,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content,
        },
        announcement: { entryIndex, operation, content },
      },
    })
    receive(2, 'append', 1, 'a'.repeat(10_000))
    receive(3, 'append', 2, 'obsolete'.repeat(2_000))
    receive(4, 'replace', 2, 'replacement')
  })

  const announcement = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('[data-transcript-announcement]')
    .last()
  await expect(announcement).toContainText('[Earlier live output was omitted.]')
  await expect(announcement).toContainText('replacement')
  await expect(announcement).not.toContainText('obsolete')
})
