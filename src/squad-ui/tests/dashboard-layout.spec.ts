import { expect, test } from '@playwright/test'
import {
  loadRoleSnapshots,
  loadSnapshot,
  roleSnapshot,
  roleTranscript,
} from './support/dashboardHarness'

test('renders complete panels at wide desktop resolution', async ({ page }) => {
  await page.setViewportSize({ width: 2560, height: 1440 })
  await loadSnapshot(page)

  await expect(page.getByRole('heading', { name: 'coder' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'reviewer' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'writer' })).toBeVisible()
  await expect(page.getByText('Permission required')).toBeVisible()
  await expect(page.getByText('Input requested')).toBeVisible()
  await expect(page.getByText('Form requested')).toBeVisible()

  const panels = page.locator('.role-panel')
  await expect(panels).toHaveCount(3)
  const firstPanel = await panels.nth(0).boundingBox()
  const secondPanel = await panels.nth(1).boundingBox()
  expect(firstPanel?.y).toBe(secondPanel?.y)
})

test('shows a prominent activity marker beside the working agent', async ({ page }) => {
  await loadSnapshot(page)

  const coderPanel = page.locator('.role-panel').filter({ hasText: 'coder' })
  const reviewerPanel = page.locator('.role-panel').filter({ hasText: 'reviewer' })
  const marker = coderPanel.getByRole('status', { name: 'Working' })

  await expect(marker).toBeVisible()
  await expect(marker).toHaveCSS('width', '12px')
  await expect(marker).toHaveCSS('height', '12px')
  await expect(reviewerPanel.getByRole('status', { name: 'Working' })).toHaveCount(0)
})

for (const scenario of [
  { name: 'idle', status: 'idle', isWorking: false, presentsWorking: false },
  { name: 'running', status: 'running', isWorking: false, presentsWorking: true },
  { name: 'starting', status: 'starting', isWorking: false, presentsWorking: true },
  { name: 'stopped', status: 'stopped', isWorking: false, presentsWorking: true },
  { name: 'error', status: 'error', isWorking: false, presentsWorking: true },
  { name: 'explicitly working', status: 'idle', isWorking: true, presentsWorking: true },
] as const) {
  test(`presents ${scenario.name} role activity`, async ({ page }) => {
    await loadRoleSnapshots(page, [
      roleSnapshot('agent', {
        status: scenario.status,
        isWorking: scenario.isWorking,
      }),
    ])

    const panel = page.locator('.role-panel')
      .filter({ has: page.getByRole('heading', { name: 'agent', exact: true }) })
    const workingStatus = panel.locator('.working-dot')
    await expect(panel).toHaveClass(
      scenario.presentsWorking ? 'role-panel is-working' : 'role-panel',
    )
    await expect(workingStatus).toHaveCount(scenario.presentsWorking ? 1 : 0)
    if (scenario.presentsWorking) {
      await expect(workingStatus).toHaveAttribute('role', 'status')
      await expect(workingStatus).toHaveAccessibleName('Working')
    }
  })
}

test('shows thinking only while explicitly working without an active tool', async ({ page }) => {
  await loadRoleSnapshots(page, [
    roleSnapshot('thinking', { status: 'idle', isWorking: true }),
    roleSnapshot('using-tool', {
      status: 'running',
      isWorking: true,
      activeTool: 'read_file',
    }),
    roleSnapshot('status-only', { status: 'running', isWorking: false }),
  ])

  await expect(roleTranscript(page, 'thinking').locator('.is-thinking'))
    .toHaveText('>Thinking ...')
  await expect(roleTranscript(page, 'using-tool').locator('.is-thinking'))
    .toHaveCount(0)
  await expect(roleTranscript(page, 'status-only').locator('.is-thinking'))
    .toHaveCount(0)
})

