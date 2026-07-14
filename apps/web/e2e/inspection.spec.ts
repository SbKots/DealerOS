import { expect, test } from '@playwright/test'

test('diagnost completes an immutable vehicle inspection with a defect photo', async ({ page }) => {
  const browserErrors: string[] = []
  page.on('pageerror', (error) => browserErrors.push(error.message))
  page.on('console', (message) => { if (message.type() === 'error') browserErrors.push(message.text()) })
  const vin = `WVWZZZ3CZ6E${Math.floor(100000 + Math.random() * 899999)}`

  await page.goto('/')
  await page.getByRole('button', { name: 'Войти в DealerOS' }).click()
  await page.getByRole('navigation', { name: 'Разделы' }).getByRole('button', { name: 'Приёмка' }).click()
  await expect(page.getByRole('heading', { name: 'Приёмка и реестр' })).toBeVisible()
  await page.getByLabel('Филиал').selectOption({ index: 1 })
  await page.getByLabel('VIN').fill(vin)
  await page.getByLabel('Марка').fill('Volkswagen')
  await page.getByLabel('Модель').fill('Passat')
  await page.getByLabel('Год').fill('2022')
  await page.getByLabel('Пробег').fill('41000')
  await page.getByLabel('Плановая цена закупки').fill('2650000')
  await page.getByRole('button', { name: 'Создать поступление' }).click()
  await page.getByRole('button', { name: /Принять на склад/ }).click()
  await expect(page.getByRole('status')).toContainText('Автомобиль принят на склад')

  await page.getByRole('navigation', { name: 'Разделы' }).getByRole('button', { name: 'Осмотры' }).click()
  const queueCard = page.locator('.queue-card').filter({ hasText: vin })
  await expect(queueCard).toBeVisible()
  await queueCard.getByRole('button', { name: 'Начать осмотр' }).click()
  await expect(page.getByRole('heading', { name: 'Технический чек-лист' })).toBeVisible()

  const resultGroups = page.getByRole('group', { name: /Результат:/ })
  await expect(resultGroups).toHaveCount(11)
  for (let index = 0; index < 11; index += 1) {
    await resultGroups.nth(index).getByRole('button', { name: 'Норма' }).click()
  }
  await expect(page.getByText('100%')).toBeVisible()

  await page.getByLabel('Категория дефекта').selectOption('Brakes')
  await page.getByLabel('Название дефекта').fill('Критический износ колодок')
  await page.getByLabel('Описание дефекта').fill('Остаток ниже допуска, эксплуатация опасна')
  await page.getByLabel('Серьёзность').selectOption('Critical')
  await page.getByLabel('Оценка ремонта').fill('18000')
  await page.getByRole('button', { name: 'Добавить дефект' }).click()
  const defectCard = page.locator('.defect-card').filter({ hasText: 'Критический износ колодок' })
  await expect(defectCard).toContainText('Продажа заблокирована')
  await defectCard.getByLabel('Фото: Критический износ колодок').setInputFiles({
    name: 'brake.png', mimeType: 'image/png',
    buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64'),
  })
  await expect(defectCard.getByRole('img', { name: 'brake.png' })).toBeVisible()

  await page.getByRole('button', { name: 'Завершить осмотр' }).click()
  await page.getByLabel('Итоговый комментарий').fill('Направить на предпродажную подготовку')
  await page.getByRole('button', { name: 'Подтвердить и завершить' }).click()
  await expect(page.getByText(/Результат зафиксирован/)).toBeVisible()
  await expect(page.getByText(/открыть раздел «Подготовка»/)).toBeVisible()

  await page.reload()
  await page.getByRole('navigation', { name: 'Разделы' }).getByRole('button', { name: 'Приёмка' }).click()
  await expect(page.getByRole('heading', { name: 'Приёмка и реестр' })).toBeVisible()
  await page.getByRole('row').filter({ hasText: vin }).click()
  await page.getByRole('button', { name: /Открыть осмотры/ }).click()
  await page.getByRole('button', { name: /Ревизия 1 · Завершён/ }).click()
  await expect(page.getByText(/Результат зафиксирован/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Добавить дефект' })).toHaveCount(0)
  await expect(page.getByRole('img', { name: 'brake.png' })).toBeVisible()
  expect(browserErrors).toEqual([])
})
