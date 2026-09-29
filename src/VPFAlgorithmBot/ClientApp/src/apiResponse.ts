export async function readApiResponse<T>(response: Response): Promise<T> {
  const text = await response.text()
  let data: unknown
  if (text.trim()) {
    try { data = JSON.parse(text) } catch {
      if (response.ok) throw new Error('Сервер повернув неочікувану відповідь. Оновіть сторінку.')
    }
  }
  if (!response.ok) {
    const problem = data as { detail?: string, title?: string } | undefined
    const fallback: Record<number, string> = {
      400: 'Перевірте обов’язкові поля та формат чисел.',
      401: 'Відкрийте застосунок через Telegram повторно.',
      403: 'Немає доступу або завершився сеанс адміністратора. Увійдіть повторно.',
      404: 'Запис не знайдено.',
      409: 'Такий запис уже існує. Оновіть довідник.',
    }
    throw new Error(typeof data === 'string' ? data : problem?.detail || problem?.title || fallback[response.status] || `Помилка сервера (HTTP ${response.status}).`)
  }
  return data as T
}
