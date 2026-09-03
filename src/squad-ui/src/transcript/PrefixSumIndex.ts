export class PrefixSumIndex {
  private myTree: number[] = [0]

  get length() {
    return this.myTree.length - 1
  }

  rebuild(values: readonly number[]) {
    this.myTree = new Array(values.length + 1).fill(0)
    for (let index = 0; index < values.length; index++)
      this.addDelta(index, values[index])
  }

  append(value: number) {
    const index = this.length
    const treeIndex = index + 1
    const rangeStart = treeIndex - (treeIndex & -treeIndex)
    this.myTree.push(
      value + this.prefixSum(index) - this.prefixSum(rangeStart))
  }

  addDelta(index: number, delta: number) {
    for (
      let treeIndex = index + 1;
      treeIndex < this.myTree.length;
      treeIndex += treeIndex & -treeIndex
    )
      this.myTree[treeIndex] += delta
  }

  prefixSum(count: number) {
    let total = 0
    for (
      let treeIndex = count;
      treeIndex > 0;
      treeIndex -= treeIndex & -treeIndex
    )
      total += this.myTree[treeIndex]
    return total
  }

  total() {
    return this.prefixSum(this.length)
  }

  lastPositionWithPrefixAtMost(offset: number) {
    if (this.length === 0)
      return 0
    let index = 0
    let total = 0
    let bit = 1
    while (bit * 2 <= this.length)
      bit *= 2
    for (; bit > 0; bit >>= 1) {
      const candidate = index + bit
      if (candidate <= this.length
        && total + this.myTree[candidate] <= offset) {
        index = candidate
        total += this.myTree[candidate]
      }
    }
    return Math.min(index, this.length - 1)
  }

  firstPositionWhosePrefixReaches(
    target: number,
    low: number,
    high: number,
  ) {
    while (low < high) {
      const middle = Math.floor((low + high) / 2)
      if (this.prefixSum(middle + 1) >= target)
        high = middle
      else
        low = middle + 1
    }
    return low
  }
}
