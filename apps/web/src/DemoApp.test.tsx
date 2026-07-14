import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import DemoApp from './DemoApp'

describe('DealerOS static demo', () => {
  it('opens without an API and navigates to a vehicle card', async () => {
    render(<DemoApp />)
    await userEvent.click(screen.getByRole('button', { name: 'Открыть демоверсию DealerOS' }))
    expect(screen.getAllByText('Демонстрационный режим').length).toBeGreaterThan(0)
    await userEvent.click(within(screen.getByRole('navigation', { name: 'Разделы' })).getByRole('button', { name: 'Автомобили' }))
    await userEvent.click(screen.getByRole('button', { name: 'Открыть Skoda Kodiaq' }))
    expect(screen.getAllByRole('heading', { name: 'Skoda Kodiaq' }).length).toBeGreaterThan(0)
    expect(screen.getByText('DEMO0000000000002')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Фотогалерея' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Открыть Передний ракурс · демо-фото' }))
    expect(screen.getByRole('dialog', { name: 'Передний ракурс · демо-фото' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Следующее фото' }))
    expect(screen.getByRole('dialog', { name: 'Задний ракурс · демо-фото' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Закрыть' }))
    await userEvent.click(screen.getAllByRole('button', { name: 'Обложка' })[0])
    expect(screen.getAllByText('Обложка').length).toBeGreaterThan(0)
  })
})
