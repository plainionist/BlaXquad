import { expect, test } from '@playwright/test'
import {
  firstVisibleTranscriptRow,
  loadSnapshot,
  stateSnapshot,
} from './support/dashboardHarness'

test('keeps transcript DOM rows bounded for large history', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 5000 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: entryIndex % 2 === 0 ? 'assistant' : 'tool_output',
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await expect(transcript.getByText('history-4999')).toBeVisible()
  expect(await transcript.locator('.transcript-line').count()).toBeLessThan(100)
})

test('renders an off-window empty entry after it receives content', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: entryIndex === 250 ? ' ' : `history-${entryIndex}`,
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await expect(transcript.locator('[data-entry-index="250"]')).toHaveCount(0)
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 2,
        operation: 'append-content',
        entryIndex: 250,
        content: 'now visible',
      },
    })
  })
  await transcript.evaluate(element => {
    element.scrollTop = element.scrollHeight / 2
    element.dispatchEvent(new Event('scroll'))
  })

  await expect(transcript.getByText('now visible')).toBeVisible()
  await expect(transcript.locator('[data-entry-index="250"]')).toHaveCount(1)
})

test('uses renderable-row coordinates across long hidden runs', async ({ page }) => {
  await loadSnapshot(page)
  const entries = [
    {
      entryIndex: 0,
      occurredAt: '2026-03-01T12:00:00Z',
      source: 'assistant',
      content: 'first visible assistant',
    },
    {
      entryIndex: 1,
      occurredAt: '2026-03-01T12:00:01Z',
      source: 'tool_output',
      content: ' ',
    },
    {
      entryIndex: 2,
      occurredAt: '2026-03-01T12:00:02Z',
      source: 'assistant',
      content: 'second visible assistant',
    },
    ...Array.from({ length: 100 }, (_, offset) => ({
      entryIndex: offset + 3,
      occurredAt: '2026-03-01T12:01:00Z',
      source: 'assistant',
      content: `before-gap-${offset}`,
    })),
    ...Array.from({ length: 5000 }, (_, offset) => ({
      entryIndex: offset + 103,
      occurredAt: '2026-03-01T12:02:00Z',
      source: offset % 2 === 0 ? 'tool_output' : 'assistant',
      content: '',
    })),
    ...Array.from({ length: 100 }, (_, offset) => ({
      entryIndex: offset + 5103,
      occurredAt: '2026-03-01T12:03:00Z',
      source: 'tool_output',
      content: `after-gap-${offset}`,
    })),
  ]
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

  const transcript = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('.transcript')
  await expect(transcript.getByText('after-gap-99')).toBeVisible()
  expect(Number(await transcript.getAttribute('data-window-rows')))
    .toBeLessThan(100)
  expect(Number(await transcript.getAttribute('data-projected-rows')))
    .toBeLessThan(100)

  const assertBoundaryWindow = async () => {
    await transcript.evaluate(element => {
      element.scrollTop = element.scrollHeight / 2
      element.dispatchEvent(new Event('scroll'))
    })
    await expect(transcript.locator('[data-entry-index="102"]')).toBeVisible()
    const afterGap = transcript.locator('[data-entry-index="5103"]')
    await expect(afterGap).toBeVisible()
    await expect(afterGap).toHaveClass(/is-separated/)
    expect(Number(await transcript.getAttribute('data-window-rows')))
      .toBeLessThan(100)
    expect(Number(await transcript.getAttribute('data-projected-rows')))
      .toBeLessThan(100)
  }
  await assertBoundaryWindow()
  await transcript.evaluate(element => {
    element.scrollTop = 0
    element.dispatchEvent(new Event('scroll'))
  })
  const first = transcript.locator('[data-entry-index="0"]')
  const second = transcript.locator('[data-entry-index="2"]')
  await expect(first).toBeVisible()
  await expect(second).toBeVisible()
  await expect(second).not.toHaveClass(/is-separated/)
  const gap = await second.evaluate((element, firstBottom) =>
    element.getBoundingClientRect().top - firstBottom,
  await first.evaluate(element => element.getBoundingClientRect().bottom))
  expect(gap).toBeCloseTo(0, 0)
  expect(Number(await transcript.getAttribute('data-window-rows')))
    .toBeLessThan(100)
  await assertBoundaryWindow()
  await transcript.evaluate(element => {
    element.scrollTop = element.scrollHeight
    element.dispatchEvent(new Event('scroll'))
  })
  await expect(transcript.getByText('after-gap-99')).toBeVisible()
  await assertBoundaryWindow()
})