test('shows semantic subagent activity with a yellow double-chevron prefix', async ({ page }) => {
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
          occurredAt: '2026-03-01T12:00:01Z',
          source: 'subagent',
          content: 'Code Review · gpt-5.6-sol · Review authentication changes',
        },
      },
    })
  })

  const entry = roleTranscript(page, 'coder').locator('.is-subagent')
  await expect(entry.locator('.transcript-prefix')).toHaveText('>>')
  await expect(entry.locator('.transcript-prefix')).toHaveCSS('width', '24px')
  await expect(entry.locator('.transcript-prefix')).toHaveCSS('color', 'rgb(230, 230, 0)')
  await expect(entry.locator('.transcript-content'))
    .toHaveText('Code Review · gpt-5.6-sol · Review authentication changes')
})

test('puts send, cancel, and scroll-to-end controls beside the prompt', async ({ page }) => {
  await loadSnapshot(page)

  const panel = page.locator('.role-panel').first()
  const actions = panel.locator('.composer-actions button')
  await expect(actions).toHaveText(['Send', 'Cancel', '\u2193'])
  await expect(panel.getByRole('button', { name: 'Scroll terminal output to end' })).toHaveAttribute(
    'title',
    'Scroll terminal output to end',
  )

  const entries = Array.from({ length: 100 }, (_, entryIndex) => ({
    entryIndex,
    occurredAt: '2026-03-01T12:00:00Z',
    source: 'assistant',
    content: `line-${entryIndex}`,
  }))
  await page.evaluate(entries => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'coder',
          sequence: 2,
          entries,
          hasMore: false,
          historyTruncated: false,
        }],
      },
    })
  }, entries)

  const transcript = panel.locator('.transcript')
  await expect(transcript.getByText('line-99')).toBeVisible()
  await transcript.evaluate(element => { element.scrollTop = element.scrollHeight / 2 })
  await expect.poll(() => transcript.evaluate(element => element.scrollTop)).toBeGreaterThan(0)
  await panel.getByRole('button', { name: 'Scroll terminal output to end' }).click()
  await expect.poll(() => transcript.evaluate(element =>
    element.scrollHeight - element.scrollTop - element.clientHeight)).toBeLessThan(2)
})

test('renders unknown usage without fabricated zero values', async ({ page }) => {
  await loadSnapshot(page)

  await expect(page.getByText('AIC unavailable').first()).toBeVisible()
  await expect(page.locator('.context-meter').first()).toHaveAttribute('aria-label', 'Context usage unavailable')
})

test('renders file reads with a green marker and full path', async ({ page }) => {
  await loadSnapshot(page)
  const fullPath = 'C:\\ws\\BlaXquad\\src\\squad\\Program.cs'

  await page.evaluate((path) => {
    window.__blaxquadHarness?.receive({
      version: 3,
      type: 'transcript.synchronize',
      payload: {
        roles: [{
          role: 'coder',
          sequence: 2,
          startIndex: 0,
          entries: [{ occurredAt: '2026-03-01T12:00:00Z', source: 'read', content: path }],
        }],
      },
    })
  }, fullPath)

  const readLine = page.locator('.transcript-line').filter({ hasText: fullPath })
  await expect(readLine.locator('.transcript-marker.is-read')).toBeVisible()
  await expect(readLine.locator('.transcript-marker.is-read')).toHaveCSS('background-color', 'rgb(137, 209, 133)')
  await expect(readLine.locator('.transcript-prefix')).toHaveCount(0)
})

test('stacks role panels and reports recoverable host errors on narrow screens', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await loadSnapshot(page)

  const panels = page.locator('.role-panel')
  const firstPanel = await panels.nth(0).boundingBox()
  const secondPanel = await panels.nth(1).boundingBox()
  expect(secondPanel?.y).toBeGreaterThan((firstPanel?.y ?? 0) + (firstPanel?.height ?? 0) - 1)

  await page.evaluate(() => window.__blaxquadHarness?.receive({ version: 3, type: 'unrecognized.message' }))
  await expect(page.locator('.protocol-error')).toContainText('Unknown host message')
})
