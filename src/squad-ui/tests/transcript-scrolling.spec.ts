import { expect, test } from '@playwright/test'
import {
  firstVisibleTranscriptRow,
  loadPagedTranscriptAtReadingPosition,
  loadSnapshot,
  stateSnapshot,
} from './support/dashboardHarness'

test('follows streaming output while positioned at the transcript tail', async ({ page }) => {
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await expect(transcript.getByText('history-499')).toBeVisible()
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
          content: 'latest streamed output',
        },
      },
    })
  })

  await expect(transcript.getByText('latest streamed output')).toBeVisible()
  expect(await transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight)).toBeLessThan(44)
})

test('stops following after an unattributed browser scroll', async ({ page }) => {
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await expect(transcript.getByText('history-499')).toBeVisible()
  await expect(transcript).toHaveAttribute('data-pending-internal-scrolls', '0')
  await transcript.evaluate(element => {
    element.scrollTop = Math.max(0, element.scrollTop - 600)
  })

  await expect(transcript).toHaveAttribute('data-following', 'false')
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeGreaterThan(100)
  const anchor = await firstVisibleTranscriptRow(transcript)

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
          content: 'output after unattributed movement',
        },
      },
    })
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
    .toBeGreaterThan(100)
})

test('resumes following after the user scrolls back to the transcript tail', async ({ page }) => {
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await expect(transcript.getByText('history-499')).toBeVisible()
  await transcript.hover()
  await page.mouse.wheel(0, -600)
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeGreaterThan(100)
  const readingScrollTop = await transcript.evaluate(element => element.scrollTop)

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
          content: 'paused while reading',
        },
      },
    })
  })
  await expect.poll(() => transcript.evaluate(element => element.scrollTop))
    .toBeCloseTo(readingScrollTop, 0)

  await page.mouse.wheel(0, 10000)
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight)).toBeLessThan(44)
  await page.waitForTimeout(50)
  await expect(transcript).toHaveAttribute('data-following', 'true')
  await page.evaluate(() => {
    for (let offset = 0; offset < 4; offset++) {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence: 3 + offset,
          operation: 'append',
          entryIndex: 501 + offset,
          entry: {
            occurredAt: '2026-03-01T12:01:01Z',
            source: 'assistant',
            content: `following resumed ${offset + 1}`,
          },
        },
      })
    }
  })

  await expect(transcript.getByText('following resumed 4')).toBeVisible()
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight)).toBeLessThan(2)
  await page.evaluate(() => {
    for (let offset = 0; offset < 4; offset++) {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence: 7 + offset,
          operation: 'append',
          entryIndex: 505 + offset,
          entry: {
            occurredAt: '2026-03-01T12:01:02Z',
            source: 'assistant',
            content: `continued following ${offset + 1}`,
          },
        },
      })
    }
  })
  await expect(transcript.getByText('continued following 4')).toBeVisible()
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight)).toBeLessThan(2)
})

test('resumes against the updated tail when output arrives during scroll classification', async ({ page }) => {
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

  const transcript = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('.transcript')
  await expect(transcript.getByText('history-499')).toBeVisible()
  await transcript.hover()
  await page.mouse.wheel(0, -600)
  await expect(transcript).toHaveAttribute('data-following', 'false')
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeGreaterThan(100)

  await transcript.evaluate(element => {
    element.addEventListener('scroll', event => {
      if (!event.isTrusted || element.dataset.appendedDuringClassification)
        return
      element.dataset.appendedDuringClassification = 'true'
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
            content: 'appended during deferred classification',
          },
        },
      })
    })
  })

  await page.mouse.wheel(0, 10000)
  await expect(transcript.getByText(
    'appended during deferred classification')).toBeVisible()
  await expect(transcript).toHaveAttribute('data-following', 'true')
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeLessThan(2)
  await transcript.evaluate(() => new Promise<void>(resolve => {
    let remainingFrames = 5
    const waitForFrame = () => {
      if (--remainingFrames === 0)
        resolve()
      else
        requestAnimationFrame(waitForFrame)
    }
    requestAnimationFrame(waitForFrame)
  }))
  expect(await transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeLessThan(2)
})

test('consumes each expected internal scroll position once', async ({ page }) => {
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

  const transcript = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('.transcript')
  await expect(transcript.getByText('history-499')).toBeVisible()
  await expect(transcript).toHaveAttribute(
    'data-pending-internal-scrolls', '0')

  const formerTailPositions = await page.evaluate(async () => {
    const positions: number[] = []
    const transcriptElement = document.querySelector<HTMLElement>(
      '.role-panel .transcript')
    for (let offset = 0; offset < 3; offset++) {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence: 2 + offset,
          operation: 'append',
          entryIndex: 500 + offset,
          entry: {
            occurredAt: '2026-03-01T12:01:00Z',
            source: 'assistant',
            content: Array.from(
              { length: 6 },
              (_, line) => `internal-${offset}-${line}`).join('\n'),
          },
        },
      })
      await new Promise<void>(resolve =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve())))
      positions.push(transcriptElement?.scrollTop ?? 0)
    }
    return positions
  })

  await expect(transcript).toHaveAttribute(
    'data-pending-internal-scrolls', '0', { timeout: 400 })
  const currentPosition = await transcript.evaluate(element => element.scrollTop)
  const formerPosition = formerTailPositions[0]
  expect(currentPosition - formerPosition).toBeGreaterThan(44)

  await transcript.hover()
  await page.mouse.wheel(0, formerPosition - currentPosition)
  await expect.poll(() => transcript.evaluate(element => element.scrollTop))
    .toBeCloseTo(formerPosition, 0)
  await expect(transcript).toHaveAttribute('data-following', 'false')
})

