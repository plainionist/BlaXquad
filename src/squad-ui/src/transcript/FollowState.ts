import { readonly, ref } from 'vue'

export type FollowTransition = 'unchanged' | 'started' | 'stopped'

export class FollowState {
  private readonly myFollowing = ref(true)

  readonly following = readonly(this.myFollowing)

  onExternalMove(position: number, realMaximum: number): FollowTransition {
    const next = position >= Math.max(0, realMaximum) - 44
    if (next === this.myFollowing.value)
      return 'unchanged'
    this.myFollowing.value = next
    return next ? 'started' : 'stopped'
  }

  suspend(): FollowTransition {
    if (!this.myFollowing.value)
      return 'unchanged'
    this.myFollowing.value = false
    return 'stopped'
  }

  reconcileInternal(previousFollowing: boolean) {
    this.myFollowing.value = previousFollowing
  }
}
