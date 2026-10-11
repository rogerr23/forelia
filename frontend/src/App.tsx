import { useEffect, useState } from 'react'
import { AppLayout } from './components/AppLayout'
import { Heading } from './components/Heading'
import { Notice } from './components/Notice'
import { DashboardPage } from './pages/DashboardPage'
import { HistoryPage } from './pages/HistoryPage'
import { ImportPage } from './pages/ImportPage'
import { ResultsPage } from './pages/results/ResultsPage'
import './styles/app.css'

function renderPage(route: string, id?: string) {
  switch (route) {
    case 'visao-geral':
      return <DashboardPage />
    case 'nova-conciliacao':
      return <ImportPage />
    case 'historico':
      return <HistoryPage />
    case 'resultados':
      if (
        id &&
        /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
          id,
        )
      )
        return <ResultsPage id={id} />
      return (
        <>
          <Heading
            title="Resultados"
            sub="Escolha uma execução concluída para consultar os resultados."
          />
          <Notice>
            <a href="#historico">Abrir histórico de conciliações</a>
          </Notice>
        </>
      )
    default:
      return (
        <>
          <Heading
            title="Página não encontrada"
            sub="Use a navegação para continuar."
          />
          <a href="#visao-geral">Ir para visão geral</a>
        </>
      )
  }
}

function App() {
  const [hash, setHash] = useState(
    () => window.location.hash.slice(1) || 'visao-geral',
  )
  useEffect(() => {
    const update = () => setHash(window.location.hash.slice(1) || 'visao-geral')
    window.addEventListener('hashchange', update)
    return () => window.removeEventListener('hashchange', update)
  }, [])
  const [route, id] = hash.split('/')
  return (
    <AppLayout route={route} contentKey={hash}>
      {renderPage(route, id)}
    </AppLayout>
  )
}

export default App