test('keeps a newer anchor correction authoritative over deferred classification', async ({ page }) => {
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

  const transcript = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('.transcript')
  await expect(transcript.getByText('history-599')).toBeVisible()
  await transcript.hover()
  await page.mouse.wheel(0, -600)
  await expect(transcript).toHaveAttribute('data-following', 'false')

  const olderEntries = Array.from({ length: 100 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T11:00:00Z',
    source: 'tool_output',
    content: `older-${entryIndex}`,
  }))
  await transcript.evaluate((element, older) => {
    element.addEventListener('scroll', event => {
      if (!event.isTrusted || element.dataset.updatedDuringClassification)
        return
      element.dataset.updatedDuringClassification = 'true'
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.page',
        payload: {
          role: 'coder',
          entries: older,
          hasMore: false,
          historyTruncated: false,
        },
      })
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence: 2,
          operation: 'append',
          entryIndex: 600,
          entry: {
            occurredAt: '2026-03-01T12:01:00Z',
            source: 'assistant',
            content: Array.from(
              { length: 6 },
              (_, line) => `new-tail-${line}`).join('\n'),
          },
        },
      })
    })
  }, olderEntries)

  await page.mouse.wheel(0, 10000)
  await expect(transcript.getByText('new-tail-5')).toBeVisible()
  await expect(transcript).toHaveAttribute('data-following', 'true')
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeLessThan(2)
})

test('restores the reading anchor after confirming a concurrent scroll away', async ({ page }) => {
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

  const transcript = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('.transcript')
  await expect(transcript.getByText('history-599')).toBeVisible()
  const olderEntries = Array.from({ length: 100 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T11:00:00Z',
    source: 'tool_output',
    content: `older-${entryIndex}`,
  }))
  await transcript.evaluate((element, older) => {
    element.addEventListener('scroll', event => {
      if (!event.isTrusted || element.dataset.pagedDuringClassification)
        return
      element.dataset.pagedDuringClassification = 'true'
      const viewportTop = element.getBoundingClientRect().top
      const anchor = [...element.querySelectorAll<HTMLElement>(
        '.transcript-line[data-entry-index]')]
        .find(row => row.getBoundingClientRect().bottom > viewportTop)
      if (!anchor)
        throw new Error('Expected a visible reading anchor.')
      element.dataset.concurrentAnchorIndex = anchor.dataset.entryIndex
      element.dataset.concurrentAnchorOffset = String(
        anchor.getBoundingClientRect().top - viewportTop)
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.page',
        payload: {
          role: 'coder',
          entries: older,
          hasMore: false,
          historyTruncated: false,
        },
      })
    })
  }, olderEntries)

  await transcript.hover()
  await page.mouse.wheel(0, -600)
  await expect(transcript).toHaveAttribute('data-following', 'false')
  const anchor = await transcript.evaluate(element => ({
    entryIndex: Number(element.dataset.concurrentAnchorIndex),
    offset: Number(element.dataset.concurrentAnchorOffset),
  }))
  expect(anchor.entryIndex).not.toBeNaN()
  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
  await transcript.evaluate(() => new Promise<void>(resolve => {
    let remainingFrames = 5
    const waitForFrame = () => {
      if (--remainingFrames === 0)
        resolve()
      else
        requestAnimationFrame(waitForFrame)
    }
    requestAnimationFrame(waitForFrame)
  }))
  expect(await transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
})

test('keeps following when a tail row grows before a programmatic scroll event is handled', async ({ page }) => {
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await expect(transcript.getByText('history-499')).toBeVisible()
  await page.waitForTimeout(50)
  await transcript.hover()
  await page.mouse.wheel(0, 120)
  const initialScrollHeight = await transcript.evaluate(element => element.scrollHeight)
  await transcript.evaluate(element => {
    const growTail = () => {
      const tail = element.querySelector<HTMLElement>('[data-entry-index="500"]')
      if (!tail)
        return
      tail.style.minHeight = '180px'
      element.dataset.tailGrowth = 'grown'
      element.removeEventListener('scroll', growTail, true)
    }
    element.addEventListener('scroll', growTail, true)
  })

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
          content: 'first tail update',
        },
      },
    })
  })
  await expect(transcript.getByText('first tail update')).toBeVisible()
  await expect(transcript).toHaveAttribute('data-tail-growth', 'grown')
  await expect(transcript).toHaveAttribute('data-following', 'true')
  await expect.poll(() => transcript.locator('[data-entry-index="500"]')
    .evaluate(element => element.getBoundingClientRect().height)).toBeGreaterThanOrEqual(180)
  await expect.poll(() => transcript.evaluate(element => element.scrollHeight))
    .toBeGreaterThan(initialScrollHeight)
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight)).toBeLessThan(2)

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 3,
        operation: 'append',
        entryIndex: 501,
        entry: {
          occurredAt: '2026-03-01T12:01:01Z',
          source: 'assistant',
          content: 'second tail update',
        },
      },
    })
  })

  await expect(transcript.getByText('second tail update')).toBeVisible()
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight)).toBeLessThan(44)
})

