import { readonly, ref } from 'vue'
import type { ScrollAnchor } from './ReadingAnchor'

export type ScrollOrigin =
  'self' | 'layout' | 'resize' | 'external' | 'pending' | 'superseded'

export type InternalScrollOrigin = 'self' | 'layout' | 'resize'

export interface ClassifiedScroll {
  origin: ScrollOrigin
  position: number
  chainId?: number
  externalGeneration?: number
}

export interface LayoutTransaction {
  generation: number
  anchor: ScrollAnchor
  scrollTop: number
  maximum: number
}

interface ExpectedPosition {
  generation: number
  target: number
  origin: InternalScrollOrigin
  timeout: number
}

interface PendingClassification {
  position: number
  maximum: number
  chainId: number
  origin?: InternalScrollOrigin
}

interface ClassificationChain {
  id: number
  members: Set<number>
  internalOrigin?: InternalScrollOrigin
  external?: {
    generation: number
    position: number
  }
}

export class ScrollIntentClassifier {
  private myGeneration = 0
  private myClassificationGeneration = 0
  private myChainGeneration = 0
  private myActiveClassificationChain?: ClassificationChain
  private readonly myExpectedPositions: ExpectedPosition[] = []
  private readonly myPendingClassifications =
    new Map<number, PendingClassification>()
  private readonly myLayoutTransactions: LayoutTransaction[] = []
  private readonly myPendingCount = ref(0)
  private readonly myAnimationFrames = new Set<number>()
  private readonly myClassificationFrames = new Map<number, () => void>()
  private readonly myPosition: () => number | undefined
  private readonly myMaximum: () => number
  private readonly myRecordClassification: (
    origin: ScrollOrigin,
    position: number,
  ) => ClassifiedScroll
  private myDisposed = false

  readonly pendingPositionCount = readonly(this.myPendingCount)

  constructor(
    position: () => number | undefined,
    maximum: () => number,
    recordClassification: (
      origin: ScrollOrigin,
      position: number,
    ) => ClassifiedScroll,
  ) {
    this.myPosition = position
    this.myMaximum = maximum
    this.myRecordClassification = recordClassification
  }

  async classifyScroll(
    trusted: boolean,
    onUnclassifiedMovement?: (
      position: number,
      generation: number,
      chainId: number,
      chainStarted: boolean,
    ) => void,
  ): Promise<ClassifiedScroll> {
    const position = this.myPosition() ?? 0
    const generation = ++this.myClassificationGeneration
    if (!trusted) {
      this.clearExpectedPositions()
      const { chain, started } = this.classificationChain()
      chain.members.add(generation)
      onUnclassifiedMovement?.(position, generation, chain.id, started)
      this.myRecordClassification('external', position)
      const outcome = this.completeChainMember(
        chain,
        generation,
        'external',
        position)
      this.addExpectedPosition(position, 'self')
      return outcome
    }
    const classified = this.classifyKnownMovement(position)
    if (classified) {
      const chain = this.myActiveClassificationChain
      if (!chain)
        return classified
      chain.internalOrigin = classified.origin as InternalScrollOrigin
      return { origin: 'pending', position, chainId: chain.id }
    }

    const { chain, started } = this.classificationChain()
    chain.members.add(generation)
    const pending: PendingClassification = {
      position,
      maximum: this.myMaximum(),
      chainId: chain.id,
    }
    this.myPendingClassifications.set(generation, pending)
    onUnclassifiedMovement?.(
      position,
      generation,
      chain.id,
      started)
    await this.nextClassificationFrame()
    if (this.myDisposed) {
      this.myPendingClassifications.delete(generation)
      return { origin: 'superseded', position, chainId: chain.id }
    }
    await this.nextClassificationFrame()
    const currentPosition = this.myPosition()
    if (this.myDisposed || currentPosition == null) {
      this.myPendingClassifications.delete(generation)
      return { origin: 'superseded', position, chainId: chain.id }
    }

    const superseded = generation !== this.myClassificationGeneration
      || Math.abs(currentPosition - position) >= 0.5
    this.myPendingClassifications.delete(generation)
    let origin: InternalScrollOrigin | 'external'
    if (pending.origin) {
      origin = pending.origin
      if (!superseded)
        this.myRecordClassification(origin, position)
    }
    else {
      origin = 'external'
      if (!superseded)
        this.myRecordClassification(origin, position)
    }
    return this.completeChainMember(
      chain,
      generation,
      origin,
      position)
  }

  expectPosition(
    target: number,
    origin: InternalScrollOrigin = 'layout',
  ) {
    if (this.resolvePendingClassification(target, origin))
      return
    this.addExpectedPosition(target, origin)
  }

  beginLayoutTransaction(anchor: ScrollAnchor) {
    const position = this.myPosition()
    if (position == null)
      return undefined
    const transaction = {
      generation: ++this.myGeneration,
      anchor,
      scrollTop: position,
      maximum: this.myMaximum(),
    }
    this.myLayoutTransactions.push(transaction)
    return transaction
  }

  wasClamped(transaction: LayoutTransaction) {
    const position = this.myPosition()
    if (position == null)
      return false
    const maximum = this.myMaximum()
    return transaction.scrollTop > maximum + 0.5
      && Math.abs(position - maximum) < 0.5
  }

  finishLayoutTransaction(transaction: LayoutTransaction) {
    if (this.myDisposed) {
      this.removeLayoutTransaction(transaction.generation)
      return
    }
    const first = requestAnimationFrame(() => {
      this.myAnimationFrames.delete(first)
      if (this.myDisposed)
        return
      const second = requestAnimationFrame(() => {
        this.myAnimationFrames.delete(second)
        this.removeLayoutTransaction(transaction.generation)
      })
      this.myAnimationFrames.add(second)
    })
    this.myAnimationFrames.add(first)
  }

