export interface ViewportGeometry {
  scrollTop: number
  scrollHeight: number
  clientHeight: number
  clientWidth: number
}

export interface ViewportSizeChange {
  widthChanged: boolean
  heightChanged: boolean
  previousWidth: number
  previousHeight: number
}

export class ViewportGeometryObserver {
  private myViewport?: HTMLElement
  private myResizeObserver?: ResizeObserver
  private myMeasuredWidth = 0
  private myMeasuredHeight = 0

  attach(viewport: HTMLElement) {
    this.myViewport = viewport
    this.myMeasuredWidth = viewport.clientWidth
    this.myMeasuredHeight = viewport.clientHeight
  }

  observe(onChange: (change: ViewportSizeChange) => void) {
    this.myResizeObserver = new ResizeObserver(changes => {
      const element = this.myViewport
      if (!element)
        return
      const previousWidth = this.myMeasuredWidth
      const previousHeight = this.myMeasuredHeight
      const width = element.clientWidth ?? changes[0]?.contentRect.width ?? 0
      const height = element.clientHeight ?? changes[0]?.contentRect.height ?? 0
      const widthChanged =
        previousWidth > 0 && Math.abs(width - previousWidth) >= 1
      const heightChanged =
        previousHeight > 0 && Math.abs(height - previousHeight) >= 1
      this.myMeasuredWidth = width
      this.myMeasuredHeight = height
      if (widthChanged || heightChanged) {
        onChange({
          widthChanged,
          heightChanged,
          previousWidth,
          previousHeight,
        })
      }
    })
    const viewport = this.myViewport
    if (viewport)
      this.myResizeObserver.observe(viewport)
  }

  geometry(): ViewportGeometry {
    const element = this.myViewport
    return {
      scrollTop: element?.scrollTop ?? 0,
      scrollHeight: element?.scrollHeight ?? 0,
      clientHeight: element?.clientHeight ?? 0,
      clientWidth: element?.clientWidth ?? 0,
    }
  }

  visibleContentRange(content: HTMLElement | null) {
    const element = this.myViewport
    if (!element)
      return { visibleTop: 0, visibleBottom: 0 }
    const contentTop = content
      ? content.getBoundingClientRect().top
        - element.getBoundingClientRect().top
        + element.scrollTop
      : 0
    const visibleTop = Math.max(0, element.scrollTop - contentTop)
    return {
      visibleTop,
      visibleBottom: visibleTop + element.clientHeight,
    }
  }

  naturalMaximum() {
    const element = this.myViewport
    return element
      ? Math.max(0, element.scrollHeight - element.clientHeight)
      : 0
  }

  realContentMaximum(compensation: number) {
    const element = this.myViewport
    return element
      ? Math.max(0, element.scrollHeight - compensation - element.clientHeight)
      : 0
  }

  viewportElement() {
    return this.myViewport
  }

  dispose() {
    this.myResizeObserver?.disconnect()
    this.myResizeObserver = undefined
    this.myViewport = undefined
  }
}