test('keeps a queued internal tail scroll causal when input starts before delivery', async ({ page }) => {
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await expect(transcript.getByText('history-499')).toBeVisible()
  await page.waitForTimeout(50)
  await transcript.evaluate((element) => {
    element.addEventListener('scroll', () => {
      element.dispatchEvent(new PointerEvent('pointerdown', {
        bubbles: true,
        pointerId: 73,
      }))
      element.dataset.inputBeforeInternalScroll = 'received'
    }, { capture: true, once: true })
  })
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
          content: 'queued internal tail scroll',
        },
      },
    })
  })

  await expect(transcript).toHaveAttribute('data-input-before-internal-scroll', 'received')
  await expect(transcript).toHaveAttribute('data-following', 'true')
  await transcript.evaluate((element) => {
    element.dispatchEvent(new PointerEvent('pointerup', {
      bubbles: true,
      pointerId: 73,
    }))
  })
})

test('preserves the reading anchor when a tall tail replacement clamps the scroll range', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1400 })
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: entryIndex === 499
      ? Array.from({ length: 20 }, (_, line) => `tall tail line ${line + 1}`).join('\n')
      : `history-${entryIndex}`,
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
  await expect.poll(() => transcript.locator('[data-entry-index="499"]')
    .evaluate(element => element.getBoundingClientRect().height)).toBeGreaterThan(300)
  await transcript.hover()
  await page.mouse.wheel(0, -250)
  await expect(transcript).toHaveAttribute('data-following', 'false')
  const anchor = await firstVisibleTranscriptRow(transcript)
  expect(anchor.entryIndex).toBeLessThan(499)
  await transcript.evaluate((element) => {
    element.dispatchEvent(new PointerEvent('pointerdown', {
      bubbles: true,
      pointerId: 74,
      clientX: 40,
      clientY: 40,
    }))
    element.dispatchEvent(new PointerEvent('pointermove', {
      bubbles: true,
      pointerId: 74,
      clientX: 80,
      clientY: 40,
    }))
  })

  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 2,
        operation: 'replace',
        entryIndex: 499,
        entry: {
          occurredAt: '2026-03-01T12:01:00Z',
          source: 'assistant',
          content: 'short tail',
        },
      },
    })
  })
  await expect(transcript).toHaveAttribute('data-following', 'false')
  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
  await page.evaluate(async () => {
    for (let offset = 0; offset < 12; offset++) {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence: 3 + offset,
          operation: 'append',
          entryIndex: 500 + offset,
          entry: {
            occurredAt: '2026-03-01T12:01:01Z',
            source: 'assistant',
            content: `new output ${offset + 1}`,
          },
        },
      })
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
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
  await expect(transcript).toHaveAttribute('data-anchor-compensation', '0')
  await transcript.evaluate((element) => {
    element.dispatchEvent(new PointerEvent('pointerup', {
      bubbles: true,
      pointerId: 74,
    }))
  })
  await transcript.hover()
  await page.mouse.wheel(0, 10000)
  await expect(transcript).toHaveAttribute('data-following', 'true')
  const lastRow = transcript.locator('[data-entry-index="511"]')
  await expect(lastRow).toBeVisible()
  await expect.poll(async () => {
    const contentBottom = await transcript.evaluate((element) => {
      const style = getComputedStyle(element)
      return element.getBoundingClientRect().bottom
        - Number.parseFloat(style.paddingBottom)
    })
    const rowBottom = await lastRow.evaluate(
      element => element.getBoundingClientRect().bottom)
    return Math.abs(contentBottom - rowBottom)
  }).toBeLessThan(2)
})

