import type { ScrollAnchor } from './ReadingAnchor'
import {
  ScrollIntentClassifier,
  type ClassifiedScroll,
  type InternalScrollOrigin,
  type LayoutTransaction,
  type ScrollOrigin,
} from './ScrollIntentClassifier'
import {
  ViewportGeometryObserver,
  type ViewportGeometry,
} from './ViewportGeometryObserver'

export type {
  ClassifiedScroll,
  LayoutTransaction,
  ScrollOrigin,
} from './ScrollIntentClassifier'

export interface ScrollPositionGuard {
  position: number
  lastClassifiedPosition: number
}

export interface ViewportChange {
  widthChanged: boolean
  heightChanged: boolean
  clamped: boolean
  previousWidth: number
  previousHeight: number
  geometry: ViewportGeometry
}

export class ScrollController {
  private myLastScrollTop = 0
  private myObservedMaximum = 0
  private readonly myIntentClassifier: ScrollIntentClassifier
  private myDisposed = false

  readonly viewportGeometry = new ViewportGeometryObserver()

  constructor() {
    this.myIntentClassifier = new ScrollIntentClassifier(
      () => this.viewportGeometry.viewportElement()?.scrollTop,
      () => this.viewportGeometry.naturalMaximum(),
      (origin, position) => this.recordClassification(origin, position),
    )
  }

  get pendingPositionCount() {
    return this.myIntentClassifier.pendingPositionCount
  }

  attach(
    viewport: HTMLElement,
    onViewportChange: (change: ViewportChange) => void,
  ) {
    this.viewportGeometry.attach(viewport)
    this.myLastScrollTop = viewport.scrollTop
    this.myObservedMaximum = this.viewportGeometry.naturalMaximum()
    this.viewportGeometry.observe(change => {
      if (this.myDisposed)
        return
      const clamped = this.reportGeometryChange('resize')
      onViewportChange({
        ...change,
        clamped,
        geometry: this.viewportGeometry.geometry(),
      })
    })
  }

  classifyScroll(
    trusted: boolean,
    onUnclassifiedMovement?: (
      position: number,
      generation: number,
      chainId: number,
      chainStarted: boolean,
    ) => void,
  ): Promise<ClassifiedScroll> {
    return this.myIntentClassifier.classifyScroll(
      trusted,
      onUnclassifiedMovement)
  }

  writePosition(target: number) {
    const element = this.viewportGeometry.viewportElement()
    if (!element)
      return
    const maximum = this.viewportGeometry.naturalMaximum()
    const clampedTarget = Math.max(0, Math.min(target, maximum))
    if (Math.abs(element.scrollTop - clampedTarget) < 0.5)
      return
    element.scrollTop = clampedTarget
    this.expectPosition(element.scrollTop, 'self')
    this.myLastScrollTop = element.scrollTop
  }

  positionGuard(): ScrollPositionGuard {
    return {
      position: this.viewportGeometry.viewportElement()?.scrollTop ?? 0,
      lastClassifiedPosition: this.myLastScrollTop,
    }
  }

  async scrollToTail(
    guard: ScrollPositionGuard,
    isCurrent: () => boolean,
  ) {
    await Promise.resolve()
    const element = this.viewportGeometry.viewportElement()
    if (!isCurrent()
      || !element
      || Math.abs(
        guard.position - guard.lastClassifiedPosition) >= 0.5
      || Math.abs(element.scrollTop - guard.position) >= 0.5)
      return
    this.writePosition(element.scrollHeight)
  }

  expectPosition(
    target: number,
    origin: InternalScrollOrigin = 'layout',
  ) {
    this.myIntentClassifier.expectPosition(target, origin)
  }

  beginLayoutTransaction(anchor: ScrollAnchor) {
    return this.myIntentClassifier.beginLayoutTransaction(anchor)
  }

  wasClamped(transaction: LayoutTransaction) {
    return this.myIntentClassifier.wasClamped(transaction)
  }

  finishLayoutTransaction(transaction: LayoutTransaction) {
    this.myIntentClassifier.finishLayoutTransaction(transaction)
  }

  reportGeometryChange(origin: 'layout' | 'resize' = 'layout') {
    const element = this.viewportGeometry.viewportElement()
    if (!element)
      return false
    const previousMaximum = this.myObservedMaximum
    const maximum = this.viewportGeometry.naturalMaximum()
    const position = element.scrollTop
    const clamped = maximum < this.myObservedMaximum - 0.5
      && this.myLastScrollTop > maximum + 0.5
      && Math.abs(position - maximum) < 0.5
    if (clamped) {
      const currentPositionResolved =
        this.myIntentClassifier.resolveGeometryClamp(
          maximum,
          previousMaximum,
          origin)
      if (!currentPositionResolved)
        this.expectPosition(position, origin)
    }
    this.myObservedMaximum = maximum
    return clamped
  }

  dispose() {
    this.myDisposed = true
    this.viewportGeometry.dispose()
    this.myIntentClassifier.dispose()
  }

  private recordClassification(
    origin: ScrollOrigin,
    position: number,
  ): ClassifiedScroll {
    this.myLastScrollTop = position
    return { origin, position }
  }
}
