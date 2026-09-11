import { expect, test } from '@playwright/test'
import {
  deliverHostMessages,
  issueCatalog,
  lastClientMessage,
  loadRoleSnapshots,
  loadSnapshot,
  protocolMessage,
  roleSnapshot,
} from './support/dashboardHarness'

function playButton(page: import('@playwright/test').Page, title: string) {
  return page.getByRole('button', { name: `Prepare the leader's prompt for ${title}` })
}

function issueTrigger(page: import('@playwright/test').Page) {
  return page.getByRole('button', { name: 'Issues', exact: true })
}

test('every opening sends one fresh issues.list request, and only its matching response completes the load', async ({ page }) => {
  await loadSnapshot(page)
  const trigger = issueTrigger(page)

  await trigger.click()
  const firstMessage = await lastClientMessage(page)
  expect(firstMessage.type).toBe('issues.list')
  expect(firstMessage.requestId).toBeTruthy()

  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId: 'stale-request', payload: { issues: issueCatalog } }),
  ])
  await expect(page.locator('.issue-status')).toContainText('Loading issues')
  await expect(page.getByRole('option', { name: issueCatalog[0].title })).toHaveCount(0)

  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId: firstMessage.requestId, payload: { issues: issueCatalog } }),
  ])
  await expect(page.getByRole('option', { name: issueCatalog[0].title })).toBeVisible()

  await trigger.click() // close
  await trigger.click() // reopen issues a fresh request
  const secondMessage = await lastClientMessage(page)
  expect(secondMessage.requestId).not.toBe(firstMessage.requestId)

  // A response matching the superseded request must not affect the new pending load.
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId: firstMessage.requestId, payload: { issues: [] } }),
  ])
  await expect(page.locator('.issue-status')).toContainText('Loading issues')
})

test('hovering an issue and focusing its row select the same issue and show the same plain-text flyout', async ({ page }) => {
  await loadSnapshot(page)
  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])

  const firstRow = page.getByRole('option', { name: issueCatalog[0].title })
  await firstRow.hover()
  await expect(page.locator('.issue-flyout-frontmatter')).toHaveText(issueCatalog[0].frontmatter)
  await expect(page.locator('.issue-flyout-preview')).toHaveText(issueCatalog[0].previewLines.join('\n'))

  const secondRow = page.getByRole('option', { name: issueCatalog[1].title })
  await secondRow.focus()
  await expect(page.locator('.issue-flyout-frontmatter')).toHaveText(issueCatalog[1].frontmatter)
  // The preview renders raw text (including angle brackets) rather than interpreting it as HTML.
  await expect(page.locator('.issue-flyout-preview')).toHaveText(issueCatalog[1].previewLines.join('\n'))
  await expect(page.locator('.issue-flyout-preview b')).toHaveCount(0)
})

test('the trigger, Escape, and an outside click close the menu; an inside interaction does not', async ({ page }) => {
  await loadSnapshot(page)
  const trigger = issueTrigger(page)

  await trigger.click()
  await expect(page.locator('.issue-panel')).toBeVisible()
  await trigger.click()
  await expect(page.locator('.issue-panel')).toHaveCount(0)

  await trigger.click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])
  await page.getByRole('option', { name: issueCatalog[0].title }).hover()
  await expect(page.locator('.issue-panel')).toBeVisible()

  await page.keyboard.press('Escape')
  await expect(page.locator('.issue-panel')).toHaveCount(0)
  await expect(trigger).toBeFocused()

  await trigger.click()
  const panelBox = (await page.locator('.issue-panel').boundingBox())!
  // Click well below the overlay panel, but still inside the workspace, to prove it is a true outside click.
  await page.mouse.click(panelBox.x + 5, panelBox.y + panelBox.height + 40)
  await expect(page.locator('.issue-panel')).toHaveCount(0)
})

test('missing or empty results show a disabled entry, and catalog errors are visible and retry on the next open', async ({ page }) => {
  await loadSnapshot(page)
  const trigger = issueTrigger(page)

  await trigger.click()
  const emptyRequest = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId: emptyRequest.requestId, payload: { issues: [] } }),
  ])
  const emptyRow = page.getByRole('option', { name: '<no issues>', exact: true })
  await expect(emptyRow).toBeVisible()
  await expect(emptyRow).toHaveAttribute('aria-disabled', 'true')

  await trigger.click() // close
  await trigger.click() // reopen
  const errorRequest = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('protocol.error', {
      requestId: errorRequest.requestId,
      payload: { message: 'The issues directory could not be read.' },
    }),
  ])
  await expect(page.locator('.issue-status-error')).toContainText('The issues directory could not be read.')
  await expect(page.locator('.protocol-error')).toHaveCount(0)

  await trigger.click() // close
  await trigger.click() // reopen retries with a fresh request
  const retryRequest = await lastClientMessage(page)
  expect(retryRequest.requestId).not.toBe(errorRequest.requestId)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId: retryRequest.requestId, payload: { issues: issueCatalog } }),
  ])
  await expect(page.getByRole('option', { name: issueCatalog[0].title })).toBeVisible()
  await expect(page.locator('.issue-status-error')).toHaveCount(0)
})