test('preserves the reading anchor when a measured tail row shrinks', async ({ page }) => {
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  const tail = transcript.locator('[data-entry-index="499"]')
  await expect(tail).toBeVisible()
  await tail.evaluate(element => {
    if (!(element instanceof HTMLElement))
      throw new Error('Expected the rendered tail row.')
    element.style.minHeight = '400px'
  })
  await expect.poll(() => tail.evaluate(element =>
    element.getBoundingClientRect().height)).toBeGreaterThanOrEqual(400)
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeLessThan(2)

  await transcript.hover()
  await page.mouse.wheel(0, -250)
  await expect(transcript).toHaveAttribute('data-following', 'false')
  const anchor = await firstVisibleTranscriptRow(transcript)

  await tail.evaluate(element => {
    if (!(element instanceof HTMLElement))
      throw new Error('Expected the rendered tail row.')
    element.style.minHeight = ''
  })

  await expect.poll(() => tail.evaluate(element =>
    element.getBoundingClientRect().height)).toBeLessThan(100)
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

test('keeps a superseded layout clamp internal during anchor correction', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: entryIndex === 499
      ? Array.from(
        { length: 20 },
        (_, line) => `deferred clamp line ${line + 1}`).join('\n')
      : `history-${entryIndex}`,
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
  const tail = transcript.locator('[data-entry-index="499"]')
  await expect.poll(() => tail.evaluate(element =>
    element.getBoundingClientRect().height)).toBeGreaterThan(300)
  await transcript.hover()
  await page.mouse.wheel(0, -250)
  await transcript.evaluate(() => new Promise<void>(resolve => {
    let remainingFrames = 3
    const waitForFrame = () => {
      if (--remainingFrames === 0)
        resolve()
      else
        requestAnimationFrame(waitForFrame)
    }
    requestAnimationFrame(waitForFrame)
  }))
  await expect(transcript).toHaveAttribute('data-following', 'false')
  const anchor = await firstVisibleTranscriptRow(transcript)

  await transcript.evaluate(element => {
    element.dataset.trustedCorrectionPositions = '[]'
    element.addEventListener('scroll', event => {
      if (!event.isTrusted)
        return
      const positions = JSON.parse(
        element.dataset.trustedCorrectionPositions ?? '[]') as number[]
      positions.push(element.scrollTop)
      element.dataset.trustedCorrectionPositions = JSON.stringify(positions)
    })
  })
  await page.evaluate(() => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 2,
        operation: 'replace',
        entryIndex: 499,
        entry: {
          occurredAt: '2026-03-01T12:01:00Z',
          source: 'assistant',
          content: 'short deferred clamp tail',
        },
      },
    })
  })

  await expect(tail).toContainText('short deferred clamp tail')
  await expect.poll(() => transcript.evaluate(element => {
    const positions = JSON.parse(
      element.dataset.trustedCorrectionPositions ?? '[]') as number[]
    return new Set(positions.map(position => Math.round(position))).size
  })).toBeGreaterThanOrEqual(2)
  await expect(transcript).toHaveAttribute('data-following', 'false')
  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
})

test('preserves reading state across overlapping progressive clamps', async ({ page }) => {
  await page.addInitScript(() => {
    type ControlledWindow = Window & {
      __delayTranscriptResize?: boolean
      __flushTranscriptResize?: () => void
      __holdTranscriptFrames?: boolean
      __releaseTranscriptFrames?: () => void
    }
    const controlledWindow = window as ControlledWindow
    const NativeResizeObserver = window.ResizeObserver
    const nativeRequestAnimationFrame =
      window.requestAnimationFrame.bind(window)
    const nativeCancelAnimationFrame =
      window.cancelAnimationFrame.bind(window)
    const pendingCallbacks: (() => void)[] = []
    const pendingFrames = new Map<number, FrameRequestCallback>()
    let frameId = 1_000_000
    window.requestAnimationFrame = callback => {
      if (!controlledWindow.__holdTranscriptFrames)
        return nativeRequestAnimationFrame(callback)
      const id = frameId++
      pendingFrames.set(id, callback)
      return id
    }
    window.cancelAnimationFrame = id => {
      if (!pendingFrames.delete(id))
        nativeCancelAnimationFrame(id)
    }
    controlledWindow.__releaseTranscriptFrames = () => {
      controlledWindow.__holdTranscriptFrames = false
      for (const callback of pendingFrames.values())
        nativeRequestAnimationFrame(callback)
      pendingFrames.clear()
    }
    controlledWindow.__flushTranscriptResize = () => {
      controlledWindow.__delayTranscriptResize = false
      for (const publish of pendingCallbacks.splice(0))
        publish()
    }
    class ControlledResizeObserver {
      private readonly myObserver: ResizeObserver

      constructor(callback: ResizeObserverCallback) {
        this.myObserver = new NativeResizeObserver((entries) => {
          const publish = () =>
            callback(entries, this as unknown as ResizeObserver)
          if (controlledWindow.__delayTranscriptResize)
            pendingCallbacks.push(publish)
          else
            publish()
        })
      }

      observe(target: Element, options?: ResizeObserverOptions) {
        this.myObserver.observe(target, options)
      }

      unobserve(target: Element) {
        this.myObserver.unobserve(target)
      }

      disconnect() {
        this.myObserver.disconnect()
      }
    }
    window.ResizeObserver =
      ControlledResizeObserver as unknown as typeof ResizeObserver
  })
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

  const transcript = page.locator('.role-panel')
    .filter({ hasText: 'coder' })
    .locator('.transcript')
  const tail = transcript.locator('[data-entry-index="499"]')
  await expect(tail).toBeVisible()
  await tail.evaluate(element => {
    if (!(element instanceof HTMLElement))
      throw new Error('Expected the rendered tail row.')
    element.style.minHeight = '600px'
  })
  await expect.poll(() => tail.evaluate(element =>
    element.getBoundingClientRect().height)).toBeGreaterThanOrEqual(600)
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeLessThan(2)

  await transcript.hover()
  await page.mouse.wheel(0, -250)
  await transcript.evaluate(() => new Promise<void>(resolve => {
    let remainingFrames = 3
    const waitForFrame = () => {
      if (--remainingFrames === 0)
        resolve()
      else
        requestAnimationFrame(waitForFrame)
    }
    requestAnimationFrame(waitForFrame)
  }))
  await expect(transcript).toHaveAttribute('data-following', 'false')
  const anchor = await firstVisibleTranscriptRow(transcript)

  await page.evaluate(() => {
    const controlledWindow = window as Window & {
      __delayTranscriptResize?: boolean
      __holdTranscriptFrames?: boolean
    }
    controlledWindow.__delayTranscriptResize = true
    controlledWindow.__holdTranscriptFrames = true
  })
  await transcript.evaluate(element => {
    element.dataset.progressiveClampPositions = '[]'
    element.addEventListener('scroll', event => {
      if (!event.isTrusted)
        return
      const positions = JSON.parse(
        element.dataset.progressiveClampPositions ?? '[]') as number[]
      positions.push(element.scrollTop)
      element.dataset.progressiveClampPositions = JSON.stringify(positions)
      const distinctPositions = new Set(
        positions.map(position => Math.round(position)))
      if (distinctPositions.size === 1) {
        const tailRow = element.querySelector<HTMLElement>(
          '[data-entry-index="499"]')
        if (!tailRow)
          throw new Error('Expected the rendered tail row.')
        tailRow.style.minHeight = ''
      }
      if (distinctPositions.size < 2
        || element.dataset.progressiveGeometryFlushed)
        return
      element.dataset.progressiveGeometryFlushed =
        String(distinctPositions.size)
      queueMicrotask(() => {
        const controlledWindow = window as Window & {
          __flushTranscriptResize?: () => void
          __releaseTranscriptFrames?: () => void
        }
        controlledWindow.__flushTranscriptResize?.()
        controlledWindow.__releaseTranscriptFrames?.()
      })
    })
  })
  await tail.evaluate(element => {
    if (!(element instanceof HTMLElement))
      throw new Error('Expected the rendered tail row.')
    element.style.minHeight = '250px'
  })

  await expect.poll(() => tail.evaluate(element =>
    element.getBoundingClientRect().height)).toBeLessThan(100)
  await expect(transcript).toHaveAttribute(
    'data-progressive-geometry-flushed', '2')
  await expect(transcript).toHaveAttribute('data-following', 'false')
  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
})

