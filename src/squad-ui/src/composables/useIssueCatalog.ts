import { ref } from 'vue'
import type { IssueDescriptor, IssueListPayload } from '../protocol/messages'

type SendIssuesRequest = (
  type: 'issues.list',
  options: { requestId: string },
) => void

/**
 * Owns the "issues.list" request/response lifecycle around the shared bridge - not menu open state or
 * selection, which stay local to the presentation component. Every call to {@link requestCatalog} supersedes
 * any still-outstanding request, so only the latest request's matching "issues.list" response or correlated
 * "protocol.error" can complete the load; an unrelated, uncorrelated protocol error never does.
 */
export function useIssueCatalog(send: SendIssuesRequest) {
  const issues = ref<IssueDescriptor[]>([])
  const isLoading = ref(false)
  const catalogError = ref<string | null>(null)
  let pendingRequestId: string | null = null

  function requestCatalog() {
    const requestId = crypto.randomUUID()
    pendingRequestId = requestId
    isLoading.value = true
    catalogError.value = null
    send('issues.list', { requestId })
  }

  function applyIssues(payload: IssueListPayload, requestId?: string) {
    if (requestId == null || requestId !== pendingRequestId) return
    issues.value = payload.issues
    isLoading.value = false
    pendingRequestId = null
  }

  /**
   * Consumes a correlated "protocol.error" belonging to the pending catalog request, returning true. Returns
   * false for every other error (no request ID, or one that does not match) so the caller still treats it as an
   * uncorrelated, general protocol failure.
   */
  function applyProtocolError(message: string, requestId?: string): boolean {
    if (requestId == null || requestId !== pendingRequestId) return false
    catalogError.value = message
    isLoading.value = false
    pendingRequestId = null
    return true
  }

  return {
    issues,
    isLoading,
    catalogError,
    requestCatalog,
    applyIssues,
    applyProtocolError,
  }
}
