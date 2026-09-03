import { expect, test } from '@playwright/test'
import {
  deliverHostMessages,
  loadSnapshot,
  protocolMessage,
  stateSnapshot,
} from './support/dashboardHarness'

test('keeps controls usable at standard desktop resolution', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await loadSnapshot(page)

  const prompt = page.getByRole('textbox', { name: 'Message coder' })
  const reviewerPrompt = page.getByRole('textbox', { name: 'Message reviewer' })
  await reviewerPrompt.fill('Preserve this role draft.')
  await prompt.fill('  Please continue.  ')
  await prompt.locator('..').getByRole('button', { name: 'Send', exact: true }).click()
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))).toEqual({
    version: 3,
    type: 'prompt.send',
    role: 'coder',
    payload: {
      prompt: 'Please continue.',
    },
  })
  await expect(prompt).toHaveValue('')
  await expect(reviewerPrompt).toHaveValue('Preserve this role draft.')
  await page.getByRole('button', { name: 'Approve' }).click()
  await expect.poll(() => page.evaluate(() => window.__blaxquadHarness?.messages.at(-1))).toContain('permission.respond')
})

test('sends prompts on exact Enter but not modified Enter', async ({ page }) => {
  await loadSnapshot(page)
  const prompt = page.getByRole('textbox', { name: 'Message reviewer' })
  const initialMessageCount = await page.evaluate(() =>
    window.__blaxquadHarness!.messages.length)

  await prompt.fill('Review this')
  await prompt.press('Shift+Enter')
  expect(await page.evaluate(() =>
    window.__blaxquadHarness!.messages.length)).toBe(initialMessageCount)

  await prompt.press('Enter')
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))).toEqual({
    version: 3,
    type: 'prompt.send',
    role: 'reviewer',
    payload: {
      prompt: 'Review this',
    },
  })
})

test('sends binary permission and cancellation commands', async ({ page }) => {
  await loadSnapshot(page)

  await page.getByRole('button', { name: 'Reject' }).click()
  await expect.poll(() => page.evaluate(() => window.__blaxquadHarness?.messages.at(-1))).toContain('"approved":false')
  await page.getByTitle('Cancel current response').first().click()
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))).toEqual({
    version: 3,
    type: 'role.abort',
    role: 'coder',
  })
})

test('aborts the role focused at the second Escape key event', async ({ page }) => {
  await loadSnapshot(page)
  const initialMessageCount = await page.evaluate(() =>
    window.__blaxquadHarness!.messages.length)

  await page.getByRole('textbox', { name: 'Message coder' }).focus()
  const firstPrevented = await page.evaluate(() => {
    const event = new KeyboardEvent('keydown', {
      key: 'Escape',
      bubbles: true,
      cancelable: true,
    })
    document.activeElement!.dispatchEvent(event)
    return event.defaultPrevented
  })
  expect(firstPrevented).toBe(false)
  expect(await page.evaluate(() =>
    window.__blaxquadHarness!.messages.length)).toBe(initialMessageCount)

  await page.getByRole('textbox', { name: 'Message reviewer' }).focus()
  const secondPrevented = await page.evaluate(() => {
    const event = new KeyboardEvent('keydown', {
      key: 'Escape',
      bubbles: true,
      cancelable: true,
    })
    document.activeElement!.dispatchEvent(event)
    return event.defaultPrevented
  })
  expect(secondPrevented).toBe(true)
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))).toEqual({
    version: 3,
    type: 'role.abort',
    role: 'reviewer',
  })
})