test('rebuilds the virtual index when multiple row mutations are batched', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: entryIndex === 200 || entryIndex === 300 ? ' ' : `history-${entryIndex}`,
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
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 2,
        operation: 'append-content',
        entryIndex: 200,
        content: 'first visible row',
      },
    })
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 3,
        operation: 'append-content',
        entryIndex: 300,
        content: 'second visible row',
      },
    })
  }, entries)

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await transcript.evaluate(element => {
    element.scrollTop = 200 * 22
    element.dispatchEvent(new Event('scroll'))
  })
  await expect(transcript.getByText('first visible row')).toBeVisible()
  await transcript.evaluate(element => {
    element.scrollTop = 300 * 22
    element.dispatchEvent(new Event('scroll'))
  })
  await expect(transcript.getByText('second visible row')).toBeVisible()
})

test('rebuilds the virtual index for authoritative synchronization', async ({ page }) => {
  await loadSnapshot(page)
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        recovery: false,
        roles: [{
          role: 'coder',
          sequence: 1,
          entries: [
            { entryIndex: 0, occurredAt: '2026-03-01T12:00:00Z', source: 'assistant', content: 'existing' },
            { entryIndex: 1, occurredAt: '2026-03-01T12:00:01Z', source: 'assistant', content: ' ' },
          ],
          hasMore: false,
          historyTruncated: false,
        }],
      },
    })
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        recovery: true,
        roles: [{
          role: 'coder',
          sequence: 3,
          entries: [
            { entryIndex: 0, occurredAt: '2026-03-01T12:00:00Z', source: 'assistant', content: 'existing' },
            { entryIndex: 1, occurredAt: '2026-03-01T12:00:01Z', source: 'assistant', content: 'now visible' },
            { entryIndex: 2, occurredAt: '2026-03-01T12:00:02Z', source: 'assistant', content: 'newer entry' },
          ],
          hasMore: false,
          historyTruncated: false,
          announcement: {
            fragments: [
              { entryIndex: 1, operation: 'replace', content: 'now visible' },
              { entryIndex: 2, operation: 'append', content: 'newer entry' },
            ],
            truncated: false,
          },
        }],
      },
    })
  })

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await expect(transcript.locator('[data-entry-index="1"]')).toContainText('now visible')
  await expect(transcript.locator('[data-entry-index="2"]')).toContainText('newer entry')
})

test('uses role-local targeted mutations for alternating large histories', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 2000 }, (_, entryIndex) => ({
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
        roles: ['coder', 'reviewer'].map(role => ({
          role,
          sequence: 1,
          entries: history,
          hasMore: false,
          historyTruncated: false,
        })),
      },
    })
  }, entries)

  const coder = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  const reviewer = page.locator('.role-panel').filter({ hasText: 'reviewer' }).locator('.transcript')
  const rebuilds = await Promise.all([
    coder.getAttribute('data-index-rebuilds'),
    reviewer.getAttribute('data-index-rebuilds'),
  ])
  for (let offset = 0; offset < 100; offset++) {
    await page.evaluate(async ({ sequence }) => {
      for (const role of ['coder', 'reviewer']) {
        window.__blaxquadHarness?.receive({
          version: 3,
          type: 'transcript.update',
          payload: {
            role,
            sequence,
            operation: 'append-content',
            entryIndex: 1999,
            content: `-${role}-${sequence}`,
          },
        })
      }
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
    }, { sequence: offset + 2 })
  }

  await expect(coder).toHaveAttribute('data-index-rebuilds', rebuilds[0]!)
  await expect(reviewer).toHaveAttribute('data-index-rebuilds', rebuilds[1]!)
  await expect(coder).toHaveAttribute('data-targeted-mutations', '100')
  await expect(reviewer).toHaveAttribute('data-targeted-mutations', '100')
  expect(await coder.locator('.transcript-line').count()).toBeLessThan(100)
  expect(Number(await coder.getAttribute('data-window-rows'))).toBeLessThan(100)
  expect(Number(await coder.getAttribute('data-projected-rows'))).toBeLessThan(100)
  expect(Number(await coder.getAttribute('data-measurement-count'))).toBeLessThan(100)
  await expect(coder).not.toHaveAttribute('data-measured-entry-ids')

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 102,
        operation: 'replace',
        entryIndex: 1000,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: '',
        },
      },
    })
  })
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 103,
        operation: 'append-content',
        entryIndex: 1000,
        content: 'visible again',
      },
    })
  })
  await expect(coder).toHaveAttribute('data-index-rebuilds', rebuilds[0]!)
  await expect(coder).toHaveAttribute('data-targeted-mutations', '102')
})

