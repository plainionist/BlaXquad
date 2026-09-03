import { nextTick, readonly, ref } from 'vue'
import type { TranscriptIndex } from './TranscriptIndex'
import type { RowMeasurements } from './RowMeasurements'
import type { ScrollController } from './ScrollController'

export interface ScrollAnchor {
  entryIndex: number
  offset: number
}

export interface RestoreToken {
  anchor: ScrollAnchor
  version: number
}

export class ReadingAnchor {
  private myReadingAnchor?: ScrollAnchor
  private myLayoutAnchor?: ScrollAnchor
  private myRestorationVersion = 0
  private myLayoutRestorationVersion?: number
  private readonly myTopCompensation = ref(0)
  private readonly myBottomCompensation = ref(0)
  private readonly myFrames = new Map<number, () => void>()
  private readonly myIndex: TranscriptIndex
  private readonly myMeasurements: RowMeasurements
  private readonly myScroll: ScrollController
  private readonly myContentElement: () => HTMLElement | null
  private readonly myPlaceWindowAround: (entryIndex: number) => void
  private readonly myUpdateWindow: () => void
  private myDisposed = false

  readonly topCompensation = readonly(this.myTopCompensation)
  readonly bottomCompensation = readonly(this.myBottomCompensation)

  constructor(
    index: TranscriptIndex,
    measurements: RowMeasurements,
    scroll: ScrollController,
    contentElement: () => HTMLElement | null,
    placeWindowAround: (entryIndex: number) => void,
    updateWindow: () => void,
  ) {
    this.myIndex = index
    this.myMeasurements = measurements
    this.myScroll = scroll
    this.myContentElement = contentElement
    this.myPlaceWindowAround = placeWindowAround
    this.myUpdateWindow = updateWindow
  }

  get reading() {
    return this.myReadingAnchor
  }

  get layout() {
    return this.myLayoutAnchor
  }

  capture() {
    const viewport = this.myScroll.viewportGeometry.viewportElement()
    if (!viewport)
      return undefined
    const visible = this.myMeasurements.visibleRects(viewport)[0]
    if (visible)
      return { entryIndex: visible.entryIndex, offset: visible.top }
    if (this.myIndex.renderableCount === 0)
      return undefined
    const content = this.myContentElement()
    const contentTop = content
      ? content.getBoundingClientRect().top
        - viewport.getBoundingClientRect().top
        + viewport.scrollTop
      : 0
    const visibleTop = Math.max(0, viewport.scrollTop - contentTop)
    const position = this.myIndex.positionForRank(
      this.myIndex.rankAtOffset(visibleTop))
    return {
      entryIndex: this.myIndex.entryIdAt(position),
      offset: contentTop
        + this.myIndex.heightBefore(position)
        - viewport.scrollTop,
    }
  }

  resolve(anchor: ScrollAnchor | undefined) {
    if (!anchor)
      return undefined
    const entryIndex = this.myIndex.nearestRenderableEntry(anchor.entryIndex)
    return entryIndex == null
      ? undefined
      : { entryIndex, offset: anchor.offset }
  }

  retain(anchor: ScrollAnchor | undefined) {
    this.myReadingAnchor = anchor
  }

  clearReading() {
    this.myReadingAnchor = undefined
  }

  beginRestore(anchor: ScrollAnchor): RestoreToken {
    if (this.myLayoutAnchor?.entryIndex === anchor.entryIndex
      && this.myLayoutRestorationVersion != null
      && this.isActive(this.myLayoutRestorationVersion)) {
      return {
        anchor: this.myLayoutAnchor,
        version: this.myLayoutRestorationVersion,
      }
    }
    const version = ++this.myRestorationVersion
    this.myLayoutAnchor = anchor
    this.myLayoutRestorationVersion = version
    return { anchor, version }
  }

  cancel() {
    this.myRestorationVersion++
    this.myLayoutAnchor = undefined
    this.myLayoutRestorationVersion = undefined
  }

  releaseCompensation() {
    this.myTopCompensation.value = 0
    this.myBottomCompensation.value = 0
  }

  reduceCompensationBy(realHeightGrowth: number) {
    this.myBottomCompensation.value = Math.max(
      0,
      this.myBottomCompensation.value - realHeightGrowth)
  }

