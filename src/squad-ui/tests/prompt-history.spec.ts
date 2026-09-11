import { expect, test, type Page } from '@playwright/test'
import { loadSnapshot } from './support/dashboardHarness'

async function sendPrompt(page: Page, roleLabel: string, text: string) {
  const prompt = page.getByRole('textbox', { name: roleLabel })
  await prompt.fill(text)
  await prompt.press('Enter')
}

test('recalls prompts newest-to-oldest and traverses back toward the newest', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await sendPrompt(page, 'Message coder', 'first prompt')
  await sendPrompt(page, 'Message coder', 'second prompt')

  await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('second prompt')
  await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('first prompt')
  await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('first prompt')

  await prompt.press('ArrowDown')
  await expect(prompt).toHaveValue('second prompt')
})

test('restores the pre-navigation draft after moving past the newest entry', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await sendPrompt(page, 'Message coder', 'only prompt')

  await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('only prompt')
  await prompt.press('ArrowDown')
  await expect(prompt).toHaveValue('')
})

test('keeps prompt histories isolated between role composers', async ({ page }) => {
  await loadSnapshot(page)
  await sendPrompt(page, 'Message coder', 'coder prompt one')
  await sendPrompt(page, 'Message reviewer', 'reviewer prompt one')

  const coderPrompt = page.getByRole('textbox', { name: 'Message coder' })
  const reviewerPrompt = page.getByRole('textbox', { name: 'Message reviewer' })

  await coderPrompt.press('ArrowUp')
  await expect(coderPrompt).toHaveValue('coder prompt one')
  await coderPrompt.press('ArrowUp')
  await expect(coderPrompt).toHaveValue('coder prompt one')

  await reviewerPrompt.press('ArrowUp')
  await expect(reviewerPrompt).toHaveValue('reviewer prompt one')
})

test('records the trimmed prompt regardless of surrounding whitespace', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await sendPrompt(page, 'Message coder', '   padded prompt   ')

  await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('padded prompt')
})

test('does not record blank submissions in history', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await sendPrompt(page, 'Message coder', 'real prompt')

  const messageCountBeforeBlank = await page.evaluate(() =>
    window.__blaxquadHarness!.messages.length)
  await prompt.fill('   ')
  await prompt.press('Enter')
  expect(await page.evaluate(() =>
    window.__blaxquadHarness!.messages.length)).toBe(messageCountBeforeBlank)

  await prompt.fill('')
  await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('real prompt')
})

test('evicts the oldest entries beyond the 50-entry limit', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  for (let index = 1; index <= 51; index++)
    await sendPrompt(page, 'Message coder', `prompt ${index}`)

  for (let index = 0; index < 50; index++)
    await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('prompt 2')

  await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('prompt 2')
})

test('does not persist prompt history across a dashboard reload', async ({ page }) => {
  await loadSnapshot(page)
  await sendPrompt(page, 'Message coder', 'before reload')

  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('')
})

test('recalling and resubmitting a prompt sends the existing envelope and clears the composer', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await sendPrompt(page, 'Message coder', 'resend me')
  await sendPrompt(page, 'Message coder', 'second prompt')

  await prompt.press('ArrowUp')
  await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('resend me')
  await prompt.press('Enter')

  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))).toEqual({
    version: 5,
    type: 'prompt.send',
    role: 'coder',
    payload: {
      prompt: 'resend me',
    },
  })
  await expect(prompt).toHaveValue('')
})

test('does not consume ArrowUp when no prompt history is available', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await prompt.focus()
  const prevented = await page.evaluate(() => {
    const event = new KeyboardEvent('keydown', {
      key: 'ArrowUp',
      bubbles: true,
      cancelable: true,
    })
    document.activeElement!.dispatchEvent(event)
    return event.defaultPrevented
  })
  expect(prevented).toBe(false)
  await expect(prompt).toHaveValue('')
})
