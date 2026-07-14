import type { ReactNode } from 'react'
import logoMarkDark from './assets/logo-mark-dark.svg'
import logoMarkLight from './assets/logo-mark-light.svg'

export type IconName = 'dashboard' | 'intake' | 'inspection' | 'tools' | 'operations' | 'quality' | 'customers' | 'sales' | 'reservation' | 'deal' | 'finance' | 'menu' | 'close' | 'logout' | 'chevron' | 'alert' | 'calendar' | 'car' | 'user' | 'building' | 'search'

const paths: Record<IconName, ReactNode> = {
  dashboard: <><path d="M4 4h6v6H4zM14 4h6v4h-6zM14 12h6v8h-6zM4 14h6v6H4z" /></>,
  intake: <><path d="M3 13h18M5 13l2-5h10l2 5M6 17h.01M18 17h.01" /><path d="M4 13v5h2m14-5v5h-2M7 18h10" /></>,
  inspection: <><path d="M9 5h6M9 3h6v4H9zM6 5H4v16h16V5h-2" /><path d="m8 13 2 2 5-5" /></>,
  tools: <><path d="m14.5 6.5 3-3a4 4 0 0 1-5 5L6 15l3 3 6.5-6.5a4 4 0 0 1 5-5l-3 3z" /><path d="m4 17 3 3" /></>,
  operations: <><path d="M4 5h16v14H4zM8 3v4M16 3v4M4 10h16" /><path d="m8 14 2 2 4-4" /></>,
  quality: <><path d="M12 3 5 6v5c0 4.6 2.8 8.2 7 10 4.2-1.8 7-5.4 7-10V6z" /><path d="m9 12 2 2 4-4" /></>,
  customers: <><circle cx="9" cy="8" r="3" /><path d="M3 20c0-4 2-7 6-7s6 3 6 7M16 7a3 3 0 0 1 0 6M17 14c2.5.7 4 2.8 4 6" /></>,
  sales: <><path d="M4 5h16v14H4zM8 9h8M8 13h5" /><path d="m16 16 2-2 2 2" /></>,
  reservation: <><path d="M5 4h14v17l-7-3-7 3z" /><path d="M9 8h6M9 12h6" /></>,
  deal: <><path d="M4 7h16v12H4zM8 7V5h8v2M4 12h16" /><path d="M11 12v2h2v-2" /></>,
  finance: <><path d="M4 20V10M10 20V4M16 20v-7M22 20H2" /><path d="m3 7 6-4 6 6 6-5" /></>,
  menu: <><path d="M4 7h16M4 12h16M4 17h16" /></>,
  close: <><path d="m6 6 12 12M18 6 6 18" /></>,
  logout: <><path d="M10 4H4v16h6M14 8l4 4-4 4M8 12h10" /></>,
  chevron: <><path d="m9 18 6-6-6-6" /></>,
  alert: <><path d="M12 3 2.8 19h18.4zM12 9v4M12 17h.01" /></>,
  calendar: <><path d="M4 5h16v16H4zM8 3v4M16 3v4M4 10h16" /></>,
  car: <><path d="M3 13h18M5 13l2.2-5h9.6L19 13M6 17h.01M18 17h.01" /><path d="M4 13v5h2m14-5v5h-2M7 18h10" /></>,
  user: <><circle cx="12" cy="8" r="4" /><path d="M4 21c0-5 3-8 8-8s8 3 8 8" /></>,
  building: <><path d="M4 21V5l8-3 8 3v16M9 21v-4h6v4M8 8h2M14 8h2M8 12h2M14 12h2" /></>,
  search: <><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></>,
}

export function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  return <svg className="icon" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>
}

export function AppLogo({ compact = false, inverse = false }: { compact?: boolean; inverse?: boolean }) {
  return <div className={`app-logo ${compact ? 'compact' : ''} ${inverse ? 'inverse' : ''}`} aria-label="DealerOS" title="DealerOS">
    <span className="logo-mark" aria-hidden="true"><img src={inverse ? logoMarkLight : logoMarkDark} alt="" /></span>
    <span className="logo-copy"><strong>DealerOS</strong><small>Управление автосалоном</small></span>
  </div>
}

export function PageHeader({ eyebrow, title, description, children }: { eyebrow?: string; title: string; description?: string; children?: ReactNode }) {
  return <div className="page-heading">
    <div className="page-heading-copy">{eyebrow && <p className="eyebrow">{eyebrow}</p>}<h1>{title}</h1>{description && <p className="muted">{description}</p>}</div>
    {children && <div className="page-heading-actions">{children}</div>}
  </div>
}

export function EmptyState({ icon = 'car', title, description, action }: { icon?: IconName; title: string; description?: string; action?: ReactNode }) {
  return <div className="empty-state"><span className="empty-state-icon"><Icon name={icon} size={26} /></span><strong>{title}</strong>{description && <p>{description}</p>}{action}</div>
}

export function LoadingState({ label = 'Загружаем данные…' }: { label?: string }) {
  return <div className="loading-state" role="status"><span className="spinner" /><span>{label}</span></div>
}

export function VehicleVisual({ name, imageUrl }: { name: string; imageUrl?: string }) {
  return <div className="vehicle-visual">{imageUrl ? <img src={imageUrl} alt={name} /> : <><svg viewBox="0 0 320 160" role="img" aria-label={`Силуэт ${name}`}><path d="M48 106c11-3 22-18 34-35 9-13 19-22 37-25 38-6 76-6 103 4 15 6 27 22 38 39l24 7c10 3 15 11 15 22v10H21v-12c0-7 5-12 12-13l15-3z" /><path d="m91 75 28-5c27-4 65-4 88 2 10 3 17 10 25 20H74z" /><circle cx="79" cy="126" r="23" /><circle cx="245" cy="126" r="23" /></svg><span>Фото появится после контент-подготовки</span></>}</div>
}

const journey = ['Поступление', 'Осмотр', 'Подготовка', 'Работы', 'Качество', 'Продажа', 'Сделка', 'Выдача']

export function VehicleJourney({ status }: { status: string }) {
  const current: Record<string, number> = { IntakeDraft: 0, InStock: 0, InspectionInProgress: 1, ReconditioningRequired: 2, InspectionPassed: 4, ReadyForSale: 5, Reserved: 5, SaleInProgress: 6, Sold: 7 }
  const currentIndex = current[status] ?? 0
  return <div className="vehicle-journey" aria-label="Этапы пути автомобиля">
    {journey.map((label, index) => <div key={label} className={`${index < currentIndex ? 'complete' : ''} ${index === currentIndex ? 'current' : ''}`}><span>{index < currentIndex ? '✓' : index + 1}</span><small>{label}</small></div>)}
  </div>
}

export function Money({ value, currency = 'RUB', className = '' }: { value: number; currency?: string; className?: string }) {
  return <span className={`money-value ${className}`}>{new Intl.NumberFormat('ru-RU', { style: 'currency', currency, maximumFractionDigits: 0 }).format(value)}</span>
}
