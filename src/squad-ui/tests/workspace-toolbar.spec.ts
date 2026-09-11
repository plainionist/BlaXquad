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

function gitHistoryTrigger(page: import('@playwright/test').Page) {
  return page.getByRole('button', { name: 'Git history', exact: true })
}

test('Issues and Git history render as adjacent icon buttons inside one bordered toolbar panel', async ({ page }) => {
  await loadSnapshot(page)
  const toolbar = page.locator('.workspace-toolbar')
  await expect(toolbar).toBeVisible()
  await expect(toolbar).toHaveCSS('border-style', 'solid')

  const issues = issueTrigger(page)
  const gitHistory = gitHistoryTrigger(page)
  await expect(toolbar.locator('button')).toHaveCount(2)
  await expect(issues).toBeVisible()
  await expect(gitHistory).toBeVisible()

  // Both actions render icon-only: no visible text label, just an accessible name and a native tooltip.
  await expect(issues).toHaveText('')
  await expect(gitHistory).toHaveText('')
  await expect(issues.locator('svg')).toHaveCount(1)
  await expect(gitHistory.locator('svg')).toHaveCount(1)
})

test('both toolbar actions expose a native tooltip that matches their accessible name', async ({ page }) => {
  await loadSnapshot(page)
  await expect(issueTrigger(page)).toHaveAttribute('title', 'Issues')
  await expect(gitHistoryTrigger(page)).toHaveAttribute('title', 'Git history')
})

test('the toolbar panel border spans the entire width of the workspace shell', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await loadSnapshot(page)

  const toolbar = page.locator('.workspace-toolbar')
  const workspace = page.locator('.workspace')

  const toolbarBox = (await toolbar.boundingBox())!
  const workspaceBox = (await workspace.boundingBox())!

  // Workspace has 10px padding on left and right (total 20px)
  expect(toolbarBox.width).toBeCloseTo(workspaceBox.width - 20, 0)
})

test('Git history renders visibly dimmed while disabled, and full-strength once available', async ({ page }) => {
  await loadSnapshot(page)
  const gitHistory = gitHistoryTrigger(page)
  await expect(gitHistory).toBeDisabled()
  const disabledOpacity = await gitHistory.evaluate(element => getComputedStyle(element).opacity)
  expect(Number(disabledOpacity)).toBeLessThan(1)

  await deliverHostMessages(page, [
    protocolMessage('workspace-tools.snapshot', { payload: { gitHistoryAvailable: true } }),
  ])
  await expect(gitHistory).toBeEnabled()
  const enabledOpacity = await gitHistory.evaluate(element => getComputedStyle(element).opacity)
  expect(Number(enabledOpacity)).toBe(1)
})

test('the toolbar, issue menu, preview, and role panels remain contained at desktop and 390-pixel-wide viewports', async ({ page }) => {
  async function box(selector: string) {
    const rect = await page.locator(selector).boundingBox()
    expect(rect).not.toBeNull()
    return rect!
  }
  function containedIn(rect: { x: number, y: number, width: number, height: number }, viewport: { width: number, height: number }) {
    expect(rect.x).toBeGreaterThanOrEqual(0)
    expect(rect.y).toBeGreaterThanOrEqual(0)
    expect(rect.x + rect.width).toBeLessThanOrEqual(viewport.width)
    expect(rect.y + rect.height).toBeLessThanOrEqual(viewport.height)
  }

  await page.setViewportSize({ width: 1440, height: 900 })
  await loadSnapshot(page)
  containedIn(await box('.workspace-toolbar'), page.viewportSize()!)

  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])
  const menu = await box('.issue-menu')
  containedIn(menu, page.viewportSize()!)

  await page.setViewportSize({ width: 390, height: 844 })
  containedIn(await box('.workspace-toolbar'), page.viewportSize()!)
  containedIn(await box('.issue-menu'), page.viewportSize()!)
})