  async stabilize(token: RestoreToken, isCurrent: () => boolean) {
    await this.restoreAfterRender(token, isCurrent)
    if (!this.canContinue(token, isCurrent))
      return false
    await this.nextFrame()
    this.myScroll.reportGeometryChange()
    if (!this.canContinue(token, isCurrent))
      return false
    await this.nextFrame()
    this.myScroll.reportGeometryChange()
    if (!this.canContinue(token, isCurrent)
      || this.myLayoutAnchor !== token.anchor)
      return false
    this.myLayoutAnchor = undefined
    this.myLayoutRestorationVersion = undefined
    this.myReadingAnchor = token.anchor
    this.myUpdateWindow()
    await nextTick()
    this.myScroll.reportGeometryChange()
    if (!this.canContinue(token, isCurrent))
      return false
    await this.restore(token, isCurrent)
    return this.canContinue(token, isCurrent)
  }

  async restoreRetained(isCurrent: () => boolean) {
    const anchor = this.resolve(this.myReadingAnchor)
    if (!anchor)
      return false
    this.myPlaceWindowAround(anchor.entryIndex)
    return this.stabilize(this.beginRestore(anchor), isCurrent)
  }

  dispose() {
    this.myDisposed = true
    this.cancel()
    for (const [frame, resolve] of this.myFrames) {
      cancelAnimationFrame(frame)
      resolve()
    }
    this.myFrames.clear()
  }

  private async restoreAfterRender(
    token: RestoreToken,
    isCurrent: () => boolean,
  ) {
    await nextTick()
    this.myScroll.reportGeometryChange()
    if (!this.canContinue(token, isCurrent))
      return
    await this.nextFrame()
    this.myScroll.reportGeometryChange()
    if (!this.canContinue(token, isCurrent))
      return
    await this.restore(token, isCurrent)
    await this.nextFrame()
    this.myScroll.reportGeometryChange()
    if (!this.canContinue(token, isCurrent))
      return
    await this.restore(token, isCurrent)
  }

  private async restore(token: RestoreToken, isCurrent: () => boolean) {
    if (!this.canContinue(token, isCurrent))
      return
    const viewport = this.myScroll.viewportGeometry.viewportElement()
    const row = viewport
      ? this.myMeasurements.rectFor(token.anchor.entryIndex, viewport)
      : undefined
    if (!viewport || !row)
      return
    const desiredScrollTop = viewport.scrollTop + row.top - token.anchor.offset
    if (desiredScrollTop < 0) {
      this.myTopCompensation.value -= desiredScrollTop
      await nextTick()
      this.myScroll.reportGeometryChange()
    }
    else {
      const naturalScrollHeight =
        viewport.scrollHeight - this.myBottomCompensation.value
      const requiredCompensation = Math.max(
        0,
        desiredScrollTop + viewport.clientHeight - naturalScrollHeight)
      if (Math.abs(
        this.myBottomCompensation.value - requiredCompensation) >= 0.5) {
        this.myScroll.expectPosition(
          viewport.scrollTop
            + requiredCompensation
            - this.myBottomCompensation.value,
          'layout')
        this.myBottomCompensation.value = requiredCompensation
        await nextTick()
        this.myScroll.reportGeometryChange()
      }
    }
    if (!this.canContinue(token, isCurrent))
      return
    const renderedRow = this.myMeasurements.rectFor(
      token.anchor.entryIndex,
      viewport)
    if (!renderedRow)
      return
    this.myScroll.writePosition(
      viewport.scrollTop + renderedRow.top - token.anchor.offset)
    this.myReadingAnchor = token.anchor
  }

  private canContinue(token: RestoreToken, isCurrent: () => boolean) {
    return !this.myDisposed && isCurrent() && this.isActive(token.version)
  }

  private isActive(version: number) {
    return this.myRestorationVersion === version
  }

  private nextFrame() {
    if (this.myDisposed)
      return Promise.resolve()
    return new Promise<void>(resolve => {
      const frame = requestAnimationFrame(() => {
        this.myFrames.delete(frame)
        resolve()
      })
      this.myFrames.set(frame, resolve)
    })
  }
}
