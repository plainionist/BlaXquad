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

test('preserves an existing non-empty draft and restores it exactly after navigating past the newest entry', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await sendPrompt(page, 'Message coder', 'stored prompt')

  await prompt.fill('unsent draft')
  await prompt.evaluate((element) => {

    if (!(element instanceof HTMLTextAreaElement))
      throw new Error('Expected a textarea.')

    element.setSelectionRange(0, 0)
  })

  await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('stored prompt')
  await prompt.press('ArrowDown')

  await expect(prompt).toHaveValue('unsent draft')
})

test('editing a recalled prompt leaves history navigation and treats the edit as the new draft', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await sendPrompt(page, 'Message coder', 'first prompt')
  await sendPrompt(page, 'Message coder', 'second prompt')

  await prompt.press('ArrowUp')
  await expect(prompt).toHaveValue('second prompt')
  await prompt.fill('second prompt!')
  await expect(prompt).toHaveValue('second prompt!')

  await prompt.evaluate((element) => {

    if (!(element instanceof HTMLTextAreaElement))
      throw new Error('Expected a textarea.')

    element.setSelectionRange(0, 0)
  })
  await prompt.press('ArrowUp')

  await expect(prompt).toHaveValue('second prompt')
  await prompt.press('ArrowDown')
  await expect(prompt).toHaveValue('second prompt!')
})

test('starts history navigation when the caret is collapsed at the absolute start of a multiline prompt', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await sendPrompt(page, 'Message coder', 'stored prompt')

  const draft = 'line one\nline two'
  await prompt.fill(draft)
  await prompt.evaluate((element) => {

    if (!(element instanceof HTMLTextAreaElement))
      throw new Error('Expected a textarea.')

    element.setSelectionRange(0, 0)
  })
  await prompt.press('ArrowUp')

  await expect(prompt).toHaveValue('stored prompt')
  await prompt.press('ArrowDown')
  await expect(prompt).toHaveValue(draft)
})

test('does not start navigation when the selection is not collapsed at the absolute start of a multiline prompt', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await sendPrompt(page, 'Message coder', 'stored prompt')

  const draft = 'line one\nline two'
  await prompt.fill(draft)
  await prompt.evaluate((element) => {

    if (!(element instanceof HTMLTextAreaElement))
      throw new Error('Expected a textarea.')

    element.setSelectionRange(0, 4)
  })
  const prevented = await prompt.evaluate((element) => {
    const event = new KeyboardEvent('keydown', {
      key: 'ArrowUp',
      bubbles: true,
      cancelable: true,
    })
    element.dispatchEvent(event)
    return event.defaultPrevented
  })
  expect(prevented).toBe(false)
  await expect(prompt).toHaveValue(draft)
})

test('retains normal caret behavior for ArrowUp away from the absolute start in a multiline prompt', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await sendPrompt(page, 'Message coder', 'stored prompt')

  const draft = 'line one\nline two'
  await prompt.fill(draft)
  await prompt.evaluate((element, position) => {

    if (!(element instanceof HTMLTextAreaElement))
      throw new Error('Expected a textarea.')

    element.setSelectionRange(position, position)
  }, draft.length)
  await prompt.press('ArrowUp')

  await expect(prompt).toHaveValue(draft)
  const caretPosition = await prompt.evaluate((element) => {

    if (!(element instanceof HTMLTextAreaElement))
      throw new Error('Expected a textarea.')

    return element.selectionStart
  })
  expect(caretPosition).toBeLessThan(draft.length)
})

test('leaves modified ArrowUp events to the textarea', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })

  await sendPrompt(page, 'Message coder', 'stored prompt')

  await prompt.fill('')
  await prompt.press('Shift+ArrowUp')
  await expect(prompt).toHaveValue('')
})

test('does not intercept ArrowUp while composing with an IME', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  await sendPrompt(page, 'Message coder', 'stored prompt')

  await prompt.fill('')
  const prevented = await prompt.evaluate((element) => {
    element.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }))
    const event = new KeyboardEvent('keydown', {
      key: 'ArrowUp',
      bubbles: true,
      cancelable: true,
      isComposing: true,
    } as KeyboardEventInit)
    element.dispatchEvent(event)
    return event.defaultPrevented
  })
  expect(prevented).toBe(false)
  await expect(prompt).toHaveValue('')
})
