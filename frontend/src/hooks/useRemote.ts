import { useEffect, useState } from 'react'
import { api } from '../api/client'

export function useRemote<T>(path: string | null, revision = 0) {
  const [state, setState] = useState<{
    path: string | null
    revision: number
    data: T | null
    error: string
    loading: boolean
  }>({ path: null, revision: -1, data: null, error: '', loading: false })
  useEffect(() => {
    if (!path) return
    const controller = new AbortController()
    api<T>(path, { signal: controller.signal })
      .then((data) => {
        if (!controller.signal.aborted)
          setState({ path, revision, data, loading: false, error: '' })
      })
      .catch((error) => {
        if (!controller.signal.aborted)
          setState({
            path,
            revision,
            data: null,
            loading: false,
            error:
              error instanceof Error ? error.message : 'Falha na consulta.',
          })
      })
    return () => controller.abort()
  }, [path, revision])
  return !path
    ? { data: null, error: '', loading: false }
    : state.path !== path || state.revision !== revision
      ? { data: null, error: '', loading: true }
      : state
}