test('releases and invalidates row measurements on authoritative reset', async ({ page }) => {
  await loadSnapshot(page)
  const synchronize = async (
    sequence: number,
    start: number,
    content: (entryIndex: number) => string,
  ) => {
    const entries = Array.from({ length: 100 }, (_, offset) => ({
      entryIndex: start + offset,
      occurredAt: '2026-03-01T12:00:00Z',
      source: 'assistant',
      content: content(start + offset),
    }))
    await page.evaluate(({ sequence, entries }) => {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.synchronize',
        payload: {
          roles: [{
            role: 'coder',
            sequence,
            entries,
            hasMore: false,
            historyTruncated: false,
          }],
        },
      })
    }, { sequence, entries })
  }

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await synchronize(1, 0, entryIndex => `first-${entryIndex}`)
  await expect.poll(() => transcript.getAttribute('data-measurement-count'))
    .not.toBe('0')
  await synchronize(2, 1000, entryIndex => `second-${entryIndex}`)
  await expect(transcript).not.toHaveAttribute('data-measured-entry-ids')
  await expect.poll(async () =>
    Number(await transcript.getAttribute('data-measurement-count')))
    .toBeLessThanOrEqual(await transcript.locator('.transcript-line').count())

  const row = transcript.locator('[data-entry-index="1099"]')
  const shortHeight = await row.evaluate(element => element.getBoundingClientRect().height)
  await synchronize(3, 1000, entryIndex =>
    entryIndex === 1099
      ? 'changed wrapped content '.repeat(100)
      : `second-${entryIndex}`)
  await expect.poll(() => row.evaluate(element => element.getBoundingClientRect().height))
    .toBeGreaterThan(shortHeight)
})

test('clears measurements when a mutation batch requires rebuilding', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: entryIndex === 100
      ? 'measured-100\n'.repeat(8)
      : entryIndex === 300
        ? 'measured-300\n'.repeat(12)
        : `measured-${entryIndex}`,
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
  const transcript = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('.transcript')
  const measuredHeights: number[] = []
  for (const entryIndex of [100, 300]) {
    await transcript.evaluate((element, top) => {
      element.scrollTop = top
      element.dispatchEvent(new Event('scroll'))
    }, entryIndex * 22)
    const row = transcript.locator(`[data-entry-index="${entryIndex}"]`)
    await expect(row).toBeVisible()
    measuredHeights.push(
      await row.evaluate(element => element.getBoundingClientRect().height))
  }
  await transcript.evaluate(element => {
    element.scrollTop = element.scrollHeight
    element.dispatchEvent(new Event('scroll'))
  })
  await expect(transcript.locator('[data-entry-index="499"]')).toBeVisible()
  await expect(transcript.locator('[data-entry-index="100"]')).toHaveCount(0)
  await expect(transcript.locator('[data-entry-index="300"]')).toHaveCount(0)
  const accumulatedMeasurements = Number(
    await transcript.getAttribute('data-measurement-count'))
  expect(accumulatedMeasurements).toBeGreaterThan(
    await transcript.locator('.transcript-line').count())

  await page.evaluate(() => {
    for (const [sequence, entryIndex] of [[2, 100], [3, 300]]) {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence,
          operation: 'replace',
          entryIndex,
          entry: {
            occurredAt: '2026-03-01T12:00:00Z',
            source: 'assistant',
            content: `changed-${entryIndex}`,
          },
        },
      })
    }
  })
  await expect.poll(async () =>
    Number(await transcript.getAttribute('data-measurement-count')))
    .toBeLessThanOrEqual(await transcript.locator('.transcript-line').count())

  for (const [targetIndex, measuredHeight] of [[100, measuredHeights[0]], [300, measuredHeights[1]]]) {
    await transcript.evaluate((element, top) => {
      element.scrollTop = top
      element.dispatchEvent(new Event('scroll'))
    }, targetIndex * 22)
    const row = transcript.locator(`[data-entry-index="${targetIndex}"]`)
    await expect(row).toBeVisible()
    expect(await row.evaluate(element => element.getBoundingClientRect().height))
      .toBeLessThan(measuredHeight)
  }

  const replacement = Array.from({ length: 100 }, (_, offset) => ({
    entryIndex: offset + 1000,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `replacement-${offset}`,
  }))
  await page.evaluate((history) => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'coder',
          sequence: 3,
          entries: history,
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
        sequence: 4,
        operation: 'append-content',
        entryIndex: 1099,
        content: '-updated',
      },
    })
  }, replacement)
  await expect.poll(async () =>
    Number(await transcript.getAttribute('data-measurement-count')))
    .toBeLessThanOrEqual(await transcript.locator('.transcript-line').count())
  await transcript.evaluate(element => {
    element.scrollTop = element.scrollHeight
    element.dispatchEvent(new Event('scroll'))
  })
  await expect(transcript.locator('[data-entry-index="1099"]'))
    .toHaveText('replacement-99-updated')
})

