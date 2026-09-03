import { computed, type Ref } from 'vue'
import type {
  Elicitation,
  InputRequest,
  Permission,
} from '../protocol/messages'

interface RoleRequest {
  role: string
}

function indexByRole<T extends RoleRequest>(
  requests: readonly T[],
): ReadonlyMap<string, readonly T[]> {
  const requestsByRole = new Map<string, T[]>()
  for (const request of requests) {
    const roleRequests = requestsByRole.get(request.role)
    if (roleRequests)
      roleRequests.push(request)
    else
      requestsByRole.set(request.role, [request])
  }
  return requestsByRole
}

function requestsFor<T>(
  requestsByRole: ReadonlyMap<string, readonly T[]>,
  role: string,
): readonly T[] {
  return requestsByRole.get(role) ?? []
}

export function useRoleInteractions(
  permissions: Readonly<Ref<Permission[]>>,
  inputs: Readonly<Ref<InputRequest[]>>,
  elicitations: Readonly<Ref<Elicitation[]>>,
) {
  const permissionsByRole = computed(() => indexByRole(permissions.value))
  const inputsByRole = computed(() => indexByRole(inputs.value))
  const elicitationsByRole = computed(() => indexByRole(elicitations.value))

  return {
    permissionsFor: (role: string) =>
      requestsFor(permissionsByRole.value, role),
    inputsFor: (role: string) =>
      requestsFor(inputsByRole.value, role),
    elicitationsFor: (role: string) =>
      requestsFor(elicitationsByRole.value, role),
  }
}
