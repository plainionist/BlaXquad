import { expect, test } from '@playwright/test'
import {
  deliverHostMessages,
  issueCatalog,
  lastClientMessage,
  loadSnapshot,
  protocolMessage,
} from './support/dashboardHarness'

function gitHistoryButton(page: import('@playwright/test').Page) {
  return page.getByRole('button', { name: 'Git history', exact: true })
}

function issueTrigger(page: import('@playwright/test').Page) {
  return page.getByRole('button', { name: 'Issues', exact: true })
}

test('the Git history button starts disabled before the host publishes any availability', async ({ page }) => {
  await loadSnapshot(page)
  await expect(gitHistoryButton(page)).toBeDisabled()
})

test('the Git history button follows the host-published workspace-tools.snapshot availability', async ({ page }) => {
  await loadSnapshot(page)
  const button = gitHistoryButton(page)
  await expect(button).toBeDisabled()

  await deliverHostMessages(page, [
    protocolMessage('workspace-tools.snapshot', { payload: { gitHistoryAvailable: true } }),
  ])
  await expect(button).toBeEnabled()

  await deliverHostMessages(page, [
    protocolMessage('workspace-tools.snapshot', { payload: { gitHistoryAvailable: false } }),
  ])
  await expect(button).toBeDisabled()
})

test('clicking the enabled Git history button sends exactly one git-history.open command', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('workspace-tools.snapshot', { payload: { gitHistoryAvailable: true } }),
  ])
  const button = gitHistoryButton(page)
  await expect(button).toBeEnabled()

  const messageCountBefore = await page.evaluate(() => window.__blaxquadHarness!.messages.length)
  await button.click()

  const message = await lastClientMessage(page)
  expect(message.type).toBe('git-history.open')
  const messageCountAfter = await page.evaluate(() => window.__blaxquadHarness!.messages.length)
  expect(messageCountAfter).toBe(messageCountBefore + 1)
})

test('clicking the disabled Git history button never sends a protocol command', async ({ page }) => {
  await loadSnapshot(page)
  const button = gitHistoryButton(page)
  await expect(button).toBeDisabled()

  const messageCountBefore = await page.evaluate(() => window.__blaxquadHarness!.messages.length)
  await button.click({ force: true })
  const messageCountAfter = await page.evaluate(() => window.__blaxquadHarness!.messages.length)
  expect(messageCountAfter).toBe(messageCountBefore)
})

test('Git history availability leaves Issues behavior unaffected', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('workspace-tools.snapshot', { payload: { gitHistoryAvailable: true } }),
  ])
  await expect(gitHistoryButton(page)).toBeEnabled()

  await issueTrigger(page).click()
  const { requestId } = await lastClientMessage(page)
  expect(requestId).toBeTruthy()
  await deliverHostMessages(page, [
    protocolMessage('issues.list', { requestId, payload: { issues: issueCatalog } }),
  ])
  await expect(page.getByRole('option', { name: issueCatalog[0].title })).toBeVisible()
})