test('clamps the renderable window when tail rows become empty', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 100 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `row-${entryIndex}`,
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
  const transcript = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('.transcript')
  await expect(transcript.locator('[data-entry-index="99"]')).toBeVisible()
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 2,
        operation: 'replace',
        entryIndex: 99,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: '',
        },
      },
    })
  })
  await expect(transcript.locator('[data-entry-index="99"]')).toHaveCount(0)
  await expect(transcript.locator('[data-entry-index="98"]')).toBeVisible()
  expect(Number(await transcript.getAttribute('data-window-rows')))
    .toBeLessThanOrEqual(99)

  const singleEntry = [{
    entryIndex: 200,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: 'only row',
  }]
  await page.evaluate((history) => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'coder',
          sequence: 2,
          entries: history,
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
        sequence: 3,
        operation: 'replace',
        entryIndex: 200,
        entry: {
          occurredAt: '2026-03-01T12:00:00Z',
          source: 'assistant',
          content: '',
        },
      },
    })
  }, singleEntry)
  await expect(transcript.locator('[data-entry-index]')).toHaveCount(0)
  await expect(transcript.getByText('No events yet.')).toBeVisible()
  await expect(transcript).toHaveAttribute('data-window-rows', '0')
})

test('preserves the viewport when an older page is prepended', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, offset) => ({
    entryIndex: offset + 100,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `history-${offset + 100}`,
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
          hasMore: true,
          historyTruncated: false,
        }],
      },
    })

  }, entries)

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await transcript.evaluate(element => {
    element.scrollTop = 0
    element.dispatchEvent(new Event('scroll'))
  })
  await page.waitForTimeout(50)
  const anchor = await firstVisibleTranscriptRow(transcript)
  await transcript.getByRole('button', { name: 'Load older' }).click()

  const olderEntries = Array.from({ length: 100 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T11:00:00Z',
    source: 'tool_output',
    content: `older-${entryIndex}`,
  }))
  await page.evaluate((history) => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.page',
      payload: {
        role: 'coder',
        entries: history,
        hasMore: false,
        historyTruncated: false,
      },
    })
  }, olderEntries)
  await page.waitForTimeout(50)

  const after = await transcript.locator(`[data-entry-index="${anchor.entryIndex}"]`).evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top))
  expect(Math.abs(after - anchor.offset)).toBeLessThan(2)
  expect(await transcript.evaluate(element => element.scrollTop)).toBeGreaterThan(0)
  expect(await transcript.locator('.transcript-line').count()).toBeLessThan(100)
})