for (const scenario of [
  {
    name: 'scroll-away',
    beginReading: false,
    userDelta: -250,
    expectedFollowing: 'false',
  },
  {
    name: 'tail-return',
    beginReading: true,
    userDelta: 10000,
    expectedFollowing: 'true',
  },
] as const) {
  test(`preserves external ${scenario.name} intent across an internal overlap`, async ({ page }) => {
    await page.addInitScript(() => {
      const NativeResizeObserver = window.ResizeObserver
      class DeferredResizeObserver {
        private readonly myObserver: ResizeObserver

        constructor(callback: ResizeObserverCallback) {
          this.myObserver = new NativeResizeObserver(entries => {
            requestAnimationFrame(() =>
              callback(entries, this as unknown as ResizeObserver))
          })
        }

        observe(target: Element, options?: ResizeObserverOptions) {
          this.myObserver.observe(target, options)
        }

        unobserve(target: Element) {
          this.myObserver.unobserve(target)
        }

        disconnect() {
          this.myObserver.disconnect()
        }
      }
      window.ResizeObserver =
        DeferredResizeObserver as unknown as typeof ResizeObserver
    })
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

    const transcript = page.locator('.role-panel')
      .filter({ hasText: 'coder' })
      .locator('.transcript')
    const tail = transcript.locator('[data-entry-index="499"]')
    await expect(tail).toBeVisible()
    await tail.evaluate(element => {
      if (!(element instanceof HTMLElement))
        throw new Error('Expected the rendered tail row.')
      element.style.minHeight = '600px'
    })
    await expect.poll(() => tail.evaluate(element =>
      element.getBoundingClientRect().height)).toBeGreaterThanOrEqual(600)
    await expect.poll(() => transcript.evaluate(element =>
      element.scrollHeight - element.scrollTop - element.clientHeight))
      .toBeLessThan(2)

    await transcript.hover()
    if (scenario.beginReading) {
      await page.mouse.wheel(0, -700)
      await transcript.evaluate(() => new Promise<void>(resolve => {
        let remainingFrames = 3
        const waitForFrame = () => {
          if (--remainingFrames === 0)
            resolve()
          else
            requestAnimationFrame(waitForFrame)
        }
        requestAnimationFrame(waitForFrame)
      }))
      await expect(transcript).toHaveAttribute('data-following', 'false')
    }

    await transcript.evaluate(element => {
      element.dataset.mixedOriginPositions = '[]'
      element.addEventListener('scroll', event => {
        if (!event.isTrusted)
          return
        const positions = JSON.parse(
          element.dataset.mixedOriginPositions ?? '[]') as number[]
        positions.push(element.scrollTop)
        element.dataset.mixedOriginPositions = JSON.stringify(positions)
        if (element.dataset.mixedOriginCorrectionStarted)
          return
        element.dataset.mixedOriginCorrectionStarted = 'true'
        const tailRow = element.querySelector<HTMLElement>(
          '[data-entry-index="499"]')
        if (!tailRow)
          throw new Error('Expected the rendered tail row.')
        const viewportTop = element.getBoundingClientRect().top
        const anchor = [...element.querySelectorAll<HTMLElement>(
          '.transcript-line[data-entry-index]')]
          .find(row => row.getBoundingClientRect().bottom > viewportTop)
        if (anchor) {
          element.dataset.mixedOriginAnchorIndex = anchor.dataset.entryIndex
          element.dataset.mixedOriginAnchorOffset = String(
            anchor.getBoundingClientRect().top - viewportTop)
        }
        tailRow.style.minHeight = ''
      })
    })

    await page.mouse.wheel(0, scenario.userDelta)
    await expect.poll(() => tail.evaluate(element =>
      element.getBoundingClientRect().height)).toBeLessThan(100)
    await expect.poll(() => transcript.evaluate(element => {
      const positions = JSON.parse(
        element.dataset.mixedOriginPositions ?? '[]') as number[]
      return new Set(positions.map(position => Math.round(position))).size
    })).toBeGreaterThanOrEqual(2)
    await expect(transcript).toHaveAttribute(
      'data-following', scenario.expectedFollowing)

    if (scenario.expectedFollowing === 'true') {
      await expect.poll(() => transcript.evaluate(element =>
        element.scrollHeight - element.scrollTop - element.clientHeight))
        .toBeLessThan(2)
    }
    else {
      const anchor = await transcript.evaluate(element => ({
        entryIndex: Number(element.dataset.mixedOriginAnchorIndex),
        offset: Number(element.dataset.mixedOriginAnchorOffset),
      }))
      expect(anchor.entryIndex).not.toBeNaN()
      await expect.poll(async () => transcript
        .locator(`[data-entry-index="${anchor.entryIndex}"]`)
        .evaluate((element, viewportTop) =>
          element.getBoundingClientRect().top - viewportTop,
        await transcript.evaluate(element =>
          element.getBoundingClientRect().top)))
        .toBeCloseTo(anchor.offset, 0)
    }
  })
}

