import { expect, test } from '@playwright/test'
import path from 'node:path'

test('employee creates an intake and accepts the vehicle to stock', async ({ page }) => {
  const browserErrors: string[] = []
  page.on('pageerror', (error) => browserErrors.push(error.message))
  page.on('console', (message) => { if (message.type() === 'error') browserErrors.push(message.text()) })
  const vin = `JHMCM56557C${Math.floor(100000 + Math.random() * 899999)}`
  await page.goto('/')
  await page.getByRole('button', { name: 'Войти в DealerOS' }).click()
  await expect(page.getByRole('heading', { name: 'Обзор' })).toBeVisible()
  await page.getByRole('navigation', { name: 'Разделы' }).getByRole('button', { name: 'Приёмка' }).click()
  await expect(page.getByRole('heading', { name: 'Приёмка и реестр' })).toBeVisible()

  await page.getByLabel('VIN').fill(vin)
  await page.getByLabel('Марка').fill('Honda')
  await page.getByLabel('Модель').fill('Accord')
  await page.getByLabel('Год').fill('2021')
  await page.getByLabel('Пробег').fill('54000')
  await page.getByLabel('Плановая цена закупки').fill('2150000')
  await page.getByRole('button', { name: 'Создать поступление' }).click()

  await expect(page.getByText(vin).first()).toBeVisible()
  await page.reload()
  await page.getByRole('navigation', { name: 'Разделы' }).getByRole('button', { name: 'Приёмка' }).click()
  await expect(page.getByText(vin).first()).toBeVisible()
  await page.getByRole('row').filter({ hasText: vin }).click()
  await page.getByRole('button', { name: /Принять на склад/ }).click()
  const success = page.getByRole('status')
  await expect(success).toContainText('Автомобиль принят на склад')
  await expect(success).toContainText(/MSK-\d{4}-/)

  await expect(page.getByRole('heading', { name: 'Фотогалерея' })).toBeVisible()
  await expect(page.locator('.vehicle-photo-grid > article')).toHaveCount(0)
  await page.getByLabel('Категория новых фотографий').selectOption('MainView')
  await page.getByLabel('Выбрать фотографии').setInputFiles([
    path.resolve('public/demo/gallery/silver-sedan-front.webp'),
    path.resolve('public/demo/gallery/silver-sedan-rear.webp'),
  ])
  await expect(page.locator('.vehicle-photo-grid > article')).toHaveCount(2)
  await expect(page.getByText('Загружено')).toHaveCount(2)
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.locator('.vehicle-gallery-panel').screenshot({ path: 'TestResults/vehicle-gallery-1440.png' })
  await page.setViewportSize({ width: 1024, height: 900 })
  await page.locator('.vehicle-gallery-panel').screenshot({ path: 'TestResults/vehicle-gallery-1024.png' })
  await page.setViewportSize({ width: 390, height: 844 })
  await page.locator('.vehicle-gallery-panel').screenshot({ path: 'TestResults/vehicle-gallery-390.png' })
  await page.setViewportSize({ width: 1440, height: 1000 })

  await page.getByRole('button', { name: 'Открыть silver-sedan-front.webp' }).click()
  await expect(page.getByRole('dialog', { name: 'silver-sedan-front.webp' })).toBeVisible()
  await page.screenshot({ path: 'TestResults/vehicle-gallery-lightbox.png' })
  await page.getByRole('button', { name: 'Следующее фото' }).click()
  await expect(page.getByRole('dialog', { name: 'silver-sedan-rear.webp' })).toBeVisible()
  await page.getByRole('button', { name: 'Закрыть' }).click()

  const firstCard = page.locator('.vehicle-photo-grid > article').filter({ has: page.getByRole('button', { name: 'Открыть silver-sedan-front.webp' }) })
  await firstCard.getByRole('button', { name: 'Изменить', exact: true }).click()
  await firstCard.getByLabel('Подпись').fill('Главная фотография автомобиля')
  await firstCard.getByRole('button', { name: 'Сохранить' }).click()
  await expect(page.getByText('Главная фотография автомобиля')).toBeVisible()
  const updatedFirstCard = page.locator('.vehicle-photo-grid > article').filter({ hasText: 'Главная фотография автомобиля' })
  await updatedFirstCard.getByRole('button', { name: 'Обложка' }).click()
  await expect(updatedFirstCard.getByText('Обложка')).toBeVisible()
  page.once('dialog', (dialog) => dialog.accept())
  await page.locator('.vehicle-photo-grid > article').filter({ hasText: 'silver-sedan-rear.webp' }).getByRole('button', { name: 'Удалить' }).click()
  await expect(page.locator('.vehicle-photo-grid > article')).toHaveCount(1)
  expect(browserErrors).toEqual([])
})

test('tablet intake form exposes validation without a server round trip', async ({ page }) => {
  await page.setViewportSize({ width: 820, height: 1180 })
  await page.goto('/')
  await page.getByRole('button', { name: 'Войти в DealerOS' }).click()
  await page.getByRole('button', { name: 'Открыть меню' }).click()
  await page.getByRole('navigation', { name: 'Разделы' }).getByRole('button', { name: 'Приёмка' }).click()
  await expect(page.getByRole('heading', { name: 'Приёмка и реестр' })).toBeVisible()

  await page.getByLabel('VIN').fill('INVALID')
  await page.getByRole('button', { name: 'Создать поступление' }).click()

  await expect(page.getByText('VIN: 17 символов без I, O и Q')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Создать поступление' })).toBeVisible()
})