test('projects interleaved requests to their owning roles in source order', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('state.snapshot', {
      payload: {
        ...stateSnapshot,
        permissions: [
          { requestId: 'review-1', role: 'reviewer', description: 'Review permission one.' },
          { requestId: 'code-1', role: 'coder', description: 'Coder permission.' },
          { requestId: 'review-2', role: 'reviewer', description: 'Review permission two.' },
        ],
        inputs: [
          { requestId: 'code-input-1', role: 'coder', prompt: 'Coder input one.', allowFreeform: false },
          { requestId: 'review-input', role: 'reviewer', prompt: 'Reviewer input.', allowFreeform: false },
          { requestId: 'code-input-2', role: 'coder', prompt: 'Coder input two.', allowFreeform: false },
        ],
        elicitations: [
          { requestId: 'review-form', role: 'reviewer', prompt: 'Reviewer form.', mode: 'form' },
          { requestId: 'code-form', role: 'coder', prompt: 'Coder form.', mode: 'form' },
        ],
      },
    }),
  ])

  const panelFor = (role: string) => page.locator('.role-panel')
    .filter({ has: page.getByRole('heading', { name: role, exact: true }) })
  await expect(panelFor('coder').locator('.interaction p')).toHaveText([
    'Coder permission.',
    'Coder input one.',
    'Coder input two.',
    'Coder form.',
  ])
  await expect(panelFor('reviewer').locator('.interaction p')).toHaveText([
    'Review permission one.',
    'Review permission two.',
    'Reviewer input.',
    'Reviewer form.',
  ])
  await expect(panelFor('writer').locator('.interaction')).toHaveCount(0)
})

test('sends fixed-choice and free-form input response envelopes', async ({ page }) => {
  await loadSnapshot(page)

  await page.getByRole('button', { name: 'Quick' }).click()
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))).toEqual({
    version: 3,
    type: 'input.respond',
    role: 'reviewer',
    requestId: 'input-1',
    payload: {
      answer: 'Quick',
      wasFreeform: false,
    },
  })

  const freeform = page.getByLabel('Response for reviewer')
  await freeform.fill('Keep this draft')
  await deliverHostMessages(page, [
    protocolMessage('state.snapshot', { payload: stateSnapshot }),
  ])
  await expect(freeform).toHaveValue('Keep this draft')
  await freeform.locator('..').getByRole('button', { name: 'Send' }).click()
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))).toEqual({
    version: 3,
    type: 'input.respond',
    role: 'reviewer',
    requestId: 'input-1',
    payload: {
      answer: 'Keep this draft',
      wasFreeform: true,
    },
  })
})

test('preserves IME composition and same-tick free-form submission', async ({ page }) => {
  await loadSnapshot(page)
  await deliverHostMessages(page, [
    protocolMessage('state.snapshot', {
      payload: {
        ...stateSnapshot,
        inputs: [
          ...stateSnapshot.inputs,
          {
            ...stateSnapshot.inputs[0],
            role: 'coder',
          },
        ],
      },
    }),
  ])
  const reviewerPanel = page.locator('.role-panel')
    .filter({ has: page.getByRole('heading', { name: 'reviewer', exact: true }) })
  const coderPanel = page.locator('.role-panel')
    .filter({ has: page.getByRole('heading', { name: 'coder', exact: true }) })
  const freeform = reviewerPanel.getByPlaceholder('Type a response')
  const sharedRequestFreeform = coderPanel.getByPlaceholder('Type a response')

  await freeform.evaluate((element) => {
    if (!(element instanceof HTMLInputElement) || !element.form)
      throw new Error('Expected a free-form input inside a form.')
    element.dispatchEvent(new CompositionEvent('compositionstart', {
      bubbles: true,
    }))
    element.value = 'intermediate'
    element.dispatchEvent(new InputEvent('input', {
      bubbles: true,
      data: 'intermediate',
      inputType: 'insertCompositionText',
      isComposing: true,
    }))
    element.form.dispatchEvent(new SubmitEvent('submit', {
      bubbles: true,
      cancelable: true,
    }))
  })
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!).payload)).toEqual({
    answer: '',
    wasFreeform: true,
  })

  await freeform.evaluate((element) => {
    if (!(element instanceof HTMLInputElement) || !element.form)
      throw new Error('Expected a free-form input inside a form.')
    element.value = 'committed'
    element.dispatchEvent(new CompositionEvent('compositionend', {
      bubbles: true,
      data: 'committed',
    }))
    element.form.dispatchEvent(new SubmitEvent('submit', {
      bubbles: true,
      cancelable: true,
    }))
  })
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!).payload)).toEqual({
    answer: 'committed',
    wasFreeform: true,
  })

  await freeform.evaluate((element, otherInput) => {
    if (!(element instanceof HTMLInputElement)
      || !(otherInput instanceof HTMLInputElement)
      || !otherInput.form)
      throw new Error('Expected free-form inputs inside forms.')
    element.value = 'shared same tick'
    element.dispatchEvent(new InputEvent('input', {
      bubbles: true,
      data: 'shared same tick',
      inputType: 'insertText',
    }))
    otherInput.form.dispatchEvent(new SubmitEvent('submit', {
      bubbles: true,
      cancelable: true,
    }))
  }, await sharedRequestFreeform.elementHandle())
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))).toEqual({
    version: 3,
    type: 'input.respond',
    role: 'coder',
    requestId: 'input-1',
    payload: {
      answer: 'shared same tick',
      wasFreeform: true,
    },
  })
})