test('transitions atomically from a large tail to a small tail reset', async ({ page }) => {
  await loadSnapshot(page)
  const history = Array.from({ length: 1000 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `large-${entryIndex}`,
  }))
  await page.evaluate((entries) => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'coder',
          sequence: 10,
          entries,
          hasMore: false,
          historyTruncated: false,
        }],
      },
    })
  }, history)
  const transcript = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('.transcript')
  await expect(transcript.getByText('large-999')).toBeVisible()
  await transcript.evaluate(element => {
    element.dataset.sawInvalidResetWindow = 'false'
    new MutationObserver(() => {
      const ids = [...element.querySelectorAll<HTMLElement>(
        '.transcript-line[data-entry-index]')]
        .map(row => row.dataset.entryIndex)
      if (ids.length === 0 || new Set(ids).size !== ids.length)
        element.dataset.sawInvalidResetWindow = 'true'
    }).observe(element, { childList: true, subtree: true })
  })
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'coder',
          sequence: 11,
          entries: [
            { entryIndex: 997, occurredAt: '2026-03-01T12:00:00Z', source: 'assistant', content: 'small-997' },
            { entryIndex: 998, occurredAt: '2026-03-01T12:00:00Z', source: 'assistant', content: 'small-998' },
            { entryIndex: 999, occurredAt: '2026-03-01T12:00:00Z', source: 'assistant', content: 'small-999' },
          ],
          hasMore: false,
          historyTruncated: false,
        }],
      },
    })
  })

  await expect(transcript.getByText('small-999')).toBeVisible()
  await expect(transcript.locator('[data-entry-index]')).toHaveCount(3)
  await expect(transcript).toHaveAttribute('data-saw-invalid-reset-window', 'false')
  await expect(transcript).toHaveAttribute('data-window-rows', '3')
})

test('preserves a short transcript viewport when an older page is prepended', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 4 }, (_, offset) => ({
    entryIndex: offset + 4,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `recent-${offset + 4}`,
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
          hasMore: true,
          historyTruncated: false,
        }],
      },
    })
  }, entries)

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  const anchor = await transcript.locator('[data-entry-index="4"]').evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top))
  await transcript.getByRole('button', { name: 'Load older' }).click()

  const olderEntries = Array.from({ length: 4 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T11:00:00Z',
    source: 'tool_output',
    content: `older-${entryIndex}`,
  }))
  await page.evaluate((history) => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.page',
      payload: {
        role: 'coder',
        entries: history,
        hasMore: false,
        historyTruncated: false,
      },
    })
  }, olderEntries)

  await expect.poll(async () => transcript.locator('[data-entry-index="4"]').evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top))).toBeCloseTo(anchor, 0)
  expect(await transcript.evaluate(element => element.scrollTop)).toBeGreaterThan(0)
})

test('preserves the viewport when an older page fills a sparse gap', async ({ page }) => {
  await loadSnapshot(page)
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'coder',
          sequence: 1,
          entries: [
            { entryIndex: 0, occurredAt: '2026-03-01T11:00:00Z', source: 'assistant', content: 'retained-oldest' },
            { entryIndex: 100, occurredAt: '2026-03-01T12:00:00Z', source: 'assistant', content: 'retained-newest' },
          ],
          hasMore: true,
          historyTruncated: false,
        }],
      },
    })
  })

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  const anchor = await transcript.locator('[data-entry-index="0"]').evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top))
  await transcript.getByRole('button', { name: 'Load older' }).click()
  const olderEntries = Array.from({ length: 99 }, (_, offset) => ({
    entryIndex: offset + 1,
    occurredAt: '2026-03-01T11:00:00Z',
    source: 'tool_output',
    content: `gap-${offset + 1}`,
  }))
  await page.evaluate((history) => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.page',
      payload: {
        role: 'coder',
        entries: history,
        hasMore: false,
        historyTruncated: false,
      },
    })
  }, olderEntries)

  await expect.poll(async () => transcript.locator('[data-entry-index="0"]').evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top))).toBeCloseTo(anchor, 0)
  await expect(transcript.getByText('retained-newest')).toHaveCount(0)
})