test('preserves the reading anchor when the transcript viewport grows during scroll intent', async ({ page }) => {
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
  await expect(panel.getByText('Permission required')).toBeVisible()
  await transcript.hover()
  await page.mouse.wheel(0, -80)
  await expect(transcript).toHaveAttribute('data-following', 'false')
  const anchor = await firstVisibleTranscriptRow(transcript)
  await page.evaluate((state) => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'state.snapshot',
      payload: state,
    })
  }, { ...stateSnapshot, permissions: [] })

  await expect(panel.getByText('Permission required')).toHaveCount(0)
  await expect(transcript).toHaveAttribute('data-following', 'false')
  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
  await page.evaluate(() => {
    for (let offset = 0; offset < 8; offset++) {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence: 2 + offset,
          operation: 'append',
          entryIndex: 500 + offset,
          entry: {
            occurredAt: '2026-03-01T12:01:00Z',
            source: 'assistant',
            content: `output after viewport growth ${offset + 1}`,
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

test('stops following after a nested transcript button scrolls with the keyboard', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `history-${entryIndex}`,
    hasArchivedContent: entryIndex === 499,
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
  const loadEntry = transcript.getByRole('button', { name: 'Load full entry' })
  await expect(loadEntry).toBeVisible()
  const tailScrollTop = await transcript.evaluate(element => element.scrollTop)
  await loadEntry.press('PageUp')
  await expect.poll(() => transcript.evaluate(element => element.scrollTop))
    .toBeLessThan(tailScrollTop - 100)
  await page.waitForTimeout(50)
  const anchor = await firstVisibleTranscriptRow(transcript)

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
          content: 'output after keyboard scroll',
        },
      },
    })
  })

  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeGreaterThan(100)
})

test('stops following when focus navigation scrolls to an off-screen transcript control', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `history-${entryIndex}`,
    hasArchivedContent: entryIndex === 499,
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
  const loadEntry = transcript.getByRole('button', { name: 'Load full entry' })
  const loadOlder = transcript.getByRole('button', { name: 'Load older' })
  await expect(loadEntry).toBeVisible()
  const tailScrollTop = await transcript.evaluate(element => element.scrollTop)
  await loadEntry.press('Shift+Tab')
  await expect(loadOlder).toBeFocused()
  await expect.poll(() => transcript.evaluate(element => element.scrollTop))
    .toBeLessThan(tailScrollTop - 100)
  await page.waitForTimeout(50)
  const anchor = await firstVisibleTranscriptRow(transcript)

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
          content: 'output after focus navigation',
        },
      },
    })
  })

  await expect(loadOlder).toBeFocused()
  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeGreaterThan(100)
})

test('resumes following when focus navigation enters the transcript at its tail', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `history-${entryIndex}`,
    hasArchivedContent: entryIndex === 499,
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
  const loadEntry = transcript.getByRole('button', { name: 'Load full entry' })
  const outsideControl = panel.getByRole('button', { name: 'Reject' })
  await expect(loadEntry).toBeVisible()
  await transcript.hover()
  await page.mouse.wheel(0, -300)
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeGreaterThan(100)
  await outsideControl.focus()
  await page.waitForTimeout(250)
  await outsideControl.press('Shift+Tab')

  await expect(loadEntry).toBeFocused()
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight)).toBeLessThan(44)
  await expect(transcript).toHaveAttribute('data-following', 'true')
  await page.evaluate(() => {
    for (let offset = 0; offset < 4; offset++) {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence: 2 + offset,
          operation: 'append',
          entryIndex: 500 + offset,
          entry: {
            occurredAt: '2026-03-01T12:01:00Z',
            source: 'assistant',
            content: `focus returned to tail ${offset + 1}`,
          },
        },
      })
    }
  })

  await expect(transcript.getByText('focus returned to tail 4')).toBeVisible()
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight)).toBeLessThan(2)
})