test('submits schema-driven elicitation values as an object', async ({ page }) => {
  await loadSnapshot(page)

  await expect(page.getByLabel('Release name *')).toHaveValue('August')
  await page.getByLabel('Build count *').fill('4')
  await expect(page.getByLabel('Publish now')).toBeChecked()
  await page.getByLabel('Release mode *').selectOption('fast')
  await page.getByLabel('Tags').selectOption(['docs', 'runtime'])
  await deliverHostMessages(page, [
    protocolMessage('state.snapshot', { payload: stateSnapshot }),
  ])
  await expect(page.getByLabel('Release name *')).toHaveValue('August')
  await expect(page.getByLabel('Build count *')).toHaveValue('4')
  await expect(page.getByLabel('Publish now')).toBeChecked()
  await expect(page.getByLabel('Release mode *')).toHaveValue('fast')
  await expect(page.getByLabel('Tags')).toHaveValues(['docs', 'runtime'])
  await page.getByRole('button', { name: 'Submit' }).click()

  const message = await page.evaluate(() => JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))
  expect(message).toMatchObject({
    type: 'elicitation.respond',
    requestId: 'elicitation-1',
    payload: {
      action: 'accept',
      content: { name: 'August', count: 4, enabled: true, mode: 'fast', tags: ['docs', 'runtime'] },
    },
  })

  await page.getByRole('button', { name: 'Decline' }).click()
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))).toEqual({
    version: 3,
    type: 'elicitation.respond',
    role: 'writer',
    requestId: 'elicitation-1',
    payload: {
      action: 'decline',
    },
  })
})

test('requires explicit URL consent without webview navigation', async ({ page }) => {
  await loadSnapshot(page)

  const interaction = page.locator('.interaction').filter({ hasText: 'Complete sign-in.' })
  await expect(interaction.getByText('example.test', { exact: true })).toBeVisible()
  await expect(interaction.getByText('https://example.test/authorize?client=blaxquad', { exact: true })).toBeVisible()
  await expect(interaction.locator('a')).toHaveCount(0)
  await interaction.getByRole('button', { name: 'Open requested page' }).click()
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))).toEqual({
    version: 3,
    type: 'elicitation.respond',
    role: 'writer',
    requestId: 'elicitation-url',
    payload: {
      action: 'accept',
    },
  })

  await interaction.getByRole('button', { name: 'Cancel request' }).click()
  expect(await page.evaluate(() =>
    JSON.parse(window.__blaxquadHarness!.messages.at(-1)!))).toEqual({
    version: 3,
    type: 'elicitation.respond',
    role: 'writer',
    requestId: 'elicitation-url',
    payload: {
      action: 'cancel',
    },
  })
})
