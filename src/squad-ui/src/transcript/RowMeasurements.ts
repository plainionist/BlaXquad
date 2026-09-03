import { readonly, ref } from 'vue'

export interface GeometryChange {
  entryIndex: number
  height: number
}

export interface GeometryBatch {
  revision: number
  changes: GeometryChange[]
  auxiliaryChanged: boolean
}

export interface RelativeRowRect {
  entryIndex: number
  top: number
  bottom: number
}

export class RowMeasurements {
  private readonly myElements = new Map<number, HTMLElement>()
  private myElementEntryIndices = new WeakMap<Element, number>()
  private readonly myAuxiliaryElements = new Map<string, HTMLElement>()
  private readonly myElementAuxiliaryKeys = new WeakMap<Element, string>()
  private readonly myHeights = new Map<number, number>()
  private readonly myAuxiliaryHeights = new Map<string, number>()
  private readonly myInvalidatedEntries = new Set<number>()
  private readonly myCount = ref(0)
  private readonly myObserver: ResizeObserver
  private myRevision = 0
  private myDisposed = false
  private myOnGeometryBatch?: (batch: GeometryBatch) => void

  readonly count = readonly(this.myCount)

  constructor() {
    this.myObserver = new ResizeObserver(changes => {
      const measurements: GeometryChange[] = []
      let auxiliaryChanged = false
      for (const change of changes) {
        const entryIndex = this.myElementEntryIndices.get(change.target)
        const height =
          change.borderBoxSize[0]?.blockSize ?? change.contentRect.height
        if (entryIndex != null
          && !this.myInvalidatedEntries.has(entryIndex)
          && this.remember(entryIndex, height))
          measurements.push({ entryIndex, height })
        const auxiliaryKey = this.myElementAuxiliaryKeys.get(change.target)
        if (auxiliaryKey != null
          && this.rememberAuxiliary(auxiliaryKey, height))
          auxiliaryChanged = true
      }
      this.publish(measurements, auxiliaryChanged)
    })
  }

  setGeometryBatchCallback(callback: (batch: GeometryBatch) => void) {
    this.myOnGeometryBatch = callback
  }

  observe(entryIndex: number, element: Element | null) {
    const previous = this.myElements.get(entryIndex)
    if (previous && previous !== element)
      this.myObserver.unobserve(previous)
    if (element instanceof HTMLElement) {
      this.myInvalidatedEntries.delete(entryIndex)
      this.myElements.set(entryIndex, element)
      this.myElementEntryIndices.set(element, entryIndex)
      this.myObserver.observe(element)
    }
    else {
      this.myElements.delete(entryIndex)
      this.myInvalidatedEntries.delete(entryIndex)
    }
  }

  observeAuxiliary(key: string, element: Element | null) {
    const previous = this.myAuxiliaryElements.get(key)
    if (previous && previous !== element)
      this.myObserver.unobserve(previous)
    if (element instanceof HTMLElement) {
      this.myAuxiliaryElements.set(key, element)
      this.myElementAuxiliaryKeys.set(element, key)
      this.myObserver.observe(element)
      this.publish(
        [],
        this.rememberAuxiliary(key, element.getBoundingClientRect().height))
    }
    else {
      this.myAuxiliaryElements.delete(key)
      if (this.myAuxiliaryHeights.delete(key))
        queueMicrotask(() => this.publish([], true))
    }
  }

  heightFor(entryIndex: number) {
    return this.myHeights.get(entryIndex)
  }

  invalidate(entryIndex: number) {
    if (!this.myHeights.delete(entryIndex))
      return
    this.myCount.value = this.myHeights.size
  }

  clear() {
    if (this.myHeights.size === 0)
      return
    this.myHeights.clear()
    for (const entryIndex of this.myElements.keys())
      this.myInvalidatedEntries.add(entryIndex)
    this.myCount.value = 0
  }

  resetRows() {
    for (const element of this.myElements.values())
      this.myObserver.unobserve(element)
    this.myElements.clear()
    this.myElementEntryIndices = new WeakMap<Element, number>()
    this.myHeights.clear()
    this.myInvalidatedEntries.clear()
    this.myCount.value = 0
  }

  sweep() {
    const measurements: GeometryChange[] = []
    let auxiliaryChanged = false
    for (const [entryIndex, element] of this.myElements) {
      this.myInvalidatedEntries.delete(entryIndex)
      const height = element.getBoundingClientRect().height
      if (this.remember(entryIndex, height))
        measurements.push({ entryIndex, height })
    }
    for (const [key, element] of this.myAuxiliaryElements)
      if (this.rememberAuxiliary(key, element.getBoundingClientRect().height))
        auxiliaryChanged = true
    this.publish(measurements, auxiliaryChanged)
    return measurements.length > 0 || auxiliaryChanged
  }

  rectFor(entryIndex: number, viewport: HTMLElement) {
    const element = this.myElements.get(entryIndex)
    if (!element)
      return undefined
    const viewportTop = viewport.getBoundingClientRect().top
    const rect = element.getBoundingClientRect()
    return {
      entryIndex,
      top: rect.top - viewportTop,
      bottom: rect.bottom - viewportTop,
    }
  }

  visibleRects(viewport: HTMLElement) {
    const viewportTop = viewport.getBoundingClientRect().top
    const rows: RelativeRowRect[] = []
    for (const [entryIndex, element] of this.myElements) {
      const rect = element.getBoundingClientRect()
      const row = {
        entryIndex,
        top: rect.top - viewportTop,
        bottom: rect.bottom - viewportTop,
      }
      if (row.bottom >= 0 && row.top <= viewport.clientHeight)
        rows.push(row)
    }
    return rows.sort((left, right) => left.top - right.top)
  }

  dispose() {
    this.myDisposed = true
    this.myOnGeometryBatch = undefined
    this.myObserver.disconnect()
    this.myElements.clear()
    this.myAuxiliaryElements.clear()
    this.myHeights.clear()
    this.myAuxiliaryHeights.clear()
    this.myInvalidatedEntries.clear()
    this.myCount.value = 0
  }

  private remember(entryIndex: number, height: number) {
    if (this.myHeights.get(entryIndex) === height)
      return false
    this.myHeights.set(entryIndex, height)
    this.myCount.value = this.myHeights.size
    return true
  }

  private rememberAuxiliary(key: string, height: number) {
    if (this.myAuxiliaryHeights.get(key) === height)
      return false
    this.myAuxiliaryHeights.set(key, height)
    return true
  }

  private publish(changes: GeometryChange[], auxiliaryChanged = false) {
    if (this.myDisposed || (changes.length === 0 && !auxiliaryChanged))
      return
    this.myOnGeometryBatch?.({
      revision: ++this.myRevision,
      changes,
      auxiliaryChanged,
    })
  }
}