test('stops following when text selection autoscrolls the transcript', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `selectable history ${entryIndex}`,
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
  const tailContent = transcript.locator('[data-entry-index="499"] .transcript-content')
  await expect(tailContent).toBeVisible()
  const tailScrollTop = await transcript.evaluate(element => element.scrollTop)
  const transcriptBox = await transcript.boundingBox()
  const contentBox = await tailContent.boundingBox()
  if (!transcriptBox || !contentBox)
    throw new Error('Expected the transcript and tail content to have layout boxes.')
  await page.mouse.move(contentBox.x + contentBox.width / 2, contentBox.y + contentBox.height / 2)
  await page.mouse.down()
  await page.waitForTimeout(250)
  await page.mouse.move(contentBox.x + contentBox.width / 2, transcriptBox.y - 30, { steps: 12 })
  await page.waitForTimeout(400)
  await page.mouse.up()

  await expect.poll(() => transcript.evaluate(element => element.scrollTop))
    .toBeLessThan(tailScrollTop - 100)
  await page.waitForTimeout(50)
  const anchor = await firstVisibleTranscriptRow(transcript)

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
          content: 'output after selection scroll',
        },
      },
    })
  })

  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight))
    .toBeGreaterThan(100)
})

test('active selection movement cancels concurrent history anchor restoration', async ({ page }) => {
  await loadSnapshot(page)
  const entries = Array.from({ length: 500 }, (_, offset) => ({
    entryIndex: offset + 50,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `selectable history ${offset + 50}`,
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
  const tailContent = transcript.locator('[data-entry-index="549"] .transcript-content')
  await expect(tailContent).toBeVisible()
  const transcriptBox = await transcript.boundingBox()
  const contentBox = await tailContent.boundingBox()
  if (!transcriptBox || !contentBox)
    throw new Error('Expected the transcript and tail content to have layout boxes.')
  const selectionTailScrollTop = await transcript.evaluate(element => element.scrollTop)
  await page.mouse.move(contentBox.x + contentBox.width / 2, contentBox.y + contentBox.height / 2)
  await page.mouse.down()
  await page.mouse.move(contentBox.x + contentBox.width / 2, transcriptBox.y - 30, { steps: 12 })
  await expect.poll(() => transcript.evaluate(element => element.scrollTop))
    .toBeLessThan(selectionTailScrollTop - 100)
  const olderEntries = Array.from({ length: 50 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T11:00:00Z',
    source: 'assistant',
    content: `older selectable history ${entryIndex}`,
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
  const scrollTopAfterMerge = await transcript.evaluate(element => element.scrollTop)
  await page.waitForTimeout(300)
  expect(await transcript.evaluate(element => element.scrollTop))
    .toBeLessThan(scrollTopAfterMerge - 20)
  await page.mouse.up()

  await expect(transcript).toHaveAttribute('data-following', 'false')
  const anchor = await firstVisibleTranscriptRow(transcript)
  await page.waitForTimeout(300)
  await expect.poll(async () => transcript
    .locator(`[data-entry-index="${anchor.entryIndex}"]`)
    .evaluate((element, viewportTop) =>
      element.getBoundingClientRect().top - viewportTop,
    await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchor.offset, 0)
})

test('preserves the viewport while updates arrive in older history', async ({ page }) => {
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await transcript.evaluate(element => {
    element.scrollTop = element.scrollHeight / 2
    element.dispatchEvent(new Event('scroll'))
  })
  await page.waitForTimeout(50)
  const anchor = await firstVisibleTranscriptRow(transcript)

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
          content: 'background update',
        },
      },
    })
  })
  await page.waitForTimeout(50)

  const after = await transcript.locator(`[data-entry-index="${anchor.entryIndex}"]`).evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top))
  expect(Math.abs(after - anchor.offset)).toBeLessThan(2)
  await expect(transcript.getByText('background update')).toHaveCount(0)
})

test('resumes viewport windowing after preserving a reading position', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1400 })
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await transcript.evaluate(element => {
    element.scrollTop = element.scrollHeight / 2
    element.dispatchEvent(new Event('scroll'))
  })
  await page.waitForTimeout(50)
  const anchor = await firstVisibleTranscriptRow(transcript)

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
          content: 'background update',
        },
      },
    })
  })

  await expect.poll(async () => transcript.locator(`[data-entry-index="${anchor.entryIndex}"]`).evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top))).toBeCloseTo(anchor.offset, 0)
  await expect.poll(() => transcript.evaluate((element) => {
    const viewportBottom = element.getBoundingClientRect().bottom
    const rowBottoms = [...element.querySelectorAll<HTMLElement>('.transcript-line')]
      .map(row => row.getBoundingClientRect().bottom)
    return viewportBottom - Math.max(...rowBottoms)
  })).toBeLessThan(30)
})