async function stubClipboard(page: import('@playwright/test').Page, behavior: 'resolve' | 'reject') {
  await page.evaluate((mode) => {
    const win = window as unknown as { __clipboardWrites: string[] }
    win.__clipboardWrites = []
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: (text: string) => {
          win.__clipboardWrites.push(text)
          return mode === 'resolve' ? Promise.resolve() : Promise.reject(new Error('denied'))
        },
      },
    })
  }, behavior)
}

function clipboardWrites(page: import('@playwright/test').Page) {
  return page.evaluate(() => (window as unknown as { __clipboardWrites: string[] }).__clipboardWrites ?? [])
}

test('copy writes the exact workspace-relative path to the clipboard and announces success', async ({ page }) => {
  await loadSnapshot(page)
  await stubClipboard(page, 'resolve')
  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])

  const copyButton = page.getByRole('button', { name: `Copy path for ${issueCatalog[0].title}` })
  await expect(copyButton).toBeEnabled()
  await copyButton.click()

  await expect.poll(() => clipboardWrites(page)).toEqual([issueCatalog[0].path])
  await expect(page.locator('.issue-copy-status')).toContainText(issueCatalog[0].path)
  await expect(page.locator('.issue-copy-status-error')).toHaveCount(0)
})

test('a rejected clipboard write surfaces a recoverable error and does not claim success', async ({ page }) => {
  await loadSnapshot(page)
  await stubClipboard(page, 'reject')
  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])

  const copyButton = page.getByRole('button', { name: `Copy path for ${issueCatalog[0].title}` })
  await copyButton.click()
  await expect(page.locator('.issue-copy-status-error')).toBeVisible()
  await expect(page.locator('.issue-copy-status-error')).not.toContainText('Copied')

  // Recoverable: a subsequent successful attempt clears the error and reports success.
  await stubClipboard(page, 'resolve')
  await copyButton.click()
  await expect.poll(() => clipboardWrites(page)).toEqual([issueCatalog[0].path])
  await expect(page.locator('.issue-copy-status')).toContainText(issueCatalog[0].path)
  await expect(page.locator('.issue-copy-status-error')).toHaveCount(0)
})

test('copy remains enabled with no configured roles and never sends a UI protocol command', async ({ page }) => {
  await loadRoleSnapshots(page, [])
  await stubClipboard(page, 'resolve')
  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])

  const copyButton = page.getByRole('button', { name: `Copy path for ${issueCatalog[0].title}` })
  await expect(copyButton).toBeEnabled()
  const messageCountBefore = await page.evaluate(() => window.__blaxquadHarness!.messages.length)
  await copyButton.click()

  await expect.poll(() => clipboardWrites(page)).toEqual([issueCatalog[0].path])
  const messageCountAfter = await page.evaluate(() => window.__blaxquadHarness!.messages.length)
  expect(messageCountAfter).toBe(messageCountBefore)
})

test('play replaces and focuses the configured leader\'s draft with the exact issue path, leaving other roles\' drafts untouched', async ({ page }) => {
  await loadSnapshot(page)
  const coderPrompt = page.getByLabel('Message coder')
  const reviewerPrompt = page.getByLabel('Message reviewer')
  const writerPrompt = page.getByLabel('Message writer')
  await reviewerPrompt.fill('existing reviewer draft')
  await writerPrompt.fill('existing writer draft')

  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])

  await playButton(page, issueCatalog[0].title).click()

  await expect(coderPrompt).toHaveValue(`process this issue: '${issueCatalog[0].path}'`)
  await expect(coderPrompt).toBeFocused()
  await expect(reviewerPrompt).toHaveValue('existing reviewer draft')
  await expect(writerPrompt).toHaveValue('existing writer draft')
})

test('play targets the configured leader even when a different role reports runtime activity first, and does not send prompt.send', async ({ page }) => {
  await loadSnapshot(page)

  // Runtime activity for a non-leader role arrives before Play is used; targeting must still follow
  // squad.json's configured leader, not "most recently active."
  await deliverHostMessages(page, [
    protocolMessage('transcript.update', {
      payload: {
        role: 'reviewer',
        sequence: 2,
        operation: 'append',
        entryIndex: 1,
        entry: { occurredAt: '2026-03-01T12:05:00Z', source: 'assistant', content: 'Reviewer is now active.' },
      },
    }),
  ])

  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])
  const messageCountBefore = await page.evaluate(() => window.__blaxquadHarness!.messages.length)

  await playButton(page, issueCatalog[0].title).click()

  await expect(page.getByLabel('Message coder')).toHaveValue(`process this issue: '${issueCatalog[0].path}'`)
  await expect(page.getByLabel('Message reviewer')).toHaveValue('')

  const messageCountAfter = await page.evaluate(() => window.__blaxquadHarness!.messages.length)
  expect(messageCountAfter).toBe(messageCountBefore) // Play never emits a protocol command by itself.

  // The user must still explicitly submit; Play only stages the draft.
  await page.getByLabel('Message coder').press('Enter')
  const sendMessage = await lastClientMessage(page)
  expect(sendMessage.type).toBe('prompt.send')
})

