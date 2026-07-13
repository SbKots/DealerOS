import { expect, test } from '@playwright/test'

test('employee creates an intake and accepts the vehicle to stock', async ({ page }) => {
  const browserErrors: string[] = []
  page.on('pageerror', (error) => browserErrors.push(error.message))
  page.on('console', (message) => { if (message.type() === 'error') browserErrors.push(message.text()) })
  const vin = `JHMCM56557C${Math.floor(100000 + Math.random() * 899999)}`
  await page.goto('/')
  await page.getByRole('button', { name: 'Войти в DealerOS' }).click()
  await expect(page.getByRole('heading', { name: 'Приём автомобиля' })).toBeVisible()

  await page.getByLabel('VIN').fill(vin)
  await page.getByLabel('Марка').fill('Honda')
  await page.getByLabel('Модель').fill('Accord')
  await page.getByLabel('Год').fill('2021')
  await page.getByLabel('Пробег').fill('54000')
  await page.getByLabel('Плановая цена закупки').fill('2150000')
  await page.getByRole('button', { name: 'Создать поступление' }).click()

  await expect(page.getByText(vin).first()).toBeVisible()
  await page.reload()
  await expect(page.getByText(vin).first()).toBeVisible()
  await page.getByRole('row').filter({ hasText: vin }).click()
  await page.getByRole('button', { name: /Принять на склад/ }).click()
  const success = page.getByRole('status')
  await expect(success).toContainText('Автомобиль принят на склад')
  await expect(success).toContainText(/MSK-\d{4}-/)
  expect(browserErrors).toEqual([])
})

test('tablet intake form exposes validation without a server round trip', async ({ page }) => {
  await page.setViewportSize({ width: 820, height: 1180 })
  await page.goto('/')
  await page.getByRole('button', { name: 'Войти в DealerOS' }).click()
  await expect(page.getByRole('heading', { name: 'Приём автомобиля' })).toBeVisible()

  await page.getByLabel('VIN').fill('INVALID')
  await page.getByRole('button', { name: 'Создать поступление' }).click()

  await expect(page.getByText('VIN: 17 символов без I, O и Q')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Создать поступление' })).toBeVisible()
})
