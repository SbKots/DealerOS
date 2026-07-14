import { expect, test } from '@playwright/test'

const image = (name: string) => ({
  name,
  mimeType: 'image/png',
  buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64'),
})

test('release train 0.4-0.8 moves an approved offer to a concurrent-safe reservation', async ({ page }) => {
  test.setTimeout(420_000)
  const browserErrors: string[] = []
  page.on('pageerror', (error) => browserErrors.push(error.message))
  page.on('console', (message) => { if (message.type() === 'error') browserErrors.push(message.text()) })
  const suffix = Math.floor(100000 + Math.random() * 899999).toString()
  const vin = `WVWZZZ3CZ8E${suffix}`
  const model = `Train-${suffix}`
  const defect = `Ремонт бампера ${suffix}`
  const workTitle = `Устранить: ${defect}`
  const customer = `Покупатель ${suffix}`

  await page.goto('/')
  await login(page, 'admin@volga-auto.demo')
  const adminSession = await captureSession(page)
  await page.getByLabel('Филиал').selectOption({ index: 1 })
  await page.getByLabel('VIN').fill(vin)
  await page.getByLabel('Марка').fill('Volkswagen')
  await page.getByLabel('Модель').fill(model)
  await page.getByLabel('Год').fill('2023')
  await page.getByLabel('Пробег').fill('46250')
  await page.getByLabel('Плановая цена закупки').fill('990000')
  await page.getByRole('button', { name: 'Создать поступление' }).click()
  await page.getByRole('button', { name: /Принять на склад/ }).click()
  await expect(page.getByRole('status')).toContainText('Автомобиль принят на склад')

  await navigate(page, 'Осмотры')
  const inspectionCard = page.locator('.queue-card').filter({ hasText: vin })
  await inspectionCard.getByRole('button', { name: 'Начать осмотр' }).click()
  const checks = page.getByRole('group', { name: /Результат:/ })
  await expect(checks).toHaveCount(11)
  for (let index = 0; index < 11; index += 1) await checks.nth(index).getByRole('button', { name: 'Норма' }).click()
  await page.getByLabel('Категория дефекта').selectOption('Body')
  await page.getByLabel('Название дефекта').fill(defect)
  await page.getByLabel('Описание дефекта').fill('Требуется восстановление креплений и окраска')
  await page.getByLabel('Серьёзность').selectOption('Major')
  await page.getByLabel('Оценка ремонта').fill('35000')
  await page.getByRole('checkbox', { name: 'Требует устранения' }).check()
  await page.getByRole('button', { name: 'Добавить дефект' }).click()
  await page.getByRole('button', { name: 'Завершить осмотр' }).click()
  await page.getByLabel('Итоговый комментарий').fill('Требуется предпродажная подготовка')
  await page.getByRole('button', { name: 'Подтвердить и завершить' }).click()
  await expect(page.getByText(/открыть раздел «Подготовка»/)).toBeVisible()

  await logout(page)
  await login(page, 'prep@volga-auto.demo')
  const prepSession = await captureSession(page)
  await navigate(page, 'Подготовка')
  const preparationCard = page.locator('.queue-card').filter({ hasText: vin })
  await preparationCard.getByRole('button', { name: 'Создать план' }).click()
  await page.getByLabel(`Тип исполнителя: ${defect}`).selectOption('External')
  await page.getByLabel(`Исполнитель: ${defect}`).fill('Кузовной центр')
  await page.getByLabel(`Стоимость работы: ${defect}`).fill('20000')
  await page.getByLabel(`Стоимость запчастей: ${defect}`).fill('15000')
  await page.getByLabel(`Срок: ${defect}`).fill('3')
  await page.getByRole('button', { name: 'Сохранить работу' }).click()
  await page.getByRole('button', { name: 'Отправить руководителю' }).click()
  await expect(page.locator('.plan-status')).toHaveText('На согласовании')

  await logout(page)
  await login(page, 'manager@volga-auto.demo')
  const managerSession = await captureSession(page)
  await navigate(page, 'Подготовка')
  const approval = page.locator('.approvals-queue .history-list button').filter({ hasText: vin })
  await approval.click()
  await page.getByLabel('Одобренный лимит').fill('35000')
  await page.getByLabel('Причина решения').fill('Бюджет подтверждён')
  await page.getByRole('button', { name: 'Утвердить бюджет' }).click()
  await expect(page.getByText('Бюджет утверждён')).toBeVisible()

  await switchSession(page, prepSession)
  await navigate(page, 'Выполнение')
  const executionCard = page.locator('.queue-card').filter({ hasText: vin })
  await executionCard.getByRole('button', { name: 'Открыть выполнение' }).click()
  await page.getByRole('button', { name: 'Начать выполнение' }).click()
  const work = page.locator('.work-card').filter({ hasText: defect })
  await work.getByRole('button', { name: 'Начать работу' }).click()
  await work.getByLabel(`Часы: ${workTitle}`).fill('2')
  await work.getByLabel(`Факт подрядчика: ${workTitle}`).fill('30000')
  await work.getByRole('button', { name: 'Сохранить факт' }).click()
  await expect(work.getByLabel(`Факт подрядчика: ${workTitle}`)).toHaveValue('30000')
  await work.getByRole('button', { name: 'Завершить работу' }).click()
  await expect(work.getByText('Completed')).toBeVisible()
  await page.getByRole('button', { name: 'Завершить подготовку' }).click()
  await expect(page.getByText('Execution · Completed')).toBeVisible()

  await switchSession(page, managerSession)
  await navigate(page, 'Качество и контент')
  const qualityCard = page.locator('.queue-card').filter({ hasText: vin })
  await qualityCard.getByRole('button', { name: 'Открыть QC' }).click()
  await page.getByRole('button', { name: 'Подтвердить ReadyForSale' }).click()
  await expect(page.getByText('QC rev. 1 · Passed')).toBeVisible()

  await switchSession(page, prepSession)
  await navigate(page, 'Качество и контент')
  const listingCard = page.locator('.queue-card').filter({ hasText: vin })
  await listingCard.getByRole('button', { name: 'Подготовить объявление' }).click()
  await page.getByLabel('Фото Exterior').setInputFiles(image('exterior.png'))
  await expect(page.locator('.media-card')).toHaveCount(1)
  await page.getByLabel('Фото Interior').setInputFiles(image('interior.png'))
  await expect(page.locator('.media-card')).toHaveCount(2)
  await page.getByLabel('Фото DamageHistory').setInputFiles(image('damage.png'))
  await expect(page.locator('.media-card')).toHaveCount(3)
  await expect(page.locator('.media-card img')).toHaveCount(3)
  await page.locator('.media-card').filter({ hasText: 'Exterior' }).getByRole('button', { name: 'Сделать обложкой' }).click()
  await expect(page.locator('.media-card').filter({ hasText: 'Exterior · Обложка' })).toBeVisible()
  await page.getByRole('button', { name: 'Создать Content Pack' }).click()
  await page.getByLabel('Комплектация').fill('Климат-контроль, зимний комплект')
  await page.getByLabel('Преимущества').fill('Прозрачная история подготовки и QC')
  await page.getByLabel('Описание состояния').fill('Подготовка завершена, замечания раскрыты')
  await page.getByLabel('Публичная цена').fill('1350000')
  const listingSaved = page.waitForResponse((response) => response.request().method() === 'PUT'
    && response.url().includes('/api/listings/') && response.ok())
  await page.getByRole('button', { name: 'Сохранить Content Pack' }).click()
  await listingSaved
  const listingReady = page.waitForResponse((response) => response.request().method() === 'POST'
    && response.url().endsWith('/ready') && response.ok())
  await page.getByRole('button', { name: 'Проверить и сделать Listing Ready' }).click()
  await listingReady
  await expect(page.locator('.status').filter({ hasText: 'Listing Ready' })).toBeVisible()

  await switchSession(page, managerSession)
  await navigate(page, 'Клиенты и лиды')
  await page.getByLabel('Имя клиента').fill(customer)
  await page.getByLabel('Телефон клиента').fill(`+7999${suffix}1`)
  await page.getByRole('checkbox', { name: 'Согласие на контакт' }).check()
  await page.getByRole('button', { name: 'Создать клиента' }).click()
  await expect(page.getByText(`Новый лид: ${customer}`)).toBeVisible()
  await page.getByLabel('Автомобиль лида').selectOption({ label: `Volkswagen ${model}` })
  await page.getByLabel('Источник лида').fill('Визит в салон')
  await page.getByRole('button', { name: 'Создать лид' }).click()
  await page.getByRole('button', { name: 'Назначить round-robin' }).click()
  await expect(page.locator('.lead-card .status')).toContainText('Assigned')
  await page.getByLabel('Итог первого контакта').fill('Клиент подтвердил интерес и время визита')
  await page.getByLabel('Следующее действие').fill('Провести test drive')
  await page.getByRole('button', { name: 'Зафиксировать первый контакт' }).click()
  await expect(page.locator('.lead-card .status')).toContainText('FirstContact')
  await page.getByLabel('Действие после квалификации').fill('Провести test drive')
  await page.getByRole('button', { name: 'Квалифицировать' }).click()
  await expect(page.locator('.lead-card .status')).toContainText('Qualified')

  await navigate(page, 'Визиты и Offer')
  const startsAt = new Date(Date.now() + 2 * 86_400_000 + (Number(suffix) % 20_000) * 60_000)
  const endsAt = new Date(startsAt.getTime() + 3_600_000)
  await page.getByLabel('Начало визита').fill(dateInput(startsAt))
  await page.getByLabel('Окончание визита').fill(dateInput(endsAt))
  await page.getByLabel('Lead для визита').selectOption({ label: `${customer} · автомобиль` })
  await page.getByRole('button', { name: 'Назначить визит' }).click()
  await expect(page.locator('.visit-card')).toContainText(customer)
  await page.getByRole('button', { name: 'Клиент прибыл' }).click()
  await page.getByLabel('Пробег до').fill('46250')
  await page.getByRole('button', { name: 'Выдать на test drive' }).click()
  await page.getByLabel('Пробег после').fill('46266')
  await page.getByRole('button', { name: 'Принять автомобиль' }).click()
  await page.getByRole('button', { name: 'Завершить визит' }).click()
  await expect(page.locator('.visit-card .status')).toContainText('Completed')
  await page.getByLabel('Lead для предложения').selectOption({ label: customer })
  await page.getByLabel('Скидка').fill('100000')
  await page.getByRole('button', { name: 'Рассчитать на сервере' }).click()
  await expect(page.getByText(/Требуется manager approval/)).toBeVisible()
  await page.getByRole('button', { name: 'Создать Offer' }).click()
  await page.getByRole('button', { name: 'Отправить / auto-approve' }).click()
  await expect(page.locator('.offer-card .status')).toContainText('Submitted')

  await switchSession(page, adminSession)
  await navigate(page, 'Визиты и Offer')
  await page.locator('.offer-list > button').filter({ hasText: customer }).click()
  await page.getByLabel('Причина решения по Offer').fill('Маржа и скидка подтверждены руководителем')
  await page.getByRole('button', { name: 'Утвердить Offer' }).click()
  await expect(page.getByText('Immutable Approved Offer')).toBeVisible()
  await expect(page.locator('.offer-card .status')).toContainText('Approved')

  await navigate(page, 'Брони')
  const approvedOption = page.getByRole('option').filter({ hasText: customer })
  const approvedSnapshotId = await approvedOption.getAttribute('value')
  expect(approvedSnapshotId).toBeTruthy()
  await page.getByLabel('Approved Offer для брони').selectOption(approvedSnapshotId!)
  await page.getByRole('button', { name: 'Создать бронь' }).click()
  await expect(page.locator('.reservation-card')).toContainText(customer)
  await expect(page.locator('.reservation-card .status')).toContainText('Active')
  await expect(page.locator('.reservation-card .countdown')).toContainText(/ч .*мин/)
  expect(browserErrors).toEqual([])
})