test('play is disabled with no configured roles, while copy remains enabled', async ({ page }) => {
  await loadRoleSnapshots(page, [])
  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])

  await expect(playButton(page, issueCatalog[0].title)).toBeDisabled()
  await expect(page.getByRole('button', { name: `Copy path for ${issueCatalog[0].title}` })).toBeEnabled()
})

test('play targets the configured leader by name even when the leader is not the first configured role', async ({ page }) => {
  // Roles are configured in the order "reviewer,architect,coder", but squad.json's leader is "architect" - Play
  // must follow the published leader, never fall back to the first role in this order.
  await loadRoleSnapshots(page, [
    roleSnapshot('reviewer'),
    roleSnapshot('architect'),
    roleSnapshot('coder'),
  ], 'architect')
  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])

  await playButton(page, issueCatalog[0].title).click()

  await expect(page.getByLabel('Message architect')).toHaveValue(`process this issue: '${issueCatalog[0].path}'`)
  await expect(page.getByLabel('Message reviewer')).toHaveValue('')
  await expect(page.getByLabel('Message coder')).toHaveValue('')
})

test('play is disabled when the configured leader is not among the current roles, while copy remains enabled', async ({ page }) => {
  // The published leader "coder" (the harness default) is absent from this snapshot's roles - Play must not
  // silently fall back to targeting a different, present role.
  await loadRoleSnapshots(page, [roleSnapshot('reviewer'), roleSnapshot('writer')])
  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])

  await expect(playButton(page, issueCatalog[0].title)).toBeDisabled()
  await expect(page.getByRole('button', { name: `Copy path for ${issueCatalog[0].title}` })).toBeEnabled()
})

interface Box { x: number, y: number, width: number, height: number }

function containedIn(box: Box, viewport: { width: number, height: number }) {
  expect(box.x).toBeGreaterThanOrEqual(0)
  expect(box.y).toBeGreaterThanOrEqual(0)
  expect(box.x + box.width).toBeLessThanOrEqual(viewport.width)
  expect(box.y + box.height).toBeLessThanOrEqual(viewport.height)
}

async function boxOf(page: import('@playwright/test').Page, selector: string) {
  const box = await page.locator(selector).boundingBox()
  expect(box).not.toBeNull()
  return box as Box
}

async function panelBoxes(page: import('@playwright/test').Page) {
  const panels = page.locator('.role-panel')
  const count = await panels.count()
  expect(count).toBeGreaterThan(0)
  const boxes: Box[] = []
  for (let index = 0; index < count; index++)
    boxes.push(await boxOf(page, `.role-panel >> nth=${index}`))
  return boxes
}

test('desktop and 390-pixel-wide viewports contain the toolbar, menu, flyout, and role panels without clipping or overlap', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await loadSnapshot(page)
  let viewport = page.viewportSize()!

  const closedPanels = await panelBoxes(page)
  for (const panel of closedPanels)
    containedIn(panel, viewport) // the desktop grid fits within one viewport before the toolbar is used; no outer-page scroll

  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])
  await page.getByRole('option', { name: issueCatalog[0].title }).hover()

  const toolbar = await boxOf(page, '.issue-toolbar')
  const menu = await boxOf(page, '.issue-menu')
  const flyout = await boxOf(page, '.issue-flyout')
  for (const box of [toolbar, menu, flyout]) containedIn(box, viewport)

  const openPanels = await panelBoxes(page)
  for (const panel of openPanels)
    containedIn(panel, viewport) // opening the menu/flyout does not push the grid past the viewport either
  for (let index = 0; index < openPanels.length; index++) {
    expect(openPanels[index].x).toBe(closedPanels[index].x)
    expect(openPanels[index].width).toBe(closedPanels[index].width) // the floating menu/flyout do not widen or narrow role panels
  }
  expect(openPanels[0].y).toBe(openPanels[1].y) // the toolbar does not push role panels out of their row

  await page.setViewportSize({ width: 390, height: 844 })
  viewport = page.viewportSize()!
  const narrowToolbar = await boxOf(page, '.issue-toolbar')
  const narrowMenu = await boxOf(page, '.issue-menu')
  const narrowFlyout = await boxOf(page, '.issue-flyout')
  for (const box of [narrowToolbar, narrowMenu, narrowFlyout]) containedIn(box, viewport)

  const narrowFirstPanel = await boxOf(page, '.role-panel >> nth=0')
  expect(narrowFirstPanel.x).toBeGreaterThanOrEqual(0)
  expect(narrowFirstPanel.x + narrowFirstPanel.width).toBeLessThanOrEqual(viewport.width)
})