  resolveGeometryClamp(
    maximum: number,
    previousMaximum: number,
    origin: InternalScrollOrigin,
  ) {
    let currentPositionResolved = false
    for (const pending of this.myPendingClassifications.values()) {
      const isProgressiveClamp = pending.origin == null
        && Math.abs(pending.position - pending.maximum) < 0.5
        && pending.maximum < previousMaximum - 0.5
        && pending.maximum >= maximum - 0.5
      if (!isProgressiveClamp)
        continue
      pending.origin = origin
      if (Math.abs(pending.position - maximum) < 0.5)
        currentPositionResolved = true
    }
    return currentPositionResolved
  }

  dispose() {
    this.myDisposed = true
    this.myClassificationGeneration++
    this.clearExpectedPositions()
    this.myPendingClassifications.clear()
    this.myActiveClassificationChain = undefined
    this.myLayoutTransactions.length = 0
    for (const frame of this.myAnimationFrames)
      cancelAnimationFrame(frame)
    this.myAnimationFrames.clear()
    for (const [frame, resolve] of this.myClassificationFrames) {
      cancelAnimationFrame(frame)
      resolve()
    }
    this.myClassificationFrames.clear()
  }

  private classifyKnownMovement(position: number) {
    const expected = this.consumeExpectedPosition(position)
    if (expected)
      return this.myRecordClassification(expected.origin, position)
    if (this.consumeLayoutClamp())
      return this.myRecordClassification('layout', position)
    return undefined
  }

  private addExpectedPosition(
    target: number,
    origin: InternalScrollOrigin,
  ) {
    const generation = ++this.myGeneration
    const timeout = window.setTimeout(
      () => this.removeExpectedPosition(generation),
      500)
    this.myExpectedPositions.push({ generation, target, origin, timeout })
    this.myPendingCount.value = this.myExpectedPositions.length
  }

  private nextClassificationFrame() {
    if (this.myDisposed)
      return Promise.resolve()
    return new Promise<void>(resolve => {
      const frame = requestAnimationFrame(() => {
        this.myClassificationFrames.delete(frame)
        resolve()
      })
      this.myClassificationFrames.set(frame, resolve)
    })
  }

  private consumeExpectedPosition(position: number) {
    for (let index = this.myExpectedPositions.length - 1; index >= 0; index--) {
      const expected = this.myExpectedPositions[index]
      if (Math.abs(expected.target - position) < 0.5) {
        const consumed = this.myExpectedPositions.splice(0, index + 1)
        for (const candidate of consumed)
          window.clearTimeout(candidate.timeout)
        this.myPendingCount.value = this.myExpectedPositions.length
        return expected
      }
    }
    this.clearExpectedPositions()
    return undefined
  }

  private resolvePendingClassification(
    target: number,
    origin: InternalScrollOrigin,
  ) {
    const pending = this.myPendingClassifications.get(
      this.myClassificationGeneration)
    if (pending?.origin != null
      || pending == null
      || Math.abs(pending.position - target) >= 0.5)
      return false
    pending.origin = origin
    return true
  }

  private classificationChain() {
    if (this.myActiveClassificationChain) {
      return {
        chain: this.myActiveClassificationChain,
        started: false,
      }
    }
    const chain: ClassificationChain = {
      id: ++this.myChainGeneration,
      members: new Set<number>(),
    }
    this.myActiveClassificationChain = chain
    return { chain, started: true }
  }

  private completeChainMember(
    chain: ClassificationChain,
    generation: number,
    origin: InternalScrollOrigin | 'external',
    position: number,
  ): ClassifiedScroll {
    chain.members.delete(generation)
    if (origin === 'external') {
      if (!chain.external || generation > chain.external.generation)
        chain.external = { generation, position }
    }
    else {
      chain.internalOrigin = origin
    }
    if (chain.members.size > 0) {
      return { origin: 'pending', position, chainId: chain.id }
    }
    if (this.myActiveClassificationChain === chain)
      this.myActiveClassificationChain = undefined
    if (chain.external) {
      return {
        origin: 'external',
        position: chain.external.position,
        chainId: chain.id,
        externalGeneration: chain.external.generation,
      }
    }
    return {
      origin: chain.internalOrigin ?? 'layout',
      position,
      chainId: chain.id,
    }
  }

  private consumeLayoutClamp() {
    const position = this.myPosition()
    if (position == null)
      return false
    const maximum = this.myMaximum()
    const match = this.myLayoutTransactions.findIndex(transaction =>
      maximum < transaction.maximum - 0.5
      && transaction.scrollTop > maximum + 0.5
      && Math.abs(position - maximum) < 0.5)
    if (match < 0)
      return false
    this.myLayoutTransactions.splice(0, match + 1)
    return true
  }

  private removeExpectedPosition(generation: number) {
    const index = this.myExpectedPositions.findIndex(
      expected => expected.generation === generation)
    if (index >= 0)
      this.myExpectedPositions.splice(index, 1)
    this.myPendingCount.value = this.myExpectedPositions.length
  }

  private removeLayoutTransaction(generation: number) {
    const index = this.myLayoutTransactions.findIndex(
      candidate => candidate.generation === generation)
    if (index >= 0)
      this.myLayoutTransactions.splice(index, 1)
  }

  private clearExpectedPositions() {
    for (const expected of this.myExpectedPositions)
      window.clearTimeout(expected.timeout)
    this.myExpectedPositions.length = 0
    this.myPendingCount.value = 0
  }
}
