import { expect, test } from '@playwright/test'
import {
  deliverHostMessages,
  issueCatalog,
  lastClientMessage,
  loadSnapshot,
  protocolMessage,
} from './support/dashboardHarness'

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

test('desktop and 390-pixel-wide viewports contain the toolbar, menu, flyout, and role panels without clipping or overlap', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await loadSnapshot(page)
  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])
  await page.getByRole('option', { name: issueCatalog[0].title }).hover()

  let viewport = page.viewportSize()!
  for (const selector of ['.issue-toolbar', '.issue-menu', '.issue-flyout']) {
    const box = await page.locator(selector).boundingBox()
    expect(box).not.toBeNull()
    expect(box!.x).toBeGreaterThanOrEqual(0)
    expect(box!.x + box!.width).toBeLessThanOrEqual(viewport.width)
  }

  const panels = page.locator('.role-panel')
  const firstPanel = await panels.nth(0).boundingBox()
  const secondPanel = await panels.nth(1).boundingBox()
  expect(firstPanel?.y).toBe(secondPanel?.y) // the toolbar does not push role panels out of their row

  await page.setViewportSize({ width: 390, height: 844 })
  viewport = page.viewportSize()!
  for (const selector of ['.issue-toolbar', '.issue-menu', '.issue-flyout']) {
    const box = await page.locator(selector).boundingBox()
    expect(box).not.toBeNull()
    expect(box!.x).toBeGreaterThanOrEqual(0)
    expect(box!.x + box!.width).toBeLessThanOrEqual(viewport.width)
  }
})