async function navigate(page: import('@playwright/test').Page, section: string) {
  await page.getByRole('navigation', { name: 'Разделы' }).getByRole('button', { name: section }).click()
}

async function login(page: import('@playwright/test').Page, email: string) {
  await expect(page.getByRole('heading', { name: 'Войдите в рабочее пространство' })).toBeVisible()
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Пароль').fill('DealerOS!2026')
  await page.getByRole('button', { name: 'Войти в DealerOS' }).click()
  await expect(page.getByRole('navigation', { name: 'Разделы' })).toBeVisible()
}

async function logout(page: import('@playwright/test').Page) {
  await page.getByRole('button', { name: 'Выйти' }).click()
  await expect(page.getByRole('heading', { name: 'Войдите в рабочее пространство' })).toBeVisible()
}

async function captureSession(page: import('@playwright/test').Page) {
  return page.evaluate(() => sessionStorage.getItem('dealeros.session') ?? '')
}

async function switchSession(page: import('@playwright/test').Page, session: string) {
  await page.evaluate((value) => sessionStorage.setItem('dealeros.session', value), session)
  await page.reload()
  await expect(page.getByRole('navigation', { name: 'Разделы' })).toBeVisible()
}

function dateInput(value: Date) {
  return new Date(value.getTime() - value.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}
