import type { ReactNode } from 'react'
import {
  ArrowLeftRight,
  Files,
  History,
  LayoutDashboard,
  ListFilter,
} from 'lucide-react'

const navigation = [
  { id: 'visao-geral', title: 'Visão geral', icon: LayoutDashboard },
  { id: 'nova-conciliacao', title: 'Nova conciliação', icon: Files },
  { id: 'historico', title: 'Histórico', icon: History },
  { id: 'resultados', title: 'Resultados', icon: ListFilter },
]

interface AppLayoutProps {
  route: string
  contentKey: string
  children: ReactNode
}

export function AppLayout({ route, contentKey, children }: AppLayoutProps) {
  const page = navigation.find((item) => item.id === route)
  return (
    <div id="forelia-layout">
      <div className="fl-app fl-top" data-palette="grafite-azul">
        <header className="fl-nav">
          <div>
            <a
              className="fl-logo"
              href="#visao-geral"
              aria-label="Forelia, visão geral"
            >
              <span className="fl-logomark">
                <ArrowLeftRight aria-hidden />
              </span>
              Forelia
            </a>
            <div className="fl-sub">Conciliação financeira</div>
          </div>
          <nav aria-label="Navegação principal">
            {navigation.map(({ id, title, icon: Icon }) => (
              <a
                className="fl-navbtn"
                href={'#' + id}
                key={id}
                aria-current={route === id ? 'page' : undefined}
              >
                <Icon aria-hidden />
                {title}
              </a>
            ))}
          </nav>
        </header>
        <main className="fl-main">
          <div className="fl-topbar">
            <span>
              Área de trabalho / {page?.title || 'Página não encontrada'}
            </span>
            <span>Forelia · V1</span>
          </div>
          <div className="fl-content" key={contentKey}>
            {children}
          </div>
        </main>
      </div>
    </div>
  )
}