test('keeps viewport windowing active during rapid tail streaming', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1400 })
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await transcript.evaluate(element => {
    element.scrollTop = element.scrollHeight / 2
    element.dispatchEvent(new Event('scroll'))
  })
  await page.waitForTimeout(50)
  const anchor = await firstVisibleTranscriptRow(transcript)

  await page.evaluate(async () => {
    for (let sequence = 2; sequence <= 21; sequence++) {
      window.__blaxquadHarness?.receive({
        version: 3,
        type: 'transcript.update',
        payload: {
          role: 'coder',
          sequence,
          operation: 'append-content',
          entryIndex: 499,
          content: '.',
        },
      })
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
    }
  })

  const after = await transcript.locator(`[data-entry-index="${anchor.entryIndex}"]`).evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top))
  expect(Math.abs(after - anchor.offset)).toBeLessThan(2)
  const blankSpace = await transcript.evaluate((element) => {
    const viewportBottom = element.getBoundingClientRect().bottom
    const rowBottoms = [...element.querySelectorAll<HTMLElement>('.transcript-line')]
      .map(row => row.getBoundingClientRect().bottom)
    return viewportBottom - Math.max(...rowBottoms)
  })
  expect(blankSpace).toBeLessThan(30)
})

test('does not restore an anchor after the user starts scrolling', async ({ page }) => {
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

  const transcript = page.locator('.role-panel').filter({ hasText: 'coder' }).locator('.transcript')
  await transcript.evaluate(element => {
    element.scrollTop = element.scrollHeight / 2
    element.dispatchEvent(new Event('scroll'))
  })
  await page.waitForTimeout(50)

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
          content: 'background update',
        },
      },
    })
  })
  await transcript.dispatchEvent('pointerdown')
  await transcript.evaluate(element => {
    element.scrollTop = 0
    element.dispatchEvent(new Event('scroll'))
  })

  await expect.poll(() => transcript.evaluate(element => element.scrollTop)).toBeLessThan(2)
  await expect(transcript.getByText('history-0', { exact: true })).toBeVisible()
})

test('keeps paged history and its reading anchor across recovery synchronization', async ({ page }) => {
  const { transcript, anchorOffset } =
    await loadPagedTranscriptAtReadingPosition(page)
  const recentEntries = Array.from({ length: 50 }, (_, offset) => ({
    entryIndex: offset + 150,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `recovered-${offset + 150}`,
  }))
  await page.evaluate((entries) => {
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
          entries,
          hasMore: true,
          historyTruncated: false,
        }],
      },
    })
  }, recentEntries)

  const anchor = transcript.locator('[data-entry-index="40"]')
  await expect(anchor).toBeVisible()
  await expect.poll(async () => anchor.evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchorOffset, 0)
  expect(await transcript.locator('[data-entry-index]').evaluateAll(rows =>
    new Set(rows.map(row => row.getAttribute('data-entry-index'))).size))
    .toBe(await transcript.locator('[data-entry-index]').count())
  await expect(transcript).toHaveAttribute('data-saw-invalid-reset-window', 'false')
  expect(await transcript.locator('.transcript-line').count()).toBeLessThan(100)
})

test('keeps a paged reading anchor when recovery is batched with a live update', async ({ page }) => {
  const { transcript, anchorOffset } =
    await loadPagedTranscriptAtReadingPosition(page)
  const recentEntries = Array.from({ length: 50 }, (_, offset) => ({
    entryIndex: offset + 150,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `recovered-${offset + 150}`,
  }))
  await page.evaluate((entries) => {
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
          entries,
          hasMore: true,
          historyTruncated: false,
        }],
      },
    })

    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.update',
      payload: {
        role: 'coder',
        sequence: 12,
        operation: 'append-content',
        entryIndex: 199,
        content: '-live',
      },
    })
  }, recentEntries)

  const anchor = transcript.locator('[data-entry-index="40"]')
  await expect(anchor).toBeVisible()
  await expect.poll(async () => anchor.evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top)))
    .toBeCloseTo(anchorOffset, 0)
  await expect(transcript).toHaveAttribute('data-saw-invalid-reset-window', 'false')
  expect(await transcript.locator('[data-entry-index]').evaluateAll(rows =>
    new Set(rows.map(row => row.getAttribute('data-entry-index'))).size))
    .toBe(await transcript.locator('[data-entry-index]').count())
})

test('resolves an omitted reading anchor during batched recovery', async ({ page }) => {
  await loadSnapshot(page)
  const history = Array.from({ length: 200 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `history-${entryIndex}`,
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
  await transcript.evaluate(element => {
    element.scrollTop = 40 * 22
    element.dispatchEvent(new Event('scroll'))
  })
  const anchor = await firstVisibleTranscriptRow(transcript)
  const recentEntries = Array.from({ length: 50 }, (_, offset) => ({
    entryIndex: offset + 150,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `recovered-${offset + 150}`,
  }))
  await page.evaluate((entries) => {
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
          entries,
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
        sequence: 12,
        operation: 'append-content',
        entryIndex: 199,
        content: '-live',
      },
    })
  }, recentEntries)

  const nearest = transcript.locator('[data-entry-index="150"]')
  await expect(nearest).toBeVisible()
  const nearestOffset = await nearest.evaluate((element, viewportTop) =>
    element.getBoundingClientRect().top - viewportTop,
  await transcript.evaluate(element => element.getBoundingClientRect().top))
  expect(nearestOffset).toBeGreaterThanOrEqual(-22)
  expect(nearestOffset).toBeLessThan(22)
  await expect(transcript.locator('[data-entry-index="40"]')).toHaveCount(0)
})