test('updates virtual row geometry when wrapped content changes width', async ({ page }) => {
  await page.setViewportSize({ width: 2560, height: 900 })
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `history-${entryIndex} ${'wrapped transcript content '.repeat(10)}`,
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await transcript.evaluate(element => {
    element.scrollTop = element.scrollHeight / 2
    element.dispatchEvent(new Event('scroll'))
  })
  await page.waitForTimeout(50)
  const wideScrollHeight = await transcript.evaluate(element => element.scrollHeight)
  const anchor = await firstVisibleTranscriptRow(transcript)

  await page.setViewportSize({ width: 1100, height: 900 })

  await expect.poll(() => transcript.evaluate(element => element.scrollHeight))
    .toBeGreaterThan(wideScrollHeight)
  await expect.poll(async () => transcript.locator(`[data-entry-index="${anchor.entryIndex}"]`).evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top))).toBeCloseTo(anchor.offset, 0)

  await transcript.evaluate(element => {
    element.scrollTop = element.scrollHeight
    element.dispatchEvent(new Event('scroll'))
  })
  await expect(transcript.getByText(/^history-499 /)).toBeVisible()
  expect(await transcript.locator('.transcript-line').count()).toBeLessThan(100)
})

test('preserves the reading anchor when widening wrapped content reduces the scroll range', async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 900 })
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `history-${entryIndex} ${'wrapped transcript content '.repeat(20)}`,
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await expect(transcript.getByText(/^history-499 /)).toBeVisible()
  const narrowScrollHeight = await transcript.evaluate(element => element.scrollHeight)
  await transcript.hover()
  await page.mouse.wheel(0, -250)
  await expect(transcript).toHaveAttribute('data-following', 'false')
  await page.waitForTimeout(250)
  const anchor = await firstVisibleTranscriptRow(transcript)

  await page.setViewportSize({ width: 2560, height: 900 })

  await expect.poll(() => transcript.evaluate(element => element.scrollHeight))
    .toBeLessThan(narrowScrollHeight)
  await expect(transcript).toHaveAttribute('data-following', 'false')
  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)

  await page.evaluate(() => {
    for (let offset = 0; offset < 12; offset++) {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence: offset + 2,
          operation: 'append',
          entryIndex: offset + 500,
          entry: {
            occurredAt: '2026-03-01T12:01:00Z',
            source: 'assistant',
            content: `new output ${offset + 1}`,
          },
        },
      })
    }
  })

  await expect(transcript).toHaveAttribute('data-following', 'false')
  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeGreaterThan(44)
})

test('keeps lower-grid panels at the transcript tail', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `writer-history-${entryIndex}`,
  }))
  await page.evaluate((history) => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'writer',
          sequence: 1,
          entries: history,
          hasMore: false,
          historyTruncated: false,
        }],
      },
    })
  }, entries)

  const transcript = page.locator('.role-panel').filter({ hasText: 'writer' }).locator('.transcript')
  await expect(transcript.getByText('writer-history-499')).toBeVisible()
  expect(await transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight)).toBeLessThan(44)
})

test('continues following the tail when the transcript viewport becomes shorter', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `resized-history-${entryIndex}`,
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await expect(transcript.getByText('resized-history-499')).toBeVisible()
  await page.setViewportSize({ width: 1440, height: 700 })

  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight)).toBeLessThan(44)
  await expect(transcript.getByText('resized-history-499')).toBeVisible()
})

test('corrects thinking row geometry while following and reading', async ({ page }) => {
  await loadSnapshot(page)
  const updateWorkingState = async (isWorking: boolean) => {
    await page.evaluate(({ state, isWorking }) => {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'state.snapshot',
        payload: {
          ...state,
          roles: state.roles.map(role => role.role === 'coder'
            ? {
                ...role,
                status: isWorking ? 'running' : 'idle',
                isWorking,
                activeTool: undefined,
              }
            : role),
        },
      })
    }, { state: stateSnapshot, isWorking })
  }
  await updateWorkingState(false)
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await expect(transcript.getByText('history-499')).toBeVisible()
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeLessThan(2)

  await updateWorkingState(true)

  await expect(transcript.getByText('Thinking ...')).toBeVisible()
  await expect(transcript).toHaveAttribute('data-following', 'true')
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeLessThan(2)

  await transcript.hover()
  await page.mouse.wheel(0, -250)
  await expect(transcript).toHaveAttribute('data-following', 'false')
  const anchor = await firstVisibleTranscriptRow(transcript)

  await updateWorkingState(false)

  await expect(transcript.getByText('Thinking ...')).toHaveCount(0)
  await expect(transcript).toHaveAttribute('data-following', 'false')
  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
})
